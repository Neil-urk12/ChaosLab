using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Scenarios;

namespace Backend.Features.TestRuns;

// Turns the observations of one scenario execution into a finding (FeaturesFlow 2.18). Pure: no database, no requests.
// Every finding says what was observed, what it suggests and what is not proven (PROJECT.md 11.2), and a violated
// developer invariant makes it Fail (PROJECT.md 13).
// The thresholds of the injector's and the simulator's faults are decided in PROJECT.md 12.3.
public static class Evaluator
{
    // Decided threshold (PROJECT.md 12.3): slower than three times the baseline and by more than a second is Degraded.
    private const long SlowMarginMs = 1000;
    // Decided thresholds (PROJECT.md 12.3). A smaller body has left the target before the slow read's first pause.
    private const long SlowReadMinBytes = 64 * 1024;
    // More calls to one operation during one request are a retry storm; a shorter gap is a retry without backoff.
    private const int MaxRetryCalls = 5;
    private const long MinRetryGapMs = 100;

    // What went wrong behind a Failed or Degraded finding, which its scoring category alone does not say: a retry storm
    // and a target that fails without any fault are both Retry findings. Recommender has one rule per value.
    public enum Problem
    {
        Unhandled, NotRecovered, NoReadDeadline,
        RetryStorm, RetryWithoutPause,
        DuplicatesBroke, SeparateRejected, DuplicateSideEffects,
        AnsweredWithoutCredential, AuthUnhandled, AuthWrongStatus,
        DependencyNotContained, NoFallback, IgnoredRetryAfter, DependencyResponseUnhandled, BreakerStuckOpen,
        InvariantViolated, FailedWithoutFault
    }

    // Problem is set on every Failed and Degraded finding, and on no other.
    public sealed record Finding(
        ResiliencePattern Pattern, ScoringCategory Category, ResilienceFindingOutcome Outcome, RecommendationSeverity Severity,
        string Summary, JsonDocument Evidence, Problem? Problem = null);

    private enum Check { Held, Violated, NotEvaluated }

    private sealed record Verdict(
        ResilienceFindingOutcome Outcome, string Summary, string Expected, string Observed, string Suggests, string NotProven,
        // The responses alone can't decide; a record count read back from the target can.
        bool NeedsReadBack = false,
        // Null: the outcome's usual severity.
        RecommendationSeverity? Severity = null,
        Problem? Problem = null);

    private sealed record Rated(ResiliencePattern Pattern, ScoringCategory Category, Verdict Verdict);

    // Empty when there is nothing to judge: Baseline is not a fault, and a scenario without fault requests never ran.
    // A Dependency failure yields up to four findings, a 5xx Storm in the dependency path up to two, every other fault one.
    public static IReadOnlyList<Finding> Evaluate(
        ScenarioType type, JsonElement parameters, IReadOnlyList<Invariant> invariants, IReadOnlyList<HttpObservation> observations)
    {
        List<HttpObservation> In(string phase) => observations.Where(o => Phase(o) == phase).OrderBy(o => o.AttemptNo).ToList();
        var baseline = In("baseline").FirstOrDefault();
        var faults = In("fault");
        // Only Dependency failure may send several, while a circuit breaker closes: the last shows how the target came back.
        var probe = In("recovery").LastOrDefault();
        if (faults.Count == 0)
            return [];
        // Faults the target met in its own dependency calls; the others only changed what the runner received.
        var simulated = DependencySimulator.Applies(type, parameters);

        List<Rated> rated;
        switch (type)
        {
            case ScenarioType.Latency:
                rated = [new(ResiliencePattern.Timeout, ScoringCategory.Timeout, RunnerOnly(type, baseline, faults, probe))];
                break;
            case ScenarioType.TimeoutDrop when probe is not null:
                rated = [new(ResiliencePattern.Timeout, ScoringCategory.Timeout, Compare(baseline, probe,
                    parameters.GetProperty("mode").GetString() == "hold"
                        ? "The request after one the runner held open until it timed out"
                        : "The request after one whose connection the runner dropped",
                    "Whether the target stopped working on the abandoned request or released its resources."))];
                break;
            case ScenarioType.DuplicateRequest:
                rated = [new(ResiliencePattern.Idempotency, ScoringCategory.Idempotency,
                    Duplicates(parameters.GetProperty("key").GetString(), faults))];
                break;
            case ScenarioType.AuthExpiry when probe is not null:
                rated = [new(ResiliencePattern.AuthenticationFailure, ScoringCategory.AuthenticationFailure,
                    AuthFailure(baseline, faults[0], probe))];
                break;
            case ScenarioType.SlowDrip when probe is not null:
                rated = [new(ResiliencePattern.Timeout, ScoringCategory.Timeout, SlowRead(baseline, faults[0], probe))];
                break;
            case ScenarioType.DnsFailure when probe is not null:
                rated = Dependency(baseline, faults, probe);
                break;
            case ScenarioType.ServerErrorStorm when simulated && probe is not null:
                rated = Storm(baseline, faults[0], probe);
                break;
            case ScenarioType.Throttle429 when simulated && probe is not null:
                rated = [new(ResiliencePattern.RateLimit, ScoringCategory.RateLimit,
                    Throttled(parameters.GetProperty("retryAfterSeconds").GetInt32(), baseline, faults[0], probe))];
                break;
            case ScenarioType.MalformedJson or ScenarioType.EmptyResponse when simulated && probe is not null:
                rated = [new(ResiliencePattern.ResponseValidation, ScoringCategory.ResponseValidation,
                    BrokenAnswer(type, baseline, faults[0], probe))];
                break;
            case ScenarioType.ServerErrorStorm when !simulated:
                rated = [new(ResiliencePattern.Retry, ScoringCategory.Retry, RunnerOnly(type, baseline, faults, probe))];
                break;
            case ScenarioType.Throttle429 when !simulated:
                rated = [new(ResiliencePattern.RateLimit, ScoringCategory.RateLimit, RunnerOnly(type, baseline, faults, probe))];
                break;
            case ScenarioType.MalformedJson or ScenarioType.EmptyResponse when !simulated:
                rated = [new(ResiliencePattern.ResponseValidation, ScoringCategory.ResponseValidation,
                    RunnerOnly(type, baseline, faults, probe))];
                break;
            default:
                return [];
        }

        var checks = invariants.Select((invariant, index) => (invariant.Type, Result: CheckInvariant(type, invariant, index, observations, faults))).ToList();
        // The invariants belong to the scenario's own finding, not to the extra ones a Dependency failure adds.
        var first = rated[0].Verdict;
        if (checks.Where(check => check.Result.State == Check.Violated).Select(check => check.Result.Detail).FirstOrDefault() is { } violated)
            first = first with
            {
                Outcome = ResilienceFindingOutcome.Failed,
                Summary = $"A developer invariant was violated: {violated}",
                Suggests = "The endpoint breaks a rule its developers set for this scenario.",
                // A broken rule about duplicates is the side effect PROJECT.md 15.1 gives idempotency advice for.
                Problem = type == ScenarioType.DuplicateRequest ? Problem.DuplicateSideEffects : Problem.InvariantViolated
            };
        else if (first.NeedsReadBack && checks.Any(check => check is { Type: InvariantType.MaxRecordsAfterDuplicates, Result.State: Check.Held }))
            first = first with
            {
                Outcome = ResilienceFindingOutcome.Passed,
                Summary = "After the duplicate requests, the read-back found no more records than the invariant allows",
                Suggests = "The target did not create extra records for the duplicate requests.",
                NotProven = "How the target avoided them."
            };
        rated[0] = rated[0] with { Verdict = first };

        return rated.Select((entry, index) => Build(entry, index == 0, checks, observations)).ToList();
    }

    private static Finding Build(
        Rated entry, bool own, List<(InvariantType? Type, (Check State, string Detail) Result)> checks,
        IReadOnlyList<HttpObservation> observations)
    {
        var verdict = entry.Verdict;
        var evidence = new
        {
            expected = verdict.Expected,
            observed = verdict.Observed,
            invariantViolated = own && verdict.Outcome == ResilienceFindingOutcome.Failed && checks.Any(check => check.Result.State == Check.Violated),
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
            ResilienceFindingOutcome.Failed => verdict.Severity ?? RecommendationSeverity.High,
            ResilienceFindingOutcome.Degraded => RecommendationSeverity.Medium,
            _ => RecommendationSeverity.Info
        };
        return new Finding(entry.Pattern, entry.Category, verdict.Outcome, severity, verdict.Summary,
            JsonSerializer.SerializeToDocument(evidence, JsonSerializerOptions.Web),
            verdict.Outcome is ResilienceFindingOutcome.Failed or ResilienceFindingOutcome.Degraded ? verdict.Problem : null);
    }

    // Authentication Expiry: the target itself has to turn the request away, then accept the valid credential again.
    private static Verdict AuthFailure(HttpObservation? baseline, HttpObservation fault, HttpObservation probe)
    {
        var what = fault.Metadata.RootElement.TryGetProperty("sent", out var sent) && sent.GetString() == "missing"
            ? "A request without its credential" : "A request with an invalid credential";
        const string expected = "The request is rejected with 401 or 403, and the valid credential still works afterwards.";
        const string notProven = "Whether an expired credential is treated the same way; one cannot be made from outside the target.";
        var status = fault.UpstreamStatusCode;
        var verdict = status switch
        {
            null or >= 500 => new Verdict(ResilienceFindingOutcome.Failed, $"{what} got {Describe(fault)}", expected, "",
                "The endpoint does not handle a failed authentication.", notProven, Problem: Problem.AuthUnhandled),
            // The only failure that exposes data (PROJECT.md 12.3). A 3xx carries none: redirects aren't followed, so it
            // falls through to Degraded with the other refusals.
            >= 200 and < 300 => new Verdict(ResilienceFindingOutcome.Failed, $"{what} was answered with {status}", expected, "",
                "The endpoint answers without the credential its spec requires.", "Whether the response held protected data.",
                Severity: RecommendationSeverity.Critical, Problem: Problem.AnsweredWithoutCredential),
            401 or 403 => new Verdict(ResilienceFindingOutcome.Passed, $"{what} was rejected with {status}", expected, "",
                "The endpoint fails closed.", notProven),
            _ => new Verdict(ResilienceFindingOutcome.Degraded, $"{what} was rejected with {status} rather than 401 or 403", expected, "",
                "The request is turned away, but not as an authentication failure.", notProven, Problem: Problem.AuthWrongStatus)
        };
        return After(verdict, baseline, probe, Seen(baseline, what, fault, probe));
    }

    // Slow Drip: a target with a complete-operation deadline ends a response that is read too slowly.
    private static Verdict SlowRead(HttpObservation? baseline, HttpObservation fault, HttpObservation probe)
    {
        const string what = "A response read a few bytes at a time";
        const string expected = "The target ends a response that is read too slowly, and answers normally afterwards.";
        const string notProven = "What deadline the target applies, and to which part of the operation.";
        var seen = Seen(baseline, what, fault, probe);
        if (Broke(fault))
            return new(ResilienceFindingOutcome.Failed, $"{what} got {Describe(fault)}", expected, seen,
                "The endpoint did not handle this condition.", "Where in the target the failure comes from.", Problem: Problem.Unhandled);
        // A read the timeout ended has not counted the whole body; the declared length has.
        var size = Math.Max(fault.ResponseBytes ?? 0, long.TryParse(Header(fault, "Content-Length"), out var declared) ? declared : 0);
        if (size == 0 && fault.ErrorType is null)
            return new(ResilienceFindingOutcome.NotApplicable, "The response has no body, so there was nothing to read slowly",
                expected, seen, "Slow Drip does not apply to this endpoint.", notProven);
        Verdict verdict;
        if (fault is { ErrorType: not null, OutcomeType: not ObservationOutcomeType.Timeout })
            verdict = new(ResilienceFindingOutcome.Passed, $"The target closed the connection {fault.DurationMs} ms into the slow read",
                expected, seen, "Behavior consistent with a complete-operation deadline.", notProven);
        else if (size >= SlowReadMinBytes)
            verdict = new(ResilienceFindingOutcome.Degraded,
                fault.OutcomeType == ObservationOutcomeType.Timeout
                    ? "The response was still being delivered to a slow reader when the request timed out"
                    : $"A {size}-byte response was delivered in full over {fault.DurationMs} ms of slow reading",
                expected, seen, "No complete-operation deadline was observed.", notProven, Problem: Problem.NoReadDeadline);
        else
            verdict = new(ResilienceFindingOutcome.Inconclusive,
                $"The {size}-byte body is under {SlowReadMinBytes / 1024} KB, so it left the target before the first pause",
                expected, seen, "The target never faced a slow reader.", "Whether the target enforces a complete-operation deadline.");
        return After(verdict, baseline, probe, seen);
    }

    // Dependency failure: how the endpoint contains it, how it retries the cut calls, and what its caller gets instead,
    // all judged on the first request. Several requests also show whether a circuit breaker opens.
    private static List<Rated> Dependency(HttpObservation? baseline, List<HttpObservation> faults, HttpObservation probe)
    {
        var fault = faults[0];
        const string what = "A request sent while the target's dependency was down";
        // A gap in attempt numbers after the last fault: the recovery request was repeated while a breaker closed.
        var seen = Seen(baseline, what, fault, probe,
            probe.AttemptNo > faults[^1].AttemptNo + 1 ? "The last request after it" : "The request after it");
        var status = fault.UpstreamStatusCode;
        const string isolated = "The target answers in a controlled way while its dependency is down";
        const string contained = isolated + ", and normally once it is back.";
        const string howContained = "How the target isolates the dependency.";
        var verdict = status switch
        {
            null => new Verdict(ResilienceFindingOutcome.Failed, $"{what} got {Describe(fault)}", contained, "",
                "The dependency's failure became the endpoint's own.", howContained, Problem: Problem.DependencyNotContained),
            < 400 or 502 or 503 or 504 => new Verdict(ResilienceFindingOutcome.Passed, $"{what} was answered with {status}", contained, "",
                "The endpoint contains its dependency's failure.", howContained),
            _ => new Verdict(ResilienceFindingOutcome.Degraded, $"{what} was answered with {status}", contained, "",
                "The endpoint answers, but not with a controlled error for a failed dependency.", howContained,
                Problem: Problem.DependencyNotContained)
        };
        // A baseline that never worked decides nothing about retries, fallbacks or breakers either.
        var judged = baseline is null || Succeeded(baseline);
        // Once a breaker opened, whether the target answers again is the Circuit Breaker finding's alone (PROJECT.md 26).
        var isolation = judged && Opened(faults) >= 0
            ? verdict with { Expected = isolated + ".", Observed = seen }
            : After(verdict, baseline, probe, seen);
        List<Rated> rated = [new(ResiliencePattern.DependencyFailure, ScoringCategory.DependencyFailure, isolation)];
        if (!judged)
            return rated;

        rated.Add(new(ResiliencePattern.Retry, ScoringCategory.Retry, Retries(fault, seen)));
        rated.Add(new(ResiliencePattern.Fallback, ScoringCategory.GracefulDegradation, Fallback(what, "down", fault, seen)));
        if (faults.Count > 1)
            rated.Add(new(ResiliencePattern.CircuitBreaker, ScoringCategory.DependencyFailure, Breaker(baseline, faults, probe)));
        return rated;
    }

    // The first request after the first answered without a dependency call, the breaker opening; -1 when none was.
    private static int Opened(List<HttpObservation> faults) =>
        faults.FindIndex(1, fault => fault.UpstreamStatusCode is not null && CallLog(fault).Total == 0);

    // Requests sent one after another while the dependency is down (PROJECT.md 12.3, decision 10). One answered without a
    // dependency call, after the first did call, is a breaker that opened; the target answering normally again, once the
    // dependency is back and within RunExecutor.BreakerWait, is the breaker closing. Not opening is no defect: its
    // threshold may be above the number of requests.
    private static Verdict Breaker(HttpObservation? baseline, List<HttpObservation> faults, HttpObservation probe)
    {
        const string expected = "After repeated dependency failures the target stops calling the dependency for a while, and calls it again once it is back.";
        const string notProven = "The breaker's failure threshold and how long it stays open.";
        var totals = faults.Select(fault => CallLog(fault).Total).ToList();
        var observed = $"Dependency calls for each of the {faults.Count} requests sent while it was down: {string.Join(", ", totals)}. "
            + $"The last request after it was back: {Describe(probe)}.";
        var opened = Opened(faults);
        if (opened < 0)
            return new(ResilienceFindingOutcome.Inconclusive, $"The target called its dependency on all {faults.Count} requests",
                expected, observed, "No circuit breaker opened within this many failures.",
                "Whether the target has a breaker that opens after more failures.");
        var back = Compare(baseline, probe, "The request after the dependency was back", notProven).Outcome;
        if (back == ResilienceFindingOutcome.Inconclusive)
            return new(ResilienceFindingOutcome.Inconclusive, "The target stopped calling its dependency, but its normal answer is unknown",
                expected, observed, "Behavior consistent with a circuit breaker opening.", "Whether the breaker closes again.");
        if (back == ResilienceFindingOutcome.Failed)
            return new(ResilienceFindingOutcome.Degraded,
                $"The target stopped calling its dependency after {opened} failed requests, and was still not answering normally {RunExecutor.BreakerWait.TotalSeconds:0} s after it was back",
                expected, observed, "A circuit breaker opened but did not close again.", notProven, Problem: Problem.BreakerStuckOpen);
        return new(ResilienceFindingOutcome.Passed,
            $"The target stopped calling its dependency after {opened} failed requests, and answered normally once it was back",
            expected, observed, "Behavior consistent with a circuit breaker that opens and closes again.", notProven);
    }

    // 5xx Storm in the dependency path: the target's first dependency calls failed with a 5xx and later ones were
    // answered. How it retried is the scenario's own finding; what its caller got is the fallback one.
    private static List<Rated> Storm(HttpObservation? baseline, HttpObservation fault, HttpObservation probe)
    {
        const string what = "A request sent while the target's dependency answered with server errors";
        var seen = Seen(baseline, what, fault, probe);
        // Each failure is a draw against `probability`, so every one can miss and leave the target nothing to handle.
        if (!CallLog(fault).Calls.Any(call => call.Injected))
        {
            Verdict Missed(string expected) => new(ResilienceFindingOutcome.NotApplicable,
                "Every draw missed the scenario's probability, so none of the dependency's answers failed", expected, seen,
                "5xx Storm changed nothing in this request.", "How the target handles a failing dependency.");
            return [new(ResiliencePattern.Retry, ScoringCategory.Retry,
                    Missed("A failed dependency call is retried a few times, with a pause between attempts.")),
                new(ResiliencePattern.Fallback, ScoringCategory.GracefulDegradation,
                    Missed("The target offers an alternative or reduced response while its dependency is failing."))];
        }
        var retries = Retries(fault, seen);
        List<Rated> rated = [new(ResiliencePattern.Retry, ScoringCategory.Retry, After(retries, baseline, probe, retries.Observed))];
        // A baseline that never worked decides nothing about fallbacks either.
        if (baseline is not null && !Succeeded(baseline))
            return rated;
        rated.Add(new(ResiliencePattern.Fallback, ScoringCategory.GracefulDegradation, Fallback(what, "failing", fault, seen)));
        return rated;
    }

    // What the caller gets while the dependency fails (PROJECT.md 12.3, decision 4). state: "down" (cut) or "failing"
    // (answering with errors).
    private static Verdict Fallback(string what, string state, HttpObservation fault, string seen)
    {
        var alternative = $"The target offers an alternative or reduced response while its dependency is {state}.";
        var status = fault.UpstreamStatusCode;
        return status switch
        {
            < 400 => new Verdict(ResilienceFindingOutcome.Passed, $"The target answered {status} while its dependency was {state}",
                alternative, seen, "Behavior consistent with a fallback or a reduced response.",
                "Whether the response was complete or a reduced one."),
            502 or 503 or 504 => new Verdict(ResilienceFindingOutcome.Degraded,
                $"The target answered {status} while its dependency was {state}, with nothing in its place", alternative, seen,
                "The failure is reported in a controlled way, but no alternative is offered.", "Whether a fallback exists for other failures.",
                Problem: Problem.NoFallback),
            _ => new Verdict(ResilienceFindingOutcome.Failed, $"{what} got {Describe(fault)}", alternative, seen,
                "The endpoint has no fallback for a failed dependency.", "Where in the target the failure comes from.",
                Problem: Problem.NoFallback)
        };
    }

    // 429 Throttle in the dependency path: the target's first dependency call was answered with 429 and Retry-After, and
    // its next call to the same operation shows whether it waited (PROJECT.md 12.3, decision 8).
    private static Verdict Throttled(int retryAfterSeconds, HttpObservation? baseline, HttpObservation fault, HttpObservation probe)
    {
        const string what = "A request sent while the target's dependency answered 429 with Retry-After";
        var expected = $"The target waits the {retryAfterSeconds} s that Retry-After asks for before calling its dependency again, and answers in a controlled way.";
        const string notProven = "Whether the target also honours a Retry-After given as a date, and whether it slows its own callers down in turn.";
        var (calls, total) = CallLog(fault);
        var seen = $"{Seen(baseline, what, fault, probe)} Dependency calls during the fault: {total}.";
        var status = fault.UpstreamStatusCode;
        var busiest = calls.GroupBy(call => call.Operation).Select(group => (Operation: group.Key, Count: group.Count()))
            .OrderByDescending(group => group.Count).FirstOrDefault();
        var throttled = calls.FindIndex(call => call.Injected);
        var again = throttled < 0 ? -1 : calls.FindIndex(throttled + 1, call => call.Operation == calls[throttled].Operation);
        long? waited = again < 0 ? null : calls[again].AtMs - calls[throttled].AtMs;
        Verdict verdict;
        if (busiest.Count > MaxRetryCalls)
            verdict = new(ResilienceFindingOutcome.Failed, $"The target called {busiest.Operation} {busiest.Count} times during one request",
                expected, seen, "Retries are not limited enough to spare a throttled dependency.", notProven, Problem: Problem.RetryStorm);
        else if (status is null or (>= 500 and not (502 or 503 or 504)))
            verdict = new(ResilienceFindingOutcome.Failed, $"{what} got {Describe(fault)}", expected, seen,
                "The dependency's 429 became the endpoint's own failure.", "Where in the target the failure comes from.",
                Problem: Problem.DependencyNotContained);
        else if (waited < retryAfterSeconds * 1000L)
            verdict = new(ResilienceFindingOutcome.Degraded,
                $"The target called {calls[again].Operation} again {waited} ms after the 429, before the {retryAfterSeconds} s Retry-After",
                expected, seen, "The target retries a throttled dependency without honouring Retry-After.", notProven,
                Problem: Problem.IgnoredRetryAfter);
        else
            verdict = new(ResilienceFindingOutcome.Passed, waited is { } ms
                    ? $"The target waited {ms} ms before calling {calls[again].Operation} again, as Retry-After asked"
                    : $"The target did not call its throttled dependency again and answered {status}",
                expected, seen, "Behavior consistent with honouring Retry-After.", notProven);
        return After(verdict, baseline, probe, seen);
    }

    // Malformed JSON and Empty Response in the dependency path: the target had to read a broken answer from its
    // dependency (PROJECT.md 12.3, decision 9).
    private static Verdict BrokenAnswer(ScenarioType type, HttpObservation? baseline, HttpObservation fault, HttpObservation probe)
    {
        var malformed = type == ScenarioType.MalformedJson;
        var what = $"A request sent while the target's dependency answered with {(malformed ? "a malformed body" : "an empty body")}";
        const string expected = "The target checks its dependency's answer and responds in a controlled way: with a usable answer, or with 502, 503 or 504.";
        const string notProven = "Whether any of the broken data reached the target's answer or its stored state.";
        var (calls, total) = CallLog(fault);
        var seen = $"{Seen(baseline, what, fault, probe)} Dependency calls during the fault: {total}.";
        if (!calls.Any(call => call.Injected))
            return new(ResilienceFindingOutcome.NotApplicable,
                malformed ? "The dependency's answers had nothing to corrupt, so the fault changed nothing"
                    : "The dependency's answers were already empty, so the fault changed nothing",
                expected, seen, $"{(malformed ? "Malformed JSON" : "Empty Response")} does not apply to this dependency call.", notProven);
        var status = fault.UpstreamStatusCode;
        return After(status switch
        {
            < 300 or 502 or 503 or 504 => new Verdict(ResilienceFindingOutcome.Passed, $"{what} was answered with {status}", expected, "",
                "Behavior consistent with checking the dependency's answer.", notProven),
            >= 300 and < 500 => new Verdict(ResilienceFindingOutcome.Degraded, $"{what} was answered with {status}", expected, "",
                "The endpoint answers, but not with a controlled error for a broken dependency answer.", notProven,
                Problem: Problem.DependencyResponseUnhandled),
            _ => new Verdict(ResilienceFindingOutcome.Failed, $"{what} got {Describe(fault)}", expected, "",
                "The broken answer became the endpoint's own failure.", "Where in the target the failure comes from.",
                Problem: Problem.DependencyResponseUnhandled)
        }, baseline, probe, seen);
    }

    // The simulator's log of the faulted request (DependencySimulator.CallLog): the stored calls in arrival order, and
    // how many there were in all.
    internal static (List<(string Operation, long AtMs, bool Injected)> Calls, int Total) CallLog(HttpObservation fault)
    {
        if (!fault.Metadata.RootElement.TryGetProperty("dependencyCalls", out var log) || log.ValueKind != JsonValueKind.Object)
            return ([], 0);
        var calls = log.GetProperty("calls").EnumerateArray()
            .Select(call => (Operation: $"{call.GetProperty("method").GetString()} {call.GetProperty("path").GetString()}",
                AtMs: call.GetProperty("atMs").GetInt64(),
                // Logs from before `injected` existed hold cut calls only.
                Injected: !call.TryGetProperty("injected", out var injected) || injected.GetBoolean()))
            .OrderBy(call => call.AtMs).ToList();
        return (calls, log.GetProperty("total").GetInt32());
    }

    // The target's calls through the simulator during the fault, per dependency operation: their number shows retries,
    // their spacing shows backoff.
    private static Verdict Retries(HttpObservation fault, string seen)
    {
        const string expected = "A failed dependency call is retried a few times, with a pause between attempts.";
        const string notProven = "Whether the pauses grow or carry jitter, and what the target's retry limit is.";
        // shortcut: only the first 100 calls are stored (DependencySimulator.CallLog), enough to tell a storm.
        var (calls, total) = CallLog(fault);
        var worst = calls
            .GroupBy(call => call.Operation)
            .Select(group =>
            {
                var times = group.Select(call => call.AtMs).ToList();
                var gap = times.Zip(times.Skip(1), (earlier, later) => later - earlier).DefaultIfEmpty(long.MaxValue).Min();
                var outcome = times.Count > MaxRetryCalls ? ResilienceFindingOutcome.Failed
                    : times.Count == 1 ? ResilienceFindingOutcome.Inconclusive
                    : gap < MinRetryGapMs ? ResilienceFindingOutcome.Degraded : ResilienceFindingOutcome.Passed;
                return (Operation: group.Key, times.Count, Gap: gap, Outcome: outcome);
            })
            // Fail first, then Degraded, Pass, and a single call last: one operation retried well is still a retry.
            .OrderBy(group => group.Outcome switch
            {
                ResilienceFindingOutcome.Failed => 0, ResilienceFindingOutcome.Degraded => 1, ResilienceFindingOutcome.Passed => 2, _ => 3
            })
            .ThenByDescending(group => group.Count).FirstOrDefault();
        var observed = $"{seen} Dependency calls during the fault: {total}.";
        return worst.Outcome switch
        {
            ResilienceFindingOutcome.Failed => new(worst.Outcome, $"The target called {worst.Operation} {worst.Count} times during one request",
                expected, observed, "Retries are not limited enough to spare a failing dependency.", notProven, Problem: Problem.RetryStorm),
            ResilienceFindingOutcome.Degraded => new(worst.Outcome,
                $"The target called {worst.Operation} {worst.Count} times, as little as {worst.Gap} ms apart", expected, observed,
                "Retry-like behavior without a pause between attempts.", notProven, Problem: Problem.RetryWithoutPause),
            ResilienceFindingOutcome.Passed when worst.Operation is not null => new(worst.Outcome,
                $"The target called {worst.Operation} {worst.Count} times, at least {worst.Gap} ms apart", expected, observed,
                "Behavior consistent with a few retries with a pause between them.", notProven),
            _ => new(ResilienceFindingOutcome.Inconclusive, "The target called its dependency once and did not try again", expected, observed,
                "There is no retry behavior to judge.", "Whether the target retries other kinds of failure.")
        };
    }

    // 5xx Storm, 429 Throttle, Malformed JSON and Empty Response in the runner's path change only what the runner
    // receives (PROJECT.md 10.2), and Latency only delays the runner's own request: the target never meets the fault,
    // so nothing here can pass (PROJECT.md 12.3).
    private static Verdict RunnerOnly(ScenarioType type, HttpObservation? baseline, List<HttpObservation> faults, HttpObservation? probe)
    {
        var name = type switch
        {
            ScenarioType.Latency => "a slow answer",
            ScenarioType.ServerErrorStorm => "a run of 5xx answers",
            ScenarioType.Throttle429 => "a 429 with Retry-After",
            ScenarioType.MalformedJson => "a malformed response body",
            _ => "an empty response body"
        };
        var expected = $"The target handles {name} from a service it depends on.";
        var all = faults.Append(probe).OfType<HttpObservation>().ToList();
        var seen = string.Join(" ", all.Prepend(baseline).OfType<HttpObservation>()
            .Select(o => $"Attempt {o.AttemptNo} ({Phase(o)}), the target's own answer: {Describe(o)}."));
        var notProven = type == ScenarioType.Latency
            ? "How the target handles a slow answer from one of its own dependencies; a delay in the dependency path would show that, and it is not built."
            : $"How the target handles {name} from one of its own dependencies; the fault has to be in the dependency path for that.";
        if (baseline is not null && !Succeeded(baseline))
            return new(ResilienceFindingOutcome.Inconclusive, "The baseline request did not succeed, so the fault's effect could not be judged",
                expected, seen, "The endpoint or the spec's example request does not work without a fault either.", notProven);
        if (all.FirstOrDefault(Broke) is { } broken)
            return new(ResilienceFindingOutcome.Failed, $"The target itself failed during the scenario: attempt {broken.AttemptNo} got {Describe(broken)}",
                expected, seen, "The endpoint failed on an ordinary request; the injected fault was not the cause.", notProven,
                Problem: Problem.FailedWithoutFault);
        if (type == ScenarioType.EmptyResponse && faults[0].ResponseBytes is null or 0)
            return new(ResilienceFindingOutcome.NotApplicable, "The target's own response body is already empty, so the fault changed nothing",
                expected, seen, "Empty Response does not apply to this endpoint.", notProven);
        if (type == ScenarioType.MalformedJson
            && (faults[0].ResponseBytes is null or 0 || Header(faults[0], "Content-Type")?.Contains("json", StringComparison.OrdinalIgnoreCase) != true))
            return new(ResilienceFindingOutcome.NotApplicable, "The target's own response is not JSON, so there was nothing to corrupt",
                expected, seen, "Malformed JSON does not apply to this endpoint.", notProven);
        return new(ResilienceFindingOutcome.Inconclusive, type == ScenarioType.Latency
                ? "The runner only delayed its own request, so how the target handles a slow answer was not observed"
                : $"Only the runner received {name}, so how the target handles it was not observed",
            expected, seen, "The fault was reproduced, and the target answered normally throughout.", notProven);
    }

    // The fault's own verdict, lowered by the request sent after it: a target that does not answer normally again has
    // not recovered, and a baseline that never worked decides nothing.
    private static Verdict After(Verdict fault, HttpObservation? baseline, HttpObservation probe, string seen)
    {
        var recovery = Compare(baseline, probe, "The request after the fault", fault.NotProven);
        var lowered = recovery.Outcome == ResilienceFindingOutcome.Inconclusive
            || (recovery.Outcome == ResilienceFindingOutcome.Failed && fault.Outcome != ResilienceFindingOutcome.Failed)
            || (recovery.Outcome == ResilienceFindingOutcome.Degraded && fault.Outcome == ResilienceFindingOutcome.Passed);
        return (lowered ? recovery : fault) with { Expected = fault.Expected, Observed = seen };
    }

    private static string Seen(HttpObservation? baseline, string what, HttpObservation fault, HttpObservation probe,
        string after = "The request after it") =>
        $"{(baseline is null ? "" : $"Baseline request: {Describe(baseline)}. ")}{what}: {Describe(fault)}. {after}: {Describe(probe)}.";

    private static bool Succeeded(HttpObservation o) => o.UpstreamStatusCode < 400 && o.ErrorType is null;

    // Judged on the target's own status, also where the injector showed the runner another one.
    private static bool Broke(HttpObservation o) => o.UpstreamStatusCode is null or >= 500;

    private static string? Header(HttpObservation o, string name) =>
        o.Metadata.RootElement.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object
            ? headers.EnumerateObject().Where(header => header.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(header => header.Value.GetString()).FirstOrDefault()
            : null;

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

    // The request sent after the fault, compared with the baseline. Whatever it got, a request after the fault that is not
    // answered normally is a missing recovery.
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
                "The endpoint did not handle this condition.", "Where in the target the failure comes from.", Problem: Problem.NotRecovered);
        if (status >= 400)
            return new(ResilienceFindingOutcome.Inconclusive, $"The target rejected the request with {status}, so the fault's effect could not be judged",
                expected, seen, "The spec's example request is not valid for the target.", "How the endpoint behaves under this fault.");
        if (subject.ErrorType is not null)
            return new(ResilienceFindingOutcome.Degraded, $"{what} was answered, but the response body was cut short", expected, seen,
                "The endpoint answers under this condition, but not completely.", notProven, Problem: Problem.NotRecovered);
        if (baseline?.DurationMs is { } normal && subject.DurationMs is { } took && took > Math.Max(3 * normal, normal + SlowMarginMs))
            return new(ResilienceFindingOutcome.Degraded, $"{what} was answered in {took} ms, against {normal} ms for the baseline", expected, seen,
                "The endpoint answers under this condition, but much more slowly.", notProven, Problem: Problem.NotRecovered);
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
                expected, seen, "The endpoint does not handle repeated or concurrent copies of a request.", records,
                Problem: Problem.DuplicatesBroke);
        if (accepted == 0)
            return new(ResilienceFindingOutcome.Inconclusive, "The target rejected every copy, so duplicate handling was not observed",
                expected, seen, "The spec's example request is not valid for the target.", records);
        if (separate)
            return accepted == copies.Count
                ? new(ResilienceFindingOutcome.Passed, "Every copy with its own idempotency key was accepted", expected, seen,
                    "Requests with different keys are treated as separate operations.", records)
                : new(ResilienceFindingOutcome.Degraded, "Some copies with their own idempotency key were rejected", expected, seen,
                    "Separate operations may be mistaken for duplicates.", records, Problem: Problem.SeparateRejected);
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
