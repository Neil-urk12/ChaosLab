using System.Security.Claims;
using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Projects;
using Backend.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.ApiSpecs;

// Query: the project's active spec with its endpoints, compared with the spec it replaced.
public static class GetSpec
{
    public sealed record Response(
        Guid Id,
        string? Title,
        string OpenApiVersion,
        string? Filename,
        ApiSpecSourceType SourceType,
        string? SourceUrl,
        DateTimeOffset ImportedAt,
        string ImportedBy,
        List<SecurityScheme> SecuritySchemes,
        List<EndpointRow> Endpoints,
        Changes? Changes);

    public sealed record SecurityScheme(string Name, string Type, bool Global);

    // Security lists the scheme names that apply (empty means the operation is public).
    public sealed record EndpointRow(
        Guid Id, string HttpMethod, string Path, string? OperationId, string? Tag, List<string> Security,
        string? RequestBody, List<string> Responses, int ScenarioCount, EndpointTesting Testing, string? Change);

    // Differences from the previously active spec; Removed holds "METHOD /path" labels.
    public sealed record Changes(int Added, int Modified, List<string> Removed);

    public static async Task<Results<Ok<Response>, NotFound>> Handle(
        Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (await ProjectAccess.RoleAsync(db, id, principal, cancellationToken) is null)
            return TypedResults.NotFound();

        // shortcut: loads the whole parsed document for the security section; project it in SQL if specs get large.
        var spec = await db.ApiSpecs.AsNoTracking()
            .Where(spec => spec.ProjectId == id && spec.IsActive)
            .Select(spec => new
            {
                spec.Id, spec.Title, spec.OpenApiVersion, spec.SourceFilename, spec.SourceType, spec.SourceUrl,
                spec.CreatedAt, ImportedBy = spec.UploadedBy.DisplayName, spec.ParsedDocument
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (spec is null)
            return TypedResults.NotFound();

        var endpoints = await db.ApiEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.ApiSpecId == spec.Id)
            .Select(endpoint => new
            {
                endpoint.Id, endpoint.HttpMethod, endpoint.Path, endpoint.OperationId, endpoint.Tags, endpoint.Parameters,
                endpoint.RequestBodySchema, endpoint.Responses, endpoint.SecurityRequirements, endpoint.Testing,
                ScenarioCount = endpoint.ChaosScenarios.Count()
            })
            .ToListAsync(cancellationToken);

        var previous = await db.ApiEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.ApiSpecId == db.ApiSpecs
                .Where(old => old.ProjectId == id && !old.IsActive && old.CreatedAt <= spec.CreatedAt && old.Id != spec.Id)
                .OrderByDescending(old => old.CreatedAt).Select(old => (Guid?)old.Id).FirstOrDefault())
            .Select(endpoint => new
            {
                endpoint.HttpMethod, endpoint.Path, endpoint.OperationId, endpoint.Tags, endpoint.Parameters,
                endpoint.RequestBodySchema, endpoint.Responses, endpoint.SecurityRequirements
            })
            .ToListAsync(cancellationToken);
        var hasPrevious = await db.ApiSpecs.AnyAsync(old => old.ProjectId == id && !old.IsActive, cancellationToken);

        // jsonb normalizes key order, so raw text compares structurally.
        static string Fingerprint(string? operationId, params JsonDocument?[] parts) =>
            string.Join('\n', parts.Select(part => part?.RootElement.GetRawText()).Prepend(operationId));
        var before = previous.ToDictionary(
            endpoint => (endpoint.HttpMethod, endpoint.Path),
            endpoint => Fingerprint(endpoint.OperationId, endpoint.Tags, endpoint.Parameters, endpoint.RequestBodySchema,
                endpoint.Responses, endpoint.SecurityRequirements));

        var root = spec.ParsedDocument.RootElement;
        var globalSecurity = root.TryGetProperty("security", out var security) ? SchemeNames(security) : [];
        var schemes = root.TryGetProperty("components", out var components)
            && components.TryGetProperty("securitySchemes", out var definitions) && definitions.ValueKind == JsonValueKind.Object
            ? definitions.EnumerateObject()
                .Select(scheme => new SecurityScheme(scheme.Name,
                    scheme.Value.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                    globalSecurity.Contains(scheme.Name)))
                .ToList()
            : [];

        var rows = endpoints
            .OrderBy(endpoint => endpoint.Path, StringComparer.Ordinal)
            .ThenBy(endpoint => Array.IndexOf(SpecDocument.Methods, endpoint.HttpMethod.ToLowerInvariant()))
            .Select(endpoint => new EndpointRow(
                endpoint.Id,
                endpoint.HttpMethod,
                endpoint.Path,
                endpoint.OperationId,
                endpoint.Tags.RootElement.ValueKind == JsonValueKind.Array && endpoint.Tags.RootElement.GetArrayLength() > 0
                    ? endpoint.Tags.RootElement[0].GetString() : null,
                endpoint.SecurityRequirements is null ? globalSecurity : SchemeNames(endpoint.SecurityRequirements.RootElement),
                RequestBodyName(endpoint.RequestBodySchema?.RootElement),
                endpoint.Responses.RootElement.ValueKind == JsonValueKind.Object
                    ? endpoint.Responses.RootElement.EnumerateObject().Select(response => response.Name).ToList() : [],
                endpoint.ScenarioCount,
                endpoint.Testing,
                !hasPrevious ? null
                    : !before.TryGetValue((endpoint.HttpMethod, endpoint.Path), out var old) ? "New"
                    : old != Fingerprint(endpoint.OperationId, endpoint.Tags, endpoint.Parameters, endpoint.RequestBodySchema,
                        endpoint.Responses, endpoint.SecurityRequirements) ? "Modified"
                    : null))
            .ToList();

        var current = endpoints.Select(endpoint => (endpoint.HttpMethod, endpoint.Path)).ToHashSet();
        var changes = hasPrevious
            ? new Changes(
                rows.Count(row => row.Change == "New"),
                rows.Count(row => row.Change == "Modified"),
                before.Keys.Where(key => !current.Contains(key)).Select(key => $"{key.HttpMethod} {key.Path}").Order().ToList())
            : null;

        return TypedResults.Ok(new Response(
            spec.Id, spec.Title, spec.OpenApiVersion, spec.SourceFilename, spec.SourceType, spec.SourceUrl,
            spec.CreatedAt, spec.ImportedBy, schemes, rows, changes));
    }

    internal static List<string> SchemeNames(JsonElement requirements) =>
        requirements.ValueKind == JsonValueKind.Array
            ? requirements.EnumerateArray()
                .Where(requirement => requirement.ValueKind == JsonValueKind.Object)
                .SelectMany(requirement => requirement.EnumerateObject().Select(scheme => scheme.Name))
                .Distinct().ToList()
            : [];

    // "#/components/schemas/RefundRequest" -> "RefundRequest"; inline schemas have no name.
    private static string? RequestBodyName(JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } value)
            return null;
        if (value.TryGetProperty("$ref", out var reference))
            return reference.GetString()?.Split('/')[^1];
        if (value.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
        {
            foreach (var media in content.EnumerateObject())
            {
                if (media.Value.TryGetProperty("schema", out var schema) && schema.TryGetProperty("$ref", out var schemaRef))
                    return schemaRef.GetString()?.Split('/')[^1];
            }
        }
        return "Inline";
    }
}
