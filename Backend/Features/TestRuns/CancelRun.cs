using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.TestRuns;

// Command: cancels a queued or running run (FeaturesFlow 2.17). Evidence collected so far is kept; the runner must
// re-read the run status before starting each scenario and let in-flight requests finish or time out.
// Executions that never started are left as they are here; the runner marks them Skipped when it sees the run is Cancelled.
public static class CancelRun
{
    public const string FinishedMessage = "This run has already finished.";

    public static async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<string>>> Handle(
        Guid id, Guid runId, ClaimsPrincipal principal, ApplicationDbContext db, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        // Conditional update so a run the runner finishes at the same moment is never overwritten.
        var now = time.GetUtcNow();
        var cancelled = await db.TestRuns
            .Where(run => run.Id == runId && run.ProjectId == id
                && (run.Status == TestRunStatus.Queued || run.Status == TestRunStatus.Running))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, TestRunStatus.Cancelled)
                .SetProperty(run => run.CompletedAt, now), cancellationToken);
        if (cancelled == 1)
            return TypedResults.NoContent();

        return await db.TestRuns.AnyAsync(run => run.Id == runId && run.ProjectId == id, cancellationToken)
            ? TypedResults.Conflict(FinishedMessage)
            : TypedResults.NotFound();
    }
}
