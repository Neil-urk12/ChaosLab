using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Features.Scenarios;

// Command: generates scenarios for the active spec on the user's request, or re-applies the rules, keeping edits to
// scenarios that still apply. Import never generates; this is the only way scenarios are created.
public static class GenerateScenarios
{
    public sealed record Response(int Added, int Removed);

    public static async Task<Results<Ok<Response>, NotFound, ForbidHttpResult, Conflict>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();
        var specId = await db.ApiSpecs.Where(spec => spec.ProjectId == id && spec.IsActive)
            .Select(spec => (Guid?)spec.Id).SingleOrDefaultAsync(cancellationToken);
        if (specId is null)
            return TypedResults.NotFound();
        // After a re-import the new spec starts empty, so edits come from the latest earlier spec that had scenarios.
        var previousSpecId = await db.ApiSpecs
            .Where(spec => spec.ProjectId == id && !spec.IsActive && spec.Endpoints.Any(endpoint => endpoint.ChaosScenarios.Any()))
            .OrderByDescending(spec => spec.CreatedAt)
            .Select(spec => (Guid?)spec.Id).FirstOrDefaultAsync(cancellationToken);

        try
        {
            var (added, removed) = await ScenarioGenerator.SyncAsync(db, specId.Value, previousSpecId, cancellationToken);
            return TypedResults.Ok(new Response(added, removed));
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent generate added or removed the same scenarios first.
            return TypedResults.Conflict();
        }
    }
}
