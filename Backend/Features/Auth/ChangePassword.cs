using System.Security.Claims;
using Backend.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Backend.Features.Auth;

// Command: replaces the password and clears MustChangePassword in the same save.
public static class ChangePassword
{
    public sealed record Request(string? CurrentPassword, string? NewPassword);

    public static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> Handle(
        Request request, ClaimsPrincipal principal, UserManager<User> users, SignInManager<User> signIn, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(ChangePassword).FullName!);
        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Current and new password are required."]
            });

        var user = await users.GetUserAsync(principal);
        if (user is null || user.IsDeleted)
            return TypedResults.Unauthorized();

        user.MustChangePassword = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        // ChangePasswordAsync saves the user (including the flag) only when the change succeeds.
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            logger.LogWarning("Password change failed for user {UserId}: {ErrorCodes}.",
                user.Id, string.Join(", ", result.Errors.Select(error => error.Code)));
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = result.Errors.Select(error => error.Description).ToArray()
            });
        }

        logger.LogInformation("User {UserId} changed their password; existing refresh tokens revoked.", user.Id);
        // Re-issue the web cookie with the new claims and security stamp. Bearer clients sign in again.
        await signIn.RefreshSignInAsync(user);
        return TypedResults.NoContent();
    }
}
