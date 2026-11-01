using Backend.Domain.Entities;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Backend.Features.Auth;

// Command: trades a valid refresh token for new bearer tokens; a rotated security stamp rejects it.
public static class RefreshToken
{
    public sealed record Request(string? RefreshToken);

    public static async Task<Results<SignInHttpResult, UnauthorizedHttpResult>> Handle(
        Request request, SignInManager<User> signIn, IOptionsMonitor<BearerTokenOptions> bearerOptions, TimeProvider time,
        ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(RefreshToken).FullName!);
        if (string.IsNullOrEmpty(request.RefreshToken))
            return TypedResults.Unauthorized();

        var ticket = bearerOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(request.RefreshToken);

        if (ticket?.Properties.ExpiresUtc is not { } expiresUtc || time.GetUtcNow() >= expiresUtc
            || await signIn.ValidateSecurityStampAsync(ticket.Principal) is not { IsDeleted: false } user)
        {
            logger.LogWarning("Token refresh rejected: invalid, expired, or revoked refresh token.");
            return TypedResults.Unauthorized();
        }

        logger.LogInformation("User {UserId} refreshed their bearer token.", user.Id);
        var principal = await signIn.CreateUserPrincipalAsync(user);
        return TypedResults.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
    }
}
