using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.ApiSpecs;

// Command: chooses whether runs may call an endpoint of the active spec.
public static class SetEndpointTesting
{
    public sealed record Request(EndpointTesting? Testing);

    public static async Task<Results<NoContent, ValidationProblem, NotFound, ForbidHttpResult>> Handle(
        Guid id, Guid endpointId, Request request, ClaimsPrincipal principal, ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();
        if (request.Testing is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                { ["testing"] = ["Testing must be Included, Excluded, or NeverTest."] });

        var endpoint = await db.ApiEndpoints.SingleOrDefaultAsync(endpoint => endpoint.Id == endpointId
            && endpoint.ApiSpec.ProjectId == id && endpoint.ApiSpec.IsActive, cancellationToken);
        if (endpoint is null)
            return TypedResults.NotFound();

        endpoint.Testing = request.Testing.Value;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
