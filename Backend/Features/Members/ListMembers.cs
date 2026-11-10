using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Members;

// Query: a project's active and removed members, plus the caller's role so the page knows whether it may manage them.
public static class ListMembers
{
    public sealed record Response(ProjectRole YourRole, List<Row> Members, List<Row> Removed);

    // Role of a removed row is the role the member held when removed.
    public sealed record Row(
        Guid UserId, string DisplayName, string? Email, ProjectRole Role, DateTimeOffset JoinedAt, DateTimeOffset? RemovedAt);

    public static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is not { } role)
            return TypedResults.NotFound();

        var rows = await db.ProjectMembers.AsNoTracking()
            .Where(member => member.ProjectId == id)
            .OrderBy(member => member.Role).ThenBy(member => member.JoinedAt)
            .Select(member => new Row(member.UserId, member.User.DisplayName, member.User.Email, member.Role,
                member.JoinedAt, member.RemovedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new Response(role,
            rows.Where(row => row.RemovedAt is null).ToList(),
            rows.Where(row => row.RemovedAt is not null).OrderByDescending(row => row.RemovedAt).ToList()));
    }
}
