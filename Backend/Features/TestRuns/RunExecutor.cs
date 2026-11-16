using System.Net;
using System.Text;
using System.Text.Json;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.ApiSpecs;
using Backend.Features.Environments;
using Backend.Features.Scenarios;
using Backend.Infrastructure.Data;
using Backend.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.TestRuns;

// Background runner (FeaturesFlow 2.16, 2.17). Takes Queued runs oldest first and works through each run's scenario
// executions one at a time: an optional baseline request, then the fault. Latency, Timeout / Drop and Duplicate
// Request are direct requests; 5xx Storm, 429 Throttle, Malformed JSON, Empty Response, Slow Drip and Auth Expiry go
// through the FaultInjector (PROJECT.md 10). Dependency failure arms the DependencySimulator in the target's outbound path.
// Every request is stored as an HttpObservation, and each completed scenario is evaluated into its findings
// (2.18, Evaluator). A run whose every scenario ran is then scored (2.19, Scorer).
// shortcut: ignores the run's concurrency and runner-retry settings (always 1 and 0); honour them when experiments get slow.
// shortcut: one run at a time for every project, so a long run holds up the other projects' queues; run them side by side when that hurts.
public sealed class RunExecutor(
    IServiceScopeFactory scopes, IHttpClientFactory httpClientFactory, CredentialProtector protector, TimeProvider time,
    DependencySimulator simulator, ILogger<RunExecutor> logger) : BackgroundService
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string NoDependency = "No dependency URL is set for this target, so the simulator has nothing to stand in for. Add one in Settings.";
    public const string SimulatorNotInPath = "The target made no call through the dependency simulator during the faulted request, so the fault never reached it.";
    public const string ForbiddenNeedsAccount = "A forbidden credential needs a second account without access, which ChaosLab cannot supply yet.";
    public const string NoCredential = "No credential is stored for this operation, so there is none to take away.";
    private const int MaxStoredBodyBytes = 16 * 1024;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private sealed record RunConfig(bool BaselineRequests, TargetConfig Target);

    private sealed record TargetConfig(string BaseUrl, bool VerifyTls, int RequestTimeoutSeconds);

    private sealed record Context(Guid RunId, RunConfig Config, ApiEnvironment Environment, List<SchemeInfo> Schemes, JsonElement Document);

    private sealed record Execution(
        Guid Id, Guid EndpointId, ScenarioType ScenarioType, JsonDocument Parameters, JsonDocument Invariants, string HttpMethod,
        string Path,
        JsonDocument EndpointParameters, JsonDocument? RequestBody, JsonDocument? Security);

    // The endpoint's request, ready to be sent any number of times. Authenticate applies the stored credentials, or
    // the given secret in their place.
    private sealed record Prepared(
        HttpMethod Method, Uri Url, byte[]? Body, string? ContentType, List<KeyValuePair<string, string>> Headers,
        List<string> Secrets, Action<HttpRequestMessage, string?> Authenticate, bool HasCredential)
    {
        public HttpRequestMessage Create(HttpContent? content = null)
        {
            // An empty body where there is none: without content the HTTP handler quietly re-sends the request, up to
            // three more times, when the target closes without answering, and those extra requests would go unrecorded.
            var request = new HttpRequestMessage(Method, Url) { Content = content ?? new ByteArrayContent(Body ?? []) };
            if (Body is not null)
                // The spec's own media type, so application/vnd.api+json is not sent as application/json.
                request.Content!.Headers.TryAddWithoutValidation("Content-Type", ContentType);
            foreach (var (name, configured) in Headers)
            {
                // A fresh key per request: with the spec's fixed example, a correct target replays its stored answer
                // and the request never really runs. The duplicate scenario replaces it with its own key afterwards.
                var value = name.Equals(IdempotencyHeader, StringComparison.OrdinalIgnoreCase) ? Guid.NewGuid().ToString() : configured;
                // Content headers (a global Content-Type) only fit on the body; a request without one has nothing to describe.
                if (!request.Headers.TryAddWithoutValidation(name, value) && (content is not null || Body is not null))
                {
                    request.Content!.Headers.Remove(name);
                    request.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }
            Authenticate(request, null);
            return request;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recovered = false;
        var failing = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!recovered)
                    await RecoverAsync(stoppingToken);
                recovered = true;
                var taken = await TickAsync(stoppingToken);
                failing = false;
                if (taken)
                    continue;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // For example the database is not up yet; the next tick tries again. An outage is an error once, not every poll.
                logger.Log(failing ? LogLevel.Debug : LogLevel.Error, exception, "The run executor could not poll for runs.");
                failing = true;
                // No run executes between ticks, so a run a failed tick left Running is orphaned: recovery fails it.
                recovered = false;
            }
            await Task.Delay(PollInterval, time, stoppingToken);
        }
    }

    // Executes the oldest Queued run, if there is one, then marks the executions of finished runs that never started
    // as Skipped (2.17). Returns whether a run was taken. Public so tests can drive the runner without the polling loop.
    public async Task<bool> TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var taken = await RunNextAsync(db, cancellationToken);
        var now = time.GetUtcNow();
        // shortcut: no index serves Status = Queued, so this reads every execution on each tick; add a partial index when history grows.
        await db.ScenarioExecutions
            .Where(execution => execution.Status == ScenarioExecutionStatus.Queued
                && execution.TestRun.Status != TestRunStatus.Queued && execution.TestRun.Status != TestRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(execution => execution.Status, ScenarioExecutionStatus.Skipped)
                .SetProperty(execution => execution.FailureReason, "The run ended before this scenario started.")
                .SetProperty(execution => execution.CompletedAt, now), cancellationToken);
        return taken;
    }

    // Runs a previous process or a failed tick left Running have no runner any more, so they fail.
    // shortcut: assumes one backend instance; with several, a restarting one would fail runs another is executing. Claim runs with a lease then.
    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = time.GetUtcNow();
        await db.ScenarioExecutions.Where(execution => execution.Status == ScenarioExecutionStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(execution => execution.Status, ScenarioExecutionStatus.Failed)
                .SetProperty(execution => execution.FailureReason, "The runner stopped during this scenario.")
                .SetProperty(execution => execution.CompletedAt, now), cancellationToken);
        await db.TestRuns.Where(run => run.Status == TestRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, TestRunStatus.Failed)
                .SetProperty(run => run.FailureMessage, "The runner stopped before this run finished.")
                .SetProperty(run => run.CompletedAt, now), cancellationToken);
    }

    private async Task<bool> RunNextAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var next = await db.TestRuns.Where(run => run.Status == TestRunStatus.Queued).OrderBy(run => run.CreatedAt)
            .Select(run => (Guid?)run.Id).FirstOrDefaultAsync(cancellationToken);
        if (next is not { } runId)
            return false;
        // Conditional, so a run cancelled since the query above is never started.
        var startedAt = time.GetUtcNow();
        if (await db.TestRuns.Where(run => run.Id == runId && run.Status == TestRunStatus.Queued)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, TestRunStatus.Running)
                    .SetProperty(run => run.StartedAt, startedAt), cancellationToken) == 0)
            return true;

        string? failure;
        try
        {
            failure = await ExecuteRunAsync(db, runId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Run {RunId} stopped on a runner error.", runId);
            db.ChangeTracker.Clear();
            failure = "The runner hit an internal error and stopped this run.";
            var now = time.GetUtcNow();
            await db.ScenarioExecutions
                .Where(execution => execution.TestRunId == runId && execution.Status == ScenarioExecutionStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(execution => execution.Status, ScenarioExecutionStatus.Failed)
                    .SetProperty(execution => execution.FailureReason, "The runner hit an internal error during this scenario.")
                    .SetProperty(execution => execution.CompletedAt, now), cancellationToken);
        }

        // Completed when every scenario ran, PartiallyCompleted when some did, Failed when none did.
        var statuses = await db.ScenarioExecutions.Where(execution => execution.TestRunId == runId)
            .Select(execution => execution.Status).ToListAsync(cancellationToken);
        var completed = statuses.Count(status => status == ScenarioExecutionStatus.Completed);
        var result = failure is null && completed == statuses.Count ? TestRunStatus.Completed
            : completed > 0 ? TestRunStatus.PartiallyCompleted : TestRunStatus.Failed;
        failure ??= result == TestRunStatus.Failed ? "No scenario could be executed. Each scenario shows why." : null;
        var completedAt = time.GetUtcNow();

        // Only a run whose every scenario ran is scored (FeaturesFlow 2.19): the dashboards read any run that has a score.
        Scorer.Result? score = null;
        List<EndpointResult> endpointResults = [];
        if (result == TestRunStatus.Completed)
        {
            var findings = (await db.ResilienceFindings.Where(finding => finding.TestRunId == runId)
                    .Select(finding => new
                    {
                        finding.EndpointId, finding.Pattern, finding.Category, finding.Outcome,
                        Type = (ScenarioType?)finding.ScenarioExecution!.ScenarioType
                    })
                    .ToListAsync(cancellationToken))
                .Select(finding => (finding.EndpointId,
                    Scored: (finding.Category ?? Scorer.CategoryOf(finding.Type, finding.Pattern), finding.Outcome)))
                .ToList();
            score = Scorer.Score(findings.Select(finding => finding.Scored).ToList());
            // The same method per endpoint; an endpoint with no scored findings gets no row.
            endpointResults = findings.GroupBy(finding => finding.EndpointId)
                .Select(group => (EndpointId: group.Key, Score: Scorer.Score(group.Select(finding => finding.Scored).ToList())))
                .Where(endpoint => endpoint.Score.Overall is not null)
                .Select(endpoint => new EndpointResult
                {
                    TestRunId = runId, EndpointId = endpoint.EndpointId, OverallScore = endpoint.Score.Overall!.Value,
                    Evidence = JsonSerializer.SerializeToDocument(endpoint.Score.Categories, JsonSerializerOptions.Web),
                    CreatedAt = completedAt
                })
                .ToList();
        }
        var overall = score?.Overall;
        var coverage = score?.Coverage;
        var categoryScores = score is null ? null : JsonSerializer.SerializeToDocument(score.Categories, JsonSerializerOptions.Web);

        // One transaction: a run never has a score without its endpoint scores.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Conditional, so a cancelled run stays Cancelled and unscored.
        var finished = await db.TestRuns.Where(run => run.Id == runId && run.Status == TestRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, result)
                .SetProperty(run => run.FailureMessage, failure)
                .SetProperty(run => run.ResilienceScore, overall)
                .SetProperty(run => run.EvidenceCoverage, coverage)
                .SetProperty(run => run.CategoryScores, categoryScores)
                .SetProperty(run => run.CompletedAt, completedAt), cancellationToken);
        if (finished == 1 && endpointResults.Count > 0)
        {
            db.EndpointResults.AddRange(endpointResults);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Returns why the run could not start at all, or null once it went through its scenarios.
    private async Task<string?> ExecuteRunAsync(ApplicationDbContext db, Guid runId, CancellationToken cancellationToken)
    {
        var run = await db.TestRuns.AsNoTracking().Where(run => run.Id == runId)
            .Select(run => new { run.ApiSpecId, run.EnvironmentId, run.RunConfig, run.ApiSpec.ParsedDocument })
            .SingleAsync(cancellationToken);
        // The target comes from the run's configuration snapshot, so editing the environment later can't redirect it.
        var config = run.RunConfig.Deserialize<RunConfig>(JsonSerializerOptions.Web)!;
        var environment = await db.ApiEnvironments.AsNoTracking()
            .Include(environment => environment.Headers).Include(environment => environment.AuthProfiles)
            .SingleAsync(environment => environment.Id == run.EnvironmentId, cancellationToken);
        // Headers and credentials are read live. After a move to another host they belong to that host, not to this snapshot's.
        if (!TargetClient.SameOrigin(environment.BaseUrl, new Uri(config.Target.BaseUrl)))
            return "The environment moved to another host after this run was created, so nothing was sent. Start a new run.";
        // The run's own spec, not the active one: a re-import while the run is queued must not change its auth schemes.
        var document = run.ParsedDocument.RootElement;
        var context = new Context(runId, config, environment, await SchemeInfo.ForSpecAsync(db, run.ApiSpecId, document, cancellationToken),
            document);
        var executions = await db.ScenarioExecutions.AsNoTracking()
            .Where(execution => execution.TestRunId == runId).OrderBy(execution => execution.SequenceNo)
            .Select(execution => new Execution(
                execution.Id, execution.EndpointId, execution.ScenarioType, execution.Parameters, execution.Invariants,
                execution.Endpoint.HttpMethod,
                execution.Endpoint.Path, execution.Endpoint.Parameters, execution.Endpoint.RequestBodySchema,
                execution.Endpoint.SecurityRequirements))
            .ToListAsync(cancellationToken);

        foreach (var execution in executions)
        {
            // Re-checked before every scenario (2.17): a cancelled run starts nothing new; TickAsync skips the rest.
            var startedAt = time.GetUtcNow();
            if (await db.ScenarioExecutions
                    .Where(row => row.Id == execution.Id && row.Status == ScenarioExecutionStatus.Queued
                        && row.TestRun.Status == TestRunStatus.Running)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.Status, ScenarioExecutionStatus.Running)
                        .SetProperty(row => row.StartedAt, startedAt), cancellationToken) == 0)
                return null;

            ScenarioExecutionStatus status;
            string? reason;
            try
            {
                (status, reason) = await ExecuteScenarioAsync(db, context, execution, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A runner failure in one scenario is never the target's, and the run goes on.
                logger.LogError(exception, "Scenario execution {ExecutionId} stopped on a runner error.", execution.Id);
                db.ChangeTracker.Clear();
                (status, reason) = (ScenarioExecutionStatus.Failed, "The runner hit an internal error during this scenario.");
            }
            db.ChangeTracker.Clear();

            var completedAt = time.GetUtcNow();
            await db.ScenarioExecutions.Where(row => row.Id == execution.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, status)
                    .SetProperty(row => row.FailureReason, reason)
                    .SetProperty(row => row.CompletedAt, completedAt), cancellationToken);
            // Counts every scenario the runner finished, skipped and runner errors included, so progress reaches the total.
            await db.TestRuns.Where(row => row.Id == runId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.CompletedScenarios, row => row.CompletedScenarios + 1), cancellationToken);
        }
        return null;
    }

    private async Task<(ScenarioExecutionStatus, string?)> ExecuteScenarioAsync(
        ApplicationDbContext db, Context context, Execution execution, CancellationToken cancellationToken)
    {
        // Checked before Prepare, like the forbidden credential below: it is a settings gap, not a spec problem.
        if (execution.ScenarioType == ScenarioType.DnsFailure && context.Environment.DependencyUrl is null)
            return (ScenarioExecutionStatus.Skipped, NoDependency);
        var parameters = execution.Parameters.RootElement;
        // Checked before Prepare: no fix to the spec makes it runnable, so its reason must not be a spec problem.
        if (execution.ScenarioType == ScenarioType.AuthExpiry && parameters.GetProperty("credential").GetString() == "forbidden")
            return (ScenarioExecutionStatus.Skipped, ForbiddenNeedsAccount);
        var (prepared, error) = Prepare(context, execution);
        if (prepared is null)
            return (ScenarioExecutionStatus.Failed, error);

        // Without a stored credential the faulted request would be the baseline again.
        if (execution.ScenarioType == ScenarioType.AuthExpiry && !prepared.HasCredential)
            return (ScenarioExecutionStatus.Skipped, NoCredential);
        var attempt = 0;
        var observations = new List<HttpObservation>();
        // Saved one by one, so the live view shows each request as it finishes.
        async Task<HttpObservation> RecordAsync(Task<HttpObservation> sending)
        {
            var observation = await sending;
            observations.Add(observation);
            db.HttpObservations.Add(observation);
            await db.SaveChangesAsync(cancellationToken);
            return observation;
        }

        // Duplicate requests are the scenario's own requests; a baseline would add one more record for the invariant to count.
        if (execution.ScenarioType == ScenarioType.Baseline
            || (context.Config.BaselineRequests && execution.ScenarioType != ScenarioType.DuplicateRequest))
        {
            var baseline = await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                new() { ["phase"] = "baseline" }, cancellationToken));
            // An unreachable target means the experiment never ran (PROJECT.md 10.3): skipped, not the target's failure.
            if (baseline.OutcomeType == ObservationOutcomeType.ConnectionError)
                return (ScenarioExecutionStatus.Skipped, $"The target was unreachable: {baseline.ErrorType}");
            if (execution.ScenarioType == ScenarioType.Baseline)
                return (ScenarioExecutionStatus.Completed, null);
        }

        HttpObservation[] faults = [];
        switch (execution.ScenarioType)
        {
            case ScenarioType.Latency:
                // Runner-side delay (PROJECT.md 10.2): the request reaches the target late.
                var delayMs = parameters.GetProperty("delayMs").GetInt32();
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), time, cancellationToken);
                faults = [await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                    new() { ["phase"] = "fault", ["delayMs"] = delayMs }, cancellationToken))];
                break;
            case ScenarioType.TimeoutDrop:
                var mode = parameters.GetProperty("mode").GetString();
                faults = [await RecordAsync(SendAsync(context, execution, prepared,
                    prepared.Create(new StalledContent(prepared.Body ?? [], hold: mode == "hold")), ++attempt, null,
                    new() { ["phase"] = "fault", ["mode"] = mode }, cancellationToken))];
                // The runner ended that request itself, so it shows nothing about the target. A normal request
                // afterwards is the evidence: did the target get over the abandoned one?
                await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                    new() { ["phase"] = "recovery" }, cancellationToken));
                break;
            case ScenarioType.DuplicateRequest:
                var copies = parameters.GetProperty("copies").GetInt32();
                var key = parameters.GetProperty("key").GetString();
                var gapMs = parameters.GetProperty("delayMs").GetInt32();
                var shared = Guid.NewGuid();
                var first = attempt;
                // Staggered starts: with no gap the copies race each other, like a double submit.
                faults = await Task.WhenAll(Enumerable.Range(1, copies).Select(async copy =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds((copy - 1) * gapMs), time, cancellationToken);
                    var request = prepared.Create();
                    Guid? idempotencyKey = key switch { "same" => shared, "different" => Guid.NewGuid(), _ => null };
                    request.Headers.Remove(IdempotencyHeader);
                    if (idempotencyKey is { } value)
                        request.Headers.TryAddWithoutValidation(IdempotencyHeader, value.ToString());
                    return await SendAsync(context, execution, prepared, request, first + copy, idempotencyKey,
                        new() { ["phase"] = "fault", ["copy"] = copy, ["key"] = key }, cancellationToken);
                }));
                attempt += copies;
                observations.AddRange(faults);
                db.HttpObservations.AddRange(faults);
                await db.SaveChangesAsync(cancellationToken);
                break;
            case ScenarioType.ServerErrorStorm:
                var count = parameters.GetProperty("count").GetInt32();
                var probability = parameters.GetProperty("probability").GetDouble();
                // One logical operation retried: every attempt, the one that gets through included, carries the same key.
                var retryKey = Guid.NewGuid();
                var keyed = prepared.Headers.Any(header => header.Key.Equals(IdempotencyHeader, StringComparison.OrdinalIgnoreCase));
                HttpRequestMessage Retry()
                {
                    var request = prepared.Create();
                    if (keyed)
                    {
                        request.Headers.Remove(IdempotencyHeader);
                        request.Headers.TryAddWithoutValidation(IdempotencyHeader, retryKey.ToString());
                    }
                    return request;
                }
                // shortcut: the retries follow each other at once, like a client without backoff; add a gap setting
                // when backoff evidence is wanted.
                // Below probability 1 some "fault" rows are plain Forwarded answers; a Storm rule must not assume all were injected.
                var storm = new List<HttpObservation>();
                for (var index = 0; index < count; index++)
                    storm.Add(await RecordAsync(SendAsync(context, execution, prepared, Retry(), ++attempt, keyed ? retryKey : null,
                        new() { ["phase"] = "fault" }, cancellationToken, inject: Random.Shared.NextDouble() < probability)));
                faults = [.. storm];
                await RecordAsync(SendAsync(context, execution, prepared, Retry(), ++attempt, keyed ? retryKey : null,
                    new() { ["phase"] = "recovery" }, cancellationToken));
                break;
            case ScenarioType.Throttle429:
                var retryAfter = parameters.GetProperty("retryAfterSeconds").GetInt32();
                faults = [await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                    new() { ["phase"] = "fault" }, cancellationToken, inject: true))];
                // What a client that respects Retry-After does next. No 429 reached the runner when the target itself
                // failed, so there is nothing to wait for.
                // shortcut: waits no longer than the request timeout (Retry-After may be an hour); raise the cap when
                // long waits are part of the evidence.
                if (faults[0].OutcomeType == ObservationOutcomeType.ModifiedResponse)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(retryAfter, context.Config.Target.RequestTimeoutSeconds)),
                        time, cancellationToken);
                await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                    new() { ["phase"] = "recovery" }, cancellationToken));
                break;
            case ScenarioType.MalformedJson or ScenarioType.EmptyResponse:
                // The target is not affected by what happens to its answer afterwards, so there is nothing to recover from.
                faults = [await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                    new() { ["phase"] = "fault" }, cancellationToken, inject: true))];
                break;
            case ScenarioType.SlowDrip:
                faults = [await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                    new() { ["phase"] = "fault" }, cancellationToken, inject: true))];
                await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                    new() { ["phase"] = "recovery" }, cancellationToken));
                break;
            case ScenarioType.AuthExpiry:
                // A request-side fault: the target itself has to reject it, so its answer is recorded as it came.
                // shortcut: an expired credential can't be minted from outside, so an invalid one stands in for it.
                var credential = parameters.GetProperty("credential").GetString();
                var sent = credential == "missing" ? "missing" : "invalid";
                var altered = prepared with
                {
                    Authenticate = (request, _) =>
                    {
                        if (sent == "invalid")
                            prepared.Authenticate(request, FaultInjector.InvalidCredential);
                    }
                };
                faults = [await RecordAsync(SendAsync(context, execution, altered, altered.Create(), ++attempt, null,
                    new() { ["phase"] = "fault", ["credential"] = credential, ["sent"] = sent }, cancellationToken))];
                await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                    new() { ["phase"] = "recovery" }, cancellationToken));
                break;
            case ScenarioType.DnsFailure:
                // The fault is in the target's outbound path (PROJECT.md 10.2): the runner's own request is sent as
                // usual, and the calls the target makes to its dependency meanwhile are cut by the simulator.
                var dependency = parameters.GetProperty("dependency").GetString()!;
                using (var armed = simulator.Arm(context.Environment.Id, dependency))
                {
                    // The live call log ({total, calls}): it is written into the observation when the request ends.
                    faults = [await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                        new() { ["phase"] = "fault", ["dependency"] = dependency, ["dependencyCalls"] = armed.Log },
                        cancellationToken))];
                    // A target that answered without calling its dependency never met the fault (PROJECT.md 10.3).
                    if (faults[0].OutcomeType != ObservationOutcomeType.ConnectionError && armed.Log.Total == 0)
                        return (ScenarioExecutionStatus.Skipped, SimulatorNotInPath);
                }
                await RecordAsync(SendAsync(context, execution, prepared, prepared.Create(), ++attempt, null,
                    new() { ["phase"] = "recovery" }, cancellationToken));
                break;
        }
        // The baseline's rule, for scenarios without one: no request reached the target, so the experiment never ran.
        if (faults.Length > 0 && faults.All(fault => fault.OutcomeType == ObservationOutcomeType.ConnectionError))
            return (ScenarioExecutionStatus.Skipped, $"The target was unreachable: {faults[0].ErrorType}");

        // Read-back requests of the invariants (2.14): a GET after the fault that shows the state it left behind.
        // shortcut: sent right after the last response, so a target that writes asynchronously may not show the record
        // yet; add a wait setting to the read-back when that happens.
        // shortcut: the GET also carries the endpoint's own required header parameters (a fresh idempotency key
        // included); keep them apart from the environment's headers when a target objects.
        var invariants = execution.Invariants.Deserialize<List<Invariant>>(ScenarioRules.Json) ?? [];
        for (var index = 0; index < invariants.Count; index++)
        {
            // A path that can't be filled in or leaves the target host is not sent; the evaluator reports the invariant as not evaluated.
            if (invariants[index].ReadBack?.Path is not { } template
                || Evaluator.ResolveReadBack(template, prepared.Body) is not { } path
                || TargetClient.Combine(context.Config.Target.BaseUrl, path) is not { } url)
                continue;
            var readBack = prepared with { Method = HttpMethod.Get, Url = url, Body = null };
            await RecordAsync(SendAsync(context, execution with { HttpMethod = "GET", Path = path }, readBack, readBack.Create(),
                ++attempt, null, new() { ["phase"] = "readBack", ["invariant"] = index }, cancellationToken));
        }

        var findings = Evaluator.Evaluate(execution.ScenarioType, parameters, invariants, observations);
        if (findings.Count > 0)
        {
            // One transaction: a finding never exists without its scenario's outcome.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.ResilienceFindings.AddRange(findings.Select(finding => new ResilienceFinding
            {
                TestRunId = context.RunId, EndpointId = execution.EndpointId, ScenarioExecutionId = execution.Id,
                Pattern = finding.Pattern, Category = finding.Category, Outcome = finding.Outcome, Severity = finding.Severity,
                Summary = finding.Summary, Evidence = finding.Evidence, CreatedAt = time.GetUtcNow()
            }));
            await db.SaveChangesAsync(cancellationToken);
            // The scenario's outcome is its worst finding; one that was judged outranks one that could not be.
            var outcome = findings.Select(finding => finding.Outcome switch
            {
                ResilienceFindingOutcome.Failed => ScenarioOutcome.Failed,
                ResilienceFindingOutcome.Degraded => ScenarioOutcome.Degraded,
                ResilienceFindingOutcome.Passed => ScenarioOutcome.Passed,
                ResilienceFindingOutcome.Inconclusive => ScenarioOutcome.Inconclusive,
                _ => ScenarioOutcome.NotApplicable
            }).MinBy(outcome => outcome switch
            {
                ScenarioOutcome.Failed => 0, ScenarioOutcome.Degraded => 1, ScenarioOutcome.Passed => 2,
                ScenarioOutcome.Inconclusive => 3, _ => 4
            });
            await db.ScenarioExecutions.Where(row => row.Id == execution.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Outcome, outcome), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return (ScenarioExecutionStatus.Completed, null);
    }

    // Builds the endpoint's request from the spec's examples, the environment's global headers and the stored credential.
    // An error means the request can't be built: a runner failure, never the target's.
    private (Prepared?, string?) Prepare(Context context, Execution execution)
    {
        var root = context.Document;
        var operation = $"{execution.HttpMethod} {execution.Path}";
        var path = execution.Path;
        var query = new List<string>();
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var raw in execution.EndpointParameters.RootElement.EnumerateArray())
        {
            if (Deref(root, raw) is not { ValueKind: JsonValueKind.Object } parameter)
                continue;
            var name = Text(parameter, "name");
            var location = Text(parameter, "in");
            var required = location == "path"
                || (parameter.TryGetProperty("required", out var flag) && flag.ValueKind == JsonValueKind.True);
            // shortcut: optional and cookie parameters are never sent; add them when scenarios need them.
            if (name is null || !required || location is not ("path" or "query" or "header"))
                continue;
            var value = Example(root, parameter) is { ValueKind: JsonValueKind.String or JsonValueKind.Number
                or JsonValueKind.True or JsonValueKind.False } example
                ? example.ValueKind == JsonValueKind.String ? example.GetString()! : example.GetRawText()
                : null;
            // shortcut: no example means no request (PROJECT.md 25, open decision 4); add test values when that is decided.
            if (value is null)
                return (null, $"The required {location} parameter \"{name}\" has no example, default or enum value in the spec.");
            if (location == "path")
                path = path.Replace($"{{{name}}}", Uri.EscapeDataString(value));
            else if (location == "query")
                query.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
            else
                headers.Add(new(name, value));
        }

        byte[]? body = null;
        string? contentType = null;
        if (execution.RequestBody is { } document && Deref(root, document.RootElement) is { ValueKind: JsonValueKind.Object } requestBody)
        {
            var media = requestBody.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object
                ? content.EnumerateObject().Where(type => type.Name.Contains("json", StringComparison.OrdinalIgnoreCase))
                    .Select(type => (JsonProperty?)type).FirstOrDefault()
                : null;
            if (media is { Value.ValueKind: JsonValueKind.Object } json && Example(root, json.Value) is { } example)
                (body, contentType) = (Encoding.UTF8.GetBytes(example.GetRawText()), json.Name);
            else if (requestBody.TryGetProperty("required", out var flag) && flag.ValueKind == JsonValueKind.True)
                return (null, "The request body is required, but the spec has no JSON example for it.");
        }

        var suffix = query.Count > 0 ? $"{path}?{string.Join('&', query)}" : path;
        if (TargetClient.Combine(context.Config.Target.BaseUrl, suffix) is not { } url)
            return (null, $"{operation} does not stay on the target host, so it was not called.");

        var (environmentHeaders, headerError) = TargetClient.GlobalHeaders(context.Environment.Headers, operation, protector);
        if (headerError is not null)
            return (null, headerError);
        headers.AddRange(environmentHeaders.Select(header => new KeyValuePair<string, string>(header.Name, header.Value)));
        var secrets = environmentHeaders.Where(header => header.Secret).Select(header => header.Value).ToList();

        // OpenAPI security lists alternatives, and each alternative needs all of its schemes together. The first one whose
        // schemes all have a stored credential is sent in full; without one the request goes unauthenticated.
        (SchemeInfo, AuthProfile)? Credential(string name) =>
            context.Schemes.FirstOrDefault(scheme => scheme.Name == name) is { } scheme
            && context.Environment.AuthProfiles.FirstOrDefault(profile => profile.Name == name
                && profile.AuthType != AuthType.None && profile.SecretCipherText is not null) is { } profile
                ? (scheme, profile) : null;
        var requirements = execution.Security?.RootElement
            ?? (root.TryGetProperty("security", out var global) ? global : default);
        var chosen = requirements.ValueKind == JsonValueKind.Array
            ? requirements.EnumerateArray().Where(requirement => requirement.ValueKind == JsonValueKind.Object)
                .Select(requirement => requirement.EnumerateObject().Select(scheme => scheme.Name).ToList())
                .FirstOrDefault(names => names.Count > 0 && names.All(name => Credential(name) is not null))
            : null;
        var credentials = new List<(SchemeInfo Scheme, AuthType Type, string Secret, string? Username)>();
        foreach (var (scheme, profile) in (chosen ?? []).Select(name => Credential(name)!.Value))
        {
            if (protector.TryUnprotect(profile.SecretCipherText!, profile.Id) is not { } secret)
                return (null, $"The credential of {scheme.Name} could not be decrypted. Enter the secret again.");
            credentials.Add((scheme, profile.AuthType, secret, CredentialConfig.From(profile.PublicConfig).Username));
            secrets.Add(secret);
        }
        void Authenticate(HttpRequestMessage request, string? instead)
        {
            foreach (var (scheme, type, secret, username) in credentials)
                TargetClient.Authenticate(request, type, instead ?? secret, username, scheme);
        }

        return (new Prepared(new HttpMethod(execution.HttpMethod), url, body, contentType, headers, secrets, Authenticate,
            credentials.Count > 0), null);
    }

    // Sends one request with the environment's timeout and records what came back. With inject, the scenario's fault is
    // applied to the target's answer on its way to the runner (FaultInjector).
    private async Task<HttpObservation> SendAsync(
        Context context, Execution execution, Prepared prepared, HttpRequestMessage request, int attemptNo, Guid? correlationId,
        Dictionary<string, object?> metadata, CancellationToken cancellationToken, bool inject = false)
    {
        var target = context.Config.Target;
        var parameters = execution.Parameters.RootElement;
        var drip = inject && execution.ScenarioType == ScenarioType.SlowDrip;
        using var sent = request;
        using var client = TargetClient.CreateClient(httpClientFactory, target.VerifyTls);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(target.RequestTimeoutSeconds));
        // The spec path, not the sent URL: it matches the endpoint, and an apiKey in the query never gets stored.
        var observation = new HttpObservation
        {
            ScenarioExecutionId = execution.Id, AttemptNo = attemptNo, CorrelationId = correlationId,
            RequestMethod = execution.HttpMethod, RequestPath = execution.Path, RequestStartedAt = time.GetUtcNow(),
            RequestBytes = request.Content?.Headers.ContentLength
        };
        // A target that echoes its request must not get the credential stored: the secrets as sent, as a URL shows them,
        // and the Authorization value (Basic sends base64, not the secret itself).
        // shortcut: exact matches only; a secret the target re-encodes another way gets through. Parse the body when that matters.
        // shortcut: secrets under 8 characters are not redacted, since replacing "1234" or "test" would rewrite ordinary
        // numbers and words in the body; require longer secrets when a short one must never be stored.
        var secrets = prepared.Secrets.Append(request.Headers.Authorization?.Parameter)
            .Where(secret => secret is { Length: >= 8 }).SelectMany(secret => new[] { secret!, Uri.EscapeDataString(secret!) })
            .Distinct().ToList();
        string Redact(string text) => secrets.Aggregate(text, (kept, secret) => kept.Replace(secret, "[redacted]"));
        var started = time.GetTimestamp();
        using var kept = new MemoryStream();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            // The target's status is also the client's, unless a fault is injected below.
            observation.UpstreamStatusCode = observation.ClientStatusCode = (int)response.StatusCode;
            observation.OutcomeType = ObservationOutcomeType.Forwarded;
            // Kept for evaluation (invariants on fields and headers). Cookies may carry the target's sessions, so they aren't.
            metadata["headers"] = response.Headers.Concat(response.Content.Headers)
                .Where(header => !header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(header => header.Key, header => Redact(string.Join(", ", header.Value)));
            // Slow Drip: the injector takes the body a few bytes at a time, so the target faces a slow reader.
            // shortcut: a body that fits the socket buffers has left the target before the first pause; only larger
            // ones hold its connection open.
            await ReadBodyAsync(response, kept, observation, drip ? parameters.GetProperty("bytes").GetInt32() : 8192,
                drip ? TimeSpan.FromMilliseconds(parameters.GetProperty("intervalMs").GetInt32()) : TimeSpan.Zero, timeout.Token);
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            // Thrown only while reading the body (a clean close as HttpIOException, a reset as a plain IOException), so the
            // status came back: a response the target cut short, not a runner error.
            observation.ErrorType = "The target closed the connection before the response body ended.";
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or OperationCanceledException)
        {
            // Only StalledContent cancels a request on its own: that is a drop. Only a connection that was never made is
            // unreachable: a target that accepts it and then closes, resets or answers garbage failed during the experiment.
            observation.OutcomeType = timeout.IsCancellationRequested ? ObservationOutcomeType.Timeout
                : exception is OperationCanceledException ? ObservationOutcomeType.Dropped
                : exception is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError
                    or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError }
                    ? ObservationOutcomeType.ConnectionError
                : ObservationOutcomeType.Forwarded;
            observation.ErrorType = observation.OutcomeType switch
            {
                ObservationOutcomeType.Dropped => "The runner closed the connection before the response.",
                ObservationOutcomeType.Forwarded => "The target accepted the connection but closed it without a valid response.",
                // The target did answer; the injector's own pace is what ran out the clock.
                ObservationOutcomeType.Timeout when drip && observation.UpstreamStatusCode is not null =>
                    $"The slow read did not finish within {target.RequestTimeoutSeconds} s.",
                _ => TargetClient.Describe(exception is OperationCanceledException ? new TaskCanceledException() : exception,
                    target.RequestTimeoutSeconds)
            };
        }
        // Whatever part of the body arrived, also when the target cut it off or the timeout hit mid-body.
        string? body = null;
        if (observation.ResponseBytes is not null)
            // PostgreSQL jsonb rejects \u0000, which binary bodies contain.
            metadata["body"] = body = Redact(Encoding.UTF8.GetString(kept.GetBuffer(), 0, (int)kept.Length).Replace('\0', '�'));
        // Only an answer the target completed can be replaced. The upstream columns and "body" stay the target's own;
        // the client status and "clientBody" are what the runner was made to see. The fault works on the redacted
        // body, so cutting it can't leave half a secret behind.
        // shortcut: only the stored first 16 KB of a body are corrupted; corrupt the stream when larger bodies matter.
        if (inject && observation is { ErrorType: null, UpstreamStatusCode: { } status } && body is not null
            && FaultInjector.Apply(execution.ScenarioType, parameters, status, body) is { } injected)
        {
            observation.ClientStatusCode = injected.Status;
            observation.OutcomeType = ObservationOutcomeType.ModifiedResponse;
            if (injected.Body is not null)
                metadata["clientBody"] = injected.Body;
            if (injected.RetryAfterSeconds is { } seconds)
                metadata["clientHeaders"] = new Dictionary<string, string> { ["Retry-After"] = seconds.ToString() };
        }
        observation.DurationMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        observation.RequestCompletedAt = time.GetUtcNow();
        observation.Metadata = JsonSerializer.SerializeToDocument(metadata, JsonSerializerOptions.Web);
        return observation;
    }

    // Keeps the first 16 KB of the body in kept and counts every byte in ResponseBytes as it arrives, so a read that
    // fails part way still leaves what came before it. Reads at most `chunk` bytes at a time, with `pause` in between.
    private async Task ReadBodyAsync(
        HttpResponseMessage response, MemoryStream kept, HttpObservation observation, int chunk, TimeSpan pause,
        CancellationToken cancellationToken)
    {
        observation.ResponseBytes = 0;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[chunk];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            kept.Write(buffer, 0, (int)Math.Min(read, MaxStoredBodyBytes - kept.Length));
            observation.ResponseBytes += read;
            if (pause > TimeSpan.Zero)
                await Task.Delay(pause, time, cancellationToken);
        }
    }

    // An example from the spec: the object's own example, its first named example, or its schema's example, default or
    // first enum value.
    private static JsonElement? Example(JsonElement root, JsonElement owner)
    {
        if (owner.TryGetProperty("example", out var example))
            return example;
        if (owner.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Object)
            foreach (var named in examples.EnumerateObject())
                if (Deref(root, named.Value) is { ValueKind: JsonValueKind.Object } resolved && resolved.TryGetProperty("value", out var value))
                    return value;
        if (owner.TryGetProperty("schema", out var raw) && Deref(root, raw) is { ValueKind: JsonValueKind.Object } schema)
        {
            if (schema.TryGetProperty("example", out example) || schema.TryGetProperty("default", out example))
                return example;
            if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0)
                return values[0];
        }
        return null;
    }

    private static JsonElement? Deref(JsonElement root, JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$ref", out var reference)
            ? SpecDocument.Resolve(root, reference.GetString())
            : element;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // The request body for Timeout / Drop. hold: send the headers and never finish the body, so the target has to time
    // the request out. drop: send the whole request, then close the connection before the response.
    // shortcut: while holding, HTTP/1.1 can't read an early response (a 408), so a hold always records a timeout. Move
    // holds to the injector when that evidence is needed.
    private sealed class StalledContent(byte[] body, bool hold) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            if (hold)
            {
                await stream.FlushAsync(cancellationToken);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            throw new OperationCanceledException("The runner dropped the connection.");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        // A hold is chunked, so even a request without a body stays open.
        protected override bool TryComputeLength(out long length)
        {
            length = body.Length;
            return !hold;
        }
    }
}
