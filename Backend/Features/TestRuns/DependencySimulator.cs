using System.Collections.Concurrent;
using System.Security.Cryptography;
using Backend.Domain.Enums;
using Backend.Features.Environments;
using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Forwarder;

namespace Backend.Features.TestRuns;

// The dependency simulator (PROJECT.md 10): stands in for a service the target calls. The target's dependency URL is
// pointed at this route, which forwards every call to the environment's real dependency until the runner arms a
// fault; then the calls are cut and logged instead. It is a route on this backend rather than its own listener, so
// arming is a memory write and no port is added. The price: it cannot refuse a connection or fail a name lookup, only
// reproduce how each one feels to the caller.
// The route has no sign-in; its key is the environment's SimulatorToken, which only owners and editors see.
// shortcut: the armed fault lives in this process, so a second backend instance forwards what this one should cut;
// keep it in the database when there are several.
// shortcut: every call through an environment's route is cut while its fault is armed, whoever caused it; tell them
// apart with a correlation header when scenarios run side by side.
// shortcut: the environment is read on every call; cache it when that shows in the target's latency.
public sealed class DependencySimulator(TimeProvider time)
{
    public const string Unresolvable = "unresolvable";
    public const string Route = "/simulator/{token:length(64)}/{**path}";

    // 32 random bytes as lowercase hex: the route's length(64) constraint turns away anything else before the database.
    public static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    // Replaces the project's simulator tokens, so a member losing access also loses the link they saw. Saved by the
    // caller's SaveChangesAsync, together with the change that caused it.
    public static async Task RotateTokensAsync(ApplicationDbContext db, Guid projectId, CancellationToken cancellationToken)
    {
        foreach (var environment in await db.ApiEnvironments
                     .Where(environment => environment.ProjectId == projectId && environment.SimulatorToken != null)
                     .ToListAsync(cancellationToken))
            environment.SimulatorToken = NewToken();
    }

    // A member whose role let them see the token; removing or demoting them rotates it.
    public static bool SawToken(ProjectRole role) => role != ProjectRole.Viewer;

    // One call the target made while a fault was armed, timed from the arming.
    public sealed record Call(long AtMs, string Method, string Path);

    // The calls a fault cut. Only the first MaxCalls are kept and each path is shortened, so a target retrying in a
    // tight loop, or anyone holding the token, cannot grow the stored observation or this process's memory without
    // bound; Total still counts every call, which is the evidence of a retry storm.
    public sealed class CallLog
    {
        public const int MaxCalls = 100;
        public const int MaxPathLength = 256;
        private readonly ConcurrentQueue<Call> calls = new();
        private int total;

        public int Total => Volatile.Read(ref total);
        public IEnumerable<Call> Calls => calls;

        public void Add(long atMs, string method, string path)
        {
            if (Interlocked.Increment(ref total) <= MaxCalls)
                calls.Enqueue(new(atMs, method, path.Length > MaxPathLength ? path[..MaxPathLength] : path));
        }
    }

    // An armed fault and the calls it cut, in arrival order. Disposing it disarms the simulator.
    public sealed class Fault(DependencySimulator simulator, Guid environmentId, string dependency, TimeProvider time) : IDisposable
    {
        private readonly CancellationTokenSource disarmed = new();
        private readonly long armedAt = time.GetTimestamp();

        public CallLog Log { get; } = new();

        internal async Task CutAsync(HttpContext context, string path)
        {
            // The path without its query, which may carry the target's own keys.
            Log.Add((long)time.GetElapsedTime(armedAt).TotalMilliseconds, context.Request.Method, path);
            if (dependency != Unresolvable)
            {
                // Unreachable: silence, like a host that is down, until the caller gives up or the fault is disarmed.
                using var released = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, disarmed.Token);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, released.Token);
                }
                catch (OperationCanceledException) { }
            }
            // Unresolvable fails at once. Either way there is no HTTP answer: the caller sees the connection close.
            context.Abort();
        }

        public void Dispose()
        {
            simulator.faults.TryRemove(KeyValuePair.Create(environmentId, this));
            // Not disposed: a call that found this fault just before removal still reads disarmed.Token in CutAsync.
            disarmed.Cancel();
        }
    }

    private readonly ConcurrentDictionary<Guid, Fault> faults = new();

    // dependency is the scenario's parameter: "unresolvable" or "unreachable".
    public Fault Arm(Guid environmentId, string dependency) =>
        faults[environmentId] = new Fault(this, environmentId, dependency, time);

    public async Task HandleAsync(
        HttpContext context, string token, string? path, ApplicationDbContext db, IHttpForwarder forwarder,
        IHttpMessageHandlerFactory handlers)
    {
        path = "/" + path;
        var environment = await db.ApiEnvironments.AsNoTracking().Where(environment => environment.SimulatorToken == token)
            .Select(environment => new { environment.Id, environment.DependencyUrl, environment.VerifyTls })
            .SingleOrDefaultAsync(context.RequestAborted);
        if (environment?.DependencyUrl is not { } dependencyUrl)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (faults.TryGetValue(environment.Id, out var fault))
        {
            await fault.CutAsync(context, path);
            return;
        }
        // The target's handlers: the same address guard and TLS setting as the runner's own requests. A dependency
        // that cannot be reached is answered with 502 or 504 by the forwarder.
        using var client = TargetClient.CreateInvoker(handlers, environment.VerifyTls);
        await forwarder.SendAsync(context, dependencyUrl, client, ForwarderRequestConfig.Empty, new ToDependency(path));
    }

    private sealed class ToDependency(string path) : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(
            HttpContext context, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
        {
            await base.TransformRequestAsync(context, proxyRequest, destinationPrefix, cancellationToken);
            // The dependency's own path and host; unchanged, it would be asked for this route's path under ChaosLab's host name.
            proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress(destinationPrefix, path, context.Request.QueryString);
            proxyRequest.Headers.Host = null;
            // The route needs no sign-in, so a browser that follows a link to it sends the user's ChaosLab session
            // cookie; the dependency must never receive it.
            proxyRequest.Headers.Remove("Cookie");
        }

        public override async ValueTask<bool> TransformResponseAsync(
            HttpContext context, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
        {
            var result = await base.TransformResponseAsync(context, proxyResponse, cancellationToken);
            // The dependency's answer is served on ChaosLab's origin: it must not run script or set cookies there.
            context.Response.Headers.Remove("Set-Cookie");
            context.Response.Headers.ContentSecurityPolicy = "sandbox";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return result;
        }
    }
}
