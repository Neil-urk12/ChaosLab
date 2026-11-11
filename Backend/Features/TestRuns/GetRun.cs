using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.TestRuns;

// Query: one run's results header and the failure timelines of its failing scenarios. Findings come from the
// findings query (GET /projects/{id}/findings?run=), which the results page also loads.
public static class GetRun
{
    // Failing scenario executions offered in the timeline picker.
    public const int MaxTimelines = 20;

    public sealed record Response(
        Guid Id,
        int Number,
        TestRunStatus Status,
        string EnvironmentName,
        string RequestedBy,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        int CompletedScenarios,
        int TotalScenarios,
        int SkippedScenarios,
        decimal? Score,
        string ScoringVersion,
        string? FailureMessage,
        // Previous completed, scored run on the same environment and scoring version (see ListRuns.Row.PreviousScore).
        Previous? Previous,
        List<Timeline> Timelines,
        // Set only while the run is Queued or Running.
        Live? Live);

    // Every started execution, newest first, and the oldest one still running with its requests so far.
    public sealed record Live(List<FeedRow> Feed, Current? Current);

    public sealed record FeedRow(
        Guid ExecutionId,
        string HttpMethod,
        string Path,
        ScenarioType ScenarioType,
        ScenarioExecutionStatus Status,
        ScenarioOutcome? Outcome,
        string? FailureReason,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        // Worst outcome first; empty until the evaluator has judged the execution.
        List<FeedFinding> Findings);

    public sealed record FeedFinding(ResilienceFindingOutcome Outcome, string Summary);

    public sealed record Current(Guid ExecutionId, JsonElement Parameters, JsonElement Invariants, List<Event> Events);

    public sealed record Previous(Guid Id, int Number, decimal Score);

    public sealed record Timeline(
        Guid ExecutionId,
        string HttpMethod,
        string Path,
        ScenarioType ScenarioType,
        ScenarioOutcome? Outcome,
        string? FailureReason,
        DateTimeOffset? CompletedAt,
        List<Event> Events);

    public sealed record Event(
        int AttemptNo,
        string RequestMethod,
        string RequestPath,
        DateTimeOffset StartedAt,
        long? DurationMs,
        int? ClientStatusCode,
        int? UpstreamStatusCode,
        ObservationOutcomeType OutcomeType,
        string? ErrorType);

    public static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id, Guid runId, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is null)
            return TypedResults.NotFound();

        // Same per-project creation-order numbering as ListRuns.
        var run = await db.TestRuns.AsNoTracking()
            .Where(testRun => testRun.Id == runId && testRun.ProjectId == id)
            .Select(testRun => new
            {
                testRun.Id,
                Number = db.TestRuns.Count(other => other.ProjectId == id && (other.CreatedAt < testRun.CreatedAt
                    || (other.CreatedAt == testRun.CreatedAt && other.Id.CompareTo(testRun.Id) <= 0))),
                testRun.Status, EnvironmentName = testRun.Environment.Name, RequestedBy = testRun.RequestedBy.DisplayName,
                testRun.CreatedAt, testRun.StartedAt, testRun.CompletedAt, testRun.CompletedScenarios,
                testRun.TotalScenarios, testRun.EnvironmentId,
                Skipped = testRun.ScenarioExecutions.Count(execution => execution.Status == ScenarioExecutionStatus.Skipped),
                testRun.ResilienceScore, testRun.ScoringVersion, testRun.FailureMessage
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (run is null)
            return TypedResults.NotFound();

        Previous? previous = null;
        if (run.ResilienceScore is not null && run.CompletedAt is not null)
            previous = await db.TestRuns.AsNoTracking()
                .Where(other => other.ProjectId == id && other.ResilienceScore != null
                    && other.CompletedAt < run.CompletedAt && other.Status == TestRunStatus.Completed
                    && other.EnvironmentId == run.EnvironmentId && other.ScoringVersion == run.ScoringVersion)
                .OrderByDescending(other => other.CompletedAt)
                .Select(other => new Previous(
                    other.Id,
                    db.TestRuns.Count(earlier => earlier.ProjectId == id && (earlier.CreatedAt < other.CreatedAt
                        || (earlier.CreatedAt == other.CreatedAt && earlier.Id.CompareTo(other.Id) <= 0))),
                    other.ResilienceScore!.Value))
                .FirstOrDefaultAsync(cancellationToken);

        var timelines = await db.ScenarioExecutions.AsNoTracking()
            .Where(execution => execution.TestRunId == runId && db.ResilienceFindings.Any(finding =>
                finding.ScenarioExecutionId == execution.Id
                && (finding.Outcome == ResilienceFindingOutcome.Failed || finding.Outcome == ResilienceFindingOutcome.Degraded)))
            // Worst first: the highest severity among the failing findings, then run order.
            .OrderByDescending(execution => db.ResilienceFindings
                .Where(finding => finding.ScenarioExecutionId == execution.Id
                    && (finding.Outcome == ResilienceFindingOutcome.Failed || finding.Outcome == ResilienceFindingOutcome.Degraded))
                .Max(finding => finding.Severity))
            .ThenBy(execution => execution.SequenceNo)
            .Take(MaxTimelines)
            .Select(execution => new Timeline(
                execution.Id, execution.Endpoint.HttpMethod, execution.Endpoint.Path, execution.ScenarioType,
                execution.Outcome, execution.FailureReason, execution.CompletedAt,
                execution.Observations
                    .OrderBy(observation => observation.RequestStartedAt).ThenBy(observation => observation.AttemptNo)
                    .Select(observation => new Event(
                        observation.AttemptNo, observation.RequestMethod, observation.RequestPath,
                        observation.RequestStartedAt, observation.DurationMs, observation.ClientStatusCode,
                        observation.UpstreamStatusCode, observation.OutcomeType, observation.ErrorType))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            run.Id, run.Number, run.Status, run.EnvironmentName, run.RequestedBy, run.CreatedAt, run.StartedAt,
            run.CompletedAt, run.CompletedScenarios, run.TotalScenarios, run.Skipped, run.ResilienceScore,
            run.ScoringVersion, run.FailureMessage, previous, timelines,
            run.Status is TestRunStatus.Queued or TestRunStatus.Running ? await LiveAsync(db, runId, cancellationToken) : null));
    }

    // shortcut: the feed holds every started execution of the run (bounded by the run's scenario count); page it if runs grow past a few hundred scenarios.
    private static async Task<Live> LiveAsync(ApplicationDbContext db, Guid runId, CancellationToken cancellationToken)
    {
        var feed = await db.ScenarioExecutions.AsNoTracking()
            .Where(execution => execution.TestRunId == runId && execution.Status != ScenarioExecutionStatus.Queued)
            // Never null, so an execution that was skipped before it started can't sort above newer rows (DESC puts NULLs first).
            .OrderByDescending(execution => execution.CompletedAt ?? execution.StartedAt ?? execution.CreatedAt)
            .ThenByDescending(execution => execution.SequenceNo)
            .Select(execution => new FeedRow(
                execution.Id, execution.Endpoint.HttpMethod, execution.Endpoint.Path, execution.ScenarioType,
                execution.Status, execution.Outcome, execution.FailureReason, execution.StartedAt, execution.CompletedAt,
                db.ResilienceFindings
                    .Where(finding => finding.ScenarioExecutionId == execution.Id)
                    // Enum values aren't in severity order (Degraded was appended), so rank explicitly.
                    .OrderBy(finding => finding.Outcome == ResilienceFindingOutcome.Failed ? 0
                        : finding.Outcome == ResilienceFindingOutcome.Degraded ? 1
                        : finding.Outcome == ResilienceFindingOutcome.Inconclusive ? 2
                        : finding.Outcome == ResilienceFindingOutcome.Passed ? 3 : 4)
                    .ThenByDescending(finding => finding.Severity)
                    .Select(finding => new FeedFinding(finding.Outcome, finding.Summary))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var current = await db.ScenarioExecutions.AsNoTracking()
            .Where(execution => execution.TestRunId == runId && execution.Status == ScenarioExecutionStatus.Running)
            .OrderBy(execution => execution.SequenceNo)
            .Select(execution => new
            {
                execution.Id, execution.Parameters, execution.Invariants,
                Events = execution.Observations
                    .OrderBy(observation => observation.RequestStartedAt).ThenBy(observation => observation.AttemptNo)
                    .Select(observation => new Event(
                        observation.AttemptNo, observation.RequestMethod, observation.RequestPath,
                        observation.RequestStartedAt, observation.DurationMs, observation.ClientStatusCode,
                        observation.UpstreamStatusCode, observation.OutcomeType, observation.ErrorType))
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new Live(feed, current is null ? null : new Current(
            current.Id, current.Parameters.RootElement.Clone(), current.Invariants.RootElement.Clone(), current.Events));
    }
}
