using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Backend.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Environments;

// Command: calls one included GET operation (without path parameters) that uses the scheme, with the stored
// credential and the global headers that apply to it, and records whether the target accepted it.
public static class ValidateCredential
{
    public sealed record Response(bool Valid, string Validation, DateTimeOffset ValidatedAt);

    public static async Task<Results<Ok<Response>, NotFound, ForbidHttpResult, UnprocessableEntity<string>>> Handle(
        Guid id, string scheme, ClaimsPrincipal principal, ApplicationDbContext db, CredentialProtector protector,
        IHttpClientFactory httpClientFactory, TimeProvider time, CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var profile = await db.AuthProfiles.Include(profile => profile.Environment).ThenInclude(environment => environment.Headers)
            .SingleOrDefaultAsync(profile => profile.Name == scheme
                && profile.Environment.ProjectId == id && profile.Environment.IsDefault, cancellationToken);
        if (profile is null)
            return TypedResults.NotFound();
        if (profile.AuthType == AuthType.None || profile.SecretCipherText is null)
            return TypedResults.UnprocessableEntity("There is no credential to validate.");

        var info = (await SchemeInfo.ForProjectAsync(db, id, cancellationToken)).SingleOrDefault(s => s.Name == scheme);
        var candidates = info?.Operations.Where(operation => operation.StartsWith("GET ") && !operation.Contains('{'))
            .Select(operation => operation[4..]).ToList() ?? [];
        var path = await db.ApiEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.ApiSpec.ProjectId == id && endpoint.ApiSpec.IsActive
                && endpoint.Testing == EndpointTesting.Included && endpoint.HttpMethod == "GET" && candidates.Contains(endpoint.Path))
            .OrderBy(endpoint => endpoint.Path).Select(endpoint => endpoint.Path).FirstOrDefaultAsync(cancellationToken);
        if (path is null)
            return TypedResults.UnprocessableEntity(
                "No included GET operation without path parameters uses this scheme, so there is nothing safe to call.");

        var environment = profile.Environment;
        var config = CredentialConfig.From(profile.PublicConfig);
        if (TargetClient.Combine(environment.BaseUrl, path) is not { } url)
            return TypedResults.UnprocessableEntity($"GET {path} does not stay on the target host, so it was not called.");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var header in environment.Headers)
        {
            var appliesTo = header.AppliesTo.Deserialize<List<string>>() ?? [];
            if (appliesTo.Count > 0 && !appliesTo.Contains($"GET {path}"))
                continue;
            var value = header.SecretCipherText is null ? header.Value : protector.TryUnprotect(header.SecretCipherText, header.Id);
            if (value is null)
                return TypedResults.UnprocessableEntity(
                    $"The secret value of header {header.Name} could not be decrypted, likely because Credentials:Key changed. Remove the header and add it again.");
            request.Headers.TryAddWithoutValidation(header.Name, value);
        }
        if (protector.TryUnprotect(profile.SecretCipherText, profile.Id) is not { } secret)
        {
            // Record it, so an earlier successful validation is not still shown as valid.
            const string undecryptable = "This credential could not be decrypted, likely because Credentials:Key changed. Enter the secret again.";
            profile.PublicConfig = (config with { ValidatedAt = time.GetUtcNow(), Valid = false, Validation = undecryptable }).ToDocument();
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.UnprocessableEntity(undecryptable);
        }
        TargetClient.Authenticate(request, profile.AuthType, secret, config.Username, info);

        var probe = await TargetClient.SendAsync(httpClientFactory, request, environment.VerifyTls,
            environment.RequestTimeoutSeconds, cancellationToken);
        var valid = probe.Status is >= 200 and < 300;
        var credential = profile.AuthType switch
        {
            AuthType.Bearer => "this token",
            AuthType.ApiKey => "this API key",
            _ => "these credentials"
        };
        var validation = probe.Status is { } status
            ? $"GET {path} returned {status} with {credential}."
            : $"GET {path} failed: {probe.Error}";
        var now = time.GetUtcNow();
        profile.PublicConfig = (config with { ValidatedAt = now, Valid = valid, Validation = validation }).ToDocument();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new Response(valid, validation, now));
    }
}
