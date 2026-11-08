using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Environments;

// Non-secret part of an AuthProfile, stored in PublicConfig. Hint is the last 4 characters of the secret.
public sealed record CredentialConfig(
    string? Label, string? Hint, string? Username, DateTimeOffset? ValidatedAt, bool? Valid, string? Validation)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CredentialConfig From(JsonDocument document) =>
        document.Deserialize<CredentialConfig>(Json) ?? new(null, null, null, null, null, null);

    public JsonDocument ToDocument() => JsonSerializer.SerializeToDocument(this, Json);
}

// Query: the project's default target environment, the spec's security schemes with their credentials, and
// global headers. Secrets never leave the server; only their last 4 characters do.
public static class GetEnvironment
{
    public sealed record Response(Target? Target, List<string> SpecServers, List<Scheme> Schemes, List<Header> Headers);

    public sealed record Target(
        Guid Id, string Name, string BaseUrl, EnvironmentKind Kind, int RequestTimeoutSeconds, bool VerifyTls,
        string? AuthorizedBy, DateTimeOffset? AuthorizedAt);

    // Type and the fields after it come from the spec; InSpec is false for a credential whose scheme was removed.
    public sealed record Scheme(
        string Name, bool InSpec, string Type, string? HttpScheme, string? In, string? ParameterName,
        List<string> Operations, Credential? Credential);

    public sealed record Credential(
        AuthType AuthType, string? Label, string? Hint, string? Username, DateTimeOffset? ValidatedAt, bool? Valid,
        string? Validation);

    public sealed record Header(Guid Id, string Name, string? Value, bool Secret, string? Hint, List<string> AppliesTo);

    public static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is null)
            return TypedResults.NotFound();

        var environment = await db.ApiEnvironments.AsNoTracking()
            .Where(environment => environment.ProjectId == id && environment.IsDefault)
            .Select(environment => new
            {
                Target = new Target(environment.Id, environment.Name, environment.BaseUrl, environment.Kind,
                    environment.RequestTimeoutSeconds, environment.VerifyTls,
                    environment.AuthorizationConfirmedBy!.DisplayName, environment.AuthorizationConfirmedAt),
                Profiles = environment.AuthProfiles.Select(profile => new { profile.Name, profile.AuthType, profile.PublicConfig }).ToList(),
                Headers = environment.Headers.OrderBy(header => header.CreatedAt)
                    .Select(header => new { header.Id, header.Name, header.Value, Secret = header.SecretCipherText != null, header.Hint, header.AppliesTo })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        var servers = await db.ApiSpecs.AsNoTracking().Where(spec => spec.ProjectId == id && spec.IsActive)
            .Select(spec => spec.ParsedDocument).SingleOrDefaultAsync(cancellationToken) is { } document
            && document.RootElement.TryGetProperty("servers", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Select(server => server.TryGetProperty("url", out var url) ? url.GetString() : null)
                .OfType<string>().Where(url => url != "/").ToList()
            : [];

        var credentials = (environment?.Profiles ?? []).ToDictionary(profile => profile.Name, profile =>
        {
            var config = CredentialConfig.From(profile.PublicConfig);
            return new Credential(profile.AuthType, config.Label, config.Hint, config.Username, config.ValidatedAt,
                config.Valid, config.Validation);
        });
        var schemes = (await SchemeInfo.ForProjectAsync(db, id, cancellationToken))
            .Select(scheme => new Scheme(scheme.Name, true, scheme.Type, scheme.HttpScheme, scheme.In, scheme.ParameterName,
                scheme.Operations, credentials.GetValueOrDefault(scheme.Name)))
            .ToList();
        schemes.AddRange(credentials.Where(pair => schemes.All(scheme => scheme.Name != pair.Key))
            .Select(pair => new Scheme(pair.Key, false, "", null, null, null, [], pair.Value)));

        return TypedResults.Ok(new Response(
            environment?.Target,
            servers,
            schemes,
            (environment?.Headers ?? []).Select(header => new Header(header.Id, header.Name, header.Value, header.Secret,
                header.Hint, header.AppliesTo.Deserialize<List<string>>() ?? [])).ToList()));
    }
}
