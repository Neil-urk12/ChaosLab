using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
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
}
