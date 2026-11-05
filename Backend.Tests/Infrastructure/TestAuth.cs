using System.Net.Http.Json;
using System.Security.Cryptography;
using Backend.Domain.Entities;
using Backend.Features.Auth;
using Backend.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Infrastructure;

internal static class TestAuth
{
    public const string ChangedPassword = "Bb2@changed-password";

    public static Setup.Request ValidAdmin() => new(
        null, "installation-admin", "admin@example.test", "Initial", "Administrator",
        $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}");

    public static HttpClient CreateClient(BackendFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    public static string SetupToken(BackendFactory factory) =>
        factory.Services.GetRequiredService<BootstrapSetupToken>()
            .SetupUrl("http://localhost:5173").Split("#token=")[1];

    public static async Task<HttpClient> SignedInAdminAsync(BackendFactory factory, Setup.Request admin)
    {
        var client = CreateClient(factory);
        (await client.PostAsJsonAsync("/api/auth/setup", admin with { Token = SetupToken(factory) }))
            .EnsureSuccessStatusCode();
        return client;
    }

    public static async Task InitializeAsync(
        BackendFactory factory, Setup.Request admin, bool pendingPasswordChange = false)
    {
        using var client = await SignedInAdminAsync(factory, admin);
        if (pendingPasswordChange)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await users.FindByNameAsync(admin.Username!))!;
            user.MustChangePassword = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }
    }
}
