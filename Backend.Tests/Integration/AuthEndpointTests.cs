using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.Features.Auth;
using Backend.Domain.Entities;
using Backend.Infrastructure.Identity;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]

public sealed class AuthEndpointTests(PostgreSqlFixture postgres)
{
    private sealed record TokenResponse(string AccessToken, string RefreshToken, long ExpiresIn);

    [Fact]
    public async Task Unauthenticated_requests_get_401_instead_of_a_login_redirect()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = TestAuth.CreateClient(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Cookie_login_requires_password_change_before_other_endpoints()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var admin = BootstrapTestConfiguration.Valid();
        await using var factory = new BackendFactory(database, admin);
        using var client = TestAuth.CreateClient(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
                        new
                        {
                            identifier = admin.Username,
                            password = "Wrong1!password"
                        })).StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login",
                new
                {
                    identifier = admin.Username,
                    password = admin.Password
                });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<CurrentUser.Response>("/api/auth/me");
        Assert.True(me!.MustChangePassword);
        Assert.Equal(InitialAdminBootstrapper.SuperAdminRole, Assert.Single(me.Roles));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/projects")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/change-password",
                        new
                        {
                            currentPassword = "Wrong1!password",
                            newPassword = TestAuth.ChangedPassword
                        })).StatusCode);
        Assert.True((await client.GetFromJsonAsync<CurrentUser.Response>("/api/auth/me"))!.MustChangePassword);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/change-password",
                        new
                        {
                            currentPassword = admin.Password,
                            newPassword = TestAuth.ChangedPassword
                        })).StatusCode);
        Assert.False((await client.GetFromJsonAsync<CurrentUser.Response>("/api/auth/me"))!.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/projects")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Bearer_tokens_authenticate_refresh_and_are_revoked_by_password_change()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var admin = BootstrapTestConfiguration.Valid();
        await using var factory = new BackendFactory(database, admin);
        using var client = TestAuth.CreateClient(factory);

        var token = await client.PostAsJsonAsync("/api/auth/token",
                new
                {
                    identifier = admin.Username,
                    password = admin.Password
                });
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        Assert.False(token.Headers.Contains("Set-Cookie"));
        var tokens = (await token.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.True((await client.GetFromJsonAsync<CurrentUser.Response>("/api/auth/me"))!.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/projects")).StatusCode);

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh",
                new
                {
                    refreshToken = tokens.RefreshToken
                });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/change-password",
                        new
                        {
                            currentPassword = admin.Password,
                            newPassword = TestAuth.ChangedPassword
                        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh",
                        new
                        {
                            refreshToken = tokens.RefreshToken
                        })).StatusCode);

        var relogin = await client.PostAsJsonAsync("/api/auth/token",
                new
                {
                    identifier = admin.Username,
                    password = TestAuth.ChangedPassword
                });
        var fresh = (await relogin.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fresh.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/projects")).StatusCode);
    }

    [Fact]
    public async Task Login_accepts_email_but_rejects_an_email_shared_by_two_accounts()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var admin = BootstrapTestConfiguration.Valid();
        admin.Email = "admin@toma.test";
        await using var factory = new BackendFactory(database, admin);
        using var client = TestAuth.CreateClient(factory);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login",
                        new
                        {
                            identifier = "ADMIN@toma.test",
                            password = admin.Password
                        })).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var duplicate = new User
            {
                Id = Guid.NewGuid(),
                UserName = "second-user",
                Email = admin.Email,
                Firstname = "Second",
                Lastname = "User"
            };
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
                        new
                        {
                            identifier = admin.Email,
                            password = admin.Password
                        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login",
                        new
                        {
                            identifier = admin.Username,
                            password = admin.Password
                        })).StatusCode);
    }

    [Fact]

    public async Task Bearer_logout_revokes_refresh_tokens_but_web_logout_does_not()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var admin = BootstrapTestConfiguration.Valid();
        await using var factory = new BackendFactory(database, admin);
        using var mobile = TestAuth.CreateClient(factory);
        using var web = TestAuth.CreateClient(factory);

        var tokens = (await (await mobile.PostAsJsonAsync("/api/auth/token",
                        new
                        {
                            identifier = admin.Username,
                            password = admin.Password
                        })).Content.ReadFromJsonAsync<TokenResponse>())!;

        (await web.PostAsJsonAsync("/api/auth/login",
                        new
                        {
                            identifier = admin.Username,
                            password = admin.Password
                        }))
        .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await web.PostAsync("/api/auth/logout", null)).StatusCode);
        var refreshed = await mobile.PostAsJsonAsync("/api/auth/refresh",
                new
                {
                    refreshToken = tokens.RefreshToken
                });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var current = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;

        mobile.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", current.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await mobile.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.PostAsJsonAsync("/api/auth/refresh",
                        new
                        {
                            refreshToken = current.RefreshToken
                        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.PostAsJsonAsync("/api/auth/refresh",
                        new
                        {
                            refreshToken = tokens.RefreshToken
                        })).StatusCode);
    }
}
