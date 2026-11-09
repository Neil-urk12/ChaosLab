namespace Backend.Features.Findings;

public static class FindingEndpoints
{
    public static void MapFindingEndpoints(this IEndpointRouteBuilder api)
    {
        var findings = api.MapGroup("/projects/{id:guid}/findings").WithTags("Findings");
        findings.MapGet("/", ListFindings.Handle);
    }
}
