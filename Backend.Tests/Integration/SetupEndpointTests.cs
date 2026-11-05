using System.Net;
using System.Net.Http.Json;
using Backend.Domain.Entities;
using Backend.Features.Auth;
using Backend.Infrastructure.Identity;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class SetupEndpointTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task Startup_does_not_create_accounts_or_roles_and_setup_signs_in_ready_admin()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database, publicUrl: "https://example.test/chaos/");
        using var client = TestAuth.CreateClient(factory);
        await using var db = database.CreateContext();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Roles.ToListAsync());
        var token = TestAuth.SetupToken(factory);
        var setupToken = factory.Services.GetRequiredService<BootstrapSetupToken>();
        Assert.Same(setupToken, factory.Services.GetRequiredService<BootstrapSetupToken>());
        var hasExpectedSetupPath = setupToken.SetupUrl("https://example.test/chaos/").StartsWith(
            "https://example.test/chaos/install#token=", StringComparison.Ordinal);
        Assert.True(hasExpectedSetupPath);

        var admin = TestAuth.ValidAdmin();
        var response = await client.PostAsJsonAsync("/api/auth/setup", admin with { Token = token });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains(".AspNetCore.Identity.Application=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        var me = (await client.GetFromJsonAsync<CurrentUser.Response>("/api/auth/me"))!;
        Assert.False(me.MustChangePassword);
        Assert.Equal(AuthSetup.SuperAdminRole, Assert.Single(me.Roles));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/projects")).StatusCode);

        var user = Assert.Single(await db.Users.ToListAsync());
        Assert.Equal(admin.Username, user.UserName);
        Assert.Equal(admin.Email, user.Email);
        Assert.Equal(admin.FirstName, user.Firstname);
        Assert.Equal(admin.LastName, user.Lastname);
        Assert.False(user.MustChangePassword);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.True(await users.CheckPasswordAsync(user, admin.Password!));
        var plaintextPersisted = db.Entry(user).Properties.Any(property => property.CurrentValue is string value
            && value.Contains(admin.Password!, StringComparison.Ordinal));
        Assert.False(plaintextPersisted);
        Assert.Equal(AuthSetup.SuperAdminRole, Assert.Single(await db.Roles.ToListAsync()).Name);
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Missing_or_invalid_token_is_forbidden_without_writes(string? token)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = TestAuth.CreateClient(factory);
        var response = await client.PostAsJsonAsync("/api/auth/setup", TestAuth.ValidAdmin() with { Token = token });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var db = database.CreateContext();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Roles.ToListAsync());
    }

    [Fact]
    public async Task Omitted_token_is_forbidden()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = TestAuth.CreateClient(factory);
        var response = await client.PostAsJsonAsync("/api/auth/setup", new { username = "admin" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = database.CreateContext();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Roles.ToListAsync());
    }

    [Fact]
    public async Task There_is_no_public_endpoint_to_fetch_setup_secrets()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = TestAuth.CreateClient(factory);
        var token = TestAuth.SetupToken(factory);
        foreach (var path in new[] { "/api/auth/setup", "/api/auth/bootstrap-credentials" })
        {
            var response = await client.GetAsync(path);
            Assert.False(response.IsSuccessStatusCode);
            Assert.False((await response.Content.ReadAsStringAsync()).Contains(token, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Expired_token_requires_restart_and_old_process_token_is_not_accepted()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var time = new SetupTimeProvider();
        var admin = TestAuth.ValidAdmin();
        string expiredToken;
        await using (var first = new BackendFactory(database, timeProvider: time))
        {
            using var client = TestAuth.CreateClient(first);
            expiredToken = TestAuth.SetupToken(first);
            time.Advance(TimeSpan.FromHours(1));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/setup",
                admin with { Token = expiredToken })).StatusCode);
            await using var db = database.CreateContext();
            Assert.Empty(await db.Users.ToListAsync());
            Assert.Empty(await db.Roles.ToListAsync());
        }
        await using var restarted = new BackendFactory(database);
        using var restartedClient = TestAuth.CreateClient(restarted);
        Assert.False(expiredToken == TestAuth.SetupToken(restarted));
        Assert.Equal(HttpStatusCode.Forbidden, (await restartedClient.PostAsJsonAsync("/api/auth/setup",
            admin with { Token = expiredToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await restartedClient.PostAsJsonAsync("/api/auth/setup",
            admin with { Token = TestAuth.SetupToken(restarted) })).StatusCode);
    }

    [Theory]
    [InlineData("required")]
    [InlineData("length")]
    [InlineData("email")]
    [InlineData("password")]
    [InlineData("username")]
    public async Task Validation_returns_errors_map_rolls_back_role_and_leaves_token_usable(string invalidField)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = TestAuth.CreateClient(factory);
        var valid = TestAuth.ValidAdmin() with { Token = TestAuth.SetupToken(factory) };
        var invalid = invalidField switch
        {
            "required" => valid with { Username = null, Email = null, FirstName = null, LastName = null, Password = null },
            "length" => valid with { Username = new string('a', 257), Email = new string('a', 257),
                FirstName = new string('a', 101), LastName = new string('a', 101) },
            "email" => valid with { Email = "invalid-email" },
            "password" => valid with { Password = "weak" },
            _ => valid with { Username = "invalid username" }
        };
        var response = await client.PostAsJsonAsync("/api/auth/setup", invalid);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Equal(400, problem.Status);
        Assert.NotEmpty(problem.Errors);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var db = database.CreateContext();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Roles.ToListAsync());
        Assert.Empty(await db.UserRoles.ToListAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/setup", valid)).StatusCode);
    }

    [Fact]
    public async Task Assignment_validation_failure_rolls_back_all_writes_and_can_be_retried()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database, configureServices: services =>
            services.AddSingleton<IUserValidator<User>>(new RejectSecondValidation()));
        using var client = TestAuth.CreateClient(factory);
        var request = TestAuth.ValidAdmin() with { Token = TestAuth.SetupToken(factory) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/setup", request)).StatusCode);
        await using var db = database.CreateContext();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Roles.ToListAsync());
        Assert.Empty(await db.UserRoles.ToListAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/setup", request)).StatusCode);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.Roles.ToListAsync());
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_requests_create_one_initial_admin_even_across_instances(bool separateInstances)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var first = new BackendFactory(database);
        await using var second = new BackendFactory(database);
        using var firstClient = TestAuth.CreateClient(first);
        var competingFactory = separateInstances ? second : first;
        using var secondClient = TestAuth.CreateClient(competingFactory);
        var firstRequest = TestAuth.ValidAdmin() with { Token = TestAuth.SetupToken(first) };
        var secondRequest = TestAuth.ValidAdmin() with
        {
            Token = TestAuth.SetupToken(competingFactory), Username = "competing-admin"
        };
        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/auth/setup", firstRequest),
            secondClient.PostAsJsonAsync("/api/auth/setup", secondRequest));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await using var db = database.CreateContext();
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.Roles.ToListAsync());
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    [Theory]
    [InlineData("active")]
    [InlineData("soft-deleted")]
    [InlineData("deleted")]
    [InlineData("disabled")]
    public async Task Completed_setup_rejects_replay_and_never_reopens_after_restart(string state)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var admin = TestAuth.ValidAdmin();
        string originalToken;
        await using (var first = new BackendFactory(database))
        {
            using var client = TestAuth.CreateClient(first);
            originalToken = TestAuth.SetupToken(first);
            var request = admin with { Token = originalToken };
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/setup", request)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/setup", request)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/setup",
                request with { Token = null })).StatusCode);
        }
        await using (var db = database.CreateContext())
        {
            var user = await db.Users.SingleAsync();
            if (state == "deleted")
                db.Users.Remove(user);
            if (state == "soft-deleted")
                user.IsDeleted = true;
            if (state == "disabled")
            {
                user.LockoutEnabled = true;
                user.LockoutEnd = DateTimeOffset.MaxValue;
            }
            await db.SaveChangesAsync();
        }
        await using var restarted = new BackendFactory(database);
        using var restartedClient = TestAuth.CreateClient(restarted);
        var response = await restartedClient.PostAsJsonAsync("/api/auth/setup",
            admin with { Token = originalToken, Username = "replacement-admin" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var after = database.CreateContext();
        Assert.Equal(state == "deleted" ? 0 : 1, await after.Users.CountAsync());
        Assert.Single(await after.Roles.ToListAsync());
    }

    [Fact]
    public async Task Existing_installation_preserves_accounts_passwords_and_pending_password_requirement()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var admin = TestAuth.ValidAdmin();
        await using (var first = new BackendFactory(database))
        {
            using var client = TestAuth.CreateClient(first);
            await using var scope = first.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>(AuthSetup.SuperAdminRole)
                { Id = Guid.NewGuid() })).Succeeded);
            var legacyAdmin = new User { Id = Guid.NewGuid(), UserName = admin.Username,
                Firstname = "Legacy", Lastname = "Admin", MustChangePassword = true };
            Assert.True((await users.CreateAsync(legacyAdmin, admin.Password!)).Succeeded);
            Assert.True((await users.AddToRoleAsync(legacyAdmin, AuthSetup.SuperAdminRole)).Succeeded);
            Assert.True((await users.CreateAsync(new User { Id = Guid.NewGuid(), UserName = "other-user",
                Firstname = "Other", Lastname = "User" }, admin.Password!)).Succeeded);
        }
        await using var db = database.CreateContext();
        var before = await db.Users.AsNoTracking().OrderBy(user => user.UserName).ToListAsync();
        await using var restarted = new BackendFactory(database);
        using var restartedClient = TestAuth.CreateClient(restarted);
        Assert.Equal(HttpStatusCode.Conflict, (await restartedClient.PostAsJsonAsync("/api/auth/setup", admin)).StatusCode);
        var after = await db.Users.AsNoTracking().OrderBy(user => user.UserName).ToListAsync();
        Assert.Equal(before.Select(user => user.Id), after.Select(user => user.Id));
        for (var index = 0; index < before.Count; index++)
        {
            Assert.True(before[index].PasswordHash == after[index].PasswordHash);
            Assert.Equal(before[index].SecurityStamp, after[index].SecurityStamp);
            Assert.Equal(before[index].UpdatedAt, after[index].UpdatedAt);
            Assert.Equal(before[index].MustChangePassword, after[index].MustChangePassword);
        }
        Assert.Equal(HttpStatusCode.OK, (await restartedClient.PostAsJsonAsync("/api/auth/login",
            new { identifier = admin.Username, password = admin.Password })).StatusCode);
        Assert.True((await restartedClient.GetFromJsonAsync<CurrentUser.Response>("/api/auth/me"))!.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await restartedClient.GetAsync("/api/projects")).StatusCode);
    }

    [Fact]
    public async Task Username_collision_does_not_promote_or_change_existing_user()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = TestAuth.CreateClient(factory);
        var admin = TestAuth.ValidAdmin() with { Token = TestAuth.SetupToken(factory) };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.CreateAsync(new User { Id = Guid.NewGuid(), UserName = admin.Username,
                Firstname = "Existing", Lastname = "User" }, TestAuth.ValidAdmin().Password!)).Succeeded);
        }
        await using var db = database.CreateContext();
        var before = await db.Users.AsNoTracking().SingleAsync();
        var response = await client.PostAsJsonAsync("/api/auth/setup", admin);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("username", (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
        var after = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.True(before.PasswordHash == after.PasswordHash);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal("Existing", after.Firstname);
        Assert.Empty(await db.Roles.ToListAsync());
        Assert.Empty(await db.UserRoles.ToListAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/setup",
            admin with { Username = "new-admin" })).StatusCode);
    }

    private sealed class RejectSecondValidation : IUserValidator<User>
    {
        private int _calls;

        public Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user) =>
            Task.FromResult(Interlocked.Increment(ref _calls) == 2
                ? IdentityResult.Failed(new IdentityError { Code = "AssignmentRejected", Description = "Test failure" })
                : IdentityResult.Success);
    }
}
