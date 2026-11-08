using System.Text.Json;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.ApiSpecs;
using Backend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Features.Scenarios;

// Decides which faults apply to an endpoint from its spec (PROJECT.md §8): one scenario per applicable fault type.
public static class ScenarioGenerator
{
    public sealed record Plan(ScenarioType Type, object Parameters, string Reason, bool Enabled);

    private static readonly string[] Idempotent = ["GET", "HEAD", "PUT", "DELETE", "OPTIONS"];

    public static List<Plan> For(ApiEndpoint endpoint, JsonElement root)
    {
        var method = endpoint.HttpMethod;
        var plans = new List<Plan>();
        var responses = endpoint.Responses.RootElement.ValueKind == JsonValueKind.Object
            ? endpoint.Responses.RootElement.EnumerateObject().ToList() : [];
        var declared = responses.Select(response => response.Name).ToHashSet();
        var body = responses
            .Where(response => response.Name.StartsWith('2') && response.Name != "204")
            .Select(response => Body(root, response.Value))
            .FirstOrDefault(found => found is not null);
        var idempotencyKey = endpoint.Parameters.RootElement.ValueKind == JsonValueKind.Array
            && endpoint.Parameters.RootElement.EnumerateArray().Any(parameter =>
                Resolve(root, parameter) is { } p
                && p.TryGetProperty("in", out var location) && location.GetString() == "header"
                && p.TryGetProperty("name", out var name) && string.Equals(name.GetString(), "Idempotency-Key", StringComparison.OrdinalIgnoreCase));
        var security = endpoint.SecurityRequirements is null
            ? root.TryGetProperty("security", out var global) ? GetSpec.SchemeNames(global) : []
            : GetSpec.SchemeNames(endpoint.SecurityRequirements.RootElement);
        var read = method is "GET" or "HEAD";

        plans.Add(new(ScenarioType.Latency, new { delayMs = 3000 },
            read ? "Read endpoint; tests the timeout budget." : $"State-changing {method}; tests the timeout budget without a duplicate write.",
            true));

        if (Idempotent.Contains(method))
            plans.Add(new(ScenarioType.ServerErrorStorm, new { status = 503, count = 3, probability = 1.0 },
                $"Idempotent {method}; safe to retry, so retry limits and backoff are observable.", true));
        else if (idempotencyKey)
            plans.Add(new(ScenarioType.ServerErrorStorm, new { status = 503, count = 3, probability = 1.0 },
                $"{method} with an Idempotency-Key header; retries are safe, so retry limits and backoff are observable.", true));

        // Every operation can be throttled; it is only on by default where the spec says so.
        plans.Add(declared.Contains("429")
            ? new(ScenarioType.Throttle429, new { retryAfterSeconds = 2 }, "Spec declares 429 for this operation.", true)
            : new(ScenarioType.Throttle429, new { retryAfterSeconds = 2 }, "Any operation can be rate-limited; the spec does not declare 429.", false));

        plans.Add(new(ScenarioType.TimeoutDrop, new { mode = "drop" },
            "Checks that callers end the request and clean up when the connection drops.", true));

        if (body is ({ } media, var schema))
        {
            if (media.Contains("json", StringComparison.OrdinalIgnoreCase))
                plans.Add(new(ScenarioType.MalformedJson, new { variant = "truncated" },
                    $"Returns a JSON body{(schema is null ? "" : $" ({schema})")} that callers parse.", true));
            plans.Add(new(ScenarioType.EmptyResponse, new { },
                "Returns a body; checks handling of a missing payload.", true));
            // Slow drip and dependency failure need injector support that is still a proof of concept.
            plans.Add(new(ScenarioType.SlowDrip, new { bytes = 1, intervalMs = 200 },
                "Returns a body; checks complete-operation deadlines.", false));
        }

        if (security.Count > 0)
            plans.Add(new(ScenarioType.AuthExpiry, new { credential = "expired" },
                $"Operation requires {string.Join(" or ", security)}.", true));

        var upstream = new[] { "502", "503", "504" }.Where(declared.Contains).ToList();
        if (upstream.Count > 0)
            plans.Add(new(ScenarioType.DnsFailure, new { dependency = "unreachable" },
                $"Declares {string.Join("/", upstream)}, so it calls a dependency; needs the simulator in its outbound path.", false));

        if (method is "POST" or "PUT" or "PATCH")
            plans.Add(idempotencyKey
                ? new(ScenarioType.DuplicateRequest, new { copies = 2, key = "same", delayMs = 0 },
                    $"State-changing {method} with an Idempotency-Key header in the spec.", true)
                : new(ScenarioType.DuplicateRequest, new { copies = 2, key = "none", delayMs = 0 },
                    $"State-changing {method}; a repeated request should not create a second record.", true));

        return plans;
    }

    // Brings the spec's scenarios in line with the rules. Existing scenarios keep their edits; ones that no longer
    // apply are removed unless a run used them. Scenarios of `previousSpecId` seed edits for matching new ones.
    // Only GenerateScenarios calls this, when the user asks.
    public static async Task<(int Added, int Removed)> SyncAsync(
        ApplicationDbContext db, Guid specId, Guid? previousSpecId, CancellationToken cancellationToken)
    {
        var root = await db.ApiSpecs.Where(spec => spec.Id == specId).Select(spec => spec.ParsedDocument)
            .SingleAsync(cancellationToken);
        var endpoints = await db.ApiEndpoints.Where(endpoint => endpoint.ApiSpecId == specId)
            .Include(endpoint => endpoint.ChaosScenarios)
            .ToListAsync(cancellationToken);
        var carried = previousSpecId is null ? [] : await db.ChaosScenarios.AsNoTracking()
            .Where(scenario => scenario.Endpoint.ApiSpecId == previousSpecId)
            .Select(scenario => new { scenario.Endpoint.HttpMethod, scenario.Endpoint.Path, scenario.ScenarioType,
                scenario.Parameters, scenario.IsDefaultEnabled, scenario.Invariants })
            .ToListAsync(cancellationToken);
        var previous = carried.ToDictionary(scenario => (scenario.HttpMethod, scenario.Path, scenario.ScenarioType));
        var used = await db.ScenarioExecutions
            .Where(execution => execution.Endpoint.ApiSpecId == specId)
            .Select(execution => execution.ScenarioId).Distinct().ToListAsync(cancellationToken);

        int added = 0, removed = 0;
        foreach (var endpoint in endpoints)
        {
            var plans = For(endpoint, root.RootElement);
            foreach (var plan in plans)
            {
                var existing = endpoint.ChaosScenarios.FirstOrDefault(scenario => scenario.ScenarioType == plan.Type);
                if (existing is not null)
                {
                    existing.Reason = plan.Reason;
                    continue;
                }
                var old = previous.GetValueOrDefault((endpoint.HttpMethod, endpoint.Path, plan.Type));
                db.ChaosScenarios.Add(new ChaosScenario
                {
                    Id = Guid.NewGuid(),
                    EndpointId = endpoint.Id,
                    ScenarioType = plan.Type,
                    Reason = plan.Reason,
                    Parameters = old?.Parameters ?? JsonSerializer.SerializeToDocument(plan.Parameters),
                    IsDefaultEnabled = old?.IsDefaultEnabled ?? plan.Enabled,
                    Invariants = old?.Invariants ?? JsonDocument.Parse("[]")
                });
                added++;
            }
            foreach (var stale in endpoint.ChaosScenarios
                .Where(scenario => plans.All(plan => plan.Type != scenario.ScenarioType) && !used.Contains(scenario.Id)).ToList())
            {
                db.ChaosScenarios.Remove(stale);
                removed++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return (added, removed);
    }

    // The first media type of a response and its schema's component name, or null when it has no body.
    private static (string Media, string? Schema)? Body(JsonElement root, JsonElement response)
    {
        if (Resolve(root, response) is not { } resolved
            || !resolved.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var media in content.EnumerateObject())
        {
            var schema = media.Value.ValueKind == JsonValueKind.Object && media.Value.TryGetProperty("schema", out var s)
                && s.TryGetProperty("$ref", out var reference) ? reference.GetString()?.Split('/')[^1] : null;
            return (media.Name, schema);
        }
        return null;
    }

    private static JsonElement? Resolve(JsonElement root, JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$ref", out var reference)
            ? SpecDocument.Resolve(root, reference.GetString())
            : element.ValueKind == JsonValueKind.Object ? element : null;
}
