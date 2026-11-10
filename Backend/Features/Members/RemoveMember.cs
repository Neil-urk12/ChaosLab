using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Backend.Features.Members;

// Command: soft-removes an active member (RemovedAt), keeping the row so re-adding links the same history.
public static class RemoveMember
{
    public static async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<string>>> Handle(
        Guid id, Guid userId, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role != ProjectRole.Owner)
            return TypedResults.Forbid();

        var member = await MemberRules.ActiveAsync(db, id, userId, cancellationToken);
        if (member is null)
            return TypedResults.NotFound();
        if (await MemberRules.IsLastOwnerAsync(db, member, cancellationToken))
            return TypedResults.Conflict(MemberRules.LastOwnerMessage);

        member.RemovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
