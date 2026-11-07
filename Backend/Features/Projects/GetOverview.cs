using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Projects;

// Query: workspace dashboard across every project the caller is an active member of.
// A project's current findings are the failed findings of its latest scored run.
public static class GetOverview
{
    public sealed record Response(
        int ProjectCount,
        int EndpointCount,
        int RunsLast7Days,
        int RunsPrevious7Days,
        decimal? AverageScore,
        decimal? PreviousAverageScore,
        int ScoredRunsLast30Days,
        int FailedFindingCount,
        List<ScorePoint> ScoreTrend,
        List<FindingRow> Findings,
        List<TestRunRow> RecentRuns,
        List<ProjectRow> RecentProjects);

    public sealed record ScorePoint(DateTimeOffset CompletedAt, decimal Score);

    public sealed record ProjectRow(
        Guid Id, string Name, string? Description, string? BaseUrl,
        decimal? LatestScore, DateTimeOffset? LastRunAt, int FailedFindings);

    public static async Task<Response> Handle(
        ClaimsPrincipal principal, ApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = time.GetUtcNow();

        var projects = db.Projects.AsNoTracking().Where(project => project.ArchivedAt == null
            && project.Members.Any(member => member.UserId == userId && member.RemovedAt == null));
        var projectIds = await projects.Select(project => project.Id).ToListAsync(cancellationToken);
        var runs = db.TestRuns.AsNoTracking().Where(run => projectIds.Contains(run.ProjectId));
        var scoredRuns = runs.Where(run => run.CompletedAt != null && run.ResilienceScore != null);

        var latestRunIds = await projects
            .Select(project => project.TestRuns
                .Where(run => run.CompletedAt != null && run.ResilienceScore != null)
                .OrderByDescending(run => run.CompletedAt).Select(run => (Guid?)run.Id).FirstOrDefault())
            .Where(id => id != null).Select(id => id!.Value)
            .ToListAsync(cancellationToken);
        var findings = db.ResilienceFindings.AsNoTracking()
            .Where(finding => latestRunIds.Contains(finding.TestRunId) && finding.Outcome == ResilienceFindingOutcome.Failed);

        var trend = await scoredRuns.Where(run => run.CompletedAt >= now.AddDays(-30))
            .OrderBy(run => run.CompletedAt)
            .Select(run => new ScorePoint(run.CompletedAt!.Value, run.ResilienceScore!.Value))
            .ToListAsync(cancellationToken);

        return new Response(
            projectIds.Count,
            await db.ApiEndpoints.CountAsync(endpoint => endpoint.ApiSpec.IsActive
                && projectIds.Contains(endpoint.ApiSpec.ProjectId), cancellationToken),
            await runs.CountAsync(run => run.CreatedAt >= now.AddDays(-7), cancellationToken),
            await runs.CountAsync(run => run.CreatedAt >= now.AddDays(-14) && run.CreatedAt < now.AddDays(-7),
                cancellationToken),
            trend.Count == 0 ? null : Math.Round(trend.Average(point => point.Score), 2),
            await scoredRuns.Where(run => run.CompletedAt >= now.AddDays(-60) && run.CompletedAt < now.AddDays(-30))
                .AverageAsync(run => run.ResilienceScore, cancellationToken),
            trend.Count,
            await findings.CountAsync(cancellationToken),
            trend,
            await findings.OrderByDescending(finding => finding.CreatedAt).Take(4)
                .Select(FindingRow.FromFinding).ToListAsync(cancellationToken),
            await runs.OrderByDescending(run => run.CreatedAt).Take(5)
                .Select(TestRunRow.FromRun).ToListAsync(cancellationToken),
            await projects.OrderByDescending(project => project.CreatedAt).Take(3)
                .Select(project => new
                {
                    project.Id,
                    project.Name,
                    project.Description,
                    BaseUrl = project.Environments.Where(environment => environment.IsDefault)
                        .Select(environment => environment.BaseUrl).FirstOrDefault(),
                    Latest = project.TestRuns
                        .Where(run => run.CompletedAt != null && run.ResilienceScore != null)
                        .OrderByDescending(run => run.CompletedAt)
                        .Select(run => new
                        {
                            run.ResilienceScore,
                            run.CompletedAt,
                            Failed = run.ResilienceFindings.Count(finding =>
                                finding.Outcome == ResilienceFindingOutcome.Failed)
                        })
                        .FirstOrDefault()
                })
                .Select(row => new ProjectRow(row.Id, row.Name, row.Description, row.BaseUrl,
                    row.Latest == null ? null : row.Latest.ResilienceScore,
                    row.Latest == null ? null : row.Latest.CompletedAt,
                    row.Latest == null ? 0 : row.Latest.Failed))
                .ToListAsync(cancellationToken));
    }
}
