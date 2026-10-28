using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Infrastructure.Data;
using Backend.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Projects;

// Command: validates input, writes through tracked entities, returns the created resource.
public static class CreateProject
{
    public sealed record Request(string? Name, string? Description);

    public static async Task<Results<Created<ProjectResponse>, ValidationProblem>> Handle(
        Request request, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (string.IsNullOrEmpty(name))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
        if (name.Length > 200)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name must be at most 200 characters."] });

        // shortcut: acts as the SuperAdmin until auth exists; replace with the authenticated user's id.
        var creatorId = await db.UserRoles
            .Where(userRole => db.Roles.Any(role => role.Id == userRole.RoleId && role.Name == InitialAdminBootstrapper.SuperAdminRole))
            .Select(userRole => userRole.UserId)
            .FirstAsync(cancellationToken);

        var project = new Project { Id = Guid.NewGuid(), Name = name, Description = description, CreatedById = creatorId };
        project.Members.Add(new ProjectMember { UserId = creatorId, Role = ProjectRole.Owner });
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/projects/{project.Id}", new ProjectResponse(
            project.Id, project.Name, project.Description, project.CreatedAt, project.UpdatedAt));
    }
}
