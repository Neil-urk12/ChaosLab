using Backend.Domain.Entities;
using Backend.Infrastructure.Data;
using Backend.Infrastructure.Identity;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class InitialAdminBootstrapTests(PostgreSqlFixture postgres)
{
    private static ServiceProvider Services(TestDatabase database, BootstrapAdminOptions config,
        IUserValidator<User>? validator = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(database.ConnectionString));
        services.AddIdentityCore<User>().AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.Configure<BootstrapAdminOptions>(options =>
        {
            options.Username = config.Username;
            options.Password = config.Password;
            options.FirstName = config.FirstName;
            options.LastName = config.LastName;
            options.Email = config.Email;
        });
        services.AddScoped<IInitialAdminBootstrapper, InitialAdminBootstrapper>();
        if (validator is not null)
            services.AddSingleton(validator);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task BootstrapAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInitialAdminBootstrapper>().BootstrapAsync();
    }

    [Fact]
    public async Task Fresh_installation_creates_hashed_admin_and_only_system_role()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var config = BootstrapTestConfiguration.Valid();
        await using var services = Services(database, config);
        await BootstrapAsync(services);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = Assert.Single(await db.Users.ToListAsync());
        Assert.Equal(config.Username, user.UserName);
        Assert.Equal(config.FirstName, user.Firstname);
        Assert.Equal(config.LastName, user.Lastname);
        Assert.True(user.MustChangePassword);
        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
        Assert.True(await manager.CheckPasswordAsync(user, config.Password!));
        // Assert a boolean to avoid printing the secret on assertion failure.
        var plaintextPersisted = db.Entry(user).Properties.Any(property => property.CurrentValue is string value
            && value.Contains(config.Password!, StringComparison.Ordinal));
        Assert.False(plaintextPersisted);
        Assert.Equal(InitialAdminBootstrapper.SuperAdminRole, Assert.Single(await db.Roles.ToListAsync()).Name);
        Assert.True(await manager.IsInRoleAsync(user, InitialAdminBootstrapper.SuperAdminRole));
        Assert.Single(await db.UserRoles.ToListAsync());
        Assert.Empty(await db.Projects.ToListAsync());
        Assert.Empty(await db.ProjectMembers.ToListAsync());
    }

    [Fact]
    public async Task Repeated_bootstrap_preserves_password_and_completed_password_change()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var config = BootstrapTestConfiguration.Valid();
        await using var services = Services(database, config);
        await BootstrapAsync(services);
        await using var db = database.CreateContext();
        var user = await db.Users.SingleAsync();
        var hash = user.PasswordHash;
        user.MustChangePassword = false;
        await db.SaveChangesAsync();
        config.Password = BootstrapTestConfiguration.Valid().Password;
        await using var restarted = Services(database, config);
        await BootstrapAsync(restarted);
        db.ChangeTracker.Clear();
        user = Assert.Single(await db.Users.ToListAsync());
        Assert.True(hash == user.PasswordHash);
        Assert.False(user.MustChangePassword);
        Assert.Single(await db.Roles.ToListAsync());
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    [Theory]
    [InlineData("Username")]
    [InlineData("Password")]
    [InlineData("FirstName")]
    [InlineData("LastName")]
    [InlineData("InvalidUsername")]
    [InlineData("RejectedPassword")]
    [InlineData("LongFirstName")]
    public async Task Invalid_configuration_fails_without_partial_writes(string problem)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var config = BootstrapTestConfiguration.Valid();
        switch (problem)
        {
            case "Username": config.Username = null; break;
            case "Password": config.Password = null; break;
            case "FirstName": config.FirstName = null; break;
            case "LastName": config.LastName = null; break;
            case "InvalidUsername": config.Username = "invalid username"; break;
            case "RejectedPassword": config.Password = "weak"; break;
            case "LongFirstName": config.FirstName = new string('x', 101); break;
        }
        await using var services = Services(database, config);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BootstrapAsync(services));
        Assert.Contains("BootstrapAdmin", error.Message);
        if (config.Password is not null)
            Assert.False(error.ToString().Contains(config.Password, StringComparison.Ordinal));
        await using var db = database.CreateContext();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Roles.ToListAsync());
        Assert.Empty(await db.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task Assignment_failure_rolls_back_user_and_role_and_can_be_retried()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var config = BootstrapTestConfiguration.Valid();
        await using (var services = Services(database, config, new RejectSecondValidation()))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BootstrapAsync(services));
            Assert.Contains("role assignment", error.Message);
            Assert.False(error.ToString().Contains(config.Password!, StringComparison.Ordinal));
        }
        await using var db = database.CreateContext();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Roles.ToListAsync());
        Assert.Empty(await db.UserRoles.ToListAsync());
        await using var retry = Services(database, config);
        await BootstrapAsync(retry);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    private sealed class RejectSecondValidation : IUserValidator<User>
    {
        private int _calls;
        public Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user) =>
            Task.FromResult(++_calls == 1 ? IdentityResult.Success : IdentityResult.Failed(
                new IdentityError { Code = "AssignmentRejected", Description = "Test failure" }));
    }

    [Fact]
    public async Task Existing_admin_and_unrelated_users_survive_without_bootstrap_configuration()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using (var services = Services(database, BootstrapTestConfiguration.Valid()))
        {
            await using var scope = services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(InitialAdminBootstrapper.SuperAdminRole)
                { Id = Guid.NewGuid() })).Succeeded);
            var admin = new User { Id = Guid.NewGuid(), UserName = "existing-admin", Firstname = "Existing", Lastname = "Admin" };
            Assert.True((await users.CreateAsync(admin, BootstrapTestConfiguration.Valid().Password!)).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, InitialAdminBootstrapper.SuperAdminRole)).Succeeded);
            var other = new User { Id = Guid.NewGuid(), UserName = "other-user", Firstname = "Other", Lastname = "User" };
            Assert.True((await users.CreateAsync(other, BootstrapTestConfiguration.Valid().Password!)).Succeeded);
        }
        await using var db = database.CreateContext();
        var before = await db.Users.AsNoTracking().OrderBy(user => user.UserName).ToListAsync();
        await using var noCredentials = Services(database, new BootstrapAdminOptions());
        await BootstrapAsync(noCredentials);
        var after = await db.Users.AsNoTracking().OrderBy(user => user.UserName).ToListAsync();
        Assert.Equal(2, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Id, after[i].Id);
            Assert.True(before[i].PasswordHash == after[i].PasswordHash);
            Assert.Equal(before[i].SecurityStamp, after[i].SecurityStamp);
            Assert.Equal(before[i].Firstname, after[i].Firstname);
            Assert.Equal(before[i].Lastname, after[i].Lastname);
            Assert.Equal(before[i].UpdatedAt, after[i].UpdatedAt);
            Assert.False(after[i].MustChangePassword);
        }
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task Host_bootstraps_and_restarts_after_secret_removal()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using (var first = new BackendFactory(database))
        {
            using var client = first.CreateClient();
            await using var db = database.CreateContext();
            Assert.True((await db.Users.SingleAsync()).MustChangePassword);
            Assert.Single(await db.UserRoles.ToListAsync());
        }
        await using var restarted = new BackendFactory(database, new BootstrapAdminOptions());
        using var restartedClient = restarted.CreateClient();
        await using var restartedDb = database.CreateContext();
        Assert.Single(await restartedDb.Users.ToListAsync());
        Assert.Single(await restartedDb.Roles.ToListAsync());
    }

    [Fact]
    public async Task Host_refuses_first_start_without_configuration()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database, new BootstrapAdminOptions());
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("BootstrapAdmin:Username", error.Message);
        await using var db = database.CreateContext();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_bootstraps_with_different_usernames_create_one_admin(bool roleExists)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var other = BootstrapTestConfiguration.Valid();
        other.Username = "competing-admin";
        await using var first = Services(database, BootstrapTestConfiguration.Valid());
        await using var second = Services(database, other);
        if (roleExists)
        {
            await using var scope = first.CreateAsyncScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(InitialAdminBootstrapper.SuperAdminRole)
                { Id = Guid.NewGuid() })).Succeeded);
        }
        await Task.WhenAll(BootstrapAsync(first), BootstrapAsync(second));
        await using var db = database.CreateContext();
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.Roles.ToListAsync());
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task Username_collision_does_not_promote_or_reset_existing_user()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var config = BootstrapTestConfiguration.Valid();
        await using var services = Services(database, config);
        await using (var scope = services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.CreateAsync(new User
            {
                Id = Guid.NewGuid(), UserName = config.Username, Firstname = "Existing", Lastname = "User"
            }, BootstrapTestConfiguration.Valid().Password!)).Succeeded);
        }
        await using var db = database.CreateContext();
        var before = await db.Users.AsNoTracking().SingleAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BootstrapAsync(services));
        Assert.Contains("DuplicateUserName", error.Message);
        var after = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.True(before.PasswordHash == after.PasswordHash);
        Assert.Equal("Existing", after.Firstname);
        Assert.False(after.MustChangePassword);
        Assert.Empty(await db.UserRoles.ToListAsync());
    }
}
