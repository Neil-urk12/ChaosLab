using Backend.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Auth;

// Command: signs in with the cookie scheme (web) or the bearer scheme (mobile).
public static class Login
{
    // Identifier is a username or an email address.
    public sealed record Request(string? Identifier, string? Password);

    public static async Task<Results<EmptyHttpResult, UnauthorizedHttpResult>> Handle(
        Request request, string scheme, UserManager<User> users, SignInManager<User> signIn, ILoggerFactory loggers)
    {
        // Log by user id only: raw identifiers are personal data and sometimes a mistyped password.
        var logger = loggers.CreateLogger(typeof(Login).FullName!);
        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrEmpty(request.Password))
            return TypedResults.Unauthorized();

        var user = await FindAsync(users, request.Identifier.Trim());
        if (user is null || user.IsDeleted)
        {
            logger.LogWarning("Login failed: no active account matches the identifier.");
            return TypedResults.Unauthorized();
        }

        // The bearer handler writes the token response itself; the cookie handler sets the cookie.
        signIn.AuthenticationScheme = scheme;
        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            logger.LogInformation("User {UserId} signed in with {Scheme}.", user.Id, scheme);
            return TypedResults.Empty;
        }

        if (result.IsLockedOut)
            logger.LogWarning("Login rejected: user {UserId} is locked out.", user.Id);
        else
            logger.LogWarning("Login failed: wrong password for user {UserId}.", user.Id);
        return TypedResults.Unauthorized();
    }

    private static async Task<User?> FindAsync(UserManager<User> users, string identifier)
    {
        var byName = await users.FindByNameAsync(identifier);
        if (byName is not null)
            return byName;

        // Emails are not unique in this Identity setup, so an email shared by two accounts matches none.
        var normalizedEmail = users.NormalizeEmail(identifier);
        var byEmail = await users.Users.Where(user => user.NormalizedEmail == normalizedEmail).Take(2).ToListAsync();
        return byEmail.Count == 1 ? byEmail[0] : null;
    }
}
