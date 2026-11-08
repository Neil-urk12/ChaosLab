using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Backend.Infrastructure.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Environments;

// Commands on one global header. A header can be made secret but never back: that would reveal the value.
public static class UpdateHeader
{
    public sealed record Request(bool? Secret);

    public static async Task<Results<NoContent, ValidationProblem, NotFound, ForbidHttpResult>> Handle(
        Guid id, Guid headerId, Request request, ClaimsPrincipal principal, ApplicationDbContext db,
        CredentialProtector protector, CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();
        var header = await db.EnvironmentHeaders.SingleOrDefaultAsync(header => header.Id == headerId
            && header.Environment.ProjectId == id && header.Environment.IsDefault, cancellationToken);
        if (header is null)
            return TypedResults.NotFound();
        if (request.Secret != true)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                { ["secret"] = ["A secret header cannot be made plain; remove it and add it again."] });

        if (header.Value is { } value)
        {
            header.SecretCipherText = protector.Protect(value, header.Id);
            header.Hint = CredentialProtector.Hint(value);
            header.Value = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        return TypedResults.NoContent();
    }

    public static async Task<Results<NoContent, NotFound, ForbidHttpResult>> Remove(
        Guid id, Guid headerId, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();
        var deleted = await db.EnvironmentHeaders.Where(header => header.Id == headerId
            && header.Environment.ProjectId == id && header.Environment.IsDefault).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}
