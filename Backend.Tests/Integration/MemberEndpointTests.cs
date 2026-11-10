using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Members;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class MemberEndpointTests(PostgreSqlFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Owner_adds_changes_removes_and_re_adds_a_member_keeping_history()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        var admin = TestAuth.ValidAdmin();
        using var client = await TestAuth.SignedInAdminAsync(factory, admin);
        var project = await (await client.PostAsJsonAsync("/api/projects", new { name = "Payments" }))
            .Content.ReadFromJsonAsync<ProjectResponse>();
        var members = $"/api/projects/{project!.Id}/members";

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var priya = new User { Id = Guid.NewGuid(), UserName = "priya", Email = "priya.nair@chaoslab.test", Firstname = "Priya", Lastname = "Nair", DisplayName = "Priya Nair" };
        db.Users.Add(priya);
        await db.SaveChangesAsync();
        var adminId = (await db.Users.SingleAsync(user => user.UserName == admin.Username)).Id;

        var found = await client.GetFromJsonAsync<List<SearchCandidates.Row>>($"{members}/candidates?q=PRI", Json);
        Assert.Equal((priya.Id, (ProjectRole?)null), (Assert.Single(found!).UserId, found![0].Role));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(members, new { userId = priya.Id, role = "Editor" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(members, new { userId = priya.Id, role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsJsonAsync($"{members}/{priya.Id}", new { role = "Viewer" })).StatusCode);

        // The only owner can neither step down nor leave.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"{members}/{adminId}", new { role = "Editor" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{members}/{adminId}")).StatusCode);

        var joinedAt = (await db.ProjectMembers.AsNoTracking().SingleAsync(m => m.UserId == priya.Id)).JoinedAt;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{members}/{priya.Id}")).StatusCode);
        var listed = await client.GetFromJsonAsync<ListMembers.Response>(members, Json);
        Assert.Equal(ProjectRole.Owner, listed!.YourRole);
        Assert.Equal(adminId, Assert.Single(listed.Members).UserId);
        Assert.Equal((priya.Id, ProjectRole.Viewer), (Assert.Single(listed.Removed).UserId, listed.Removed[0].Role));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(members, new { userId = priya.Id, role = "Viewer" })).StatusCode);
        var readded = await db.ProjectMembers.AsNoTracking().SingleAsync(m => m.UserId == priya.Id);
        Assert.Equal((joinedAt, (DateTimeOffset?)null), (readded.JoinedAt, readded.RemovedAt));
    }

    [Fact]
    public async Task Non_owners_cannot_manage_members()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        var admin = TestAuth.ValidAdmin();
        using var client = await TestAuth.SignedInAdminAsync(factory, admin);
        var project = await (await client.PostAsJsonAsync("/api/projects", new { name = "Payments" }))
            .Content.ReadFromJsonAsync<ProjectResponse>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var member = await db.ProjectMembers.SingleAsync(m => m.ProjectId == project!.Id);
        member.Role = ProjectRole.Editor;
        await db.SaveChangesAsync();

        var members = $"/api/projects/{project!.Id}/members";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(members)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{members}/candidates?q=a")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(members, new { userId = Guid.NewGuid(), role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"{members}/{Guid.NewGuid()}", new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{members}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{Guid.NewGuid()}/members")).StatusCode);
    }
}
