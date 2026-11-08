using System.Text.Json;
using Backend.Features.ApiSpecs;
using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Environments;

// A security scheme of the active spec. Operations lists the "METHOD /path" operations that use it.
public sealed record SchemeInfo(
    string Name, string Type, string? HttpScheme, string? In, string? ParameterName, List<string> Operations)
{
    public static async Task<List<SchemeInfo>> ForProjectAsync(ApplicationDbContext db, Guid projectId, CancellationToken cancellationToken)
    {
        // shortcut: loads the whole parsed document like GetSpec; project it in SQL if specs get large.
        var document = await db.ApiSpecs.AsNoTracking().Where(spec => spec.ProjectId == projectId && spec.IsActive)
            .Select(spec => spec.ParsedDocument).SingleOrDefaultAsync(cancellationToken);
        if (document is null)
            return [];
        var root = document.RootElement;
        var endpoints = await db.ApiEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.ApiSpec.ProjectId == projectId && endpoint.ApiSpec.IsActive)
            .OrderBy(endpoint => endpoint.Path).ThenBy(endpoint => endpoint.HttpMethod)
            .Select(endpoint => new { endpoint.HttpMethod, endpoint.Path, endpoint.SecurityRequirements })
            .ToListAsync(cancellationToken);
        var global = root.TryGetProperty("security", out var security) ? GetSpec.SchemeNames(security) : [];

        if (!root.TryGetProperty("components", out var components)
            || !components.TryGetProperty("securitySchemes", out var schemes) || schemes.ValueKind != JsonValueKind.Object)
            return [];
        return schemes.EnumerateObject().Select(scheme => new SchemeInfo(
            scheme.Name,
            Text(scheme.Value, "type") ?? "",
            Text(scheme.Value, "scheme")?.ToLowerInvariant(),
            Text(scheme.Value, "in"),
            Text(scheme.Value, "name"),
            endpoints
                .Where(endpoint => (endpoint.SecurityRequirements is null ? global : GetSpec.SchemeNames(endpoint.SecurityRequirements.RootElement))
                    .Contains(scheme.Name))
                .Select(endpoint => $"{endpoint.HttpMethod} {endpoint.Path}")
                .ToList())).ToList();
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
