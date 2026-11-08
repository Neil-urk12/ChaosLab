using System.Security.Claims;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Features.Environments;

// Command: creates or updates the project's default target. The caller must confirm they own the API or may
// test it, and that it is not production; the confirmation is recorded again whenever the base URL changes.
public static class SaveTarget
{
    public sealed record Request(string? BaseUrl, EnvironmentKind? Kind, int? RequestTimeoutSeconds, bool? VerifyTls, bool Authorized);

    public static async Task<Results<NoContent, ValidationProblem, NotFound, ForbidHttpResult, Conflict<string>>> Handle(
        Guid id, Request request, ClaimsPrincipal principal, ApplicationDbContext db, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var errors = new Dictionary<string, string[]>();
        if (TargetClient.ValidateBaseUrl(request.BaseUrl, out var url) is { } urlError)
            errors["baseUrl"] = [urlError];
        if (request.Kind is null || !Enum.IsDefined(request.Kind.Value))
            errors["kind"] = ["Choose Development, Test, Staging, or Production mirror."];
        if (request.RequestTimeoutSeconds is not (>= 1 and <= 300))
            errors["requestTimeoutSeconds"] = ["The timeout budget must be from 1 to 300 seconds."];
        if (!request.Authorized)
            errors["authorized"] = ["Confirm that you own this API or may test it, and that it is not production."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = time.GetUtcNow();
        var baseUrl = url!.OriginalString.TrimEnd('/');
        var environment = await db.ApiEnvironments.Include(environment => environment.AuthProfiles)
            .Include(environment => environment.Headers)
            .SingleOrDefaultAsync(environment => environment.ProjectId == id && environment.IsDefault, cancellationToken);
        if (environment is null)
        {
            environment = new ApiEnvironment
            {
                Id = Guid.NewGuid(), ProjectId = id, IsDefault = true, CreatedById = userId, CreatedAt = now
            };
            db.ApiEnvironments.Add(environment);
        }
        // Secrets were entered for the old host. Moving the target elsewhere must not carry them along, or anyone
        // who can edit the target could point it at their own server and validate to receive them.
        if (environment.BaseUrl.Length > 0 && !SameOrigin(environment.BaseUrl, url))
        {
            foreach (var profile in environment.AuthProfiles.Where(profile => profile.SecretCipherText is not null))
            {
                var config = CredentialConfig.From(profile.PublicConfig);
                profile.SecretCipherText = null;
                profile.PublicConfig = (config with { Hint = null, ValidatedAt = null, Valid = null, Validation = null }).ToDocument();
                profile.UpdatedAt = now;
            }
            db.EnvironmentHeaders.RemoveRange(environment.Headers.Where(header => header.SecretCipherText is not null));
        }
        if (environment.AuthorizationConfirmedAt is null || environment.BaseUrl != baseUrl)
        {
            environment.AuthorizationConfirmedById = userId;
            environment.AuthorizationConfirmedAt = now;
        }
        environment.BaseUrl = baseUrl;
        environment.Kind = request.Kind!.Value;
        // The name mirrors the kind; project lists and run rows show it.
        environment.Name = environment.Kind == EnvironmentKind.ProductionMirror ? "Production mirror" : environment.Kind.ToString();
        environment.RequestTimeoutSeconds = request.RequestTimeoutSeconds!.Value;
        environment.VerifyTls = request.VerifyTls ?? true;
        environment.UpdatedAt = now;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another first save created the default target at the same time.
            return TypedResults.Conflict("Another save created this target at the same time. Reload and try again.");
        }
        return TypedResults.NoContent();
    }

    private static bool SameOrigin(string saved, Uri next) =>
        Uri.TryCreate(saved, UriKind.Absolute, out var old)
        && old.GetLeftPart(UriPartial.Authority).Equals(next.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}
