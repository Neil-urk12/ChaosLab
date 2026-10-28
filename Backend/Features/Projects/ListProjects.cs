using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Projects;

// Query: read-only, untracked, projected straight into the response DTO.
public static class ListProjects
{
    // shortcut: lists every active project, no membership filter; scope to the caller's memberships once auth exists.
    public static async Task<List<ProjectResponse>> Handle(ApplicationDbContext db, CancellationToken cancellationToken) =>
        await db.Projects.AsNoTracking()
            .Where(project => project.ArchivedAt == null)
            .OrderByDescending(project => project.CreatedAt)
            .Select(project => new ProjectResponse(
                project.Id, project.Name, project.Description, project.CreatedAt, project.UpdatedAt))
            .ToListAsync(cancellationToken);
}
