using static Backend.Features.TestRuns.Evaluator;

namespace Backend.Features.TestRuns;

// Maps the problem behind a Failed or Degraded finding to a fixed recommendation (FeaturesFlow 2.22, PROJECT.md 15.2).
// Pure and deterministic: no AI, and ChaosLab never changes the target's code.
public static class Recommender
{
    // Title names the fix, Why says what the finding showed, Fix says what to change in the API.
    public sealed record Advice(string Title, string Why, string Fix);

    private const string TimeoutBudget =
        "Set connect, read, and overall timeout budgets on the endpoint and on its outbound calls, and return a bounded fallback or a clear error when a budget runs out.";

    private static readonly Dictionary<Problem, Advice> Rules = new()
    {
        [Problem.Unhandled] = new("Set timeout budgets and handle the failure",
            "The endpoint answered with an error, or not at all, under this fault.", TimeoutBudget),
        [Problem.NotRecovered] = new("Release what a failed request holds",
            "The request sent after the fault was not answered normally.",
            "Free connections, locks, and pooled resources when a request fails or its caller goes away, so one failure does not affect the next request."),
        [Problem.NoReadDeadline] = new("Set a deadline for the whole operation",
            "A response read very slowly was served to the end.",
            "Set a write or complete-operation timeout, not only a connect timeout, so a slow reader cannot hold a connection open."),
        [Problem.RetryStorm] = new("Limit the number of retries",
            "The endpoint called a failing dependency many times during one request.",
            "Cap retries at a few attempts, and wait between them with exponential backoff and jitter."),
        [Problem.RetryWithoutPause] = new("Wait between retries",
            "The endpoint retried a failing dependency with almost no pause.",
            "Add exponential backoff with jitter between attempts, and keep the retry limit bounded."),
        [Problem.DuplicatesBroke] = new("Make repeated requests safe",
            "Duplicate copies of a request got a server error or no response.",
            "Accept an idempotency key or enforce uniqueness in a transaction, so repeated and concurrent copies are handled as one operation."),
        [Problem.SeparateRejected] = new("Tell duplicates apart by their idempotency key",
            "Requests with different idempotency keys were rejected as duplicates.",
            "Detect duplicates by the idempotency key only, so separate operations with similar payloads are all accepted."),
        [Problem.DuplicateSideEffects] = new("Validate and store idempotency keys on the server",
            "Duplicate requests broke a rule set for this endpoint, such as the number of records they may leave behind.",
            "Store each idempotency key with its result and replay that result for a repeated key, or enforce uniqueness in a transaction."),
        [Problem.AnsweredWithoutCredential] = new("Fail closed on a missing or invalid credential",
            "The endpoint answered successfully without a valid credential.",
            "Check the credential before doing any work, reject the request with 401 or 403, and return no protected data."),
        [Problem.AuthUnhandled] = new("Handle a failed authentication explicitly",
            "The endpoint answered a bad credential with a server error or not at all.",
            "Catch the authentication failure and return 401, so a bad credential is never a server error."),
        [Problem.AuthWrongStatus] = new("Answer authentication failures with 401 or 403",
            "The endpoint turned the request away, but not with 401 or 403.",
            "Return 401 for a missing or invalid credential and 403 for one without access, consistently across endpoints."),
        [Problem.DependencyNotContained] = new("Isolate the failing dependency",
            "The endpoint did not answer in a controlled way while its dependency was down.",
            "Catch the dependency's failure at the call and return a controlled 502, 503, or 504, so it does not become the endpoint's own failure."),
        [Problem.NoFallback] = new("Offer a reduced response when a dependency is down",
            "The endpoint offered nothing in place of its failed dependency.",
            "Where the feature allows it, return a cached, default, or partial response while the dependency is unavailable."),
        [Problem.IgnoredRetryAfter] = new("Honour Retry-After from a throttled dependency",
            "The endpoint called its dependency again sooner than the dependency's Retry-After asked.",
            "When a dependency answers 429, wait at least as long as its Retry-After header says before calling it again, or answer your own caller with 503 and a Retry-After of your own."),
        [Problem.DependencyResponseUnhandled] = new("Validate a dependency's response before using it",
            "The endpoint did not answer in a controlled way when its dependency's response body was malformed or empty.",
            "Check the status, content type, and body of each dependency response, parse it inside error handling, and answer 502 when it is unusable instead of failing with a server error."),
        [Problem.BreakerStuckOpen] = new("Let the circuit breaker close again",
            "The endpoint stopped calling its failing dependency, but still did not answer normally once the dependency was back and the runner had waited for the breaker to close.",
            "Give the breaker a bounded open period, then let a trial call through (half-open) and close the breaker when it succeeds."),
        [Problem.InvariantViolated] = new("Make the endpoint keep the invariant",
            "The endpoint broke a rule its developers set for this scenario.",
            "Change the endpoint so the invariant named in the finding holds under this fault, or correct the invariant if it no longer describes the intended behavior."),
        [Problem.FailedWithoutFault] = new("Fix the endpoint before testing this fault",
            "The endpoint failed on an ordinary request; the injected fault was not the cause.",
            "Find why the endpoint returns a server error without any fault, then run the scenario again.")
    };

    public static Advice For(Problem problem) => Rules[problem];
}
