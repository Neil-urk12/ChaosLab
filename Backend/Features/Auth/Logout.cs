using System.Security.Claims;
using Backend.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Backend.Features.Auth;

// Command: web clears the cookie; mobile rotates the security stamp so every refresh token stops working.
public static class Logout
{
    public static async Task<NoContent> Handle(
        HttpContext context, ClaimsPrincipal principal, UserManager<User> users, SignInManager<User> signIn, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(Logout).FullName!);
        await signIn.SignOutAsync();

        // Bearer access tokens cannot be revoked one by one; they expire within 15 minutes.
        // Rotating the stamp also signs the user out of their other devices.
        if ((await context.AuthenticateAsync(IdentityConstants.BearerScheme)).Succeeded
            && await users.GetUserAsync(principal) is { } user)
        {
            await users.UpdateSecurityStampAsync(user);
            logger.LogInformation("User {UserId} signed out; bearer refresh tokens revoked.", user.Id);
        }
        else
        {
            logger.LogInformation("User {UserId} signed out.", users.GetUserId(principal));
        }

        return TypedResults.NoContent();
    }
}
