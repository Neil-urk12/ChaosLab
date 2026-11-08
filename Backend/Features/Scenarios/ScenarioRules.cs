using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Enums;

namespace Backend.Features.Scenarios;

public enum InvariantType
{
    StatusCodeIn,
    FieldEquals,
    FieldNotBelow,
    MaxRecordsAfterDuplicates,
    MaxDurationMs,
    HeaderPresent
}

// Flat params: each invariant type uses only its own fields (see Validate).
public sealed record InvariantParams(
    List<int>? Codes = null, string? Path = null, string? Value = null, decimal? Min = null, int? Max = null,
    int? Ms = null, string? Name = null);

// Optional GET request that reads resulting state after the scenario; Path may contain {{request.*}} templates.
public sealed record ReadBack(string? Method, string? Path);

public sealed record Invariant(InvariantType? Type, InvariantParams? Params, ReadBack? ReadBack);

// Validation for user-edited scenario parameters and invariants.
public static class ScenarioRules
{
    private sealed record Field(string Key, double Min = 0, double Max = 0, bool Integer = true, string[]? Choices = null);

    private static readonly Dictionary<ScenarioType, Field[]> Fields = new()
    {
        [ScenarioType.Latency] = [new("delayMs", 0, 120_000)],
        [ScenarioType.ServerErrorStorm] = [new("status", 500, 599), new("count", 1, 20), new("probability", 0, 1, Integer: false)],
        [ScenarioType.Throttle429] = [new("retryAfterSeconds", 0, 3600)],
        [ScenarioType.TimeoutDrop] = [new("mode", Choices: ["drop", "hold"])],
        [ScenarioType.MalformedJson] = [new("variant", Choices: ["truncated", "invalidSyntax", "wrongTypes"])],
        [ScenarioType.EmptyResponse] = [],
        [ScenarioType.SlowDrip] = [new("bytes", 1, 65_536), new("intervalMs", 1, 10_000)],
        [ScenarioType.AuthExpiry] = [new("credential", Choices: ["expired", "missing", "invalid", "forbidden"])],
        [ScenarioType.DnsFailure] = [new("dependency", Choices: ["unreachable", "unresolvable"])],
        [ScenarioType.DuplicateRequest] = [new("copies", 2, 10), new("key", Choices: ["same", "different", "none"]), new("delayMs", 0, 60_000)]
    };

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    // The parameters must be an object with exactly the fields of the scenario type.
    public static string? ValidateParameters(ScenarioType type, JsonElement parameters)
    {
        if (!Fields.TryGetValue(type, out var fields))
            return "This scenario type has no editable parameters.";
        if (parameters.ValueKind != JsonValueKind.Object)
            return "Parameters must be an object.";
        var keys = parameters.EnumerateObject().Select(p => p.Name).ToList();
        if (keys.Count != fields.Length || keys.Except(fields.Select(f => f.Key)).Any())
            return $"Parameters must be exactly: {(fields.Length == 0 ? "none" : string.Join(", ", fields.Select(f => f.Key)))}.";
        foreach (var field in fields)
        {
            var value = parameters.GetProperty(field.Key);
            if (field.Choices is not null)
            {
                if (value.ValueKind != JsonValueKind.String || !field.Choices.Contains(value.GetString()))
                    return $"{field.Key} must be one of {string.Join(", ", field.Choices)}.";
            }
            else if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)
                || number < field.Min || number > field.Max || (field.Integer && number != Math.Floor(number)))
                return $"{field.Key} must be {(field.Integer ? "a whole number" : "a number")} from {field.Min} to {field.Max}.";
        }
        return null;
    }

    public static string? ValidateInvariants(ScenarioType type, List<Invariant> invariants)
    {
        if (invariants.Count > 10)
            return "A scenario can have at most 10 invariants.";
        foreach (var invariant in invariants)
        {
            if (invariant is null)
                return "Invariants cannot be null.";
            var p = invariant.Params ?? new InvariantParams();
            var error = invariant.Type switch
            {
                InvariantType.StatusCodeIn => p.Codes is not { Count: > 0 and <= 20 } || p.Codes.Any(code => code is < 100 or > 599)
                    ? "Status code in set needs 1 to 20 codes from 100 to 599." : null,
                InvariantType.FieldEquals => !IsFieldPath(p.Path) || p.Value is null || p.Value.Length > 500
                    ? "Field equals needs a field path like $.status and a value of at most 500 characters." : null,
                InvariantType.FieldNotBelow => !IsFieldPath(p.Path) || p.Min is null
                    ? "Field not below needs a field path like $.balance and a minimum." : null,
                InvariantType.MaxRecordsAfterDuplicates => type != ScenarioType.DuplicateRequest
                    ? "Record count after duplicates only applies to duplicate-request scenarios."
                    : p.Max is not (>= 1 and <= 100) ? "Record count after duplicates needs a maximum from 1 to 100."
                    : invariant.ReadBack is null ? "Record count after duplicates needs a read-back request to count records." : null,
                InvariantType.MaxDurationMs => p.Ms is not (>= 1 and <= 600_000)
                    ? "Maximum duration must be from 1 to 600000 ms." : null,
                InvariantType.HeaderPresent => string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 200 || p.Name.Any(char.IsWhiteSpace)
                    ? "Required header present needs a header name." : null,
                _ => "Invariant type is not supported."
            };
            if (error is null && invariant.ReadBack is { } readBack
                && (readBack.Method != "GET" || readBack.Path is not { Length: <= 1024 } path || !path.StartsWith('/') || path.Any(char.IsWhiteSpace)))
                error = "The read-back request must be a GET to a path starting with /.";
            if (error is not null)
                return error;
        }
        return null;
    }

    // Keeps only the fields of each invariant's type, so stored JSON has one shape per type.
    public static JsonDocument Normalize(List<Invariant> invariants) => JsonSerializer.SerializeToDocument(
        invariants.Select(invariant =>
        {
            var p = invariant.Params!;
            InvariantParams kept = invariant.Type switch
            {
                InvariantType.StatusCodeIn => new(Codes: p.Codes),
                InvariantType.FieldEquals => new(Path: p.Path, Value: p.Value),
                InvariantType.FieldNotBelow => new(Path: p.Path, Min: p.Min),
                InvariantType.MaxRecordsAfterDuplicates => new(Max: p.Max),
                InvariantType.MaxDurationMs => new(Ms: p.Ms),
                _ => new(Name: p.Name!.Trim())
            };
            return invariant with { Params = kept };
        }).ToList(), Json);

    private static bool IsFieldPath(string? path) =>
        path is { Length: > 2 and <= 200 } && path.StartsWith("$.") && !path.Any(char.IsWhiteSpace);
}
