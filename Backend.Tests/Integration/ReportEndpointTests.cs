using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Features.Reports;
using Backend.Features.TestRuns;
using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Backend.Tests.Integration.RunExecutorTests;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReportEndpointTests(PostgreSqlFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Finished_runs_get_versioned_pdf_reports_that_every_member_can_download()
    {
        using var listener = StartTarget(new ConcurrentQueue<Received>(), out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var (projectId, scenarios) = await SetUpAsync(client, baseUrl);
        Guid Pick(string method, string path, ScenarioType type) =>
            scenarios.Single(s => s.HttpMethod == method && s.Path == path && s.Type == type).Id;
        var latency = Pick("GET", "/orders/{id}", ScenarioType.Latency);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"/api/projects/{projectId}/scenarios/{latency}",
            new { parameters = new { delayMs = 10 } })).StatusCode);
        // The dependency failure is skipped (no dependency URL), so the report has a limitation to list.
        var runId = await CreateRunAsync(client, projectId,
            [latency, Pick("POST", "/orders", ScenarioType.DuplicateRequest), Pick("POST", "/orders", ScenarioType.DnsFailure)]);
        var url = $"/api/projects/{projectId}/runs/{runId}/reports";

        var queued = await client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.Conflict, queued.StatusCode);
        Assert.Contains(GenerateReport.NotFinishedMessage, await queued.Content.ReadAsStringAsync());

        Assert.True(await ActivatorUtilities.CreateInstance<RunExecutor>(factory.Services).TickAsync(CancellationToken.None));

        var first = await client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var row = (await first.Content.ReadFromJsonAsync<ListReports.Row>(Json))!;
        Assert.Equal((1, "Orders-RUN-001-report-v1.pdf"), (row.Version, row.FileName));
        Assert.Equal($"{url}/{row.Id}/file", first.Headers.Location!.ToString());
        var second = (await (await client.PostAsync(url, null)).Content.ReadFromJsonAsync<ListReports.Row>(Json))!;
        Assert.Equal(2, second.Version);
        var listed = (await client.GetFromJsonAsync<List<ListReports.Row>>(url, Json))!;
        Assert.Equal([2, 1], listed.Select(r => r.Version));

        var file = await client.GetAsync($"{url}/{row.Id}/file");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal(row.FileName, file.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("%PDF"u8.ToArray(), (await file.Content.ReadAsByteArrayAsync())[..4]);

        // Another project's URL, an unknown report, and a report whose file is gone are all not found.
        var other = (await (await client.PostAsJsonAsync("/api/projects", new { name = "Other" }))
            .Content.ReadFromJsonAsync<ProjectResponse>())!.Id;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{other}/runs/{runId}/reports/{row.Id}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/projects/{other}/runs/{runId}/reports", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{url}/{Guid.NewGuid()}/file")).StatusCode);
        File.Delete(Path.Combine(factory.ReportsPath, $"{second.Id}.pdf"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{url}/{second.Id}/file")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // The report's content never carries the stored credential, and lists the skipped scenario.
        var adminId = await db.Users.Select(u => u.Id).SingleAsync();
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, adminId.ToString())]));
        var data = (await GenerateReport.LoadAsync(db, projectId, runId, admin, TimeProvider.System, CancellationToken.None))!;
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(data, Json));
        Assert.Equal((ScenarioExecutionStatus.Skipped, RunExecutor.NoDependency, 1),
            (Assert.Single(data.ExecutionProblems).Status, data.ExecutionProblems[0].Reason, data.ExecutionProblems[0].Count));
        Assert.NotEmpty(data.Findings);

        // Viewers download but cannot generate.
        await db.ProjectMembers.Where(m => m.ProjectId == projectId).ExecuteUpdateAsync(m => m.SetProperty(x => x.Role, ProjectRole.Viewer));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{url}/{row.Id}/file")).StatusCode);
    }
}
