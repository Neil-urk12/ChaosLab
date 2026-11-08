using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Backend.Features.Environments;

// Command: sends one GET to a base URL (saved or not) to see whether the target answers. Any HTTP response,
// even 404, counts as reachable; only the status and timing are returned.
public static class CheckConnection
{
    public sealed record Request(string? BaseUrl, bool? VerifyTls, int? RequestTimeoutSeconds);

    public sealed record Response(bool Reachable, int? Status, string? Error, long ElapsedMs);

    public static async Task<Results<Ok<Response>, ValidationProblem, NotFound, ForbidHttpResult>> Handle(
        Guid id, Request request, ClaimsPrincipal principal, ApplicationDbContext db, IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        // Viewers cannot make the server send requests.
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();
        if (TargetClient.ValidateBaseUrl(request.BaseUrl, out var url) is { } error)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["baseUrl"] = [error] });

        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        var probe = await TargetClient.SendAsync(httpClientFactory, message, request.VerifyTls ?? true,
            Math.Clamp(request.RequestTimeoutSeconds ?? 30, 1, 30), cancellationToken);
        return TypedResults.Ok(new Response(probe.Status is not null, probe.Status, probe.Error, probe.ElapsedMs));
    }
}
