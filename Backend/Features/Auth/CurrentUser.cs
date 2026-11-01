using System.Security.Claims;
using Backend.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Backend.Features.Auth;

// Query: the signed-in user's profile, roles, and whether a password change is pending.
public static class CurrentUser
{
    public sealed record Response(
        Guid Id,
        string UserName,
        string DisplayName,
        string? Email,
        bool MustChangePassword,
        IList<string> Roles);

    public static async Task<Results<Ok<Response>, UnauthorizedHttpResult>> Handle(
        ClaimsPrincipal principal, UserManager<User> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null || user.IsDeleted)
            return TypedResults.Unauthorized();

        return TypedResults.Ok(new Response(
            user.Id, user.UserName!, user.DisplayName, user.Email, user.MustChangePassword, await users.GetRolesAsync(user)));
    }
}
