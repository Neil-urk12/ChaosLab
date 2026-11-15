using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.TestRuns;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Projects;

// Query: one project's dashboard. Non-members get 404 so project ids are not disclosed.
public static class GetProject
{
    public sealed record Response(
        Guid Id,
        string Name,
        string? Description,
        EnvironmentInfo? DefaultEnvironment,
        int EndpointCount,
        int ScenarioCount,
        int CompletedRunCount,
        int FailedFindingCount,
        LatestScore? Latest,
        SpecInfo? Spec,
        List<MemberRow> Members,
        List<FindingRow> Findings,
        List<TestRunRow> RecentRuns);

    public sealed record EnvironmentInfo(string Name, string BaseUrl);

    public sealed record LatestScore(
        Guid RunId, DateTimeOffset CompletedAt, decimal Score, decimal? PreviousScore,
        // The latest run's stored category scores; null for a run scored before they were stored.
        List<Scorer.CategoryScore>? Breakdown);

    public sealed record SpecInfo(string? Title, string OpenApiVersion, DateTimeOffset ImportedAt);

    public sealed record MemberRow(Guid UserId, string DisplayName, ProjectRole Role);

    public static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var project = await db.Projects.AsNoTracking()
            .Where(project => project.Id == id && project.ArchivedAt == null
                && project.Members.Any(member => member.UserId == userId && member.RemovedAt == null))
            .Select(project => new
            {
                project.Name,
                project.Description,
                DefaultEnvironment = project.Environments.Where(environment => environment.IsDefault)
                    .Select(environment => new EnvironmentInfo(environment.Name, environment.BaseUrl))
                    .FirstOrDefault(),
                Spec = project.ApiSpecs.Where(spec => spec.IsActive)
                    .Select(spec => new SpecInfo(spec.Title, spec.OpenApiVersion, spec.CreatedAt))
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (project is null)
            return TypedResults.NotFound();

        var runs = db.TestRuns.AsNoTracking().Where(run => run.ProjectId == id);
        var scored = await runs.Where(run => run.CompletedAt != null && run.ResilienceScore != null)
            .OrderByDescending(run => run.CompletedAt).Take(2)
            .Select(run => new { run.Id, CompletedAt = run.CompletedAt!.Value, Score = run.ResilienceScore!.Value, run.CategoryScores })
            .ToListAsync(cancellationToken);
        var latestRunId = scored.FirstOrDefault()?.Id;
        var findings = db.ResilienceFindings.AsNoTracking()
            .Where(finding => finding.TestRunId == latestRunId && finding.Outcome == ResilienceFindingOutcome.Failed);
        var activeEndpoints = db.ApiEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.ApiSpec.ProjectId == id && endpoint.ApiSpec.IsActive);

        var latest = scored.Count == 0 ? null : new LatestScore(scored[0].Id, scored[0].CompletedAt, scored[0].Score,
            scored.Count > 1 ? scored[1].Score : null,
            scored[0].CategoryScores?.Deserialize<List<Scorer.CategoryScore>>(JsonSerializerOptions.Web));

        return TypedResults.Ok(new Response(
            id,
            project.Name,
            project.Description,
            project.DefaultEnvironment,
            await activeEndpoints.CountAsync(cancellationToken),
            await activeEndpoints.SelectMany(endpoint => endpoint.ChaosScenarios).CountAsync(cancellationToken),
            await runs.CountAsync(run => run.Status == TestRunStatus.Completed
                || run.Status == TestRunStatus.PartiallyCompleted, cancellationToken),
            await findings.CountAsync(cancellationToken),
            latest,
            project.Spec,
            await db.ProjectMembers.AsNoTracking()
                .Where(member => member.ProjectId == id && member.RemovedAt == null)
                .OrderBy(member => member.Role).ThenBy(member => member.JoinedAt)
                .Select(member => new MemberRow(member.UserId, member.User.DisplayName, member.Role))
                .ToListAsync(cancellationToken),
            await findings.OrderByDescending(finding => finding.CreatedAt).Take(4)
                .Select(FindingRow.FromFinding).ToListAsync(cancellationToken),
            await runs.OrderByDescending(run => run.CreatedAt).Take(5)
                .Select(TestRunRow.FromRun).ToListAsync(cancellationToken)));
    }
}
