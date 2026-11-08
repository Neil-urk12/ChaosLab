using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Backend.Features.ApiSpecs;

public enum IssueSeverity
{
    Error,
    Warning
}

// Location uses dotted OpenAPI paths, e.g. "paths./refunds.post.requestBody".
public sealed record SpecIssue(IssueSeverity Severity, string Location, string Message);

public sealed record ParsedSpec(
    string OpenApiVersion, ApiSpecFormat Format, string? Title, string? VersionLabel,
    JsonDocument Document, List<ApiEndpoint> Endpoints);

// Reads an OpenAPI 3.x (or Swagger 2.0, converted to 3.0) document and reports why it cannot be imported.
public static partial class SpecDocument
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    public static async Task<(ParsedSpec? Spec, List<SpecIssue> Issues)> ReadAsync(string text, CancellationToken cancellationToken)
    {
        var issues = new List<SpecIssue>();
        var format = text.TrimStart().StartsWith('{') ? ApiSpecFormat.Json : ApiSpecFormat.Yaml;
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        ReadResult result;
        try
        {
            result = OpenApiDocument.Parse(text, format == ApiSpecFormat.Json ? "json" : "yaml", settings);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            issues.Add(new(IssueSeverity.Error, "document", $"The document is not valid {format.ToString().ToUpperInvariant()}: {exception.Message}"));
            return (null, issues);
        }

        foreach (var error in result.Diagnostic?.Errors ?? [])
            issues.Add(new(IssueSeverity.Error, Location(error.Pointer), error.Message));
        if (result.Document is null || issues.Count > 0)
        {
            if (issues.Count == 0)
                issues.Add(new(IssueSeverity.Error, "document", "The document is not an OpenAPI 3.x or Swagger 2.0 document."));
            return (null, issues);
        }

        // Unresolved $refs are reported as errors below, so the library's warnings for them are dropped.
        foreach (var error in (result.Diagnostic?.Warnings ?? []).Concat(result.Document.Validate(ValidationRuleSet.GetDefaultRuleSet()) ?? []))
        {
            if (error.Pointer?.EndsWith("/$ref") == true)
                continue;
            var severity = error is OpenApiValidatorWarning || result.Diagnostic!.Warnings.Contains(error)
                ? IssueSeverity.Warning : IssueSeverity.Error;
            issues.Add(new(severity, Location(error.Pointer), error.Message));
        }

        var targetVersion = result.Diagnostic!.SpecificationVersion == OpenApiSpecVersion.OpenApi3_1
            ? OpenApiSpecVersion.OpenApi3_1 : OpenApiSpecVersion.OpenApi3_0;
        var document = JsonDocument.Parse(
            await result.Document.SerializeAsJsonAsync(targetVersion, cancellationToken: cancellationToken));
        var root = document.RootElement;

        CheckReferences(root, root, "", issues);
        var endpoints = ExtractEndpoints(root, issues);
        if (endpoints.Count == 0)
            issues.Add(new(IssueSeverity.Error, "paths", "The document has no operations to test."));

        if (root.TryGetProperty("components", out var components)
            && components.TryGetProperty("securitySchemes", out var schemes) && schemes.ValueKind == JsonValueKind.Object)
        {
            foreach (var scheme in schemes.EnumerateObject())
            {
                if (scheme.Value.TryGetProperty("type", out var type) && type.GetString() is "oauth2" or "openIdConnect")
                    issues.Add(new(IssueSeverity.Warning, $"components.securitySchemes.{scheme.Name}",
                        "OAuth2 flows can't issue test tokens. Use a Bearer token or API key for this target."));
            }
        }

        issues = issues.Distinct().OrderBy(issue => issue.Severity).ToList();
        if (issues.Any(issue => issue.Severity == IssueSeverity.Error))
        {
            document.Dispose();
            return (null, issues);
        }

        var info = root.TryGetProperty("info", out var i) ? i : default;
        return (new ParsedSpec(
            SourceVersion(text) ?? (root.TryGetProperty("openapi", out var v) ? v.GetString()! : "3.0"),
            format,
            info.ValueKind == JsonValueKind.Object && info.TryGetProperty("title", out var title) ? title.GetString() : null,
            info.ValueKind == JsonValueKind.Object && info.TryGetProperty("version", out var label) ? label.GetString() : null,
            document,
            endpoints), issues);
    }

    // The serialized document reports the library's version, so the declared one is read from the source text.
    private static string? SourceVersion(string text)
    {
        var match = VersionPattern().Match(text);
        return match.Success ? match.Groups[2].Value : null;
    }

    [GeneratedRegex("""^\s*["']?(openapi|swagger)["']?\s*:\s*["']?([0-9][0-9.]*)""", RegexOptions.Multiline)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"\{([^}/]+)\}")]
    private static partial Regex PathTemplate();

    private static List<ApiEndpoint> ExtractEndpoints(JsonElement root, List<SpecIssue> issues)
    {
        var endpoints = new List<ApiEndpoint>();
        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
            return endpoints;

        foreach (var path in paths.EnumerateObject())
        {
            // Other keys are x- extensions, not operations; as a path, "x-@evil.test" would move requests to another host.
            if (!path.Name.StartsWith('/'))
                continue;
            if (path.Name.Length > 1024)
            {
                issues.Add(new(IssueSeverity.Error, $"paths.{path.Name[..64]}…", "Paths longer than 1024 characters are not supported."));
                continue;
            }

            var pathParameters = path.Value.TryGetProperty("parameters", out var p) ? Parameters(root, p) : [];
            foreach (var method in Methods)
            {
                if (!path.Value.TryGetProperty(method, out var operation) || operation.ValueKind != JsonValueKind.Object)
                    continue;

                // Operation-level parameters override path-level ones with the same name and location.
                var parameters = new Dictionary<(string?, string?), JsonElement>();
                foreach (var parameter in pathParameters)
                    parameters[parameter.Key] = parameter.Value;
                if (operation.TryGetProperty("parameters", out var operationParameters))
                    foreach (var parameter in Parameters(root, operationParameters))
                        parameters[parameter.Key] = parameter.Value;

                foreach (Match match in PathTemplate().Matches(path.Name))
                {
                    var name = match.Groups[1].Value;
                    if (!parameters.ContainsKey((name, "path")))
                        issues.Add(new(IssueSeverity.Error, $"paths.{path.Name}.{method}.parameters",
                            $"Path parameter \"{name}\" is used in the path but not declared."));
                }

                var operationId = operation.TryGetProperty("operationId", out var id) ? id.GetString() : null;
                if (operationId?.Length > 256)
                    issues.Add(new(IssueSeverity.Error, $"paths.{path.Name}.{method}.operationId",
                        "Operation IDs longer than 256 characters are not supported."));

                endpoints.Add(new ApiEndpoint
                {
                    Id = Guid.NewGuid(),
                    Path = path.Name,
                    HttpMethod = method.ToUpperInvariant(),
                    OperationId = operationId,
                    Tags = Copy(operation, "tags") ?? JsonDocument.Parse("[]"),
                    Parameters = JsonSerializer.SerializeToDocument(parameters.Values),
                    RequestBodySchema = Copy(operation, "requestBody"),
                    Responses = Copy(operation, "responses") ?? JsonDocument.Parse("{}"),
                    // Null means the operation inherits the document's global security.
                    SecurityRequirements = Copy(operation, "security"),
                    IsDeprecated = operation.TryGetProperty("deprecated", out var deprecated)
                        && deprecated.ValueKind == JsonValueKind.True
                });
            }
        }

        return endpoints;
    }

    private static IEnumerable<KeyValuePair<(string?, string?), JsonElement>> Parameters(JsonElement root, JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var parameter in list.EnumerateArray())
        {
            var resolved = parameter.TryGetProperty("$ref", out var reference) ? Resolve(root, reference.GetString()) : parameter;
            if (resolved is not { ValueKind: JsonValueKind.Object } target)
                continue;
            yield return new(
                (target.TryGetProperty("name", out var name) ? name.GetString() : null,
                    target.TryGetProperty("in", out var location) ? location.GetString() : null),
                parameter);
        }
    }

    private static JsonDocument? Copy(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? JsonDocument.Parse(value.GetRawText()) : null;

    private static void CheckReferences(JsonElement root, JsonElement element, string pointer, List<SpecIssue> issues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "$ref" && property.Value.GetString() is { } reference
                        && reference.StartsWith('#') && Resolve(root, reference) is null)
                        issues.Add(new(IssueSeverity.Error, Location(pointer), $"$ref \"{reference}\" could not be resolved."));
                    else
                        CheckReferences(root, property.Value, $"{pointer}/{Escape(property.Name)}", issues);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    CheckReferences(root, item, $"{pointer}/{index++}", issues);
                break;
        }
    }

    // Resolves a local JSON pointer reference ("#/components/schemas/X"); external references are not followed.
    internal static JsonElement? Resolve(JsonElement root, string? reference)
    {
        if (reference is null || !reference.StartsWith("#"))
            return null;
        var current = root;
        foreach (var segment in reference[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(segment).Replace("~1", "/").Replace("~0", "~");
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(name, out var next))
                current = next;
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(name, out var index) && index < current.GetArrayLength())
                current = current[index];
            else
                return null;
        }
        return current;
    }

    private static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");

    // "#/paths/~1refunds/post" -> "paths./refunds.post"
    private static string Location(string? pointer) =>
        string.IsNullOrEmpty(pointer?.TrimStart('#', '/'))
            ? "document"
            : string.Join('.', pointer.TrimStart('#', '/').Split('/').Select(segment => segment.Replace("~1", "/").Replace("~0", "~")));
}
