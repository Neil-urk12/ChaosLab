using System.Security.Claims;
using Backend.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Backend.Infrastructure.Identity;

public static class AuthSetup
{
    public const string MustChangePasswordClaim = "must_change_password";
    public const string AllowPendingPasswordChangePolicy = "AllowPendingPasswordChange";
    public const string CookieOrBearerScheme = "CookieOrBearer";

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieOrBearerScheme)
            .AddPolicyScheme(CookieOrBearerScheme, CookieOrBearerScheme, options =>
                // Ordinal to match BearerTokenHandler, which only accepts "Bearer " with that exact casing.
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal)
                        ? IdentityConstants.BearerScheme
                        : IdentityConstants.ApplicationScheme)
            .AddBearerToken(IdentityConstants.BearerScheme, options =>
                options.BearerTokenExpiration = TimeSpan.FromMinutes(15))
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;

            // API clients get a status code, not a redirect to a login page that doesn't exist.
            options.Events.OnRedirectToLogin = context => WriteStatus(context, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => WriteStatus(context, StatusCodes.Status403Forbidden);
        });

        var passwordChanged = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !context.User.HasClaim(MustChangePasswordClaim, "true"))
            .Build();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(passwordChanged)
            .SetDefaultPolicy(passwordChanged)
            .AddPolicy(AllowPendingPasswordChangePolicy, policy => policy.RequireAuthenticatedUser());

        return services;
    }

    private static Task WriteStatus(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        context.Response.StatusCode = statusCode;

        return Task.CompletedTask;
    }
}

public sealed class AppUserClaimsPrincipalFactory(
    UserManager<User> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<User, IdentityRole<Guid>>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(AuthSetup.MustChangePasswordClaim, "true"));
        }

        return identity;
    }
}
