using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Members;

// Query: existing accounts on this instance matching a name, username or email, for the add-member picker. Owners only.
public static class SearchCandidates
{
    public const int Limit = 8;

    // Role is set when the account is already an active member.
    public sealed record Row(Guid UserId, string DisplayName, string? Email, ProjectRole? Role);

    public static async Task<Results<Ok<List<Row>>, NotFound, ForbidHttpResult>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken, string? q = null)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role != ProjectRole.Owner)
            return TypedResults.Forbid();

        var text = q?.Trim().ToLower();
        if (string.IsNullOrEmpty(text))
            return TypedResults.Ok(new List<Row>());

        return TypedResults.Ok(await db.Users.AsNoTracking()
            .Where(user => !user.IsDeleted && (user.DisplayName.ToLower().Contains(text)
                || user.UserName!.ToLower().Contains(text) || user.Email!.ToLower().Contains(text)))
            .OrderBy(user => user.DisplayName).ThenBy(user => user.Id)
            .Take(Limit)
            .Select(user => new Row(user.Id, user.DisplayName, user.Email, user.ProjectMemberships
                .Where(member => member.ProjectId == id && member.RemovedAt == null)
                .Select(member => (ProjectRole?)member.Role).FirstOrDefault()))
            .ToListAsync(cancellationToken));
    }
}
