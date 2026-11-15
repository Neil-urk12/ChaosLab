using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Scenarios;

namespace Backend.Features.TestRuns;

// Turns the observations of one scenario execution into a finding (FeaturesFlow 2.18). Pure: no database, no requests.
// Every finding says what was observed, what it suggests and what is not proven (PROJECT.md 11.2), and a violated
// developer invariant makes it Fail (PROJECT.md 13).
// shortcut: only the faults the runner can produce with direct requests have rules (Latency, Timeout / Drop,
// Duplicate Request). Retry, backoff, rate-limit, circuit-breaker, fallback, auth-failure, response-validation and
// dependency-failure evidence needs the injector's faults; add their rules with it.
public static class Evaluator
{
    // [Proposed] threshold (PROJECT.md 12.3): slower than three times the baseline and by more than a second is Degraded.
    private const long SlowMarginMs = 1000;

    public sealed record Finding(
        ResiliencePattern Pattern, ResilienceFindingOutcome Outcome, RecommendationSeverity Severity, string Summary,
        JsonDocument Evidence);

    private enum Check { Held, Violated, NotEvaluated }

    private sealed record Verdict(
        ResilienceFindingOutcome Outcome, string Summary, string Expected, string Observed, string Suggests, string NotProven,
        // The responses alone can't decide; a record count read back from the target can.
        bool NeedsReadBack = false);

    // Null when there is nothing to judge: Baseline is not a fault, and a scenario without fault requests never ran.
    public static Finding? Evaluate(
        ScenarioType type, JsonElement parameters, IReadOnlyList<Invariant> invariants, IReadOnlyList<HttpObservation> observations)
    {
        List<HttpObservation> In(string phase) => observations.Where(o => Phase(o) == phase).OrderBy(o => o.AttemptNo).ToList();
        var baseline = In("baseline").FirstOrDefault();
        var faults = In("fault");
        if (faults.Count == 0)
            return null;

        ResiliencePattern pattern;
        Verdict verdict;
        switch (type)
        {
            case ScenarioType.Latency:
                pattern = ResiliencePattern.Timeout;
                verdict = Compare(baseline, faults[0], $"A request delayed by {parameters.GetProperty("delayMs").GetInt32()} ms",
                    "Whether the target enforces its own timeouts on slow callers or slow dependencies.");
                break;
            case ScenarioType.TimeoutDrop:
                if (In("recovery").FirstOrDefault() is not { } probe)
                    return null;
                pattern = ResiliencePattern.Timeout;
                verdict = Compare(baseline, probe,
                    parameters.GetProperty("mode").GetString() == "hold"
                        ? "The request after one the runner held open until it timed out"
                        : "The request after one whose connection the runner dropped",
                    "Whether the target stopped working on the abandoned request or released its resources.");
                break;
            case ScenarioType.DuplicateRequest:
                pattern = ResiliencePattern.Idempotency;
                verdict = Duplicates(parameters.GetProperty("key").GetString(), faults);
                break;
            default:
                return null;
        }

        var checks = invariants.Select((invariant, index) => (invariant.Type, Result: CheckInvariant(type, invariant, index, observations, faults))).ToList();
        if (checks.Where(check => check.Result.State == Check.Violated).Select(check => check.Result.Detail).FirstOrDefault() is { } violated)
            verdict = verdict with
            {
                Outcome = ResilienceFindingOutcome.Failed,
                Summary = $"A developer invariant was violated: {violated}",
                Suggests = "The endpoint breaks a rule its developers set for this scenario."
            };
        else if (verdict.NeedsReadBack && checks.Any(check => check is { Type: InvariantType.MaxRecordsAfterDuplicates, Result.State: Check.Held }))
            verdict = verdict with
            {
                Outcome = ResilienceFindingOutcome.Passed,
                Summary = "After the duplicate requests, the read-back found no more records than the invariant allows",
                Suggests = "The target did not create extra records for the duplicate requests.",
                NotProven = "How the target avoided them."
            };

        var evidence = new
        {
            expected = verdict.Expected,
            observed = verdict.Observed,
            invariantViolated = verdict.Outcome == ResilienceFindingOutcome.Failed && checks.Any(check => check.Result.State == Check.Violated),
            facts = observations.OrderBy(o => o.AttemptNo)
                .Select(o => new { label = $"Attempt {o.AttemptNo} · {Phase(o)}", value = Describe(o) })
                .Concat(checks.Select(check => new { label = $"Invariant · {check.Type}", value = $"{Label(check.Result.State)}: {check.Result.Detail}" })),
            interpretation = new { observed = verdict.Observed, suggests = verdict.Suggests, notProven = verdict.NotProven },
            invariants = checks.Select(check => new { type = check.Type.ToString(), result = check.Result.State.ToString(), detail = check.Result.Detail }),
            observations = observations.OrderBy(o => o.AttemptNo).Select(o => new
            {
                o.AttemptNo, phase = Phase(o), o.CorrelationId, status = o.UpstreamStatusCode, o.DurationMs,
                o.RequestStartedAt, outcomeType = o.OutcomeType.ToString()
            })
        };
        var severity = verdict.Outcome switch
        {
            ResilienceFindingOutcome.Failed => RecommendationSeverity.High,
            ResilienceFindingOutcome.Degraded => RecommendationSeverity.Medium,
            _ => RecommendationSeverity.Info
        };
        return new Finding(pattern, verdict.Outcome, severity, verdict.Summary,
            JsonSerializer.SerializeToDocument(evidence, JsonSerializerOptions.Web));
    }

    // Fills the {{request.body.<field>}} templates of a read-back path from the request body that was sent. Null when
    // a template names something the request doesn't have.
    public static string? ResolveReadBack(string template, byte[]? body)
    {
        using var document = body is { Length: > 0 } ? JsonDocument.Parse(body) : null;
        var missing = false;
        var path = Regex.Replace(template, @"\{\{(.*?)\}\}", match =>
        {
            var name = match.Groups[1].Value.Trim();
            if (name.StartsWith("request.body.") && document is not null
                && Find(document.RootElement, name["request.body.".Length..]) is { ValueKind: JsonValueKind.String
                    or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } value)
                return Uri.EscapeDataString(value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText());
            missing = true;
            return "";
        });
        return missing ? null : path;
    }

    // The same request with and without the fault: `subject` is the request that shows the fault's effect.
    private static Verdict Compare(HttpObservation? baseline, HttpObservation subject, string what, string notProven)
    {
        var expected = $"{what} is answered like a normal request.";
        var seen = baseline is null
            ? $"{what}: {Describe(subject)}. No baseline request was sent."
            : $"Baseline request: {Describe(baseline)}. {what}: {Describe(subject)}.";
        var status = subject.UpstreamStatusCode;
        if (baseline is not null && !(baseline.UpstreamStatusCode < 400 && baseline.ErrorType is null))
            return new(ResilienceFindingOutcome.Inconclusive, "The baseline request did not succeed, so the fault's effect could not be judged",
                expected, seen, "The endpoint or the spec's example request does not work without a fault either.",
                "How the endpoint behaves under this fault.");
        if (status is null or >= 500 || (status >= 400 && baseline is not null))
            return new(ResilienceFindingOutcome.Failed, $"{what} got {Describe(subject)}", expected, seen,
                "The endpoint did not handle this condition.", "Where in the target the failure comes from.");
        if (status >= 400)
            return new(ResilienceFindingOutcome.Inconclusive, $"The target rejected the request with {status}, so the fault's effect could not be judged",
                expected, seen, "The spec's example request is not valid for the target.", "How the endpoint behaves under this fault.");
        if (subject.ErrorType is not null)
            return new(ResilienceFindingOutcome.Degraded, $"{what} was answered, but the response body was cut short", expected, seen,
                "The endpoint answers under this condition, but not completely.", notProven);
        if (baseline?.DurationMs is { } normal && subject.DurationMs is { } took && took > Math.Max(3 * normal, normal + SlowMarginMs))
            return new(ResilienceFindingOutcome.Degraded, $"{what} was answered in {took} ms, against {normal} ms for the baseline", expected, seen,
                "The endpoint answers under this condition, but much more slowly.", notProven);
        return new(ResilienceFindingOutcome.Passed, $"{what} was answered normally", expected, seen,
            "The endpoint tolerates this condition.", notProven);
    }

    private static Verdict Duplicates(string? key, List<HttpObservation> copies)
    {
        var accepted = copies.Count(o => o.UpstreamStatusCode < 400);
        var broken = copies.Count(o => o.UpstreamStatusCode is null or >= 500);
        var separate = key == "different";
        var keyed = key switch { "same" => "the same idempotency key", "different" => "a different idempotency key each", _ => "no idempotency key" };
        var seen = $"{copies.Count} copies with {keyed} got: {string.Join(", ", copies.Select(Describe))}.";
        var expected = separate ? "Every copy is its own operation and is accepted." : "One logical operation: at most one copy takes effect.";
        const string records = "How many records the target created.";
        if (broken > 0)
            return new(ResilienceFindingOutcome.Failed, $"{broken} of {copies.Count} duplicate requests got a server error or no response",
                expected, seen, "The endpoint does not handle repeated or concurrent copies of a request.", records);
        if (accepted == 0)
            return new(ResilienceFindingOutcome.Inconclusive, "The target rejected every copy, so duplicate handling was not observed",
                expected, seen, "The spec's example request is not valid for the target.", records);
        if (separate)
            return accepted == copies.Count
                ? new(ResilienceFindingOutcome.Passed, "Every copy with its own idempotency key was accepted", expected, seen,
                    "Requests with different keys are treated as separate operations.", records)
                : new(ResilienceFindingOutcome.Degraded, "Some copies with their own idempotency key were rejected", expected, seen,
                    "Separate operations may be mistaken for duplicates.", records);
        return accepted == 1
            ? new(ResilienceFindingOutcome.Passed, "Only one of the duplicate requests was accepted", expected, seen,
                "Behavior consistent with duplicate detection.", "That exactly one record exists; no read-back checked it.")
            : new(ResilienceFindingOutcome.Inconclusive,
                $"{accepted} duplicate requests were accepted; the responses do not show how many records exist", expected, seen,
                "Either the target replayed the first result, or it performed the operation more than once.",
                $"{records} A record-count invariant with a read-back request can decide it.", NeedsReadBack: true);
    }

    // An invariant with a read-back is checked on the read-back's response, any other on every faulted request.
    private static (Check State, string Detail) CheckInvariant(
        ScenarioType type, Invariant invariant, int index, IReadOnlyList<HttpObservation> observations, List<HttpObservation> faults)
    {
        List<HttpObservation> subjects;
        if (invariant.ReadBack is not null)
        {
            subjects = observations.Where(o => Phase(o) == "readBack" && o.Metadata.RootElement.GetProperty("invariant").GetInt32() == index).ToList();
            if (subjects.Count == 0)
                return (Check.NotEvaluated, "the read-back request could not be built from the request that was sent");
        }
        else if (type == ScenarioType.TimeoutDrop)
            return (Check.NotEvaluated, "the runner ended the faulted request itself, so there is no response to check; add a read-back request");
        else
            subjects = faults;
        var results = subjects.Select(o => CheckOne(invariant, o)).ToList();
        return results.FirstOrDefault(result => result.State == Check.Violated,
            results.FirstOrDefault(result => result.State == Check.NotEvaluated, results[0]));
    }

    private static (Check State, string Detail) CheckOne(Invariant invariant, HttpObservation o)
    {
        var p = invariant.Params ?? new InvariantParams();
        var status = o.UpstreamStatusCode;
        switch (invariant.Type)
        {
            case InvariantType.MaxDurationMs:
                return o.DurationMs is { } took
                    ? (took <= p.Ms ? Check.Held : Check.Violated, $"the request took {took} ms, the limit is {p.Ms} ms")
                    : (Check.NotEvaluated, "the request has no recorded duration");
            case InvariantType.StatusCodeIn:
                return status is { } code && p.Codes?.Contains(code) == true
                    ? (Check.Held, $"the status was {code}")
                    : (Check.Violated, $"the status was {status?.ToString() ?? "missing (no response)"}, allowed: {string.Join(", ", p.Codes ?? [])}");
        }
        if (status is null)
            return (Check.NotEvaluated, "there was no response to check");
        var metadata = o.Metadata.RootElement;
        if (invariant.Type == InvariantType.HeaderPresent)
            return metadata.TryGetProperty("headers", out var headers)
                && headers.EnumerateObject().Any(header => header.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))
                    ? (Check.Held, $"the {p.Name} header was present")
                    : (Check.Violated, $"the {p.Name} header was absent");

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(metadata.GetProperty("body").GetString()!);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or ArgumentNullException)
        {
            // shortcut: only the first 16 KB of a body are stored, so a longer one doesn't parse; store more when invariants need it.
            return (Check.NotEvaluated, "the response body was not complete JSON");
        }
        using var document = parsed;
        var root = document.RootElement;
        if (invariant.Type == InvariantType.MaxRecordsAfterDuplicates)
        {
            if (status >= 400)
                return (Check.NotEvaluated, $"the read-back request was answered with {status}");
            // shortcut: counts a top-level array, or the only array inside a top-level object ({"items": [...]}); add a
            // records path to the invariant when responses nest deeper.
            var list = root.ValueKind == JsonValueKind.Object
                ? root.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.Array)
                    .Select(property => property.Value).ToList() is [var only] ? only : default
                : root;
            if (list.ValueKind != JsonValueKind.Array)
                return (Check.NotEvaluated, "the read-back response is not a list of records");
            var count = list.GetArrayLength();
            return (count <= p.Max ? Check.Held : Check.Violated, $"the read-back returned {count} records, at most {p.Max} are allowed");
        }
        if (Find(root, p.Path![2..]) is not { } field)
            return (Check.NotEvaluated, $"{p.Path} was not in the response");
        var actual = field.ValueKind == JsonValueKind.String ? field.GetString()! : field.GetRawText();
        if (invariant.Type == InvariantType.FieldEquals)
            return (actual == p.Value ? Check.Held : Check.Violated, $"{p.Path} was {actual}, expected {p.Value}");
        return field.ValueKind == JsonValueKind.Number && field.TryGetDecimal(out var number)
            ? (number >= p.Min ? Check.Held : Check.Violated, $"{p.Path} was {actual}, the minimum is {p.Min}")
            : (Check.NotEvaluated, $"{p.Path} was not a number");
    }

    // Follows a dotted path (a.b.0.c) through objects and arrays.
    private static JsonElement? Find(JsonElement element, string path)
    {
        foreach (var part in path.Split('.'))
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(part, out var property))
                element = property;
            else if (element.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index) && index >= 0 && index < element.GetArrayLength())
                element = element[index];
            else
                return null;
        }
        return element;
    }

    private static string Phase(HttpObservation o) =>
        o.Metadata.RootElement.TryGetProperty("phase", out var phase) ? phase.GetString() ?? "" : "";

    private static string Describe(HttpObservation o) => o.UpstreamStatusCode is { } status
        ? $"{status} in {o.DurationMs} ms{(o.ErrorType is null ? "" : ", body cut short")}"
        : o.OutcomeType switch
        {
            ObservationOutcomeType.Timeout => "no response before the request timeout",
            ObservationOutcomeType.Dropped => "no response (the runner closed the connection)",
            _ => "no valid response"
        };

    private static string Label(Check state) => state switch
    {
        Check.Held => "held", Check.Violated => "violated", _ => "not evaluated"
    };
}
