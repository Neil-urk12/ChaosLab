using System.Security.Claims;
using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Projects;

// Query: read-only, untracked, projected straight into the response DTO.
public static class ListProjects
{
    // Extends ProjectResponse's fields with the inventory columns; the latest run is the latest scored one.
    public sealed record Row(
        Guid Id,
        string Name,
        string? Description,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? EnvironmentName,
        string? BaseUrl,
        int ScenarioCount,
        DateTimeOffset? LastRunAt,
        decimal? LatestScore);

    public static async Task<List<Row>> Handle(
        ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await db.Projects.AsNoTracking()
            .Where(project => project.ArchivedAt == null
                && project.Members.Any(member => member.UserId == userId && member.RemovedAt == null))
            .OrderByDescending(project => project.CreatedAt)
            .Select(project => new
            {
                project,
                Environment = project.Environments.Where(environment => environment.IsDefault)
                    .Select(environment => new { environment.Name, environment.BaseUrl }).FirstOrDefault(),
                ScenarioCount = project.ApiSpecs.Where(spec => spec.IsActive)
                    .SelectMany(spec => spec.Endpoints).SelectMany(endpoint => endpoint.ChaosScenarios).Count(),
                Latest = project.TestRuns
                    .Where(run => run.CompletedAt != null && run.ResilienceScore != null)
                    .OrderByDescending(run => run.CompletedAt)
                    .Select(run => new { run.CompletedAt, run.ResilienceScore })
                    .FirstOrDefault()
            })
            .Select(row => new Row(
                row.project.Id, row.project.Name, row.project.Description,
                row.project.CreatedAt, row.project.UpdatedAt,
                row.Environment == null ? null : row.Environment.Name,
                row.Environment == null ? null : row.Environment.BaseUrl,
                row.ScenarioCount,
                row.Latest == null ? null : row.Latest.CompletedAt,
                row.Latest == null ? null : row.Latest.ResilienceScore))
            .ToListAsync(cancellationToken);
    }
}
