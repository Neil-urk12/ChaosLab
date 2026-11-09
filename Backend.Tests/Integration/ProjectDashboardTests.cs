using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Findings;
using Backend.Features.Projects;
using Backend.Features.TestRuns;
using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProjectDashboardTests(PostgreSqlFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Project_detail_reports_latest_scored_run_and_hides_non_member_projects()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var seeded = await SeedAsync(factory, client);

        var detail = await client.GetFromJsonAsync<GetProject.Response>($"/api/projects/{seeded.ProjectId}", Json);

        Assert.NotNull(detail);
        Assert.Equal(new GetProject.EnvironmentInfo("Prod-like test", "https://api.example.test"), detail.DefaultEnvironment);
        Assert.Equal(3, detail.EndpointCount);
        Assert.Equal(2, detail.ScenarioCount);
        Assert.Equal(2, detail.CompletedRunCount);
        Assert.Equal(2, detail.FailedFindingCount);
        Assert.Equal(seeded.LatestRunId, detail.Latest!.RunId);
        Assert.Equal(78m, detail.Latest.Score);
        Assert.Equal(70m, detail.Latest.PreviousScore);
        Assert.Equal(70m, detail.Latest.Breakdown!.Retry);
        Assert.Equal(90m, detail.Latest.Breakdown.Timeout);
        Assert.Null(detail.Latest.Breakdown.Fallback);
        Assert.Equal("3.0.3", detail.Spec!.OpenApiVersion);
        Assert.Equal(ProjectRole.Owner, Assert.Single(detail.Members).Role);
        Assert.All(detail.Findings, finding => Assert.Equal(seeded.LatestRunId, seeded.FindingRuns[finding.Id]));
        Assert.Equal(2, detail.Findings.Count);
        Assert.Equal(["Running", "Completed", "Completed"], detail.RecentRuns.Select(run => run.Status.ToString()));
        Assert.Equal(2, detail.RecentRuns[1].FailedFindings);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{seeded.OtherProjectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Overview_aggregates_only_member_projects()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var seeded = await SeedAsync(factory, client);

        var overview = await client.GetFromJsonAsync<GetOverview.Response>("/api/projects/overview", Json);

        Assert.NotNull(overview);
        Assert.Equal(1, overview.ProjectCount);
        Assert.Equal(3, overview.EndpointCount);
        Assert.Equal(3, overview.RunsLast7Days);
        Assert.Equal(74m, overview.AverageScore);
        Assert.Equal(2, overview.ScoredRunsLast30Days);
        Assert.Equal(2, overview.FailedFindingCount);
        Assert.Equal([70m, 78m], overview.ScoreTrend.Select(point => point.Score));
        Assert.All(overview.Findings, finding => Assert.Equal(seeded.ProjectId, finding.ProjectId));
        Assert.All(overview.RecentRuns, run => Assert.Equal(seeded.ProjectId, run.ProjectId));
        var project = Assert.Single(overview.RecentProjects);
        Assert.Equal((78m, 2, "https://api.example.test"), (project.LatestScore, project.FailedFindings, project.BaseUrl));
    }

    [Fact]
    public async Task List_reports_inventory_columns_for_member_projects()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var seeded = await SeedAsync(factory, client);

        var row = Assert.Single((await client.GetFromJsonAsync<List<ListProjects.Row>>("/api/projects", Json))!);

        Assert.Equal(seeded.ProjectId, row.Id);
        Assert.Equal(("Prod-like test", "https://api.example.test"), (row.EnvironmentName, row.BaseUrl));
        Assert.Equal(2, row.ScenarioCount);
        Assert.Equal(78m, row.LatestScore);
        Assert.NotNull(row.LastRunAt);
    }

    [Fact]
    public async Task Run_history_numbers_runs_and_reports_score_change_and_coverage()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var seeded = await SeedAsync(factory, client);

        var url = $"/api/projects/{seeded.ProjectId}/runs";
        async Task<ListRuns.Response> Get(string query = "") =>
            (await client.GetFromJsonAsync<ListRuns.Response>(url + query, Json))!;

        var all = await Get();
        var runs = all.Runs;
        Assert.Equal((3, 3, 1), (all.Total, all.Matched, all.Page));
        Assert.Equal(["Prod-like test"], all.Environments);
        Assert.Equal(["1.0"], all.ScoringVersions);
        Assert.Equal([3, 2, 1], runs.Select(run => run.Number));
        Assert.Equal(TestRunStatus.Running, runs[0].Status);
        Assert.Null(runs[0].EvidenceCoverage);
        Assert.Equal((seeded.LatestRunId, 78m, 70m), (runs[1].Id, runs[1].Score, runs[1].PreviousScore));
        Assert.Equal(100, runs[1].EvidenceCoverage);
        Assert.Null(runs[2].PreviousScore);

        var second = await Get("?pageSize=2&page=2");
        Assert.Equal((3, 2), (second.Matched, second.Page));
        Assert.Equal([1], second.Runs.Select(run => run.Number));
        Assert.Equal(2, (await Get("?pageSize=2&page=9")).Page);
        Assert.Equal([2], (await Get("?query=RUN-002")).Runs.Select(run => run.Number));
        Assert.Equal(3, (await Get("?query=admin")).Matched);
        Assert.Equal(0, (await Get("?query=nobody")).Matched);
        Assert.Equal([3], (await Get("?status=Running")).Runs.Select(run => run.Number));
        Assert.Equal(0, (await Get("?environment=Staging")).Matched);
        Assert.Equal([3, 2], (await Get("?days=2")).Runs.Select(run => run.Number));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url + "?pageSize=500")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url + "?status=Bogus")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/projects/{seeded.OtherProjectId}/runs")).StatusCode);
    }

    [Fact]
    public async Task Run_history_score_change_compares_only_completed_runs_on_same_environment_and_scoring_version()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var seeded = await SeedAsync(factory, client);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var latest = await db.TestRuns.SingleAsync(run => run.Id == seeded.LatestRunId);
            var staging = new ApiEnvironment
            {
                Id = Guid.NewGuid(), ProjectId = seeded.ProjectId, Name = "Staging", BaseUrl = "https://staging.example.test",
                CreatedById = latest.RequestedById
            };
            // Each scored between the 70 run (-2d) and the 78 run (-1d), and each must be skipped.
            var now = DateTimeOffset.UtcNow;
            var otherEnvironment = Run(seeded.ProjectId, latest.ApiSpecId, staging.Id, latest.RequestedById, now.AddHours(-36), 40);
            var otherVersion = Run(seeded.ProjectId, latest.ApiSpecId, latest.EnvironmentId, latest.RequestedById, now.AddHours(-32), 50);
            otherVersion.ScoringVersion = "2.0";
            var partial = Run(seeded.ProjectId, latest.ApiSpecId, latest.EnvironmentId, latest.RequestedById, now.AddHours(-28), 60);
            partial.Status = TestRunStatus.PartiallyCompleted;
            db.AddRange(staging, otherEnvironment, otherVersion, partial);
            await db.SaveChangesAsync();
        }

        var all = await client.GetFromJsonAsync<ListRuns.Response>($"/api/projects/{seeded.ProjectId}/runs", Json);
        Assert.Equal(70m, all!.Runs.Single(run => run.Id == seeded.LatestRunId).PreviousScore);
    }

    [Fact]
    public async Task Findings_default_to_latest_completed_run_and_carry_severity_and_recommendation()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var seeded = await SeedAsync(factory, client);
        var critical = seeded.FindingRuns.First(pair => pair.Value == seeded.LatestRunId).Key;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var finding = await db.ResilienceFindings.SingleAsync(row => row.Id == critical);
            finding.Severity = RecommendationSeverity.Critical;
            finding.Outcome = ResilienceFindingOutcome.Degraded;
            var scenario = await db.ChaosScenarios.SingleAsync(row =>
                row.EndpointId == finding.EndpointId && row.ScenarioType == ScenarioType.TimeoutDrop);
            var execution = new ScenarioExecution
            {
                Id = Guid.NewGuid(), TestRunId = finding.TestRunId, EndpointId = finding.EndpointId, ScenarioId = scenario.Id,
                SequenceNo = 1, ScenarioType = ScenarioType.TimeoutDrop, Parameters = JsonDocument.Parse("{}")
            };
            db.Add(execution);
            finding.ScenarioExecutionId = execution.Id;
            db.Add(new Recommendation
            {
                Id = Guid.NewGuid(), TestRunId = finding.TestRunId, ResilienceFindingId = finding.Id, Category = "Idempotency",
                Severity = RecommendationSeverity.Critical, Title = "Persist keys", Description = "Generic",
                Remediation = "Persist the Idempotency-Key.", Evidence = JsonDocument.Parse("{}")
            });
            await db.SaveChangesAsync();
        }

        var url = $"/api/projects/{seeded.ProjectId}/findings";
        var latest = (await client.GetFromJsonAsync<ListFindings.Response>(url, Json))!;

        // The running run has no findings, so the picker offers the two completed runs, newest first.
        Assert.Equal([2, 1], latest.Runs.Select(run => run.Number));
        Assert.Equal(seeded.LatestRunId, latest.Run!.Id);
        Assert.Equal(3, latest.Findings.Count);
        var top = latest.Findings[0];
        Assert.Equal((critical, RecommendationSeverity.Critical, ResilienceFindingOutcome.Degraded), (top.Id, top.Severity, top.Outcome));
        Assert.Equal(new ListFindings.RecommendationInfo("Persist keys", "Persist the Idempotency-Key."), top.Recommendation);
        Assert.Null(latest.Findings[1].Recommendation);
        // ScenarioType comes from the linked execution; a finding without one gets null.
        Assert.Equal(ScenarioType.TimeoutDrop, top.ScenarioType);
        Assert.Null(latest.Findings[1].ScenarioType);

        var older = (await client.GetFromJsonAsync<ListFindings.Response>($"{url}?run={latest.Runs[1].Id}", Json))!;
        Assert.Equal(1, older.Run!.Number);
        Assert.Single(older.Findings);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{url}?run={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/projects/{seeded.OtherProjectId}/findings")).StatusCode);
    }

    [Fact]
    public async Task Findings_serve_a_requested_run_older_than_the_picker_list()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var seeded = await SeedAsync(factory, client);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var latest = await db.TestRuns.SingleAsync(run => run.Id == seeded.LatestRunId);
            var endpointId = await db.ResilienceFindings.Where(finding => finding.TestRunId == latest.Id)
                .Select(finding => finding.EndpointId).FirstAsync();
            // Enough newer runs with findings to push both seeded completed runs out of the picker.
            for (var i = 0; i < ListFindings.MaxRuns; i++)
            {
                var newer = Run(seeded.ProjectId, latest.ApiSpecId, latest.EnvironmentId, latest.RequestedById,
                    DateTimeOffset.UtcNow.AddMinutes(i + 1), 80);
                db.AddRange(newer, new ResilienceFinding
                {
                    Id = Guid.NewGuid(), TestRunId = newer.Id, EndpointId = endpointId,
                    Outcome = ResilienceFindingOutcome.Passed, Evidence = JsonDocument.Parse("{}")
                });
            }
            await db.SaveChangesAsync();
        }

        var url = $"/api/projects/{seeded.ProjectId}/findings";
        var newest = (await client.GetFromJsonAsync<ListFindings.Response>(url, Json))!;
        Assert.Equal(ListFindings.MaxRuns, newest.Runs.Count);
        Assert.DoesNotContain(newest.Runs, run => run.Id == seeded.LatestRunId);

        var old = (await client.GetFromJsonAsync<ListFindings.Response>($"{url}?run={seeded.LatestRunId}", Json))!;
        Assert.Equal((seeded.LatestRunId, 2), (old.Run!.Id, old.Run.Number));
        Assert.Equal(3, old.Findings.Count);
    }

    [Fact]
    public async Task New_project_has_empty_dashboards()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var created = await (await client.PostAsJsonAsync("/api/projects", new { name = "Empty" }))
            .Content.ReadFromJsonAsync<ProjectResponse>();

        var detail = await client.GetFromJsonAsync<GetProject.Response>($"/api/projects/{created!.Id}", Json);
        var overview = await client.GetFromJsonAsync<GetOverview.Response>("/api/projects/overview", Json);

        Assert.Null(detail!.Latest);
        Assert.Null(detail.Spec);
        Assert.Null(detail.DefaultEnvironment);
        Assert.Empty(detail.RecentRuns);
        Assert.Null(overview!.AverageScore);
        Assert.Equal(0, overview.FailedFindingCount);
        Assert.Null(Assert.Single(overview.RecentProjects).LatestScore);
        var row = Assert.Single((await client.GetFromJsonAsync<List<ListProjects.Row>>("/api/projects", Json))!);
        Assert.Equal((null, 0, null), (row.EnvironmentName, row.ScenarioCount, row.LatestScore));
    }

    private sealed record Seeded(
        Guid ProjectId, Guid OtherProjectId, Guid LatestRunId, Dictionary<Guid, Guid> FindingRuns);

    // Member project: an older scored run (70), the latest scored run (78) and a running run.
    // Non-member project: one scored run that must never leak into the caller's dashboards.
    private static async Task<Seeded> SeedAsync(BackendFactory factory, HttpClient client)
    {
        var created = await (await client.PostAsJsonAsync("/api/projects", new { name = "Payment Gateway" }))
            .Content.ReadFromJsonAsync<ProjectResponse>();
        var projectId = created!.Id;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = (await db.ProjectMembers.SingleAsync(member => member.ProjectId == projectId)).UserId;
        var now = DateTimeOffset.UtcNow;
        var findingRuns = new Dictionary<Guid, Guid>();

        var spec = Spec(projectId, userId, active: true);
        var oldSpec = Spec(projectId, userId, active: false);
        var environment = new ApiEnvironment
        {
            Id = Guid.NewGuid(), ProjectId = projectId, Name = "Prod-like test", BaseUrl = "https://api.example.test",
            IsDefault = true, CreatedById = userId
        };
        var endpoints = new[] { "/payments", "/payments/{id}", "/refunds" }
            .Select(path => Endpoint(spec.Id, path)).ToArray();
        var scenarios = new[] { ScenarioType.Latency, ScenarioType.TimeoutDrop }.Select(type => new ChaosScenario
        {
            Id = Guid.NewGuid(), EndpointId = endpoints[0].Id, ScenarioType = type, Parameters = JsonDocument.Parse("{}")
        });
        var previous = Run(projectId, spec.Id, environment.Id, userId, now.AddDays(-2), 70);
        var latest = Run(projectId, spec.Id, environment.Id, userId, now.AddDays(-1), 78);
        var running = Run(projectId, spec.Id, environment.Id, userId, now, null);
        running.Status = TestRunStatus.Running;

        db.AddRange(spec, oldSpec, environment, previous, latest, running);
        db.AddRange(endpoints);
        db.Add(Endpoint(oldSpec.Id, "/legacy"));
        db.AddRange(scenarios);
        Finding(previous, endpoints[0], ResilienceFindingOutcome.Failed);
        Finding(latest, endpoints[0], ResilienceFindingOutcome.Failed);
        Finding(latest, endpoints[1], ResilienceFindingOutcome.Failed);
        Finding(latest, endpoints[2], ResilienceFindingOutcome.Passed);
        db.AddRange(Result(latest, endpoints[0], 80, 90), Result(latest, endpoints[1], 60, null));

        var other = new Project { Id = Guid.NewGuid(), Name = "Not a member", CreatedById = userId };
        var otherSpec = Spec(other.Id, userId, active: true);
        var otherEnvironment = new ApiEnvironment
        {
            Id = Guid.NewGuid(), ProjectId = other.Id, Name = "Staging", BaseUrl = "https://other.example.test",
            IsDefault = true, CreatedById = userId
        };
        var otherEndpoint = Endpoint(otherSpec.Id, "/other");
        var otherRun = Run(other.Id, otherSpec.Id, otherEnvironment.Id, userId, now.AddHours(-1), 10);
        db.AddRange(other, otherSpec, otherEnvironment, otherEndpoint, otherRun);
        Finding(otherRun, otherEndpoint, ResilienceFindingOutcome.Failed);

        await db.SaveChangesAsync();
        return new Seeded(projectId, other.Id, latest.Id, findingRuns);

        void Finding(TestRun run, ApiEndpoint endpoint, ResilienceFindingOutcome outcome)
        {
            var finding = new ResilienceFinding
            {
                Id = Guid.NewGuid(), TestRunId = run.Id, EndpointId = endpoint.Id, Outcome = outcome,
                Summary = $"{endpoint.Path} {outcome}", Evidence = JsonDocument.Parse("{}")
            };
            findingRuns[finding.Id] = run.Id;
            db.Add(finding);
        }
    }

    private static ApiSpec Spec(Guid projectId, Guid userId, bool active) => new()
    {
        Id = Guid.NewGuid(), ProjectId = projectId, UploadedById = userId, IsActive = active, OpenApiVersion = "3.0.3",
        OriginalDocument = "{}", ParsedDocument = JsonDocument.Parse("{}")
    };

    private static ApiEndpoint Endpoint(Guid specId, string path) => new()
    {
        Id = Guid.NewGuid(), ApiSpecId = specId, Path = path, HttpMethod = "POST",
        Tags = JsonDocument.Parse("[]"), Parameters = JsonDocument.Parse("[]"), Responses = JsonDocument.Parse("{}")
    };

    private static TestRun Run(
        Guid projectId, Guid specId, Guid environmentId, Guid userId, DateTimeOffset at, decimal? score) => new()
    {
        Id = Guid.NewGuid(), ProjectId = projectId, ApiSpecId = specId, EnvironmentId = environmentId,
        RequestedById = userId, RunConfig = JsonDocument.Parse("{}"), CreatedAt = at, StartedAt = at,
        Status = TestRunStatus.Completed, CompletedAt = score is null ? null : at.AddMinutes(5),
        ResilienceScore = score, TotalScenarios = 2, CompletedScenarios = score is null ? 1 : 2
    };

    private static EndpointResult Result(TestRun run, ApiEndpoint endpoint, decimal retry, decimal? timeout) => new()
    {
        Id = Guid.NewGuid(), TestRunId = run.Id, EndpointId = endpoint.Id, RetryScore = retry, TimeoutScore = timeout,
        OverallScore = retry, Evidence = JsonDocument.Parse("{}")
    };
}
