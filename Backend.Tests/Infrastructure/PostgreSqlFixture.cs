using System.Security.Cryptography;
using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Backend.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    // Connections always originate from this test-owned container, never application configuration.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.11-alpine")
        .WithDatabase("postgres")
        .WithUsername("chaoslab_tests")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
        .Build();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch
        {
            await _container.DisposeAsync();
            throw;
        }
    }

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Pooling = false
        }.ConnectionString;
        var name = $"chaoslab_test_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();

        var database = new TestDatabase(adminConnectionString, name);
        try
        {
            await using var context = database.CreateContext();
            // Exercise the checked-in migrations, rather than bypassing them with EnsureCreated.
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public sealed class TestDatabase(string adminConnectionString, string name) : IAsyncDisposable
{
    public string ConnectionString { get; } = new NpgsqlConnectionStringBuilder(adminConnectionString)
    {
        Database = name
    }.ConnectionString;

    public ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
