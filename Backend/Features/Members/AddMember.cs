using System.Security.Claims;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Features.Members;

// Command: adds an existing account to the project. A removed member is reactivated on the same row, keeping JoinedAt.
public static class AddMember
{
    public sealed record Request(Guid UserId, ProjectRole Role);

    public static async Task<Results<NoContent, ValidationProblem, NotFound, ForbidHttpResult, Conflict<string>>> Handle(
        Guid id, Request request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role != ProjectRole.Owner)
            return TypedResults.Forbid();
        if (!Enum.IsDefined(request.Role))
            return MemberRules.Invalid("role", "Choose Owner, Editor or Viewer.");
        if (!await db.Users.AnyAsync(user => user.Id == request.UserId && !user.IsDeleted, cancellationToken))
            return MemberRules.Invalid("userId", "No account matches this person.");

        var member = await db.ProjectMembers.SingleOrDefaultAsync(
            member => member.ProjectId == id && member.UserId == request.UserId, cancellationToken);
        if (member is null)
            db.ProjectMembers.Add(new ProjectMember { ProjectId = id, UserId = request.UserId, Role = request.Role });
        else if (member.RemovedAt is null)
            return TypedResults.Conflict("This person is already a member.");
        else
            member.Reactivate(request.Role);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        // Another request added the same person between the lookup and this insert.
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict("This person is already a member.");
        }
        return TypedResults.NoContent();
    }
}
