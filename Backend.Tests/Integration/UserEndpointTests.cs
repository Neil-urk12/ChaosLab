using System.Net;
using System.Net.Http.Json;
using Backend.Features.Auth;
using Backend.Features.Projects;
using Backend.Features.Users;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class UserEndpointTests(PostgreSqlFixture postgres)
{
    private const string TemporaryPassword = "Tt3#temporary-password";

    private static object NewUser(string username = "priya", string email = "priya@example.test") => new
    {
        username, email, firstName = "Priya", lastName = "Nair", password = TemporaryPassword
    };

    [Fact]
    public async Task SuperAdmin_creates_an_account_that_must_change_its_password_and_can_join_a_project()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var admin = await TestAuth.SignedInAdminAsync(factory, TestAuth.ValidAdmin());

        var created = await admin.PostAsJsonAsync("/api/users", NewUser());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = (await created.Content.ReadFromJsonAsync<CreateUser.Response>())!;
        Assert.Equal(("priya", "Priya Nair"), (user.UserName, user.DisplayName));

        // Duplicates are validation errors, matched case-insensitively.
        var duplicateName = await admin.PostAsJsonAsync("/api/users", NewUser("PRIYA", "other@example.test"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicateName.StatusCode);
        Assert.Contains("\"username\"", await duplicateName.Content.ReadAsStringAsync());
        var duplicateEmail = await admin.PostAsJsonAsync("/api/users", NewUser("other", "PRIYA@example.test"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicateEmail.StatusCode);
        Assert.Contains("\"email\"", await duplicateEmail.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/users", new { username = "x", email = "not-an-email" })).StatusCode);

        // The new user is held at the password change until they make it.
        using var priya = TestAuth.CreateClient(factory);
        Assert.Equal(HttpStatusCode.OK, (await priya.PostAsJsonAsync("/api/auth/login",
            new { identifier = "priya", password = TemporaryPassword })).StatusCode);
        Assert.True((await priya.GetFromJsonAsync<CurrentUser.Response>("/api/auth/me"))!.MustChangePassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await priya.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await priya.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = TemporaryPassword, newPassword = TestAuth.ChangedPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await priya.GetAsync("/api/projects")).StatusCode);

        // A regular user can't create accounts; an anonymous caller isn't signed in.
        Assert.Equal(HttpStatusCode.Forbidden, (await priya.PostAsJsonAsync("/api/users", NewUser("third", "third@example.test"))).StatusCode);
        using var anonymous = TestAuth.CreateClient(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/users", NewUser("third", "third@example.test"))).StatusCode);

        var project = await (await admin.PostAsJsonAsync("/api/projects", new { name = "Payments" }))
            .Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/projects/{project!.Id}/members",
            new { userId = user.Id, role = "Viewer" })).StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_with_a_pending_password_change_cannot_create_accounts()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        var admin = TestAuth.ValidAdmin();
        await TestAuth.InitializeAsync(factory, admin, pendingPasswordChange: true);
        using var client = TestAuth.CreateClient(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login",
            new { identifier = admin.Username, password = admin.Password })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/users", NewUser())).StatusCode);
    }
}
