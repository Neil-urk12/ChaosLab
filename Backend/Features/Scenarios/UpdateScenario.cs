using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Scenarios;

// Command: changes a scenario's default, parameters, or invariants. Omitted fields stay as they are.
public static class UpdateScenario
{
    public sealed record Request(bool? Enabled, JsonElement? Parameters, List<Invariant>? Invariants);

    public static async Task<Results<NoContent, ValidationProblem, NotFound, ForbidHttpResult>> Handle(
        Guid id, Guid scenarioId, Request request, ClaimsPrincipal principal, ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var scenario = await db.ChaosScenarios.SingleOrDefaultAsync(scenario => scenario.Id == scenarioId
            && scenario.Endpoint.ApiSpec.ProjectId == id && scenario.Endpoint.ApiSpec.IsActive, cancellationToken);
        if (scenario is null)
            return TypedResults.NotFound();

        if (request.Parameters is { } parameters)
        {
            if (ScenarioRules.ValidateParameters(scenario.ScenarioType, parameters) is { } error)
                return Invalid("parameters", error);
            scenario.Parameters = JsonDocument.Parse(parameters.GetRawText());
        }
        if (request.Invariants is { } invariants)
        {
            if (ScenarioRules.ValidateInvariants(scenario.ScenarioType, invariants) is { } error)
                return Invalid("invariants", error);
            scenario.Invariants = ScenarioRules.Normalize(invariants);
        }
        if (request.Enabled is { } enabled)
            scenario.IsDefaultEnabled = enabled;

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
