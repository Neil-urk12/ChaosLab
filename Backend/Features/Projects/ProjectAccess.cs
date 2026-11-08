using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Projects;

public static class ProjectAccess
{
    // The caller's role on an active project, or null when they are not an active member (callers answer 404).
    public static Task<ProjectRole?> RoleAsync(
        ApplicationDbContext db, Guid projectId, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return db.ProjectMembers.AsNoTracking()
            .Where(member => member.ProjectId == projectId && member.UserId == userId
                && member.RemovedAt == null && member.Project.ArchivedAt == null)
            .Select(member => (ProjectRole?)member.Role)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
