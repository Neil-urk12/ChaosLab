using Backend.Domain.Entities;
using Backend.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Backend.Features.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");

        // Web: sets the HttpOnly cookie. Mobile: returns { accessToken, refreshToken, expiresIn }.
        auth.MapPost("/login", (Login.Request request, UserManager<User> users, SignInManager<User> signIn, ILoggerFactory loggers) =>
            Login.Handle(request, IdentityConstants.ApplicationScheme, users, signIn, loggers)).AllowAnonymous();
        auth.MapPost("/token", (Login.Request request, UserManager<User> users, SignInManager<User> signIn, ILoggerFactory loggers) =>
            Login.Handle(request, IdentityConstants.BearerScheme, users, signIn, loggers)).AllowAnonymous();
        auth.MapPost("/refresh", RefreshToken.Handle).AllowAnonymous();

        auth.MapPost("/setup", Setup.Handle).AllowAnonymous();

        // Reachable while a required password change is still pending.
        var pending = auth.MapGroup("").RequireAuthorization(AuthSetup.AllowPendingPasswordChangePolicy);
        pending.MapGet("/me", CurrentUser.Handle);
        pending.MapPost("/change-password", ChangePassword.Handle);
        pending.MapPost("/logout", Logout.Handle);
    }
}
