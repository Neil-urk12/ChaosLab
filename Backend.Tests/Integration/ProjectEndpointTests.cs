using System.Net;
using System.Net.Http.Json;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProjectEndpointTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task Create_persists_project_with_owner_and_list_returns_it()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var create = await client.PostAsJsonAsync("/api/projects", new { name = "  Payments API  ", description = " " });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(created);
        Assert.Equal("Payments API", created.Name);
        Assert.Null(created.Description);
        Assert.Equal($"/api/projects/{created.Id}", create.Headers.Location?.OriginalString);

        var listed = await client.GetFromJsonAsync<List<ProjectResponse>>("/api/projects");
        Assert.Equal(created.Id, Assert.Single(listed!).Id);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var member = await db.ProjectMembers.SingleAsync(x => x.ProjectId == created.Id);
        Assert.Equal(ProjectRole.Owner, member.Role);
        Assert.Equal((await db.Projects.SingleAsync(x => x.Id == created.Id)).CreatedById, member.UserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Create_rejects_missing_name(string? name)
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.PostAsJsonAsync("/api/projects", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"name\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_rejects_name_longer_than_column()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await using var factory = new BackendFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.PostAsJsonAsync("/api/projects", new { name = new string('a', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
