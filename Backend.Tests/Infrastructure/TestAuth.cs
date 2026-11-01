using System.Net.Http.Json;
using Backend.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Backend.Tests.Infrastructure;

internal static class TestAuth
{
    public const string ChangedPassword = "Bb2@changed-password";

    public static HttpClient CreateClient(BackendFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    // Signs in as the bootstrap admin and replaces the bootstrap password, leaving a ready auth cookie.
    public static async Task<HttpClient> SignedInAdminAsync(BackendFactory factory, BootstrapAdminOptions admin)
    {
        var client = CreateClient(factory);
        (await client.PostAsJsonAsync("/api/auth/login", new { identifier = admin.Username, password = admin.Password }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = admin.Password, newPassword = ChangedPassword })).EnsureSuccessStatusCode();
        return client;
    }
}
