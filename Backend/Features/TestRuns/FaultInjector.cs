using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Domain.Enums;

namespace Backend.Features.TestRuns;

// The fault injector (PROJECT.md 10): sits in the runner's request path, in process. The target is called for real;
// the injector decides what the runner is made to see instead of the target's answer. Pure: no database, no requests.
// The runner applies the faults that are about the request or the timing itself (Auth Expiry's credential, Slow Drip's
// read pace), since those happen on the connection.
// shortcut: no listener, so only the runner can be put behind it. The dependency simulator needs one the target can
// reach; reuse Apply there.
public static class FaultInjector
{
    // Sent in place of the stored secret by Auth Expiry.
    public const string InvalidCredential = "chaoslab-invalid-credential";

    // Body null: the target's own body is delivered (Slow Drip only changes how fast).
    public sealed record Injected(int Status, string? Body, int? RetryAfterSeconds = null);

    // What the runner receives for a response the target completed, or null when this fault leaves responses alone.
    public static Injected? Apply(ScenarioType type, JsonElement parameters, int upstreamStatus, string upstreamBody) => type switch
    {
        ScenarioType.ServerErrorStorm => new(parameters.GetProperty("status").GetInt32(), ""),
        ScenarioType.Throttle429 => new(429, "", parameters.GetProperty("retryAfterSeconds").GetInt32()),
        ScenarioType.MalformedJson => new(upstreamStatus, Corrupt(parameters.GetProperty("variant").GetString(), upstreamBody)),
        ScenarioType.EmptyResponse => new(upstreamStatus, ""),
        ScenarioType.SlowDrip => new(upstreamStatus, null),
        _ => null
    };

    // truncated: the first half. invalidSyntax: broken from the first token. wrongTypes: still valid JSON, every
    // scalar swapped for another type; a body that is not JSON can only be cut.
    public static string Corrupt(string? variant, string body)
    {
        if (variant == "invalidSyntax")
            return "{]" + body;
        if (variant == "wrongTypes")
            try
            {
                if (JsonNode.Parse(body) is { } node)
                    return Swap(node)!.ToJsonString();
            }
            // ArgumentException: an object with the same key twice.
            catch (Exception exception) when (exception is JsonException or ArgumentException) { }
        return body[..(body.Length / 2)];
    }

    private static JsonNode? Swap(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(property => KeyValuePair.Create(property.Key, Swap(property.Value)))),
        JsonArray a => new JsonArray(a.Select(Swap).ToArray()),
        JsonValue v when v.GetValueKind() == JsonValueKind.String => JsonValue.Create(0),
        JsonValue v => JsonValue.Create(v.ToJsonString()),
        _ => JsonValue.Create("null")
    };
}
