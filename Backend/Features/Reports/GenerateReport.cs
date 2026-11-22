using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Findings;
using Backend.Features.Projects;
using Backend.Features.TestRuns;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Features.Reports;

// Command: renders a finished run's PDF report and stores it as the run's next version (FeaturesFlow 2.24).
// shortcut: renders inside the request; move to a background job if reports of runs with thousands of findings get slow.
public static class GenerateReport
{
    public const string NotFinishedMessage = "Reports are available once the run has finished.";
    public const string BusyMessage = "Another report for this run was generated at the same moment. Try again.";

    public static async Task<Results<Created<ListReports.Row>, NotFound, ForbidHttpResult, Conflict<string>>> Handle(
        Guid id, Guid runId, ClaimsPrincipal principal, ApplicationDbContext db, ReportStorage storage, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var data = await LoadAsync(db, id, runId, principal, time, cancellationToken);
        if (data is null)
            return TypedResults.NotFound();
        if (data.Run.Status is TestRunStatus.Queued or TestRunStatus.Running)
            return TypedResults.Conflict(NotFinishedMessage);

        var version = await db.Reports.Where(report => report.TestRunId == runId)
            .MaxAsync(report => (int?)report.Version, cancellationToken) ?? 0;
        var report = new Report
        {
            Id = Guid.NewGuid(),
            TestRunId = runId,
            Version = version + 1,
            FileName = FileName(data.ProjectName, data.Run.Number, version + 1),
            GeneratedById = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
            GeneratedAt = data.GeneratedAt
        };
        report.StorageKey = $"{report.Id}.pdf";
        var path = storage.PathFor(report.StorageKey);

        // The file first, then the row: a row never points at a missing file, and a failed insert removes the file.
        Directory.CreateDirectory(storage.Root);
        try
        {
            await File.WriteAllBytesAsync(path, ReportDocument.Render(data), cancellationToken);
            db.Reports.Add(report);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            storage.TryDelete(path);
            return TypedResults.Conflict(BusyMessage);
        }
        catch
        {
            storage.TryDelete(path);
            throw;
        }

        return TypedResults.Created($"/api/projects/{id}/runs/{runId}/reports/{report.Id}/file",
            new ListReports.Row(report.Id, report.Version, report.FileName, report.GeneratedAt, data.GeneratedBy));
    }

    // The report's content, or null when the run is not in this project or the caller is not a member. Reuses the
    // results page's queries so the report always matches what the page shows.
    public static async Task<ReportData?> LoadAsync(
        ApplicationDbContext db, Guid id, Guid runId, ClaimsPrincipal principal, TimeProvider time,
        CancellationToken cancellationToken)
    {
        if ((await GetRun.Handle(id, runId, principal, db, cancellationToken)).Result is not Ok<GetRun.Response> { Value: { } run })
            return null;
        // The findings query answers 404 for a run without findings; the run itself was found above.
        var findings = (await ListFindings.Handle(id, principal, db, cancellationToken, runId)).Result is Ok<ListFindings.Response> { Value: { } list }
            ? list.Findings
            : [];

        var context = await db.TestRuns.AsNoTracking()
            .Where(testRun => testRun.Id == runId)
            .Select(testRun => new
            {
                ProjectName = testRun.Project.Name, testRun.ApiSpec.Title, testRun.ApiSpec.VersionLabel,
                testRun.ApiSpec.OpenApiVersion, testRun.RunConfig
            })
            .SingleAsync(cancellationToken);
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var generatedBy = await db.Users.Where(user => user.Id == userId).Select(user => user.DisplayName).SingleAsync(cancellationToken);

        return new ReportData(context.ProjectName, context.Title, context.VersionLabel, context.OpenApiVersion, run,
            context.RunConfig.RootElement.Clone(), findings, generatedBy, time.GetUtcNow());
    }

    // Safe for a Content-Disposition header and any file system.
    private static string FileName(string project, int run, int version)
    {
        var name = new string(project.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (name.Length > 100)
            name = name[..100];
        return $"{(name.Length == 0 ? "project" : name)}-{ReportDocument.RunLabel(run)}-report-v{version}.pdf";
    }
}
