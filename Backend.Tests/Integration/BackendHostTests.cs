using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class BackendHostTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task Host_serves_existing_endpoint_and_resolves_isolated_PostgreSql_context()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(database.ConnectionString, context.Database.GetConnectionString());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        Assert.Contains("20261004013735_InitialCreate", context.Database.GetMigrations());
    }

    [Fact]
    public async Task Host_applies_migrations_to_an_empty_database_when_MigrateOnStartup_is_on()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrate: false);
        await using var factory = new BackendFactory(database)
            .WithWebHostBuilder(builder => builder.UseSetting("Database:MigrateOnStartup", "true"));
        // Starting the host would throw on the startup role check if the tables were missing.
        using var client = factory.CreateClient();

        await using var context = database.CreateContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Host_serves_the_web_app_without_sign_in_and_keeps_api_paths_out_of_the_fallback()
    {
        var webRoot = Directory.CreateTempSubdirectory("chaoslab-webroot-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><p>ChaosLab app</p>");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "robots.txt"), "User-agent: *");
            await using var database = await postgres.CreateDatabaseAsync();
            await using var factory = new BackendFactory(database).WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            foreach (var page in new[] { "/", "/login", "/projects/abc/runs/def" })
            {
                var response = await client.GetAsync(page);
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("ChaosLab app", await response.Content.ReadAsStringAsync());
            }
            Assert.Equal("User-agent: *", await client.GetStringAsync("/robots.txt"));

            // API paths never get the page. Signed out, a missing one needs sign-in like a real one, so the routes that
            // exist don't show; signed in, a missing one is 404.
            using var admin = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());
            foreach (var (caller, path, status) in new[]
            {
                (client, "/api", System.Net.HttpStatusCode.Unauthorized),
                (client, "/api/nope", System.Net.HttpStatusCode.Unauthorized),
                (client, "/api/projects", System.Net.HttpStatusCode.Unauthorized),
                (admin, "/api", System.Net.HttpStatusCode.NotFound),
                (admin, "/api/nope", System.Net.HttpStatusCode.NotFound)
            })
            {
                var response = await caller.GetAsync(path);
                Assert.Equal(status, response.StatusCode);
                Assert.DoesNotContain("ChaosLab app", await response.Content.ReadAsStringAsync());
            }
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }
}
