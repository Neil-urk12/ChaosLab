using System.Security.Claims;
using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Projects;

// Query: read-only, untracked, projected straight into the response DTO.
public static class ListProjects
{
    public static async Task<List<ProjectResponse>> Handle(
        ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await db.Projects.AsNoTracking()
            .Where(project => project.ArchivedAt == null
                && project.Members.Any(member => member.UserId == userId && member.RemovedAt == null))
            .OrderByDescending(project => project.CreatedAt)
            .Select(project => new ProjectResponse(
                project.Id, project.Name, project.Description, project.CreatedAt, project.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
