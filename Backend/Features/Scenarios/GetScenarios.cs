using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Scenarios;

// Query: generated scenarios of the active spec's included endpoints.
public static class GetScenarios
{
    public sealed record Response(int EndpointCount, List<Row> Scenarios);

    public sealed record Row(
        Guid Id, Guid EndpointId, string HttpMethod, string Path, string? OperationId, ScenarioType Type,
        JsonElement Parameters, string Reason, bool Enabled, JsonElement Invariants);

    public static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is null)
            return TypedResults.NotFound();
        if (!await db.ApiSpecs.AnyAsync(spec => spec.ProjectId == id && spec.IsActive, cancellationToken))
            return TypedResults.NotFound();

        var included = db.ApiEndpoints.Where(endpoint => endpoint.ApiSpec.ProjectId == id && endpoint.ApiSpec.IsActive
            && endpoint.Testing == EndpointTesting.Included);
        var rows = await included
            .SelectMany(endpoint => endpoint.ChaosScenarios)
            .OrderBy(scenario => scenario.Endpoint.Path).ThenBy(scenario => scenario.Endpoint.HttpMethod)
            .ThenBy(scenario => scenario.ScenarioType)
            .Select(scenario => new
            {
                scenario.Id, scenario.EndpointId, scenario.Endpoint.HttpMethod, scenario.Endpoint.Path,
                scenario.Endpoint.OperationId, scenario.ScenarioType, scenario.Parameters, scenario.Reason,
                scenario.IsDefaultEnabled, scenario.Invariants
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            await included.CountAsync(cancellationToken),
            rows.Select(row => new Row(row.Id, row.EndpointId, row.HttpMethod, row.Path, row.OperationId, row.ScenarioType,
                row.Parameters.RootElement.Clone(), row.Reason, row.IsDefaultEnabled, row.Invariants.RootElement.Clone())).ToList()));
    }
}
