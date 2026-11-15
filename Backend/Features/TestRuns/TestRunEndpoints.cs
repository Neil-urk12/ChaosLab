using Backend.Infrastructure.Data;
using Yarp.ReverseProxy.Forwarder;

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

        // Called by the target, which cannot sign in: the environment's simulator token in the path is the only key,
        // and the route only forwards to that environment's dependency URL.
        api.Map(DependencySimulator.Route,
                (HttpContext context, string token, string? path, DependencySimulator simulator, ApplicationDbContext db,
                        IHttpForwarder forwarder, IHttpMessageHandlerFactory handlers) =>
                    simulator.HandleAsync(context, token, path, db, forwarder, handlers))
            .AllowAnonymous().ExcludeFromDescription();
    }
}
