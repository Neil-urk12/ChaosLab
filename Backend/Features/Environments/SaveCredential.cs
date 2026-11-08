using System.Security.Claims;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Backend.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Features.Environments;

// Command: sets the test credential for one of the spec's security schemes. Secret may be omitted to keep the
// stored one when only the label or username changes; a new secret clears the last validation.
public static class SaveCredential
{
    public sealed record Request(AuthType? AuthType, string? Secret, string? Username, string? Label);

    public static async Task<Results<NoContent, ValidationProblem, NotFound, ForbidHttpResult, Conflict<string>>> Handle(
        Guid id, string scheme, Request request, ClaimsPrincipal principal, ApplicationDbContext db,
        CredentialProtector protector, TimeProvider time, CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var environment = await db.ApiEnvironments.Include(environment => environment.AuthProfiles)
            .SingleOrDefaultAsync(environment => environment.ProjectId == id && environment.IsDefault, cancellationToken);
        if (environment is null)
            return TypedResults.Conflict("Save a target API before adding credentials.");
        var profile = environment.AuthProfiles.SingleOrDefault(profile => profile.Name == scheme);
        if (profile is null && (await SchemeInfo.ForProjectAsync(db, id, cancellationToken)).All(s => s.Name != scheme))
            return TypedResults.NotFound();

        var type = request.AuthType;
        var secret = request.Secret;
        var username = request.Username?.Trim();
        var label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
        var keepSecret = string.IsNullOrEmpty(secret) && profile is { SecretCipherText: not null } && profile.AuthType == type;
        var errors = new Dictionary<string, string[]>();
        if (type is null || !Enum.IsDefined(type.Value))
            errors["authType"] = ["Choose None, Bearer, ApiKey, or Basic."];
        else if (type != AuthType.None && string.IsNullOrEmpty(secret) && !keepSecret)
            errors["secret"] = [type == AuthType.Basic ? "Enter a password." : "Enter the secret."];
        if (secret is { Length: > 8192 } || secret?.Any(char.IsControl) == true)
            errors["secret"] = ["The secret must be at most 8192 characters with no line breaks."];
        // Bearer tokens and API keys travel in header values, which must be ASCII; Basic is base64-encoded first.
        else if (type is AuthType.Bearer or AuthType.ApiKey && secret?.Any(c => c > '~') == true)
            errors["secret"] = ["Tokens and API keys can only contain ASCII characters."];
        if (type == AuthType.Basic && (string.IsNullOrEmpty(username) || username.Length > 256 || username.Contains(':')))
            errors["username"] = ["Enter a username of at most 256 characters without a colon."];
        if (label is { Length: > 100 })
            errors["label"] = ["The label must be at most 100 characters."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var previous = profile is null ? null : CredentialConfig.From(profile.PublicConfig);
        if (profile is null)
        {
            profile = new AuthProfile { Id = Guid.NewGuid(), EnvironmentId = environment.Id, Name = scheme };
            db.AuthProfiles.Add(profile);
        }
        var changed = profile.AuthType != type || !keepSecret || previous?.Username != username;
        profile.AuthType = type!.Value;
        if (type == AuthType.None)
            profile.SecretCipherText = null;
        else if (!keepSecret)
            profile.SecretCipherText = protector.Protect(secret!, profile.Id);
        profile.PublicConfig = new CredentialConfig(
            label,
            type == AuthType.None ? null : keepSecret ? previous!.Hint : CredentialProtector.Hint(secret!),
            type == AuthType.Basic ? username : null,
            changed ? null : previous?.ValidatedAt,
            changed ? null : previous?.Valid,
            changed ? null : previous?.Validation).ToDocument();
        profile.UpdatedAt = time.GetUtcNow();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict("Another save added this credential at the same time. Reload and try again.");
        }
        return TypedResults.NoContent();
    }
}
