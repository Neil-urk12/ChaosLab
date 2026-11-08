using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Features.Scenarios;
using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class ScenarioEndpointTests(PostgreSqlFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string Spec = """
        openapi: 3.0.3
        info: {title: Payments, version: "1.0"}
        security: [{bearerAuth: []}]
        components:
          securitySchemes:
            bearerAuth: {type: http, scheme: bearer}
          schemas:
            Payment: {type: object}
          parameters:
            IdempotencyKey: {name: Idempotency-Key, in: header, schema: {type: string}}
          responses:
            PaymentCreated:
              description: created
              content: {application/json: {schema: {$ref: "#/components/schemas/Payment"}}}
        paths:
          /payments:
            get:
              responses:
                "200": {description: ok, content: {application/json: {schema: {$ref: "#/components/schemas/Payment"}}}}
                "429": {description: slow down}
            post:
              parameters: [{$ref: "#/components/parameters/IdempotencyKey"}]
              responses:
                "201": {$ref: "#/components/responses/PaymentCreated"}
                "503": {description: processor down}
          /health:
            get:
              security: []
              responses: {"204": {description: ok}}
        """;

    [Fact]
    public async Task Generate_creates_only_applicable_scenarios_with_reasons()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await ImportAsync(client);

        // Import alone stores the spec; scenarios wait until the user generates them.
        var before = await client.GetFromJsonAsync<GetScenarios.Response>($"/api/projects/{projectId}/scenarios", Json);
        Assert.Equal((3, 0), (before!.EndpointCount, before.Scenarios.Count));
        var generated = await GenerateAsync(client, projectId);
        Assert.Equal((22, 0), (generated.Added, generated.Removed));

        var response = await client.GetFromJsonAsync<GetScenarios.Response>($"/api/projects/{projectId}/scenarios", Json);
        Assert.Equal(3, response!.EndpointCount);
        ScenarioType[] Types(string method, string path) =>
            response.Scenarios.Where(s => s.HttpMethod == method && s.Path == path).Select(s => s.Type).Order().ToArray();

        Assert.Equal([ScenarioType.Latency, ScenarioType.ServerErrorStorm, ScenarioType.Throttle429, ScenarioType.TimeoutDrop,
            ScenarioType.MalformedJson, ScenarioType.EmptyResponse, ScenarioType.SlowDrip, ScenarioType.AuthExpiry], Types("GET", "/payments"));
        // POST: no plain 5xx retry without a key, but this one declares Idempotency-Key, a $ref'd body, and a 503.
        Assert.Equal([ScenarioType.Latency, ScenarioType.ServerErrorStorm, ScenarioType.Throttle429, ScenarioType.TimeoutDrop,
            ScenarioType.MalformedJson, ScenarioType.EmptyResponse, ScenarioType.SlowDrip, ScenarioType.AuthExpiry,
            ScenarioType.DnsFailure, ScenarioType.DuplicateRequest], Types("POST", "/payments"));
        // Public, no body, no declared 429: nothing body- or auth-related, throttle off by default.
        Assert.Equal([ScenarioType.Latency, ScenarioType.ServerErrorStorm, ScenarioType.Throttle429, ScenarioType.TimeoutDrop], Types("GET", "/health"));

        var duplicate = Assert.Single(response.Scenarios, s => s.Type == ScenarioType.DuplicateRequest);
        Assert.Equal("State-changing POST with an Idempotency-Key header in the spec.", duplicate.Reason);
        Assert.Equal("same", duplicate.Parameters.GetProperty("key").GetString());
        Assert.Equal("Returns a JSON body (Payment) that callers parse.",
            Assert.Single(response.Scenarios, s => s.Type == ScenarioType.MalformedJson && s.HttpMethod == "POST").Reason);
        Assert.False(Assert.Single(response.Scenarios, s => s.Type == ScenarioType.Throttle429 && s.Path == "/health").Enabled);
        Assert.True(Assert.Single(response.Scenarios, s => s.Type == ScenarioType.Throttle429 && s.HttpMethod == "GET" && s.Path == "/payments").Enabled);
    }

    [Fact]
    public async Task Edits_are_validated_and_survive_regeneration_and_reimport()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await ImportAsync(client);
        await GenerateAsync(client, projectId);
        var scenarios = (await client.GetFromJsonAsync<GetScenarios.Response>($"/api/projects/{projectId}/scenarios", Json))!.Scenarios;
        var duplicate = scenarios.Single(s => s.Type == ScenarioType.DuplicateRequest);
        var url = $"/api/projects/{projectId}/scenarios/{duplicate.Id}";

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(url, new { parameters = new { copies = 1, key = "same", delayMs = 0 } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(url, new { parameters = new { copies = 2 } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(url, new { invariants = new object?[] { null } })).StatusCode);
        // Record count needs a read-back request to count with.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(url,
            new { invariants = new[] { new { type = "MaxRecordsAfterDuplicates", @params = new { max = 1 } } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/projects/{projectId}/scenarios/{scenarios.First(s => s.Type == ScenarioType.Latency).Id}",
            new { invariants = new[] { new { type = "MaxRecordsAfterDuplicates", @params = new { max = 1 }, readBack = new { method = "GET", path = "/payments" } } } })).StatusCode);

        var update = await client.PatchAsJsonAsync(url, new
        {
            enabled = false,
            parameters = new { copies = 3, key = "different", delayMs = 50 },
            invariants = new object[]
            {
                new { type = "MaxRecordsAfterDuplicates", @params = new { max = 1, codes = new[] { 200 } },
                    readBack = new { method = "GET", path = "/payments?reference={{request.body.reference}}" } },
                new { type = "StatusCodeIn", @params = new { codes = new[] { 201, 409 } } }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        Assert.Equal(new GenerateScenarios.Response(0, 0), await GenerateAsync(client, projectId)); // regenerate keeps edits and adds nothing
        await ImportAsync(client, projectId);
        Assert.Empty((await client.GetFromJsonAsync<GetScenarios.Response>($"/api/projects/{projectId}/scenarios", Json))!.Scenarios);
        await GenerateAsync(client, projectId);

        var after = Assert.Single((await client.GetFromJsonAsync<GetScenarios.Response>($"/api/projects/{projectId}/scenarios", Json))!
            .Scenarios, s => s.Type == ScenarioType.DuplicateRequest);
        Assert.NotEqual(duplicate.Id, after.Id); // re-import made a new endpoint
        Assert.False(after.Enabled);
        Assert.Equal(3, after.Parameters.GetProperty("copies").GetInt32());
        var invariants = after.Invariants.EnumerateArray().ToList();
        Assert.Equal(2, invariants.Count);
        // Fields of other types are dropped.
        Assert.False(invariants[0].GetProperty("params").TryGetProperty("codes", out _));
        Assert.Equal("/payments?reference={{request.body.reference}}", invariants[0].GetProperty("readBack").GetProperty("path").GetString());
    }

    [Fact]
    public async Task Viewers_can_read_but_not_change_scenarios()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await ImportAsync(client);
        await GenerateAsync(client, projectId);
        var scenario = (await client.GetFromJsonAsync<GetScenarios.Response>($"/api/projects/{projectId}/scenarios", Json))!.Scenarios[0];
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.ProjectMembers.Where(m => m.ProjectId == projectId)
                .ExecuteUpdateAsync(m => m.SetProperty(x => x.Role, ProjectRole.Viewer));
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/projects/{projectId}/scenarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/projects/{projectId}/scenarios/generate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync(
            $"/api/projects/{projectId}/scenarios/{scenario.Id}", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{Guid.NewGuid()}/scenarios")).StatusCode);
    }

    private static async Task<Guid> ImportAsync(HttpClient client, Guid? projectId = null)
    {
        if (projectId is null)
        {
            var created = await client.PostAsJsonAsync("/api/projects", new { name = "Payments" });
            projectId = (await created.Content.ReadFromJsonAsync<ProjectResponse>())!.Id;
        }
        var import = await client.PostAsJsonAsync($"/api/projects/{projectId}/spec", new { source = "Paste", content = Spec });
        Assert.Equal(HttpStatusCode.Created, import.StatusCode);
        return projectId.Value;
    }

    private static async Task<GenerateScenarios.Response> GenerateAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsync($"/api/projects/{projectId}/scenarios/generate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GenerateScenarios.Response>(Json))!;
    }
}
