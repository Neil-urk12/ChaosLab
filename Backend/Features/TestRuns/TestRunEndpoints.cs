namespace Backend.Features.TestRuns;

public static class TestRunEndpoints
{
    public static void MapTestRunEndpoints(this IEndpointRouteBuilder api)
    {
        var runs = api.MapGroup("/projects/{id:guid}/runs").WithTags("Test runs");
        runs.MapGet("/", ListRuns.Handle);
        runs.MapPost("/", CreateRun.Handle);
        runs.MapGet("/preflight", GetPreflight.Handle);
        runs.MapGet("/{runId:guid}", GetRun.Handle);
        runs.MapPost("/{runId:guid}/cancel", CancelRun.Handle);
    }
}
