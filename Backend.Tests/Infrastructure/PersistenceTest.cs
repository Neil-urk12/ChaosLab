using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Tests.Infrastructure;

public abstract class PersistenceTest(PostgreSqlFixture postgres) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    protected ApplicationDbContext Db { get; private set; } = null!;
    private protected TestData Data { get; } = new();

    public async Task InitializeAsync()
    {
        _database = await postgres.CreateDatabaseAsync();
        Db = _database.CreateContext();
        await Data.SeedAsync(Db);
    }

    public async Task DisposeAsync()
    {
        if (Db is not null)
            await Db.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }

    protected async Task AssertViolationAsync(string sqlState, string constraint)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgresException.SqlState);
        Assert.Equal(constraint, postgresException.ConstraintName);
    }
}
