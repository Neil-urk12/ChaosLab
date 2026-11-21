namespace Backend.Features.Reports;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder api)
    {
        var reports = api.MapGroup("/projects/{id:guid}/runs/{runId:guid}/reports").WithTags("Reports");
        reports.MapGet("/", ListReports.Handle);
        reports.MapPost("/", GenerateReport.Handle);
        reports.MapGet("/{reportId:guid}/file", GetReportFile.Handle);
    }
}
