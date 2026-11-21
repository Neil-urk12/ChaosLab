using System.Security.Claims;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Reports;

// Query: downloads one stored report PDF.
public static class GetReportFile
{
    public static async Task<Results<PhysicalFileHttpResult, NotFound>> Handle(
        Guid id, Guid runId, Guid reportId, ClaimsPrincipal principal, ApplicationDbContext db, ReportStorage storage,
        CancellationToken cancellationToken)
    {
        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is null)
            return TypedResults.NotFound();

        var report = await db.Reports.AsNoTracking()
            .Where(report => report.Id == reportId && report.TestRunId == runId && report.TestRun.ProjectId == id)
            .Select(report => new { report.StorageKey, report.FileName })
            .SingleOrDefaultAsync(cancellationToken);
        // A row whose file is gone (volume lost or replaced) is reported as missing rather than failing.
        if (report is null || !File.Exists(storage.PathFor(report.StorageKey)))
            return TypedResults.NotFound();

        return TypedResults.PhysicalFile(storage.PathFor(report.StorageKey), "application/pdf", report.FileName);
    }
}
