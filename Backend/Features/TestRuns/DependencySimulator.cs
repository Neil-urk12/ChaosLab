using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Environments;
using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Yarp.ReverseProxy.Forwarder;

namespace Backend.Features.TestRuns;

// The dependency simulator (PROJECT.md 10): stands in for a service the target calls. The target's dependency URL is
// pointed at this route, which forwards every call to the environment's real dependency until the runner arms a
// fault. While armed, every call is logged, and the fault decides what happens to it: Dependency failure cuts it, 5xx
// Storm and 429 Throttle answer it in the dependency's place, Malformed JSON and Empty Response forward it and change
// the dependency's answer (FaultInjector.Apply). It is a route on this backend rather than its own listener, so
// arming is a memory write and no port is added. The price: it cannot refuse a connection or fail a name lookup, only
// reproduce how each one feels to the caller.
// The route has no sign-in; its key is the environment's SimulatorToken, which only owners and editors see.
// shortcut: the armed fault lives in this process, so a second backend instance forwards what this one should cut;
// keep it in the database when there are several.
// shortcut: every call through an environment's route meets its armed fault, whoever caused it; tell them
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

    // Whether the scenario's fault is applied here, in the target's outbound path, rather than in the runner's own
    // request path. Dependency failure always is; the faults the injector can also apply are when their `path` says so,
    // and parameters stored before `path` existed mean the runner's path.
    public static bool Applies(ScenarioType type, JsonElement parameters) =>
        type == ScenarioType.DnsFailure
        || (type is ScenarioType.ServerErrorStorm or ScenarioType.Throttle429 or ScenarioType.MalformedJson or ScenarioType.EmptyResponse
            && parameters.TryGetProperty("path", out var path) && path.GetString() == "dependency");

    // One call the target made while a fault was armed, timed from its arrival after the arming. Injected: the fault
    // changed what the target got (cut, answered here, or a body that differs from the dependency's own). A forwarded
    // call is logged on arrival and marked before its changed answer is sent, so the target can't see the change before
    // the log does; a call still running when the faulted request ends, and its log is stored, counts but reads not injected.
    public sealed record Call(long AtMs, string Method, string Path)
    {
        public bool Injected { get; set; }
    }

    // The calls an armed fault met. Only the first MaxCalls are kept and each path is shortened, so a target retrying in a
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

        // The stored entry, to mark later, or null when it is past MaxCalls and only counted.
        public Call? Add(long atMs, string method, string path, bool injected)
        {
            if (Interlocked.Increment(ref total) > MaxCalls)
                return null;
            var call = new Call(atMs, method, path.Length > MaxPathLength ? path[..MaxPathLength] : path) { Injected = injected };
            calls.Enqueue(call);
            return call;
        }
    }

    // An armed fault and the calls it met. Disposing it disarms the simulator.
    public sealed class Fault(DependencySimulator simulator, Guid environmentId, ScenarioType type, JsonElement parameters, TimeProvider time)
        : IDisposable
    {
        private readonly CancellationTokenSource disarmed = new();
        private readonly long armedAt = time.GetTimestamp();
        private int arrived;

        private volatile CallLog log = new();

        // The calls since the runner's last Next: one log per faulted request, so a run of them shows when the target
        // stopped calling (a circuit breaker opening). A late call from the request before lands in the next one's log.
        public CallLog Log => log;

        public CallLog Next() => log = new();

        // Malformed JSON and Empty Response: the call goes on to the dependency and its answer is changed on the way back.
        internal bool ChangesAnswer => type is ScenarioType.MalformedJson or ScenarioType.EmptyResponse;

        internal long ElapsedMs => (long)time.GetElapsedTime(armedAt).TotalMilliseconds;

        // The dependency's answer as the target gets it; the same text when the fault had nothing to change. Only called
        // when ChangesAnswer, for which Apply always returns a body.
        internal string Change(int status, string body) => FaultInjector.Apply(type, parameters, status, body)!.Body!;

        // Answers the call here and returns true, or returns false when it is to be forwarded. The path is without its
        // query, which may carry the target's own keys.
        internal async Task<bool> TakeAsync(HttpContext context, string path)
        {
            var atMs = ElapsedMs;
            var number = Interlocked.Increment(ref arrived);
            var answered = type switch
            {
                ScenarioType.DnsFailure => true,
                // The first `count` calls fail, each with the scenario's probability; the dependency answers the rest.
                ScenarioType.ServerErrorStorm => number <= parameters.GetProperty("count").GetInt32()
                    && Random.Shared.NextDouble() < parameters.GetProperty("probability").GetDouble(),
                // One 429; a call after it reaches the dependency, whenever it comes.
                ScenarioType.Throttle429 => number == 1,
                _ => false
            };
            if (!answered)
                return false;
            Log.Add(atMs, context.Request.Method, path, injected: true);
            if (type == ScenarioType.DnsFailure)
            {
                await CutAsync(context);
                return true;
            }
            var injected = FaultInjector.Apply(type, parameters, 0, "")!;
            context.Response.StatusCode = injected.Status;
            if (injected.RetryAfterSeconds is { } seconds)
                context.Response.Headers.RetryAfter = seconds.ToString();
            // Served on ChaosLab's origin like a forwarded answer, so fenced off the same way (ToDependency).
            context.Response.Headers.ContentSecurityPolicy = "sandbox";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return true;
        }

        private async Task CutAsync(HttpContext context)
        {
            if (parameters.GetProperty("dependency").GetString() != Unresolvable)
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

    // The scenario's own type and parameters decide what the fault does to each call (see Applies).
    public Fault Arm(Guid environmentId, ScenarioType type, JsonElement parameters) =>
        faults[environmentId] = new Fault(this, environmentId, type, parameters.Clone(), time);

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
        faults.TryGetValue(environment.Id, out var fault);
        var atMs = fault?.ElapsedMs ?? 0;
        if (fault is not null && await fault.TakeAsync(context, path))
            return;
        // Logged on arrival: the target may answer the runner before this call ends, and the log is stored then.
        var call = fault?.Log.Add(atMs, context.Request.Method, path, injected: false);
        // The target's handlers: the same address guard and TLS setting as the runner's own requests. A dependency
        // that cannot be reached is answered with 502 or 504 by the forwarder.
        using var client = TargetClient.CreateInvoker(handlers, environment.VerifyTls);
        await forwarder.SendAsync(context, dependencyUrl, client, ForwarderRequestConfig.Empty, new ToDependency(path, fault, call));
    }

    // fault: the armed fault whose calls go on to the dependency, or null. call: this call's log entry, to mark injected.
    private sealed class ToDependency(string path, Fault? fault, Call? call) : HttpTransformer
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
            // A compressed answer can't be changed as text. No header at all would accept any encoding (RFC 9110 12.5.3).
            if (fault?.ChangesAnswer == true)
            {
                proxyRequest.Headers.AcceptEncoding.Clear();
                proxyRequest.Headers.AcceptEncoding.Add(new("identity"));
            }
        }

        public override async ValueTask<bool> TransformResponseAsync(
            HttpContext context, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
        {
            var result = await base.TransformResponseAsync(context, proxyResponse, cancellationToken);
            // The dependency's answer is served on ChaosLab's origin: it must not run script or set cookies there.
            context.Response.Headers.Remove("Set-Cookie");
            context.Response.Headers.ContentSecurityPolicy = "sandbox";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            // Passed on unchanged, and so not injected: an answer that may carry no body (Kestrel throws on a body for 204,
            // 205 and 304, and drops it for HEAD), and one compressed despite the identity request.
            if (fault?.ChangesAnswer != true || proxyResponse is null || HttpMethods.IsHead(context.Request.Method)
                || context.Response.StatusCode is StatusCodes.Status204NoContent or StatusCodes.Status205ResetContent
                    or StatusCodes.Status304NotModified
                || proxyResponse.Content.Headers.ContentEncoding.Count > 0)
                return result;
            // Sent here instead of streamed by the forwarder: false tells it the response is already written.
            // shortcut: the whole answer is read into memory to change it; stream it when dependencies answer with large bodies.
            var body = await proxyResponse.Content.ReadAsStringAsync(cancellationToken);
            var changed = fault.Change(context.Response.StatusCode, body);
            // Marked before the write: once the target has the changed answer it may answer the runner, which stores the log.
            if (call is not null)
                call.Injected = changed != body;
            var bytes = Encoding.UTF8.GetBytes(changed);
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, cancellationToken);
            return false;
        }
    }
}
