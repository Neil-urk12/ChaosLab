using Backend.Infrastructure.Identity;

namespace Backend.Features.Users;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Users").RequireAuthorization(AuthSetup.SuperAdminPolicy);
        users.MapPost("/", CreateUser.Handle);
    }
}
