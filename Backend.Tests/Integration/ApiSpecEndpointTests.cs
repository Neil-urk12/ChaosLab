using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Enums;
using Backend.Features.ApiSpecs;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class ApiSpecEndpointTests(PostgreSqlFixture postgres)
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
            PaymentCreate: {type: object}
        paths:
          /payments:
            get:
              operationId: listPayments
              tags: [payments]
              responses: {"200": {description: ok}, "401": {description: no}}
            post:
              operationId: createPayment
              requestBody: {content: {application/json: {schema: {$ref: "#/components/schemas/PaymentCreate"}}}}
              responses: {"201": {description: ok}}
          /payouts:
            post:
              operationId: createPayout
              responses: {"201": {description: ok}}
          /health:
            get:
              security: []
              responses: {"200": {description: ok}}
        """;

    [Fact]
    public async Task Import_then_get_lists_endpoints_with_security_and_request_body()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);

        var import = await client.PostAsJsonAsync($"/api/projects/{projectId}/spec",
            new { source = "FileUpload", content = Spec, filename = "../payments.yaml" });
        Assert.Equal(HttpStatusCode.Created, import.StatusCode);
        var report = await import.Content.ReadFromJsonAsync<ImportSpec.Report>(Json);
        Assert.Equal("payments.yaml", report!.Filename);
        Assert.Equal("3.0.3", report.OpenApiVersion);
        Assert.Equal(4, report.OperationCount);

        var spec = await client.GetFromJsonAsync<GetSpec.Response>($"/api/projects/{projectId}/spec", Json);
        Assert.Null(spec!.Changes);
        Assert.Equal("bearerAuth", Assert.Single(spec.SecuritySchemes, s => s.Global).Name);
        var create = Assert.Single(spec.Endpoints, e => e.OperationId == "createPayment");
        Assert.Equal("PaymentCreate", create.RequestBody);
        Assert.Equal(["bearerAuth"], create.Security);
        Assert.Empty(Assert.Single(spec.Endpoints, e => e.Path == "/health").Security);
        Assert.Equal(["200", "401"], Assert.Single(spec.Endpoints, e => e.OperationId == "listPayments").Responses);

        var document = await client.GetAsync($"/api/projects/{projectId}/spec/document");
        Assert.Equal("text/plain", document.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Spec, await document.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Import_rejects_unresolved_refs_and_undeclared_path_parameters_and_warns_on_oauth2()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/spec", new
        {
            source = "Paste",
            content = """
                {"openapi": "3.0.3", "info": {"title": "t", "version": "1"},
                 "components": {"securitySchemes": {"oauth2": {"type": "oauth2", "flows": {"clientCredentials": {"tokenUrl": "https://x.test/t", "scopes": {}}}}}},
                 "paths": {
                   "/refunds": {"post": {"requestBody": {"content": {"application/json": {"schema": {"$ref": "#/components/schemas/RefundRequest"}}}}, "responses": {"201": {"description": "ok"}}}},
                   "/payments/{id}": {"get": {"responses": {"200": {"description": "ok"}}}}}}
                """
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<ImportSpec.Report>(Json);
        Assert.Contains(report!.Issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("RefundRequest\" could not be resolved"));
        Assert.Contains(report.Issues, i => i is { Severity: IssueSeverity.Error, Location: "paths./payments/{id}.get.parameters" });
        Assert.Contains(report.Issues, i => i is { Severity: IssueSeverity.Warning, Location: "components.securitySchemes.oauth2" });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{projectId}/spec")).StatusCode);
    }

    [Fact]
    public async Task Import_reports_unparseable_document()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/spec", new { source = "Paste", content = "not: [valid" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<ImportSpec.Report>(Json);
        Assert.Equal(IssueSeverity.Error, Assert.Single(report!.Issues).Severity);
    }

    [Fact]
    public async Task Reimport_keeps_testing_choices_and_reports_changes()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);
        (await client.PostAsJsonAsync($"/api/projects/{projectId}/spec", new { source = "Paste", content = Spec })).EnsureSuccessStatusCode();
        var first = await client.GetFromJsonAsync<GetSpec.Response>($"/api/projects/{projectId}/spec", Json);
        var payout = Assert.Single(first!.Endpoints, e => e.Path == "/payouts");

        var patch = await client.PatchAsJsonAsync($"/api/projects/{projectId}/spec/endpoints/{payout.Id}", new { testing = "NeverTest" });
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        var changed = Spec
            .Replace("""responses: {"201": {description: ok}}""" + "\n  /payouts", """responses: {"201": {description: ok}, "409": {description: dup}}""" + "\n  /payouts")
            .Replace("  /health:\n    get:\n      security: []\n      responses: {\"200\": {description: ok}}", "  /refunds:\n    post:\n      responses: {\"201\": {description: ok}}");
        Assert.NotEqual(Spec, changed);
        (await client.PostAsJsonAsync($"/api/projects/{projectId}/spec", new { source = "Paste", content = changed })).EnsureSuccessStatusCode();

        var second = await client.GetFromJsonAsync<GetSpec.Response>($"/api/projects/{projectId}/spec", Json);
        Assert.Equal(EndpointTesting.NeverTest, Assert.Single(second!.Endpoints, e => e.Path == "/payouts").Testing);
        Assert.Equal("New", Assert.Single(second.Endpoints, e => e.Path == "/refunds").Change);
        Assert.Equal("Modified", Assert.Single(second.Endpoints, e => e.OperationId == "createPayment").Change);
        Assert.Null(Assert.Single(second.Endpoints, e => e.OperationId == "listPayments").Change);
        Assert.Equal((1, 1), (second.Changes!.Added, second.Changes.Modified));
        Assert.Equal(["GET /health"], second.Changes.Removed);
    }

    [Fact]
    public async Task Url_import_refuses_link_local_metadata_address()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/spec",
            new { source = "Url", url = "http://169.254.169.254/latest/meta-data/openapi.yaml" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("url", (await response.Content.ReadFromJsonAsync<ImportSpec.Report>(Json))!.Issues.Single().Location);
    }

    [Theory]
    [InlineData("169.254.169.254", true)]
    [InlineData("::ffff:169.254.169.254", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00:ec2::254", true)]
    [InlineData("100.100.100.200", true)]
    [InlineData("64:ff9b::a9fe:a9fe", true)]
    [InlineData("64:ff9b::a00:5", false)]
    [InlineData("fd00::1", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("127.0.0.1", false)]
    public void Fetch_blocks_link_local_and_unspecified_addresses(string address, bool blocked) =>
        Assert.Equal(blocked, ImportSpec.IsBlocked(IPAddress.Parse(address)));

    // A proxy would receive the request instead, so the connect-time address check would never see the target.
    [Fact]
    public void Fetch_ignores_proxy_environment() => Assert.False(ImportSpec.CreateFetchHandler().UseProxy);

    [Fact]
    public async Task Viewers_cannot_import_or_change_testing()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);
        (await client.PostAsJsonAsync($"/api/projects/{projectId}/spec", new { source = "Paste", content = Spec })).EnsureSuccessStatusCode();
        var endpoint = (await client.GetFromJsonAsync<GetSpec.Response>($"/api/projects/{projectId}/spec", Json))!.Endpoints[0];

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.ProjectMembers.Where(m => m.ProjectId == projectId)
                .ExecuteUpdateAsync(m => m.SetProperty(x => x.Role, ProjectRole.Viewer));
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/projects/{projectId}/spec", new { source = "Paste", content = Spec })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PatchAsJsonAsync($"/api/projects/{projectId}/spec/endpoints/{endpoint.Id}", new { testing = "NeverTest" })).StatusCode);
        // Reads stay open to viewers.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/projects/{projectId}/spec")).StatusCode);
    }

    [Fact]
    public async Task Spec_endpoints_hide_projects_from_non_members()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());

        var response = await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/spec", new { source = "Paste", content = Spec });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new { name = "Payments" });
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>())!.Id;
    }
}
