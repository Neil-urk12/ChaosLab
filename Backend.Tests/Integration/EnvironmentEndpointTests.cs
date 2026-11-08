using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Enums;
using Backend.Features.Environments;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Backend.Infrastructure.Security;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class EnvironmentEndpointTests(PostgreSqlFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string Token = "secret-token-3f9a";

    private const string Spec = """
        openapi: 3.0.3
        info: {title: Payments, version: "1.0"}
        servers: [{url: "https://api.payments.test/v1"}]
        security: [{bearerAuth: []}]
        components:
          securitySchemes:
            bearerAuth: {type: http, scheme: bearer}
            apiKey: {type: apiKey, in: header, name: X-API-Key}
        paths:
          /payments:
            get: {responses: {"200": {description: ok}}}
          /payments/{id}:
            get:
              parameters: [{name: id, in: path, required: true, schema: {type: string}}]
              responses: {"200": {description: ok}}
          /webhooks/test:
            post:
              security: [{apiKey: []}]
              responses: {"204": {description: ok}}
        """;

    [Fact]
    public async Task Target_needs_authorization_and_records_who_confirmed_it()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);
        var url = $"/api/projects/{projectId}/environment";

        var empty = await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json);
        Assert.Null(empty!.Target);
        Assert.Equal(["https://api.payments.test/v1"], empty.SpecServers);
        Assert.Equal(["POST /webhooks/test"], Assert.Single(empty.Schemes, s => s.Name == "apiKey").Operations);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(url, Target("https://api.test/v1", authorized: false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(url, Target("https://user:pw@api.test/v1"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(url, Target("https://api.test", timeout: 0))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(url, Target("https://api.test/v1/"))).StatusCode);

        var target = (await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!.Target!;
        Assert.Equal("https://api.test/v1", target.BaseUrl);
        Assert.Equal(EnvironmentKind.Staging, target.Kind);
        Assert.Equal(30, target.RequestTimeoutSeconds);
        Assert.Equal("Initial Administrator", target.AuthorizedBy);
        Assert.NotNull(target.AuthorizedAt);

        // The name follows the kind on every save, not only the first.
        Assert.Equal("Staging", target.Name);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(url, new
            { baseUrl = "https://api.test/v1", kind = "ProductionMirror", requestTimeoutSeconds = 30, verifyTls = true, authorized = true })).StatusCode);
        Assert.Equal("Production mirror", (await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!.Target!.Name);
    }

    [Fact]
    public async Task Credentials_are_encrypted_shown_by_hint_only_and_validated_against_the_target()
    {
        using var listener = StartTarget(out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);
        var url = $"/api/projects/{projectId}/environment";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"{url}/credentials/bearerAuth", new { authType = "Bearer", secret = Token })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(url, Target(baseUrl))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"{url}/credentials/unknown", new { authType = "Bearer", secret = Token })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{url}/credentials/bearerAuth", new { authType = "Bearer" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{url}/credentials/bearerAuth", new { authType = "Bearer", secret = "tökén-1234" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{url}/credentials/bearerAuth",
            new { authType = "Bearer", secret = Token, label = "staging-ci-token" })).StatusCode);

        var raw = await client.GetStringAsync(url);
        Assert.DoesNotContain(Token, raw);
        var credential = Assert.Single((await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!.Schemes, s => s.Name == "bearerAuth").Credential!;
        Assert.Equal(("3f9a", "staging-ci-token", AuthType.Bearer), (credential.Hint, credential.Label, credential.AuthType));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuthProfiles.SingleAsync();
            Assert.DoesNotContain(Token, Encoding.UTF8.GetString(stored.SecretCipherText!));
            Assert.DoesNotContain(Token, stored.PublicConfig.RootElement.GetRawText());
        }

        // The probe skips /payments/{id} (path parameter) and calls GET /payments with the token.
        var validation = await client.PostAsync($"{url}/credentials/bearerAuth/validate", null);
        var result = await validation.Content.ReadFromJsonAsync<ValidateCredential.Response>(Json);
        Assert.True(result!.Valid, result.Validation);
        Assert.Equal("GET /payments returned 200 with this token.", result.Validation);

        // Changing only the label keeps the secret and the validation.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{url}/credentials/bearerAuth", new { authType = "Bearer", label = "ci" })).StatusCode);
        credential = Assert.Single((await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!.Schemes, s => s.Name == "bearerAuth").Credential!;
        Assert.Equal(("3f9a", true), (credential.Hint, credential.Valid));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{url}/credentials/bearerAuth", new { authType = "Bearer", secret = "wrong-token" })).StatusCode);
        result = await (await client.PostAsync($"{url}/credentials/bearerAuth/validate", null)).Content.ReadFromJsonAsync<ValidateCredential.Response>(Json);
        Assert.Equal((false, "GET /payments returned 401 with this token."), (result!.Valid, result.Validation));

        // A secret encrypted under another key (Credentials:Key changed) is a 422 asking for it again, not a 500.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var profile = await db.AuthProfiles.SingleAsync(p => p.Name == "bearerAuth");
            var otherKey = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Credentials:Key"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()) }).Build();
            profile.SecretCipherText = new CredentialProtector(otherKey).Protect(Token, profile.Id);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync($"{url}/credentials/bearerAuth/validate", null)).StatusCode);
        credential = Assert.Single((await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!.Schemes, s => s.Name == "bearerAuth").Credential!;
        Assert.False(credential.Valid);

        // apiKey is only used by a POST, so there is nothing safe to validate with.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"{url}/credentials/apiKey", new { authType = "ApiKey", secret = "key-1234" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync($"{url}/credentials/apiKey/validate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{url}/credentials/apiKey")).StatusCode);
        Assert.Null(Assert.Single((await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!.Schemes, s => s.Name == "apiKey").Credential);
    }

    [Fact]
    public async Task Check_connection_reports_status_and_unreachable_targets()
    {
        using var listener = StartTarget(out var baseUrl);
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);
        var url = $"/api/projects/{projectId}/environment/check";

        var ok = await (await client.PostAsJsonAsync(url, new { baseUrl })).Content.ReadFromJsonAsync<CheckConnection.Response>(Json);
        Assert.Equal((true, 401), (ok!.Reachable, ok.Status)); // no token: still reachable
        var blocked = await (await client.PostAsJsonAsync(url, new { baseUrl = "http://169.254.169.254/latest" })).Content.ReadFromJsonAsync<CheckConnection.Response>(Json);
        Assert.False(blocked!.Reachable);
        Assert.NotNull(blocked.Error);
    }

    [Fact]
    public async Task Global_headers_keep_secret_values_out_of_responses()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);
        var url = $"/api/projects/{projectId}/environment";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(url, Target("https://api.test"))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{url}/headers", new { name = "Authorization", value = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{url}/headers", new { name = "X-Bad", value = "a\r\nb" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{url}/headers", new { name = "X-Bad", value = "café" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{url}/headers", new { name = "X-A", value = "a", appliesTo = new[] { "payments" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"{url}/headers", new { name = "X-Tenant-Id", value = "tenant-test-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{url}/headers", new { name = "X-Tenant-Id", value = "again" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"{url}/headers",
            new { name = "X-Test-Signature", value = "signature-81c0", secret = true, appliesTo = new[] { "POST /payments" } })).StatusCode);

        var headers = (await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!.Headers;
        Assert.DoesNotContain("signature-81c0", await client.GetStringAsync(url));
        var secret = Assert.Single(headers, h => h.Secret);
        Assert.Equal(("81c0", (string?)null), (secret.Hint, secret.Value));
        Assert.Equal(["POST /payments"], secret.AppliesTo);

        var tenant = Assert.Single(headers, h => !h.Secret);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"{url}/headers/{tenant.Id}", new { secret = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"{url}/headers/{tenant.Id}", new { secret = true })).StatusCode);
        Assert.DoesNotContain("tenant-test-01", await client.GetStringAsync(url));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{url}/headers/{tenant.Id}")).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!.Headers);
    }

    [Fact]
    public async Task Moving_the_target_to_another_host_drops_stored_secrets()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
        var projectId = await CreateProjectAsync(client);
        var url = $"/api/projects/{projectId}/environment";
        await client.PutAsJsonAsync(url, Target("https://api.test/v1"));
        await client.PutAsJsonAsync($"{url}/credentials/bearerAuth", new { authType = "Bearer", secret = Token, label = "ci" });
        await client.PostAsJsonAsync($"{url}/headers", new { name = "X-Signature", value = "sig-81c0", secret = true });
        await client.PostAsJsonAsync($"{url}/headers", new { name = "X-Tenant-Id", value = "tenant-01" });

        // Same origin, different path: secrets stay.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(url, Target("https://api.test/v2"))).StatusCode);
        var settings = (await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!;
        Assert.Equal("3f9a", Assert.Single(settings.Schemes, s => s.Name == "bearerAuth").Credential!.Hint);
        Assert.Equal(2, settings.Headers.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(url, Target("https://attacker.example/v1"))).StatusCode);
        settings = (await client.GetFromJsonAsync<GetEnvironment.Response>(url, Json))!;
        var credential = Assert.Single(settings.Schemes, s => s.Name == "bearerAuth").Credential!;
        Assert.Equal((null, "ci"), (credential.Hint, credential.Label));
        Assert.Equal("X-Tenant-Id", Assert.Single(settings.Headers).Name);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync($"{url}/credentials/bearerAuth/validate", null)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuthProfiles.SingleAsync()).SecretCipherText);
    }

    [Theory]
    [InlineData("https://api.test/v1", "/payments", "https://api.test/v1/payments")]
    [InlineData("https://api.test", "x-@evil.test/me", null)]
    [InlineData("https://api.test", ".evil.test/me", null)]
    [InlineData("https://api.test:84", "43/me", null)]
    public void Combine_never_leaves_the_base_url_host(string baseUrl, string path, string? expected) =>
        Assert.Equal(expected, TargetClient.Combine(baseUrl, path)?.ToString());

    [Fact]
    public void Cookie_api_keys_are_sent_in_the_cookie_header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test/me");
        TargetClient.Authenticate(request, AuthType.ApiKey, "key-1234", null,
            new SchemeInfo("session", "apiKey", null, "cookie", "SESSION", []));
        Assert.Equal("SESSION=key-1234", Assert.Single(request.Headers.GetValues("Cookie")));
        Assert.False(request.Headers.Contains("SESSION"));
    }

    [Fact]
    public void Header_api_keys_replace_a_global_header_of_the_same_name()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.test/me");
        request.Headers.TryAddWithoutValidation("x-api-key", "global-value");
        TargetClient.Authenticate(request, AuthType.ApiKey, "key-1234", null,
            new SchemeInfo("apiKey", "apiKey", null, "header", "X-API-Key", []));
        Assert.Equal("key-1234", Assert.Single(request.Headers.GetValues("X-API-Key")));
    }

    private static object Target(string baseUrl, bool authorized = true, int timeout = 30) =>
        new { baseUrl, kind = "Staging", requestTimeoutSeconds = timeout, verifyTls = true, authorized };

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new { name = "Payments" });
        var projectId = (await response.Content.ReadFromJsonAsync<ProjectResponse>())!.Id;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/projects/{projectId}/spec", new { source = "Paste", content = Spec })).StatusCode);
        return projectId;
    }

    // A loopback target that answers 200 only to the expected bearer token.
    private static HttpListener StartTarget(out string baseUrl)
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
                context.Response.StatusCode = context.Request.Headers["Authorization"] == $"Bearer {Token}" ? 200 : 401;
                context.Response.Close();
            }
        });
        return listener;
    }
}
