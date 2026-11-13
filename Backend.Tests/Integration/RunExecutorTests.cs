using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Features.Scenarios;
using Backend.Features.TestRuns;
using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class RunExecutorTests(PostgreSqlFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string Token = "executor-test-token";

    private const string Spec = """
        openapi: 3.0.3
        info: {title: Orders, version: "1.0"}
        security: [{bearerAuth: []}]
        components:
          securitySchemes:
            bearerAuth: {type: http, scheme: bearer}
        paths:
          /orders/{id}:
            get:
              parameters: [{name: id, in: path, required: true, schema: {type: integer, example: 7}}]
              responses: {"200": {description: ok, content: {application/json: {schema: {type: object}}}}}
          /orders:
            post:
              parameters: [{name: Idempotency-Key, in: header, schema: {type: string}}]
              requestBody:
                required: true
                content: {application/vnd.orders+json: {example: {item: book}}}
              responses: {"201": {description: created}, "503": {description: down}}
          /items/{sku}:
            get:
              parameters: [{name: sku, in: path, required: true, schema: {type: string}}]
              responses: {"200": {description: ok}}
          /stream:
            get:
              responses: {"200": {description: ok}}
          /echo:
            get:
              responses: {"200": {description: ok}}
        """;

    private sealed record Received(
        string Method, string Path, string? Authorization, string? IdempotencyKey, string? ContentType, string Body,
        string? Cookie, string Query);

    [Fact]
    public async Task Runs_direct_faults_records_every_request_and_finishes_the_run()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);

        Guid Pick(string method, string path, ScenarioType type) =>
            scenarios.Single(s => s.HttpMethod == method && s.Path == path && s.Type == type).Id;
        var latency = Pick("GET", "/orders/{id}", ScenarioType.Latency);
        var drop = Pick("GET", "/orders/{id}", ScenarioType.TimeoutDrop);
        var malformed = Pick("GET", "/orders/{id}", ScenarioType.MalformedJson);
        var duplicate = Pick("POST", "/orders", ScenarioType.DuplicateRequest);
        var noExample = Pick("GET", "/items/{sku}", ScenarioType.Latency);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"/api/projects/{projectId}/scenarios/{latency}",
            new { parameters = new { delayMs = 10 } })).StatusCode);
        var runId = await CreateRunAsync(client, projectId, [latency, drop, malformed, duplicate, noExample]);

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        Assert.True(await executor.TickAsync(CancellationToken.None));

        var run = await client.GetFromJsonAsync<GetRun.Response>($"/api/projects/{projectId}/runs/{runId}", Json);
        // MalformedJson needs the injector and /items/{sku} has no example, so only some scenarios ran.
        Assert.Equal((TestRunStatus.PartiallyCompleted, 5, 5, 1), (run!.Status, run.CompletedScenarios, run.TotalScenarios, run.SkippedScenarios));
        Assert.NotNull(run.StartedAt);
        Assert.NotNull(run.CompletedAt);
        Assert.Null(run.Live);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var executions = await db.ScenarioExecutions.AsNoTracking().Include(e => e.Observations)
            .Where(e => e.TestRunId == runId).ToDictionaryAsync(e => e.ScenarioId);

        Assert.Equal((ScenarioExecutionStatus.Skipped, RunExecutor.InjectorNeeded),
            (executions[malformed].Status, executions[malformed].FailureReason));
        Assert.Empty(executions[malformed].Observations);
        Assert.Equal(ScenarioExecutionStatus.Failed, executions[noExample].Status);
        Assert.Contains("\"sku\"", executions[noExample].FailureReason);

        // Baseline, then the delayed request; both on the spec path, authenticated, with the example id filled in.
        var latencyObservations = executions[latency].Observations.OrderBy(o => o.AttemptNo).ToList();
        Assert.Equal(ScenarioExecutionStatus.Completed, executions[latency].Status);
        Assert.Equal([(1, 200), (2, 200)], latencyObservations.Select(o => (o.AttemptNo, o.UpstreamStatusCode ?? 0)));
        Assert.All(latencyObservations, o => Assert.Equal(("GET", "/orders/{id}", ObservationOutcomeType.Forwarded),
            (o.RequestMethod, o.RequestPath, o.OutcomeType)));
        Assert.Equal(10, latencyObservations[1].Metadata.RootElement.GetProperty("delayMs").GetInt32());
        Assert.Equal("{\"id\":7}", latencyObservations[1].Metadata.RootElement.GetProperty("body").GetString());

        var dropObservations = executions[drop].Observations.OrderBy(o => o.AttemptNo).ToList();
        Assert.Equal([ObservationOutcomeType.Forwarded, ObservationOutcomeType.Dropped], dropObservations.Select(o => o.OutcomeType));
        Assert.Null(dropObservations[1].UpstreamStatusCode);

        // Two copies, no baseline, sharing one idempotency key.
        var copies = executions[duplicate].Observations.OrderBy(o => o.AttemptNo).ToList();
        Assert.Equal([1, 2], copies.Select(o => o.AttemptNo));
        Assert.Single(copies.Select(o => o.CorrelationId).Distinct());
        Assert.NotNull(copies[0].CorrelationId);

        var posts = received.Where(r => r.Method == "POST").ToList();
        Assert.Equal(2, posts.Count);
        // The body goes out as the spec's own JSON media type.
        Assert.All(posts, post => Assert.Equal("application/vnd.orders+json", post.ContentType));
        Assert.All(posts, post => Assert.Equal(("/v1/orders", "book", copies[0].CorrelationId.ToString()),
            (post.Path, JsonDocument.Parse(post.Body).RootElement.GetProperty("item").GetString(), post.IdempotencyKey)));
        // The preflight probe calls the base URL itself, without the credential.
        Assert.All(received.Where(r => r.Path != "/v1"), r => Assert.Equal($"Bearer {Token}", r.Authorization));
        Assert.Contains(received, r => r.Method == "GET" && r.Path == "/v1/orders/7");
        // The target sets a session cookie on every response; no later request sends it back.
        Assert.All(received, r => Assert.Null(r.Cookie));
    }

    [Fact]
    public async Task Runs_cancelled_while_queued_never_start_and_their_scenarios_are_skipped()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var runId = await CreateRunAsync(client, projectId,
            scenarios.Where(s => s.HttpMethod == "GET" && s.Path == "/orders/{id}").Select(s => s.Id).ToList());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/projects/{projectId}/runs/{runId}/cancel", null)).StatusCode);
        var probes = received.Count;

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        Assert.False(await executor.TickAsync(CancellationToken.None));

        var run = (await client.GetFromJsonAsync<GetRun.Response>($"/api/projects/{projectId}/runs/{runId}", Json))!;
        Assert.Equal((TestRunStatus.Cancelled, 0, run.TotalScenarios), (run.Status, run.CompletedScenarios, run.SkippedScenarios));
        Assert.Null(run.StartedAt);
        Assert.Equal(probes, received.Count);
    }

    [Fact]
    public async Task Records_held_requests_cut_off_bodies_and_each_idempotency_key_mode()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl, requestTimeoutSeconds: 1);

        Guid Pick(string method, string path, ScenarioType type) =>
            scenarios.Single(s => s.HttpMethod == method && s.Path == path && s.Type == type).Id;
        var hold = Pick("GET", "/orders/{id}", ScenarioType.TimeoutDrop);
        var cut = Pick("GET", "/stream", ScenarioType.Latency);
        var duplicate = Pick("POST", "/orders", ScenarioType.DuplicateRequest);
        await SetParametersAsync(client, projectId, hold, new { mode = "hold" });
        await SetParametersAsync(client, projectId, cut, new { delayMs = 0 });
        await SetParametersAsync(client, projectId, duplicate, new { copies = 3, key = "different", delayMs = 0 });
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/projects/{projectId}/environment/headers",
            new { name = "Content-Type", value = "application/vnd.api+json", secret = false, appliesTo = new[] { "POST /orders" } })).StatusCode);
        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        var first = await CreateRunAsync(client, projectId, [hold, cut, duplicate]);
        Assert.True(await executor.TickAsync(CancellationToken.None));
        await SetParametersAsync(client, projectId, duplicate, new { copies = 2, key = "none", delayMs = 0 });
        var second = await CreateRunAsync(client, projectId, [duplicate]);
        Assert.True(await executor.TickAsync(CancellationToken.None));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var executions = await db.ScenarioExecutions.AsNoTracking().Include(e => e.Observations)
            .Where(e => e.TestRunId == first).ToDictionaryAsync(e => e.ScenarioId);
        Assert.All(executions.Values, e => Assert.Equal(ScenarioExecutionStatus.Completed, e.Status));

        // Baseline, then the request held open past the 1 s timeout.
        Assert.Equal([ObservationOutcomeType.Forwarded, ObservationOutcomeType.Timeout],
            executions[hold].Observations.OrderBy(o => o.AttemptNo).Select(o => o.OutcomeType));

        // A body the target cuts off is the target's behaviour, recorded with its status, not a runner error.
        Assert.Equal(2, executions[cut].Observations.Count);
        Assert.All(executions[cut].Observations, o => Assert.Equal(
            (ObservationOutcomeType.Forwarded, 200, "The target closed the connection before the response body ended."),
            (o.OutcomeType, o.UpstreamStatusCode ?? 0, o.ErrorType)));

        // "different": one key per copy, sent as the Idempotency-Key, with the global Content-Type on the body.
        var keys = executions[duplicate].Observations.Select(o => o.CorrelationId).ToList();
        Assert.Equal(3, keys.Distinct().Count(key => key is not null));
        var posts = received.Where(r => r.Method == "POST").ToList();
        Assert.Equal(5, posts.Count);
        Assert.Equal(keys.Select(key => key.ToString()).Order(), posts.Take(3).Select(p => p.IdempotencyKey).Order());
        Assert.All(posts, post => Assert.Equal("application/vnd.api+json", post.ContentType));

        // "none": no key at all.
        var unkeyed = await db.HttpObservations.AsNoTracking().Where(o => o.ScenarioExecution.TestRunId == second).ToListAsync();
        Assert.Equal(2, unkeyed.Count);
        Assert.All(unkeyed, o => Assert.Null(o.CorrelationId));
        Assert.All(posts.Skip(3), post => Assert.Null(post.IdempotencyKey));
    }

    [Fact]
    public async Task Runs_whose_environment_moved_to_another_host_fail_without_sending_anything()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var runId = await CreateRunAsync(client, projectId,
            scenarios.Where(s => s.HttpMethod == "GET" && s.Path == "/orders/{id}").Select(s => s.Id).ToList());

        // Moved while the run is queued: the old secret is cleared and a new one entered for the new host.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/projects/{projectId}/environment",
            new { baseUrl = "http://127.0.0.2:9/v1", kind = "Staging", requestTimeoutSeconds = 5, verifyTls = true, authorized = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/projects/{projectId}/environment/credentials/bearerAuth",
            new { authType = "Bearer", secret = "new-host-token" })).StatusCode);
        var sent = received.Count;

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        Assert.True(await executor.TickAsync(CancellationToken.None));

        var run = (await client.GetFromJsonAsync<GetRun.Response>($"/api/projects/{projectId}/runs/{runId}", Json))!;
        Assert.Equal((TestRunStatus.Failed, run.TotalScenarios), (run.Status, run.SkippedScenarios));
        Assert.Contains("another host", run.FailureMessage);
        Assert.Equal(sent, received.Count);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.HttpObservations.AnyAsync(o => o.ScenarioExecution.TestRunId == runId));
    }

    [Fact]
    public async Task Runs_authenticate_with_the_schemes_of_their_own_spec()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var latency = scenarios.Single(s => s.HttpMethod == "GET" && s.Path == "/orders/{id}" && s.Type == ScenarioType.Latency).Id;
        await SetParametersAsync(client, projectId, latency, new { delayMs = 0 });
        await CreateRunAsync(client, projectId, [latency]);

        // Re-imported while the run is queued, with the scheme renamed: the active spec no longer has bearerAuth.
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/projects/{projectId}/spec",
            new { source = "Paste", content = Spec.Replace("bearerAuth", "tokenAuth") })).StatusCode);
        var sent = received.Count;

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        Assert.True(await executor.TickAsync(CancellationToken.None));

        var requests = received.Skip(sent).ToList();
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal($"Bearer {Token}", r.Authorization));
    }

    [Fact]
    public async Task Recovery_fails_runs_and_scenarios_left_running()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var runId = await CreateRunAsync(client, projectId,
            scenarios.Where(s => s.HttpMethod == "GET" && s.Path == "/orders/{id}").Select(s => s.Id).ToList());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // What a stopped process leaves behind: the run and its first scenario Running.
        await db.TestRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, TestRunStatus.Running));
        await db.ScenarioExecutions.Where(e => e.TestRunId == runId && e.SequenceNo == 1)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, ScenarioExecutionStatus.Running));
        var sent = received.Count;

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        await executor.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (await db.ScenarioExecutions.AnyAsync(e => e.TestRunId == runId
                       && (e.Status == ScenarioExecutionStatus.Queued || e.Status == ScenarioExecutionStatus.Running)))
            {
                Assert.True(DateTime.UtcNow < deadline, "Recovery did not finish the run's scenarios.");
                await Task.Delay(100);
            }
        }
        finally
        {
            await executor.StopAsync(CancellationToken.None);
        }

        var run = (await client.GetFromJsonAsync<GetRun.Response>($"/api/projects/{projectId}/runs/{runId}", Json))!;
        Assert.Equal((TestRunStatus.Failed, "The runner stopped before this run finished."), (run.Status, run.FailureMessage));
        var executions = await db.ScenarioExecutions.AsNoTracking().Where(e => e.TestRunId == runId).OrderBy(e => e.SequenceNo).ToListAsync();
        Assert.Equal((ScenarioExecutionStatus.Failed, "The runner stopped during this scenario."),
            (executions[0].Status, executions[0].FailureReason));
        Assert.All(executions.Skip(1), e => Assert.Equal(ScenarioExecutionStatus.Skipped, e.Status));
        Assert.Equal(sent, received.Count);
    }

    [Fact]
    public async Task A_connection_reset_mid_body_is_recorded_as_the_targets_cut_off_response()
    {
        using var listener = StartRawTarget(out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var cut = scenarios.Single(s => s.HttpMethod == "GET" && s.Path == "/stream" && s.Type == ScenarioType.Latency).Id;
        await SetParametersAsync(client, projectId, cut, new { delayMs = 0 });
        var runId = await CreateRunAsync(client, projectId, [cut]);

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        Assert.True(await executor.TickAsync(CancellationToken.None));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var execution = await db.ScenarioExecutions.AsNoTracking().Include(e => e.Observations).SingleAsync(e => e.TestRunId == runId);
        Assert.Equal((ScenarioExecutionStatus.Completed, 2), (execution.Status, execution.Observations.Count));
        Assert.All(execution.Observations, o => Assert.Equal(
            (ObservationOutcomeType.Forwarded, 200, "The target closed the connection before the response body ended."),
            (o.OutcomeType, o.UpstreamStatusCode ?? 0, o.ErrorType)));
        // The 5 bytes that arrived before the reset are kept as evidence.
        Assert.All(execution.Observations, o => Assert.Equal((5L, "{\"id\""),
            (o.ResponseBytes ?? 0, o.Metadata.RootElement.GetProperty("body").GetString())));
    }

    [Fact]
    public async Task Operations_needing_several_schemes_get_every_credential_or_none()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        // One requirement with two schemes: the bearer token and an app key in the query, both needed together.
        var spec = Spec
            .Replace("security: [{bearerAuth: []}]", "security: [{bearerAuth: [], appKey: []}]")
            .Replace("bearerAuth: {type: http, scheme: bearer}",
                "bearerAuth: {type: http, scheme: bearer}\n    appKey: {type: apiKey, in: query, name: app_key}")
            .Replace("schema: {type: integer, example: 7}}]",
                "schema: {type: integer, example: 7}}, {name: fields, in: query, required: true, schema: {type: string, example: all}}]");
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl, spec: spec);
        var order = scenarios.Single(s => s.HttpMethod == "GET" && s.Path == "/orders/{id}" && s.Type == ScenarioType.Latency).Id;
        await SetParametersAsync(client, projectId, order, new { delayMs = 0 });
        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        List<Received> Orders(int skip) => received.Skip(skip).Where(r => r.Path == "/v1/orders/7").ToList();

        // Only the bearer token is stored: half a requirement is not sent at all.
        var sent = received.Count;
        await CreateRunAsync(client, projectId, [order]);
        Assert.True(await executor.TickAsync(CancellationToken.None));
        var partial = Orders(sent);
        Assert.Equal(2, partial.Count);
        Assert.All(partial, r => Assert.Equal(((string?)null, "?fields=all"), (r.Authorization, r.Query)));

        // With both stored, both go out, and the key joins the operation's own query instead of replacing it.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/projects/{projectId}/environment/credentials/appKey",
            new { authType = "ApiKey", secret = "app-key-0123456789" })).StatusCode);
        sent = received.Count;
        await CreateRunAsync(client, projectId, [order]);
        Assert.True(await executor.TickAsync(CancellationToken.None));
        var full = Orders(sent);
        Assert.Equal(2, full.Count);
        Assert.All(full, r => Assert.Equal(($"Bearer {Token}", "?fields=all&app_key=app-key-0123456789"), (r.Authorization, r.Query)));
    }

    [Fact]
    public async Task Scenarios_whose_requests_never_reach_the_target_are_skipped()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var duplicate = scenarios.Single(s => s.HttpMethod == "POST" && s.Path == "/orders" && s.Type == ScenarioType.DuplicateRequest).Id;
        var runId = await CreateRunAsync(client, projectId, [duplicate]);
        // The target goes down after the run was created. Duplicate requests have no baseline request to notice it.
        listener.Close();

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        Assert.True(await executor.TickAsync(CancellationToken.None));

        var run = (await client.GetFromJsonAsync<GetRun.Response>($"/api/projects/{projectId}/runs/{runId}", Json))!;
        Assert.Equal((TestRunStatus.Failed, 1), (run.Status, run.SkippedScenarios));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var execution = await db.ScenarioExecutions.AsNoTracking().Include(e => e.Observations).SingleAsync(e => e.TestRunId == runId);
        Assert.Equal(ScenarioExecutionStatus.Skipped, execution.Status);
        Assert.StartsWith("The target was unreachable", execution.FailureReason);
        Assert.Equal(2, execution.Observations.Count);
        Assert.All(execution.Observations, o => Assert.Equal(ObservationOutcomeType.ConnectionError, o.OutcomeType));
    }

    [Fact]
    public async Task Stored_responses_never_contain_the_credential()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var echo = scenarios.Single(s => s.HttpMethod == "GET" && s.Path == "/echo" && s.Type == ScenarioType.Latency).Id;
        await SetParametersAsync(client, projectId, echo, new { delayMs = 0 });
        var runId = await CreateRunAsync(client, projectId, [echo]);

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        Assert.True(await executor.TickAsync(CancellationToken.None));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var observations = await db.HttpObservations.AsNoTracking().Where(o => o.ScenarioExecution.TestRunId == runId).ToListAsync();
        Assert.Equal(2, observations.Count);
        // The target echoed the Authorization header in the body and in a response header; neither copy is stored.
        Assert.All(observations, o =>
        {
            var metadata = o.Metadata.RootElement;
            Assert.DoesNotContain(Token, metadata.GetRawText());
            Assert.Equal("{\"authorization\":\"Bearer [redacted]\"}", metadata.GetProperty("body").GetString());
            Assert.Equal("Bearer [redacted]", metadata.GetProperty("headers").GetProperty("X-Echo").GetString());
        });
    }

    [Fact]
    public async Task A_target_that_closes_without_answering_failed_the_experiment_and_got_each_request_once()
    {
        var unanswered = new ConcurrentQueue<string>();
        using var listener = StartRawTarget(out var baseUrl, unanswered);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var silent = scenarios.Single(s => s.HttpMethod == "GET" && s.Path == "/stream" && s.Type == ScenarioType.Latency).Id;
        await SetParametersAsync(client, projectId, silent, new { delayMs = 0 });
        var runId = await CreateRunAsync(client, projectId, [silent]);

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        Assert.True(await executor.TickAsync(CancellationToken.None));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var execution = await db.ScenarioExecutions.AsNoTracking().Include(e => e.Observations).SingleAsync(e => e.TestRunId == runId);
        // The target was reached, so this is its failure during the experiment, not a skipped scenario.
        Assert.Equal((ScenarioExecutionStatus.Completed, 2), (execution.Status, execution.Observations.Count));
        Assert.All(execution.Observations, o => Assert.Equal(
            (ObservationOutcomeType.Forwarded, (int?)null, "The target accepted the connection but closed it without a valid response."),
            (o.OutcomeType, o.UpstreamStatusCode, o.ErrorType)));
        // One connection per stored request: the empty body kept the HTTP handler from re-sending them on its own.
        Assert.Equal(2, unanswered.Count);
        Assert.All(unanswered, head => Assert.Contains("Content-Length: 0", head));
    }

    [Fact]
    public async Task Short_credentials_are_not_redacted_so_ordinary_values_stay_intact()
    {
        var received = new ConcurrentQueue<Received>();
        using var listener = StartTarget(received, out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        // A one-character credential that is also the order's id.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/projects/{projectId}/environment/credentials/bearerAuth",
            new { authType = "Bearer", secret = "7" })).StatusCode);
        var order = scenarios.Single(s => s.HttpMethod == "GET" && s.Path == "/orders/{id}" && s.Type == ScenarioType.Latency).Id;
        await SetParametersAsync(client, projectId, order, new { delayMs = 0 });
        var runId = await CreateRunAsync(client, projectId, [order]);

        var executor = ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services);
        Assert.True(await executor.TickAsync(CancellationToken.None));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var observations = await db.HttpObservations.AsNoTracking().Where(o => o.ScenarioExecution.TestRunId == runId).ToListAsync();
        Assert.Equal(2, observations.Count);
        Assert.All(observations, o => Assert.Equal("{\"id\":7}", o.Metadata.RootElement.GetProperty("body").GetString()));
    }

    private static async Task SetParametersAsync(HttpClient client, Guid projectId, Guid scenarioId, object parameters) =>
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync($"/api/projects/{projectId}/scenarios/{scenarioId}", new { parameters })).StatusCode);

    // A project with the spec imported, the loopback target and a bearer credential, and generated scenarios.
    private static async Task<(Guid, List<GetScenarios.Row>)> SetUpAsync(
        HttpClient client, string baseUrl, int requestTimeoutSeconds = 5, string spec = Spec)
    {
        var created = await client.PostAsJsonAsync("/api/projects", new { name = "Orders" });
        var projectId = (await created.Content.ReadFromJsonAsync<ProjectResponse>())!.Id;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/projects/{projectId}/spec", new { source = "Paste", content = spec })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/projects/{projectId}/environment",
            new { baseUrl, kind = "Staging", requestTimeoutSeconds, verifyTls = true, authorized = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/projects/{projectId}/environment/credentials/bearerAuth",
            new { authType = "Bearer", secret = Token })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/projects/{projectId}/scenarios/generate", null)).StatusCode);
        var scenarios = await client.GetFromJsonAsync<GetScenarios.Response>($"/api/projects/{projectId}/scenarios", Json);
        return (projectId, scenarios!.Scenarios);
    }

    private static async Task<Guid> CreateRunAsync(HttpClient client, Guid projectId, List<Guid> scenarioIds)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/runs", new { scenarioIds, confirmStateChanging = true });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateRun.Response>(Json))!.Id;
    }

    // A loopback target that records every request and echoes the path's last segment as {"id": n}. /stream promises
    // 100 bytes, sends 5 and closes the connection. Every response sets a session cookie.
    private static HttpListener StartTarget(ConcurrentQueue<Received> received, out string baseUrl)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        baseUrl = $"http://127.0.0.1:{port}/v1";
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception) { return; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var request = context.Request;
                        using var reader = new StreamReader(request.InputStream);
                        var body = await reader.ReadToEndAsync();
                        received.Enqueue(new(request.HttpMethod, request.Url!.AbsolutePath, request.Headers["Authorization"],
                            request.Headers[RunExecutor.IdempotencyHeader], request.ContentType, body,
                            request.Headers["Cookie"], request.Url!.Query));
                        context.Response.Headers.Add("Set-Cookie", "session=leaked; Path=/");
                        var last = request.Url.Segments[^1];
                        if (last == "stream")
                        {
                            context.Response.ContentLength64 = 100;
                            await context.Response.OutputStream.WriteAsync("{\"id\""u8.ToArray());
                            context.Response.Abort();
                            return;
                        }
                        var echo = last == "echo" ? request.Headers["Authorization"] : null;
                        if (echo is not null)
                            context.Response.Headers["X-Echo"] = echo;
                        var payload = Encoding.UTF8.GetBytes(int.TryParse(last, out var id) ? $"{{\"id\":{id}}}"
                            : echo is not null ? $"{{\"authorization\":\"{echo}\"}}" : "{}");
                        context.Response.ContentType = "application/json";
                        await context.Response.OutputStream.WriteAsync(payload);
                        context.Response.Close();
                    }
                    // A dropped request has no one left to answer.
                    catch (Exception) { }
                });
            }
        });
        return listener;
    }

    // A raw target for what HttpListener can't do: it answers the preflight probe of the base URL, and resets every
    // other connection after the response headers and 5 of the 100 promised bytes. With `unanswered`, it queues those
    // requests there and closes them cleanly without any response instead.
    private static TcpListener StartRawTarget(out string baseUrl, ConcurrentQueue<string>? unanswered = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        baseUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/v1";
        _ = Task.Run(async () =>
        {
            while (true)
            {
                Socket socket;
                try { socket = await listener.AcceptSocketAsync(); }
                catch (Exception) { return; }
                _ = Task.Run(async () =>
                {
                    using var connection = socket;
                    try
                    {
                        var buffer = new byte[4096];
                        var head = new StringBuilder();
                        while (!head.ToString().Contains("\r\n\r\n"))
                        {
                            var read = await socket.ReceiveAsync(buffer);
                            if (read == 0)
                                return;
                            head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        }
                        var probe = head.ToString().StartsWith("GET /v1 ");
                        if (!probe && unanswered is not null)
                        {
                            unanswered.Enqueue(head.ToString());
                            return;
                        }
                        await socket.SendAsync(Encoding.ASCII.GetBytes(probe
                            ? "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                            : "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n{\"id\""));
                        if (probe)
                            return;
                        // The runner has read the headers by now. Linger 0 makes the close a reset (RST), not a clean end (FIN).
                        await Task.Delay(300);
                        socket.LingerState = new LingerOption(true, 0);
                    }
                    catch (Exception) { }
                });
            }
        });
        return listener;
    }
}
