using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Features.Environments;

// Command: deletes a scheme's credential. Operations that need it are then skipped with a reason.
public static class RemoveCredential
{
    public static async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<string>>> Handle(
        Guid id, string scheme, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var profile = await db.AuthProfiles.SingleOrDefaultAsync(profile => profile.Name == scheme
            && profile.Environment.ProjectId == id && profile.Environment.IsDefault, cancellationToken);
        if (profile is null)
            return TypedResults.NotFound();
        db.AuthProfiles.Remove(profile);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return TypedResults.Conflict("Test runs used this credential, so it cannot be removed. Replace it instead.");
        }
        return TypedResults.NoContent();
    }
}
