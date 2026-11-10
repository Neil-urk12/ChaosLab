using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Members;

internal static class MemberRules
{
    public const string LastOwnerMessage = "A project needs at least one owner. Make someone else an owner first.";

    public static Task<ProjectMember?> ActiveAsync(
        ApplicationDbContext db, Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        db.ProjectMembers.SingleOrDefaultAsync(member => member.ProjectId == projectId && member.UserId == userId
            && member.RemovedAt == null, cancellationToken);

    // shortcut: check-then-write, so two owners demoting each other at the same moment can both succeed; add a lock if that matters.
    public static async Task<bool> IsLastOwnerAsync(ApplicationDbContext db, ProjectMember member, CancellationToken cancellationToken) =>
        member.Role == ProjectRole.Owner && !await db.ProjectMembers.AnyAsync(other => other.ProjectId == member.ProjectId
            && other.UserId != member.UserId && other.RemovedAt == null && other.Role == ProjectRole.Owner, cancellationToken);

    public static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
