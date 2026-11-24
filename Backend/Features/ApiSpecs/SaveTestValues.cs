using System.Text.Json;
using System.Security.Claims;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.ApiSpecs;

// Command: saves the values the runner sends for an endpoint of the active spec when the spec has no example
// (TestValues). Not credentials: they are stored and shown in plain text, so they are never logged.
public static class SaveTestValues
{
    public const int MaxValueLength = 2048;
    public const int MaxBodyBytes = 64 * 1024;

    // Parameters: keyed "path:name", "query:name" or "header:name". An empty value removes it; a null body removes it.
    public sealed record Request(Dictionary<string, string?>? Parameters, JsonElement? Body);

    public static async Task<Results<NoContent, ValidationProblem, NotFound, ForbidHttpResult>> Handle(
        Guid id, Guid endpointId, Request request, ClaimsPrincipal principal, ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var role = await ProjectAccess.RoleAsync(db, id, principal, cancellationToken);
        if (role is null)
            return TypedResults.NotFound();
        if (role == ProjectRole.Viewer)
            return TypedResults.Forbid();

        var endpoint = await db.ApiEndpoints.Include(endpoint => endpoint.ApiSpec)
            .SingleOrDefaultAsync(endpoint => endpoint.Id == endpointId
                && endpoint.ApiSpec.ProjectId == id && endpoint.ApiSpec.IsActive, cancellationToken);
        if (endpoint is null)
            return TypedResults.NotFound();

        var (inputs, bodyInput, _) = TestValues.Inputs(endpoint.ApiSpec.ParsedDocument.RootElement,
            endpoint.Parameters.RootElement, endpoint.RequestBodySchema?.RootElement);
        var keys = inputs.Where(input => !input.Generated).Select(input => input.Key).ToHashSet();
        var errors = new Dictionary<string, string[]>();
        var parameters = new Dictionary<string, string>();
        foreach (var (key, value) in request.Parameters ?? [])
        {
            if (!keys.Contains(key))
                errors[$"parameters.{key}"] = ["This endpoint has no required path, query or header parameter by that name."];
            else if (value?.Length > MaxValueLength)
                errors[$"parameters.{key}"] = [$"A value must be at most {MaxValueLength} characters."];
            else if (!string.IsNullOrEmpty(value))
                parameters[key] = value;
        }
        var body = request.Body is { ValueKind: not JsonValueKind.Null } given ? given : (JsonElement?)null;
        if (body is { } json)
        {
            if (bodyInput is null)
                errors["body"] = ["This endpoint has no JSON request body."];
            else if (json.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                errors["body"] = ["The body must be a JSON object or array."];
            else if (System.Text.Encoding.UTF8.GetByteCount(json.GetRawText()) > MaxBodyBytes)
                errors["body"] = [$"The body must be at most {MaxBodyBytes / 1024} KB."];
        }
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        endpoint.TestValues = parameters.Count == 0 && body is null
            ? null
            : JsonSerializer.SerializeToDocument(new { parameters, body }, JsonSerializerOptions.Web);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
