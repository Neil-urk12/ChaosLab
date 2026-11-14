using System.Security.Claims;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Environments;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.TestRuns;

// Query: the checks shown before a run is configured (FeaturesFlow 2.15 step 3). It probes the default
// environment's base URL and reports each security scheme's last credential validation without re-validating.
// Blockers lists what stops a run from starting; credential problems are warnings only, since a stored
// validation can be stale and some operations need no credential.
public static class GetPreflight
{
    public sealed record Response(
        Target? Target, Reachability? Reachability, List<CredentialCheck> Credentials, Spec? Spec,
        string ScoringVersion, List<string> Blockers);

    public sealed record Target(string Name, string BaseUrl, EnvironmentKind Kind, bool Authorized);

    public sealed record Reachability(bool Reachable, int? Status, string? Error, long ElapsedMs);

    public sealed record CredentialCheck(
        string Scheme, bool Configured, bool? Valid, string? Validation, DateTimeOffset? ValidatedAt);

    public sealed record Spec(string OpenApiVersion, int OperationCount, int NeverTestCount);

    public static async Task<Results<Ok<Response>, NotFound, ForbidHttpResult>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        // Viewers cannot start runs or make the server send requests.
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var environment = await db.ApiEnvironments.AsNoTracking().Include(environment => environment.AuthProfiles)
            .SingleOrDefaultAsync(environment => environment.ProjectId == id && environment.IsDefault, cancellationToken);
        var spec = await db.ApiSpecs.AsNoTracking().Where(spec => spec.ProjectId == id && spec.IsActive)
            .Select(spec => new Spec(spec.OpenApiVersion, spec.Endpoints.Count,
                spec.Endpoints.Count(endpoint => endpoint.Testing == EndpointTesting.NeverTest)))
            .SingleOrDefaultAsync(cancellationToken);

        var reachability = environment is null ? null : await ProbeAsync(environment, httpClientFactory, cancellationToken);

        // Only schemes that an included operation uses matter for the run.
        var included = await db.ApiEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.ApiSpec.ProjectId == id && endpoint.ApiSpec.IsActive && endpoint.Testing == EndpointTesting.Included)
            .Select(endpoint => endpoint.HttpMethod + " " + endpoint.Path)
            .ToListAsync(cancellationToken);
        var credentials = (await SchemeInfo.ForProjectAsync(db, id, cancellationToken))
            .Where(scheme => scheme.Operations.Any(included.Contains))
            .Select(scheme =>
            {
                var profile = environment?.AuthProfiles.SingleOrDefault(profile => profile.Name == scheme.Name);
                if (profile is null || profile.AuthType == AuthType.None || profile.SecretCipherText is null)
                    return new CredentialCheck(scheme.Name, false, null, null, null);
                var config = CredentialConfig.From(profile.PublicConfig);
                return new CredentialCheck(scheme.Name, true, config.Valid, config.Validation, config.ValidatedAt);
            })
            .ToList();

        return TypedResults.Ok(new Response(
            environment is null ? null : new Target(environment.Name, environment.BaseUrl, environment.Kind,
                environment.AuthorizationConfirmedAt is not null),
            reachability, credentials, spec, CreateRun.ScoringVersion,
            Blockers(environment, reachability, spec is not null, included.Count > 0)));
    }

    // Problems that stop a run from being created; CreateRun re-checks them.
    public static List<string> Blockers(ApiEnvironment? environment, Reachability? reachability, bool hasSpec, bool hasIncluded)
    {
        var blockers = new List<string>();
        if (!hasSpec)
            blockers.Add("Import an OpenAPI specification first.");
        else if (!hasIncluded)
            blockers.Add("No endpoints are included. Include some on the API specification tab.");
        if (environment is null)
            blockers.Add("Configure a target environment in Settings first.");
        else if (environment.AuthorizationConfirmedAt is null)
            blockers.Add("Confirm in Settings that you may test this target and that it is not production.");
        else if (reachability is { Reachable: false })
            blockers.Add($"The target is unreachable: {reachability.Error ?? "no response"}");
        return blockers;
    }

    // One GET to the base URL, like CheckConnection. Any HTTP response counts as reachable.
    // Capped at 30 s like CheckConnection: the target's own timeout (up to 300 s) would leave the page waiting.
    public static async Task<Reachability> ProbeAsync(
        ApiEnvironment environment, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
    {
        if (TargetClient.ValidateBaseUrl(environment.BaseUrl, out var url) is { } error)
            return new Reachability(false, null, error, 0);
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        var probe = await TargetClient.SendAsync(httpClientFactory, message, environment.VerifyTls,
            Math.Min(environment.RequestTimeoutSeconds, 30), cancellationToken);
        return new Reachability(probe.Status is not null, probe.Status, probe.Error, probe.ElapsedMs);
    }
}
