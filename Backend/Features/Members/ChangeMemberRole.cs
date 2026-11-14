using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Members;

// Command: changes an active member's project role. The last owner cannot be demoted.
public static class ChangeMemberRole
{
    // Nullable so a missing role is rejected instead of binding to default(ProjectRole), which is Owner.
    public sealed record Request(ProjectRole? Role);

    public static async Task<Results<NoContent, ValidationProblem, NotFound, ForbidHttpResult, Conflict<string>>> Handle(
        Guid id, Guid userId, Request request, ClaimsPrincipal principal, ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role != ProjectRole.Owner)
            return TypedResults.Forbid();
        if (request.Role is not { } newRole || !Enum.IsDefined(newRole))
            return MemberRules.Invalid("role", "Choose Owner, Editor or Viewer.");

        var member = await MemberRules.ActiveAsync(db, id, userId, cancellationToken);
        if (member is null)
            return TypedResults.NotFound();
        if (newRole != ProjectRole.Owner && await MemberRules.IsLastOwnerAsync(db, member, cancellationToken))
            return TypedResults.Conflict(MemberRules.LastOwnerMessage);

        member.Role = newRole;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
