using System.Text.Json;
using Backend.Features.TestRuns;

namespace Backend.Features.ApiSpecs;

// What the runner sends for an endpoint's required path, query and header parameters and its JSON body: the value an
// owner or editor saved (ApiEndpoint.TestValues), then the spec's example, default or enum value (PROJECT.md 26).
// Shared by the runner, the import warnings and the spec page, so the three can't drift apart.
// Saved shape: { "parameters": { "path:vehicle_id": "42", "header:X-Tenant": "a" }, "body": { ... } }.
public static class TestValues
{
    public const string Hint = "Enter it under Test values on the endpoint list.";

    // in: path, query or header. Generated: the runner fills it on every request, so it needs no value.
    public sealed record Input(string Key, string In, string Name, string? Example, bool Generated);

    // Schema is the JSON media type's schema, with the requestBody $ref resolved; component $refs inside stay as they are.
    public sealed record BodyInput(string ContentType, JsonElement? Schema, bool Required, bool HasExample);

    // Missing holds labels such as path parameter "id" or request body. Inputs and BodyInput are what Inputs returned.
    public sealed record Filled(List<(Input Input, string Value)> Parameters, string? ContentType, JsonElement? Body, List<string> Missing,
        List<Input> Inputs, BodyInput? BodyInput);

    public const string BodyLabel = "request body";

    // A required body with no JSON media type (multipart, form): the runner can't send it, and no test value fixes that.
    public const string NonJsonBodyLabel = "request body (not JSON)";
    public const string NonJsonBody = "The request body is required, but it isn't JSON, and ChaosLab only sends JSON request bodies.";

    public static string Key(string location, string name) => $"{location}:{name}";

    // The required path, query and header parameters, and the JSON body if the endpoint has one. bodyRequired is true
    // also for a required body without a JSON media type, which can't be filled.
    public static (List<Input> Parameters, BodyInput? Body, bool BodyRequired) Inputs(
        JsonElement root, JsonElement parameters, JsonElement? requestBody)
    {
        var inputs = new List<Input>();
        if (parameters.ValueKind == JsonValueKind.Array)
            foreach (var raw in parameters.EnumerateArray())
            {
                if (Deref(root, raw) is not { ValueKind: JsonValueKind.Object } parameter)
                    continue;
                var name = Text(parameter, "name");
                var location = Text(parameter, "in");
                var required = location == "path"
                    || (parameter.TryGetProperty("required", out var flag) && flag.ValueKind == JsonValueKind.True);
                // shortcut: optional and cookie parameters are never sent; add them when scenarios need them.
                if (name is null || !required || location is not ("path" or "query" or "header"))
                    continue;
                var example = Example(root, parameter) is { ValueKind: JsonValueKind.String or JsonValueKind.Number
                    or JsonValueKind.True or JsonValueKind.False } value
                    ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()
                    : null;
                var generated = location == "header" && name.Equals(RunExecutor.IdempotencyHeader, StringComparison.OrdinalIgnoreCase);
                inputs.Add(new(Key(location, name), location, name, example, generated));
            }

        if (requestBody is not { } document || Deref(root, document) is not { ValueKind: JsonValueKind.Object } body)
            return (inputs, null, false);
        var bodyRequired = body.TryGetProperty("required", out var bodyFlag) && bodyFlag.ValueKind == JsonValueKind.True;
        var media = body.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object
            ? content.EnumerateObject().Where(type => type.Name.Contains("json", StringComparison.OrdinalIgnoreCase))
                .Select(type => (JsonProperty?)type).FirstOrDefault()
            : null;
        if (media is not { Value.ValueKind: JsonValueKind.Object } json)
            return (inputs, null, bodyRequired);
        return (inputs, new BodyInput(json.Name, json.Value.TryGetProperty("schema", out var schema) ? schema : null, bodyRequired,
            Example(root, json.Value) is not null), bodyRequired);
    }

    // The values to send. A body is sent when one is saved or the spec has an example, even if it is optional.
    public static Filled Fill(JsonElement root, JsonElement parameters, JsonElement? requestBody, JsonElement? saved)
    {
        var (inputs, bodyInput, bodyRequired) = Inputs(root, parameters, requestBody);
        var savedParameters = saved is { ValueKind: JsonValueKind.Object } values
            && values.TryGetProperty("parameters", out var list) && list.ValueKind == JsonValueKind.Object ? list : default;
        var filled = new List<(Input, string)>();
        var missing = new List<string>();
        foreach (var input in inputs)
        {
            var value = savedParameters.ValueKind == JsonValueKind.Object && savedParameters.TryGetProperty(input.Key, out var own)
                && own.ValueKind == JsonValueKind.String ? own.GetString() : input.Example;
            // The runner replaces it on every request (RunExecutor.Prepared.Create), so a placeholder is enough.
            if (value is null && input.Generated)
                value = "";
            if (value is null)
                missing.Add($"{input.In} parameter \"{input.Name}\"");
            else
                filled.Add((input, value));
        }

        JsonElement? body = saved is { ValueKind: JsonValueKind.Object } withBody && withBody.TryGetProperty("body", out var savedBody)
            && savedBody.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? savedBody : null;
        if (bodyInput is not null)
            body ??= Deref(root, requestBody!.Value) is { } resolved && resolved.TryGetProperty("content", out var content)
                && content.TryGetProperty(bodyInput.ContentType, out var media) ? Example(root, media) : null;
        else
            body = null;
        // The unsendable body goes first, so the runner reports it rather than a value the user could enter.
        if (body is null && bodyRequired && bodyInput is null)
            missing.Insert(0, NonJsonBodyLabel);
        else if (body is null && bodyRequired)
            missing.Add(BodyLabel);
        return new(filled, body is null ? null : bodyInput!.ContentType, body, missing, inputs, bodyInput);
    }

    // Why the runner could not build the request, for one missing label from Fill.
    public static string RunnerMessage(string missing) => missing switch
    {
        NonJsonBodyLabel => NonJsonBody,
        BodyLabel => $"The request body is required, but it has no saved test value and the spec has no JSON example for it. {Hint}",
        _ => $"The required {missing} has no saved test value and no example, default or enum value in the spec. {Hint}"
    };

    // An example from the spec: the object's own example, its first named example, or its schema's example, default or
    // first enum value.
    private static JsonElement? Example(JsonElement root, JsonElement owner)
    {
        if (owner.TryGetProperty("example", out var example))
            return example;
        if (owner.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Object)
            foreach (var named in examples.EnumerateObject())
                if (Deref(root, named.Value) is { ValueKind: JsonValueKind.Object } resolved && resolved.TryGetProperty("value", out var value))
                    return value;
        if (owner.TryGetProperty("schema", out var raw) && Deref(root, raw) is { ValueKind: JsonValueKind.Object } schema)
        {
            if (schema.TryGetProperty("example", out example) || schema.TryGetProperty("default", out example))
                return example;
            if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0)
                return values[0];
        }
        return null;
    }

    private static JsonElement? Deref(JsonElement root, JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$ref", out var reference)
            ? SpecDocument.Resolve(root, reference.GetString())
            : element;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
