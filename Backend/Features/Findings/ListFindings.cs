using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Findings;

// Query: every finding of one run, with its linked recommendation. Without a run it picks the newest completed run
// that has findings, else the newest run with any. The page filters client-side; a run has at most a few hundred findings.
public static class ListFindings
{
    // Newest runs offered in the run picker.
    public const int MaxRuns = 50;

    public sealed record Response(List<RunOption> Runs, RunOption? Run, List<Row> Findings);

    // Number matches the run history numbering (per-project creation order).
    public sealed record RunOption(Guid Id, int Number, TestRunStatus Status, DateTimeOffset? CompletedAt);

    public sealed record Row(
        Guid Id,
        RecommendationSeverity Severity,
        ResilienceFindingOutcome Outcome,
        ResiliencePattern Pattern,
        string HttpMethod,
        string Path,
        // Fault of the scenario execution that produced the finding; null when the finding has no execution.
        ScenarioType? ScenarioType,
        string Summary,
        JsonElement Evidence,
        RecommendationInfo? Recommendation);

    public sealed record RecommendationInfo(string Title, string Text);

    public static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken, Guid? run = null)
    {
        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is null)
            return TypedResults.NotFound();

        var withFindings = db.TestRuns.AsNoTracking()
            .Where(testRun => testRun.ProjectId == id && testRun.ResilienceFindings.Any());
        IQueryable<RunOption> Options(IQueryable<Domain.Entities.TestRun> source) => source.Select(testRun => new RunOption(
            testRun.Id,
            db.TestRuns.Count(other => other.ProjectId == id && (other.CreatedAt < testRun.CreatedAt
                || (other.CreatedAt == testRun.CreatedAt && other.Id.CompareTo(testRun.Id) <= 0))),
            testRun.Status,
            testRun.CompletedAt));

        var runs = await Options(withFindings
                .OrderByDescending(testRun => testRun.CreatedAt).ThenByDescending(testRun => testRun.Id)
                .Take(MaxRuns))
            .ToListAsync(cancellationToken);

        // A requested run is looked up directly, so runs older than the picker's MaxRuns stay reachable by link.
        var selected = run is null
            ? runs.FirstOrDefault(option => option.Status == TestRunStatus.Completed) ?? runs.FirstOrDefault()
            : runs.FirstOrDefault(option => option.Id == run)
                ?? await Options(withFindings.Where(testRun => testRun.Id == run)).SingleOrDefaultAsync(cancellationToken);
        // Another project's run, or one without findings, is not served.
        if (run is not null && selected is null)
            return TypedResults.NotFound();
        if (selected is null)
            return TypedResults.Ok(new Response(runs, null, []));

        var rows = await db.ResilienceFindings.AsNoTracking()
            .Where(finding => finding.TestRunId == selected.Id)
            .OrderByDescending(finding => finding.Severity).ThenBy(finding => finding.CreatedAt).ThenBy(finding => finding.Id)
            .Select(finding => new
            {
                finding.Id, finding.Severity, finding.Outcome, finding.Pattern, finding.Endpoint.HttpMethod,
                finding.Endpoint.Path, ScenarioType = (ScenarioType?)finding.ScenarioExecution!.ScenarioType,
                finding.Summary, finding.Evidence,
                Recommendation = db.Recommendations
                    .Where(recommendation => recommendation.ResilienceFindingId == finding.Id)
                    .OrderByDescending(recommendation => recommendation.Severity)
                    .Select(recommendation => new RecommendationInfo(
                        recommendation.Title, recommendation.Remediation ?? recommendation.Description))
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new Response(runs, selected, rows.Select(row => new Row(
            row.Id, row.Severity, row.Outcome, row.Pattern, row.HttpMethod, row.Path, row.ScenarioType, row.Summary,
            row.Evidence.RootElement.Clone(), row.Recommendation)).ToList()));
    }
}
