using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Features.TestRuns;
using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Backend.Tests.Integration.RunExecutorTests;

namespace Backend.Tests.Integration;

// Run creation, preflight, cancel and the live view. History, run detail and findings are covered in
// ProjectDashboardTests; the runner skipping executions of a cancelled run is covered in RunExecutorTests.
[Collection(PostgreSqlCollection.Name)]
public sealed class TestRunEndpointTests(PostgreSqlFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Create_run_validates_the_request_and_stores_a_queued_snapshot()
    {
        using var listener = StartTarget(new ConcurrentQueue<Received>(), out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var url = $"/api/projects/{projectId}/runs";
        var gets = scenarios.Where(s => s.HttpMethod == "GET" && s.Path == "/orders/{id}").Select(s => s.Id).ToList();
        var post = scenarios.First(s => s.HttpMethod == "POST").Id;

        async Task<string[]> Invalid(object request)
        {
            var response = await client.PostAsJsonAsync(url, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
            return errors.EnumerateObject().Select(error => error.Name).ToArray();
        }
        Assert.Equal(["scenarioIds"], await Invalid(new { scenarioIds = Array.Empty<Guid>() }));
        // Both errors in one response; their order is not part of the contract.
        Assert.Equal(["concurrency", "runnerRetries"],
            (await Invalid(new { scenarioIds = gets, concurrency = CreateRun.MaxConcurrency + 1, runnerRetries = -1 }))
                .Order(StringComparer.Ordinal));
        Assert.Equal(["scenarioIds"], await Invalid(new { scenarioIds = new[] { gets[0], Guid.NewGuid() } }));
        Assert.Equal(["confirmStateChanging"], await Invalid(new { scenarioIds = new[] { gets[0], post } }));

        var created = await client.PostAsJsonAsync(url, new { scenarioIds = gets, concurrency = 2, baselineRequests = false });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var runId = (await created.Content.ReadFromJsonAsync<CreateRun.Response>(Json))!.Id;
        Assert.Equal($"{url}/{runId}", created.Headers.Location!.ToString());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var run = await db.TestRuns.AsNoTracking().Include(r => r.ScenarioExecutions).SingleAsync(r => r.Id == runId);
        Assert.Equal((TestRunStatus.Queued, CreateRun.ScoringVersion, gets.Count), (run.Status, run.ScoringVersion, run.TotalScenarios));
        var config = run.RunConfig.RootElement;
        Assert.Equal((2, false, 0, 0), (config.GetProperty("concurrency").GetInt32(), config.GetProperty("baselineRequests").GetBoolean(),
            config.GetProperty("runnerRetries").GetInt32(), config.GetProperty("stateChangingScenarios").GetInt32()));
        Assert.Equal(baseUrl, config.GetProperty("target").GetProperty("baseUrl").GetString());
        Assert.Equal(gets.Order(), config.GetProperty("scenarioIds").EnumerateArray().Select(id => id.GetGuid()).Order());
        Assert.Equal(Enumerable.Range(1, gets.Count), run.ScenarioExecutions.Select(e => e.SequenceNo).Order());
        Assert.All(run.ScenarioExecutions, e => Assert.Equal(ScenarioExecutionStatus.Queued, e.Status));
    }

    [Fact]
    public async Task Preflight_reports_the_checks_and_blocks_runs_against_a_missing_or_unreachable_target()
    {
        using var listener = StartTarget(new ConcurrentQueue<Received>(), out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());

        var empty = (await (await client.PostAsJsonAsync("/api/projects", new { name = "Empty" }))
            .Content.ReadFromJsonAsync<ProjectResponse>())!.Id;
        var bare = (await client.GetFromJsonAsync<GetPreflight.Response>($"/api/projects/{empty}/runs/preflight", Json))!;
        Assert.Equal((null, null, null), (bare.Target, bare.Reachability, bare.Spec));
        Assert.Equal(2, bare.Blockers.Count);

        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var url = $"/api/projects/{projectId}/runs";
        var ready = (await client.GetFromJsonAsync<GetPreflight.Response>($"{url}/preflight", Json))!;
        Assert.Empty(ready.Blockers);
        Assert.True(ready.Target!.Authorized);
        Assert.True(ready.Reachability!.Reachable);
        Assert.Equal((5, 0), (ready.Spec!.OperationCount, ready.Spec.NeverTestCount));
        Assert.True(Assert.Single(ready.Credentials).Configured);

        // A missing credential is a warning only: the run can still be created.
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/projects/{projectId}/environment/credentials/bearerAuth")).StatusCode);
        var noCredential = (await client.GetFromJsonAsync<GetPreflight.Response>($"{url}/preflight", Json))!;
        Assert.False(Assert.Single(noCredential.Credentials).Configured);
        Assert.Empty(noCredential.Blockers);
        var gets = scenarios.Where(s => s.HttpMethod == "GET").Select(s => s.Id).ToList();
        await CreateRunAsync(client, projectId, gets);

        // The target goes down: preflight names it and CreateRun refuses without storing anything.
        listener.Stop();
        var down = (await client.GetFromJsonAsync<GetPreflight.Response>($"{url}/preflight", Json))!;
        Assert.False(down.Reachability!.Reachable);
        Assert.StartsWith("The target is unreachable", Assert.Single(down.Blockers));
        var refused = await client.PostAsJsonAsync(url, new { scenarioIds = gets });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.TestRuns.CountAsync(run => run.ProjectId == projectId));
    }

    [Fact]
    public async Task Cancel_stops_queued_and_running_runs_only_and_stays_in_its_project()
    {
        using var listener = StartTarget(new ConcurrentQueue<Received>(), out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var gets = scenarios.Where(s => s.HttpMethod == "GET").Select(s => s.Id).ToList();
        var queued = await CreateRunAsync(client, projectId, gets);
        var running = await CreateRunAsync(client, projectId, gets);
        var completed = await CreateRunAsync(client, projectId, gets);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.TestRuns.Where(r => r.Id == running).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, TestRunStatus.Running));
        await db.TestRuns.Where(r => r.Id == completed).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, TestRunStatus.Completed).SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow));
        var other = (await (await client.PostAsJsonAsync("/api/projects", new { name = "Other" }))
            .Content.ReadFromJsonAsync<ProjectResponse>())!.Id;

        Task<HttpResponseMessage> Cancel(Guid project, Guid run) => client.PostAsync($"/api/projects/{project}/runs/{run}/cancel", null);
        // Another project's URL never reaches this project's run.
        Assert.Equal(HttpStatusCode.NotFound, (await Cancel(other, queued)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Cancel(projectId, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Cancel(projectId, queued)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Cancel(projectId, running)).StatusCode);
        foreach (var finished in new[] { queued, completed })
        {
            var conflict = await Cancel(projectId, finished);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal(CancelRun.FinishedMessage, await conflict.Content.ReadFromJsonAsync<string>());
        }

        var runs = await db.TestRuns.AsNoTracking().Where(r => r.ProjectId == projectId).ToDictionaryAsync(r => r.Id);
        Assert.All(new[] { queued, running }, id =>
        {
            Assert.Equal(TestRunStatus.Cancelled, runs[id].Status);
            Assert.NotNull(runs[id].CompletedAt);
        });
        Assert.Equal(TestRunStatus.Completed, runs[completed].Status);
    }

    [Fact]
    public async Task Viewers_can_read_runs_but_not_check_create_or_cancel_them()
    {
        using var listener = StartTarget(new ConcurrentQueue<Received>(), out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var gets = scenarios.Where(s => s.HttpMethod == "GET").Select(s => s.Id).ToList();
        var runId = await CreateRunAsync(client, projectId, gets);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProjectMembers
                .Where(m => m.ProjectId == projectId).ExecuteUpdateAsync(m => m.SetProperty(x => x.Role, ProjectRole.Viewer));

        var url = $"/api/projects/{projectId}/runs";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{url}/preflight")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url, new { scenarioIds = gets })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{url}/{runId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{url}/{runId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Live_view_lists_started_executions_newest_first_and_the_oldest_running_one_with_its_requests()
    {
        using var listener = StartTarget(new ConcurrentQueue<Received>(), out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        var runId = await CreateRunAsync(client, projectId, scenarios.Where(s => s.HttpMethod == "GET").Select(s => s.Id).ToList());
        var url = $"/api/projects/{projectId}/runs/{runId}";

        var queued = (await client.GetFromJsonAsync<GetRun.Response>(url, Json))!.Live!;
        Assert.Empty(queued.Feed);
        Assert.Null(queued.Current);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var run = await db.TestRuns.Include(r => r.ScenarioExecutions).SingleAsync(r => r.Id == runId);
            var executions = run.ScenarioExecutions.OrderBy(e => e.SequenceNo).ToList();
            Assert.True(executions.Count > 3);
            var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
            run.Status = TestRunStatus.Running;
            run.StartedAt = t0;
            // #1 started first but finished last, so it tops the feed; #2 and #3 are still running.
            (executions[0].Status, executions[0].StartedAt, executions[0].CompletedAt) = (ScenarioExecutionStatus.Completed, t0, t0.AddSeconds(5));
            (executions[1].Status, executions[1].StartedAt) = (ScenarioExecutionStatus.Running, t0.AddSeconds(2));
            (executions[2].Status, executions[2].StartedAt) = (ScenarioExecutionStatus.Running, t0.AddSeconds(3));
            foreach (var (outcome, severity) in new[]
                     {
                         (ResilienceFindingOutcome.Passed, RecommendationSeverity.Critical),
                         (ResilienceFindingOutcome.Degraded, RecommendationSeverity.Low),
                         (ResilienceFindingOutcome.Failed, RecommendationSeverity.Low),
                         (ResilienceFindingOutcome.Failed, RecommendationSeverity.High)
                     })
                db.Add(new ResilienceFinding
                {
                    Id = Guid.NewGuid(), TestRunId = runId, EndpointId = executions[0].EndpointId, ScenarioExecutionId = executions[0].Id,
                    Outcome = outcome, Severity = severity, Summary = $"{outcome} {severity}", Evidence = JsonDocument.Parse("{}")
                });
            // Inserted out of order: the current execution's requests sort by start.
            foreach (var (attempt, at) in new[] { (2, t0.AddSeconds(4)), (1, t0.AddSeconds(2)) })
                db.Add(new HttpObservation
                {
                    Id = Guid.NewGuid(), ScenarioExecutionId = executions[1].Id, AttemptNo = attempt, RequestMethod = "GET",
                    RequestPath = "/orders/{id}", RequestStartedAt = at, ClientStatusCode = 200, Metadata = JsonDocument.Parse("{}")
                });
            await db.SaveChangesAsync();
            var ids = executions.Take(3).Select(e => e.Id).ToList();

            var live = (await client.GetFromJsonAsync<GetRun.Response>(url, Json))!.Live!;

            Assert.Equal([ids[0], ids[2], ids[1]], live.Feed.Select(row => row.ExecutionId));
            Assert.Equal(["Failed High", "Failed Low", "Degraded Low", "Passed Critical"], live.Feed[0].Findings.Select(f => f.Summary));
            Assert.All(live.Feed.Skip(1), row => Assert.Empty(row.Findings));
            Assert.Equal(ids[1], live.Current!.ExecutionId);
            Assert.Equal([1, 2], live.Current.Events.Select(e => e.AttemptNo));
            Assert.Equal(JsonValueKind.Object, live.Current.Parameters.ValueKind);
        }
    }
}
