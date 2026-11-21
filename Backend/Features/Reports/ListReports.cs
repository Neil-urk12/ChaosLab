using System.Security.Claims;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Reports;

// Query: a run's stored reports, newest version first. Any project member may list and download them.
public static class ListReports
{
    public sealed record Row(Guid Id, int Version, string FileName, DateTimeOffset GeneratedAt, string? GeneratedBy);

    public static async Task<Results<Ok<List<Row>>, NotFound>> Handle(
        Guid id, Guid runId, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is null
            || !await db.TestRuns.AnyAsync(run => run.Id == runId && run.ProjectId == id, cancellationToken))
            return TypedResults.NotFound();

        return TypedResults.Ok(await db.Reports.AsNoTracking()
            .Where(report => report.TestRunId == runId)
            .OrderByDescending(report => report.Version)
            .Select(report => new Row(report.Id, report.Version, report.FileName, report.GeneratedAt,
                report.GeneratedBy == null ? null : report.GeneratedBy.DisplayName))
            .ToListAsync(cancellationToken));
    }
}
