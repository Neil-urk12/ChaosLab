namespace Backend.Features.TestRuns;

public static class TestRunEndpoints
{
    public static void MapTestRunEndpoints(this IEndpointRouteBuilder api)
    {
        var runs = api.MapGroup("/projects/{id:guid}/runs").WithTags("Test runs");
        runs.MapGet("/", ListRuns.Handle);
    }
}
