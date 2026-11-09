using System.Security.Claims;
using System.Text.RegularExpressions;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.TestRuns;

// Query: one page of a project's run history, newest first, filtered by run ID or person, status,
// environment and age (days back from now, by start time). Out-of-range pages return the last page.
// Run numbers are per-project creation order; runs are never deleted, so the numbers are stable.
public static partial class ListRuns
{
    public const int MaxPageSize = 100;

    public sealed record Response(
        // All runs in the project, ignoring filters.
        int Total,
        int Matched,
        int Page,
        int PageSize,
        List<string> Environments,
        List<string> ScoringVersions,
        List<Row> Runs);

    public sealed record Row(
        Guid Id,
        int Number,
        string EnvironmentName,
        TestRunStatus Status,
        int CompletedScenarios,
        int TotalScenarios,
        string RequestedBy,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        decimal? Score,
        // Score of the previous completed, scored run on the same environment and scoring version, for the change
        // shown next to the score. Scores from other environments or versions aren't comparable.
        decimal? PreviousScore,
        string ScoringVersion,
        string? FailureMessage,
        // Scored findings (Passed/Failed) as a percentage of scored plus Inconclusive; null with no such findings.
        int? EvidenceCoverage);

    // "RUN-027", "run27" or "27" searches by run number.
    [GeneratedRegex(@"^(?:run-?)?0*(\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex RunNumber();

    public static async Task<Results<Ok<Response>, NotFound, ValidationProblem>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken,
        string? query = null, TestRunStatus? status = null, string? environment = null, int? days = null,
        int page = 1, int pageSize = 10)
    {
        var errors = new Dictionary<string, string[]>();
        if (days is < 1) errors["days"] = ["Must be at least 1."];
        if (page < 1) errors["page"] = ["Must be at least 1."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is null)
            return TypedResults.NotFound();

        var runs = db.TestRuns.AsNoTracking().Where(run => run.ProjectId == id);
        var numbered = runs.Select(run => new
        {
            Run = run,
            Number = db.TestRuns.Count(other => other.ProjectId == id && (other.CreatedAt < run.CreatedAt
                || (other.CreatedAt == run.CreatedAt && other.Id.CompareTo(run.Id) <= 0)))
        });

        if (status is not null)
            numbered = numbered.Where(row => row.Run.Status == status);
        if (!string.IsNullOrWhiteSpace(environment))
            numbered = numbered.Where(row => row.Run.Environment.Name == environment);
        if (days is not null)
        {
            var since = time.GetUtcNow().AddDays(-days.Value);
            numbered = numbered.Where(row => (row.Run.StartedAt ?? row.Run.CreatedAt) >= since);
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            // Invariant, not the server culture: a Turkish culture would turn "I" into "ı" and miss names PostgreSQL lowercases to "i".
            var text = query.Trim().ToLowerInvariant();
            var match = RunNumber().Match(text);
            // Only a run-number search filters on Number: it is a per-run count subquery, too costly to run on every search.
            if (match.Success)
            {
                var number = int.Parse(match.Groups[1].Value);
                numbered = numbered.Where(row => row.Number == number || row.Run.RequestedBy.DisplayName.ToLower().Contains(text));
            }
            else
                numbered = numbered.Where(row => row.Run.RequestedBy.DisplayName.ToLower().Contains(text));
        }

        var matched = await numbered.CountAsync(cancellationToken);
        var lastPage = Math.Max(1, (matched + pageSize - 1) / pageSize);
        page = Math.Min(page, lastPage);

        var rows = await numbered
            .OrderByDescending(row => row.Run.CreatedAt).ThenByDescending(row => row.Run.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new
            {
                row.Run.Id, row.Number, EnvironmentName = row.Run.Environment.Name, row.Run.Status,
                row.Run.CompletedScenarios, row.Run.TotalScenarios, RequestedBy = row.Run.RequestedBy.DisplayName,
                row.Run.CreatedAt, row.Run.StartedAt, row.Run.CompletedAt, row.Run.ResilienceScore,
                PreviousScore = row.Run.ResilienceScore == null || row.Run.CompletedAt == null
                    ? null
                    : db.TestRuns
                        .Where(other => other.ProjectId == id && other.ResilienceScore != null
                            && other.CompletedAt < row.Run.CompletedAt && other.Status == TestRunStatus.Completed
                            && other.EnvironmentId == row.Run.EnvironmentId && other.ScoringVersion == row.Run.ScoringVersion)
                        .OrderByDescending(other => other.CompletedAt)
                        .Select(other => other.ResilienceScore).FirstOrDefault(),
                row.Run.ScoringVersion, row.Run.FailureMessage,
                Scored = row.Run.ResilienceFindings.Count(finding => finding.Outcome == ResilienceFindingOutcome.Passed
                    || finding.Outcome == ResilienceFindingOutcome.Failed),
                Inconclusive = row.Run.ResilienceFindings.Count(finding =>
                    finding.Outcome == ResilienceFindingOutcome.Inconclusive)
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new Response(
            await runs.CountAsync(cancellationToken),
            matched,
            page,
            pageSize,
            await runs.Select(run => run.Environment.Name).Distinct().OrderBy(name => name).ToListAsync(cancellationToken),
            await runs.Select(run => run.ScoringVersion).Distinct().OrderBy(version => version).ToListAsync(cancellationToken),
            rows.Select(run => new Row(
                run.Id, run.Number, run.EnvironmentName, run.Status, run.CompletedScenarios, run.TotalScenarios,
                run.RequestedBy, run.CreatedAt, run.StartedAt, run.CompletedAt, run.ResilienceScore, run.PreviousScore,
                run.ScoringVersion, run.FailureMessage,
                run.Scored + run.Inconclusive == 0
                    ? null
                    : (int)Math.Round(100m * run.Scored / (run.Scored + run.Inconclusive)))).ToList()));
    }
}
