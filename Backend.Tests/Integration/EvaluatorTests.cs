using System.Text.Json;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Scenarios;
using Backend.Features.TestRuns;
using static Backend.Domain.Enums.ResilienceFindingOutcome;

namespace Backend.Tests.Integration;

// The evaluator is pure, so these need no database: observations in, finding out.
public sealed class EvaluatorTests
{
    private static JsonElement Parameters(object value) => JsonSerializer.SerializeToElement(value);

    private static HttpObservation Observation(
        int attempt, string phase, int? status, long durationMs = 20, string body = "{}", string? error = null,
        ObservationOutcomeType outcome = ObservationOutcomeType.Forwarded, int? invariant = null, long bytes = 2,
        string contentType = "application/json", long[]? calls = null, bool[]? injected = null) => new()
    {
        AttemptNo = attempt, UpstreamStatusCode = status, ClientStatusCode = status, DurationMs = durationMs, ErrorType = error,
        OutcomeType = outcome, ResponseBytes = status is null ? null : bytes,
        Metadata = JsonSerializer.SerializeToDocument(new Dictionary<string, object?>
        {
            ["phase"] = phase, ["invariant"] = invariant, ["body"] = body,
            ["headers"] = new Dictionary<string, string> { ["ETag"] = "1", ["Content-Type"] = contentType },
            // The simulator's call log: every call to the same dependency operation, at the given times, each changed by
            // the fault unless `injected` says otherwise.
            ["dependencyCalls"] = calls is null ? null : new
            {
                total = calls.Length,
                calls = calls.Select((atMs, index) => new { atMs, method = "GET", path = "/rates", injected = injected?[index] ?? true })
            }
        })
    };

    private static IReadOnlyList<Evaluator.Finding> Faulted(ScenarioType type, object parameters, HttpObservation fault, int recovery = 200) =>
        Evaluator.Evaluate(type, Parameters(parameters), [], [Observation(1, "baseline", 200), fault, Observation(3, "recovery", recovery)]);

    [Fact]
    public void Auth_expiry_passes_only_when_the_target_fails_closed_and_recovers()
    {
        (ResilienceFindingOutcome, RecommendationSeverity) Auth(int? status, int recovery = 200)
        {
            var finding = Faulted(ScenarioType.AuthExpiry, new { credential = "invalid" }, Observation(2, "fault", status), recovery).Single();
            Assert.Equal((ResiliencePattern.AuthenticationFailure, ScoringCategory.AuthenticationFailure), (finding.Pattern, finding.Category));
            return (finding.Outcome, finding.Severity);
        }

        Assert.Equal((Passed, RecommendationSeverity.Info), Auth(401));
        Assert.Equal((Passed, RecommendationSeverity.Info), Auth(403));
        Assert.Equal((Degraded, RecommendationSeverity.Medium), Auth(404));
        // Answered without a valid credential: the one failure that exposes data.
        Assert.Equal((Failed, RecommendationSeverity.Critical), Auth(200));
        // A redirect to a login page turns the request away without exposing anything.
        Assert.Equal((Degraded, RecommendationSeverity.Medium), Auth(302));
        Assert.Equal((Failed, RecommendationSeverity.High), Auth(500));
        // Rejected correctly, but the valid credential no longer works afterwards.
        Assert.Equal((Failed, RecommendationSeverity.High), Auth(401, recovery: 401));
    }

    [Fact]
    public void Slow_drip_needs_a_body_large_enough_to_hold_the_target()
    {
        ResilienceFindingOutcome Drip(long bytes, string? error = null, ObservationOutcomeType outcome = ObservationOutcomeType.Forwarded) =>
            Faulted(ScenarioType.SlowDrip, new { bytes = 1, intervalMs = 200 },
                Observation(2, "fault", 200, bytes: bytes, error: error, outcome: outcome)).Single().Outcome;

        Assert.Equal(Passed, Drip(1000, error: "The target closed the connection before the response body ended."));
        Assert.Equal(Degraded, Drip(100_000));
        Assert.Equal(Degraded, Drip(70_000, error: "The slow read did not finish within 30 s.", outcome: ObservationOutcomeType.Timeout));
        // Under 64 KB the body left the target before the first pause.
        Assert.Equal(Inconclusive, Drip(2000));
        Assert.Equal(NotApplicable, Drip(0));
    }

    [Fact]
    public void A_dependency_failure_yields_isolation_retry_and_fallback_findings()
    {
        (ScoringCategory, ResilienceFindingOutcome)[] Down(int? status, params long[] calls) =>
            Faulted(ScenarioType.DnsFailure, new { dependency = "unreachable" },
                    Observation(2, "fault", status, calls: calls, outcome: status is null ? ObservationOutcomeType.Timeout : ObservationOutcomeType.Forwarded))
                .Select(finding => (finding.Category, finding.Outcome)).ToArray();

        // A controlled 503 after three spaced calls: contained and retried well, but nothing offered in its place.
        Assert.Equal(
            [(ScoringCategory.DependencyFailure, Passed), (ScoringCategory.Retry, Passed), (ScoringCategory.GracefulDegradation, Degraded)],
            Down(503, 0, 200, 600));
        // One call is no retry to judge; a 2xx is fallback-like.
        Assert.Equal(
            [(ScoringCategory.DependencyFailure, Passed), (ScoringCategory.Retry, Inconclusive), (ScoringCategory.GracefulDegradation, Passed)],
            Down(200, 0));
        // A plain 500 after retries without a pause.
        Assert.Equal(
            [(ScoringCategory.DependencyFailure, Degraded), (ScoringCategory.Retry, Degraded), (ScoringCategory.GracefulDegradation, Failed)],
            Down(500, 0, 10, 20));
        // No answer, and more than five calls: a retry storm.
        Assert.Equal(
            [(ScoringCategory.DependencyFailure, Failed), (ScoringCategory.Retry, Failed), (ScoringCategory.GracefulDegradation, Failed)],
            Down(null, 0, 200, 400, 600, 800, 1000));
    }

    [Fact]
    public void Several_requests_while_the_dependency_is_down_show_whether_a_circuit_breaker_opens_and_closes()
    {
        // One call log per request sent while the dependency was down, an empty one meaning no call; then a recovery
        // request the open breaker turned away, and the last one, which is judged.
        IReadOnlyList<Evaluator.Finding> Findings(int recovery, params long[][] logs)
        {
            List<HttpObservation> observations = [Observation(1, "baseline", 200)];
            observations.AddRange(logs.Select((calls, index) => Observation(index + 2, "fault", 503, calls: calls)));
            observations.Add(Observation(logs.Length + 2, "recovery", 503));
            observations.Add(Observation(logs.Length + 3, "recovery", recovery));
            return Evaluator.Evaluate(ScenarioType.DnsFailure, Parameters(new { dependency = "unreachable", requests = logs.Length }),
                [], observations);
        }
        (ResilienceFindingOutcome, Evaluator.Problem?)? Breaker(int recovery, params long[][] logs)
        {
            return Findings(recovery, logs).Where(f => f.Pattern == ResiliencePattern.CircuitBreaker)
                .Select(f =>
                {
                    Assert.Equal(ScoringCategory.DependencyFailure, f.Category);
                    return ((ResilienceFindingOutcome, Evaluator.Problem?)?)(f.Outcome, f.Problem);
                })
                .SingleOrDefault();
        }

        // Stopped calling after two failures, and answered normally once the dependency was back.
        Assert.Equal((Passed, null), Breaker(200, [0], [0], [], []));
        // Opened, but still turning requests away when the runner stopped waiting.
        Assert.Equal((Degraded, Evaluator.Problem.BreakerStuckOpen), Breaker(503, [0], [0], [], []));
        // That stuck breaker is the only finding for the unanswered recovery: isolation keeps the faulted request's verdict.
        var isolation = Findings(503, [0], [0], [], []).Single(f => f.Pattern == ResiliencePattern.DependencyFailure);
        Assert.Equal(Passed, isolation.Outcome);
        // Its Expected no longer promises the recovery the Observed shows failing.
        Assert.DoesNotContain("once it is back", isolation.Evidence.RootElement.GetProperty("expected").GetString());
        // Without a breaker opening, the same recovery still lowers isolation to Not Recovered.
        Assert.Equal(Evaluator.Problem.NotRecovered,
            Findings(503, [0], [0], [0]).Single(f => f.Pattern == ResiliencePattern.DependencyFailure).Problem);
        // Called on every request: a breaker may open after more failures, so this proves nothing.
        Assert.Equal((Inconclusive, null), Breaker(200, [0], [0], [0]));
        // One request, as stored before `requests` existed: no breaker finding at all.
        Assert.Null(Breaker(200, [0]));

        // The isolation finding says which recovery request it judged: the last of several, or the only one.
        string Isolation(params HttpObservation[] observations) => Evaluator.Evaluate(ScenarioType.DnsFailure,
                Parameters(new { dependency = "unreachable" }), [], observations)
            .Single(f => f.Pattern == ResiliencePattern.DependencyFailure).Evidence.RootElement.GetProperty("observed").GetString()!;
        Assert.Contains("The last request after it: ", Isolation(Observation(1, "baseline", 200),
            Observation(2, "fault", 503, calls: [0]), Observation(3, "recovery", 503), Observation(4, "recovery", 200)));
        Assert.Contains("The request after it: ", Isolation(Observation(1, "baseline", 200),
            Observation(2, "fault", 503, calls: [0]), Observation(3, "recovery", 200)));
    }

    [Fact]
    public void A_fault_only_the_runner_sees_never_passes()
    {
        ResilienceFindingOutcome Seen(ScenarioType type, object parameters, int status = 200, long bytes = 2, string contentType = "application/json") =>
            Faulted(type, parameters, Observation(2, "fault", status, bytes: bytes, contentType: contentType)).Single().Outcome;
        var storm = new { status = 503, count = 1, probability = 1.0 };

        Assert.Equal(Inconclusive, Seen(ScenarioType.ServerErrorStorm, storm));
        Assert.Equal(Inconclusive, Seen(ScenarioType.Throttle429, new { retryAfterSeconds = 2 }));
        Assert.Equal(Inconclusive, Seen(ScenarioType.MalformedJson, new { variant = "truncated" }));
        Assert.Equal(Inconclusive, Seen(ScenarioType.EmptyResponse, new { }));
        // The target's own answer broke: that was observed.
        Assert.Equal(Failed, Seen(ScenarioType.ServerErrorStorm, storm, status: 500));
        // Nothing for the fault to change.
        Assert.Equal(NotApplicable, Seen(ScenarioType.EmptyResponse, new { }, bytes: 0));
        Assert.Equal(NotApplicable, Seen(ScenarioType.MalformedJson, new { variant = "truncated" }, contentType: "text/plain"));
        // Saying so changes nothing: the runner's path is the default.
        Assert.Equal(Inconclusive, Seen(ScenarioType.Throttle429, new { retryAfterSeconds = 2, path = "runner" }));
    }

    [Fact]
    public void A_storm_in_the_dependency_path_yields_retry_and_fallback_findings()
    {
        (ScoringCategory, ResilienceFindingOutcome)[] Storm(int? status, params long[] calls) =>
            Faulted(ScenarioType.ServerErrorStorm, new { status = 503, count = 2, probability = 1.0, path = "dependency" },
                    Observation(2, "fault", status, calls: calls, outcome: status is null ? ObservationOutcomeType.Timeout : ObservationOutcomeType.Forwarded))
                .Select(finding => (finding.Category, finding.Outcome)).ToArray();

        // Retried with pauses until the dependency answered, then answered normally.
        Assert.Equal([(ScoringCategory.Retry, Passed), (ScoringCategory.GracefulDegradation, Passed)], Storm(200, 0, 200, 400));
        Assert.Equal([(ScoringCategory.Retry, Degraded), (ScoringCategory.GracefulDegradation, Degraded)], Storm(503, 0, 10, 20));
        Assert.Equal([(ScoringCategory.Retry, Failed), (ScoringCategory.GracefulDegradation, Failed)], Storm(null, 0, 200, 400, 600, 800, 1000));
        // Every probability draw missed, so the dependency never failed and there is nothing to judge.
        Assert.Equal([(ScoringCategory.Retry, NotApplicable), (ScoringCategory.GracefulDegradation, NotApplicable)],
            Faulted(ScenarioType.ServerErrorStorm, new { status = 503, count = 2, probability = 0.5, path = "dependency" },
                    Observation(2, "fault", 200, calls: [0], injected: [false]))
                .Select(finding => (finding.Category, finding.Outcome)));
        // A baseline that never worked decides nothing, and leaves no fallback to judge.
        Assert.Equal([(ScoringCategory.Retry, Inconclusive)], Evaluator.Evaluate(ScenarioType.ServerErrorStorm,
                Parameters(new { status = 503, count = 2, probability = 1.0, path = "dependency" }), [],
                [Observation(1, "baseline", 500), Observation(2, "fault", 200, calls: [0, 200]), Observation(3, "recovery", 500)])
            .Select(finding => (finding.Category, finding.Outcome)));
    }

    [Fact]
    public void A_throttled_dependency_must_be_left_alone_for_as_long_as_retry_after_asks()
    {
        Evaluator.Finding Throttle(int? status, params long[] calls) =>
            Faulted(ScenarioType.Throttle429, new { retryAfterSeconds = 1, path = "dependency" },
                Observation(2, "fault", status, calls: calls, injected: calls.Select((_, index) => index == 0).ToArray(),
                    outcome: status is null ? ObservationOutcomeType.Timeout : ObservationOutcomeType.Forwarded)).Single();

        var passed = Throttle(200, 0, 1000);
        Assert.Equal((ResiliencePattern.RateLimit, ScoringCategory.RateLimit, Passed), (passed.Pattern, passed.Category, passed.Outcome));
        // No second call, and a controlled answer.
        Assert.Equal(Passed, Throttle(503, 0).Outcome);
        Assert.Equal((Degraded, Evaluator.Problem.IgnoredRetryAfter), (Throttle(200, 0, 300).Outcome, Throttle(200, 0, 300).Problem));
        Assert.Equal((Failed, Evaluator.Problem.DependencyNotContained), (Throttle(500, 0).Outcome, Throttle(500, 0).Problem));
        Assert.Equal(Failed, Throttle(null, 0).Outcome);
        Assert.Equal((Failed, Evaluator.Problem.RetryStorm), (Throttle(200, 0, 1000, 2000, 3000, 4000, 5000).Outcome,
            Throttle(200, 0, 1000, 2000, 3000, 4000, 5000).Problem));
        // Waited, but did not answer normally again once the dependency was back.
        Assert.Equal(Failed, Faulted(ScenarioType.Throttle429, new { retryAfterSeconds = 1, path = "dependency" },
            Observation(2, "fault", 200, calls: [0, 1000], injected: [true, false]), recovery: 500).Single().Outcome);
    }

    [Fact]
    public void A_broken_answer_from_the_dependency_must_be_checked_by_the_target()
    {
        Evaluator.Finding Broken(ScenarioType type, int? status, bool injected = true) =>
            Faulted(type, type == ScenarioType.MalformedJson ? new { variant = "truncated", path = "dependency" } : (object)new { path = "dependency" },
                Observation(2, "fault", status, calls: [0], injected: [injected],
                    outcome: status is null ? ObservationOutcomeType.Timeout : ObservationOutcomeType.Forwarded)).Single();

        var checkedAnswer = Broken(ScenarioType.MalformedJson, 502);
        Assert.Equal((ResiliencePattern.ResponseValidation, ScoringCategory.ResponseValidation, Passed),
            (checkedAnswer.Pattern, checkedAnswer.Category, checkedAnswer.Outcome));
        Assert.Equal(Passed, Broken(ScenarioType.EmptyResponse, 200).Outcome);
        Assert.Equal((Degraded, Evaluator.Problem.DependencyResponseUnhandled),
            (Broken(ScenarioType.EmptyResponse, 400).Outcome, Broken(ScenarioType.EmptyResponse, 400).Problem));
        Assert.Equal((Failed, Evaluator.Problem.DependencyResponseUnhandled),
            (Broken(ScenarioType.MalformedJson, 500).Outcome, Broken(ScenarioType.MalformedJson, 500).Problem));
        Assert.Equal(Failed, Broken(ScenarioType.MalformedJson, null).Outcome);
        // The dependency's answer was already empty: the fault changed nothing.
        Assert.Equal(NotApplicable, Broken(ScenarioType.EmptyResponse, 200, injected: false).Outcome);
    }

    private static ResilienceFindingOutcome? Latency(HttpObservation? baseline, HttpObservation fault, params Invariant[] invariants) =>
        Evaluator.Evaluate(ScenarioType.Latency, Parameters(new { delayMs = 3000 }), invariants,
            baseline is null ? [fault] : [baseline, fault]).SingleOrDefault()?.Outcome;

    [Fact]
    public void Compares_the_faulted_request_with_the_baseline()
    {
        var baseline = Observation(1, "baseline", 200);
        Assert.Equal(ResilienceFindingOutcome.Passed, Latency(baseline, Observation(2, "fault", 200)));
        Assert.Equal(ResilienceFindingOutcome.Failed, Latency(baseline, Observation(2, "fault", 503)));
        Assert.Equal(ResilienceFindingOutcome.Failed,
            Latency(baseline, Observation(2, "fault", null, outcome: ObservationOutcomeType.Timeout)));
        // Slower than three times the baseline and by more than a second.
        Assert.Equal(ResilienceFindingOutcome.Degraded, Latency(baseline, Observation(2, "fault", 200, durationMs: 1500)));
        Assert.Equal(ResilienceFindingOutcome.Degraded, Latency(baseline, Observation(2, "fault", 200, error: "cut")));
        // A broken baseline, or a rejected request with no baseline, proves nothing about the fault.
        Assert.Equal(ResilienceFindingOutcome.Inconclusive, Latency(Observation(1, "baseline", 500), Observation(2, "fault", 500)));
        Assert.Equal(ResilienceFindingOutcome.Inconclusive, Latency(null, Observation(1, "fault", 404)));
    }

    [Fact]
    public void Names_the_problem_behind_a_failed_or_degraded_finding_and_no_other()
    {
        Evaluator.Problem?[] Problems(ScenarioType type, object parameters, HttpObservation fault, int recovery = 200) =>
            Faulted(type, parameters, fault, recovery).Select(finding => finding.Problem).ToArray();
        var auth = new { credential = "invalid" };
        var down = new { dependency = "unreachable" };

        Assert.Equal([Evaluator.Problem.AnsweredWithoutCredential], Problems(ScenarioType.AuthExpiry, auth, Observation(2, "fault", 200)));
        Assert.Equal([Evaluator.Problem.AuthWrongStatus], Problems(ScenarioType.AuthExpiry, auth, Observation(2, "fault", 404)));
        // Rejected correctly, but the request after it failed: the problem is the missing recovery.
        Assert.Equal([Evaluator.Problem.NotRecovered], Problems(ScenarioType.AuthExpiry, auth, Observation(2, "fault", 401), recovery: 500));
        // One category, different problems: a retry storm and retries without a pause are both Retry findings.
        Assert.Equal([Evaluator.Problem.DependencyNotContained, Evaluator.Problem.RetryStorm, Evaluator.Problem.NoFallback],
            Problems(ScenarioType.DnsFailure, down,
                Observation(2, "fault", null, calls: [0, 200, 400, 600, 800, 1000], outcome: ObservationOutcomeType.Timeout)));
        Assert.Equal([Evaluator.Problem.DependencyNotContained, Evaluator.Problem.RetryWithoutPause, Evaluator.Problem.NoFallback],
            Problems(ScenarioType.DnsFailure, down, Observation(2, "fault", 500, calls: [0, 10, 20])));
        // So is a 5xx Storm during which the target itself failed, and that is no retry problem at all.
        Assert.Equal([Evaluator.Problem.FailedWithoutFault],
            Problems(ScenarioType.ServerErrorStorm, new { status = 503, count = 1, probability = 1.0 }, Observation(2, "fault", 500)));
        // A broken rule about duplicates gets the idempotency advice.
        var count = new Invariant(InvariantType.MaxRecordsAfterDuplicates, new(Max: 1), new("GET", "/orders"));
        Assert.Equal(Evaluator.Problem.DuplicateSideEffects, Evaluator.Evaluate(
            ScenarioType.DuplicateRequest, Parameters(new { copies = 2, key = "same", delayMs = 0 }), [count],
            [Observation(1, "fault", 201), Observation(2, "fault", 201), Observation(3, "readBack", 200, body: "{\"data\":[{},{}]}", invariant: 0)])
            .Single().Problem);
        // Passed and Inconclusive findings name none.
        Assert.Equal([null], Problems(ScenarioType.AuthExpiry, auth, Observation(2, "fault", 401)));
        Assert.Equal([null, null, null], Problems(ScenarioType.DnsFailure, down, Observation(2, "fault", 200, calls: [0])));
    }

    // A problem added later must be given a rule, or its findings would silently get no recommendation.
    [Fact]
    public void Every_problem_has_a_recommendation()
    {
        Assert.All(Enum.GetValues<Evaluator.Problem>(), problem =>
        {
            var advice = Recommender.For(problem);
            Assert.All(new[] { advice.Title, advice.Why, advice.Fix }, text => Assert.False(string.IsNullOrWhiteSpace(text)));
            // The Title column's limit.
            Assert.InRange(advice.Title.Length, 1, 500);
        });
    }

    [Fact]
    public void Words_findings_as_observation_suggestion_and_what_is_not_proven()
    {
        var finding = Evaluator.Evaluate(ScenarioType.TimeoutDrop, Parameters(new { mode = "drop" }), [],
        [
            Observation(1, "baseline", 200), Observation(2, "fault", null, outcome: ObservationOutcomeType.Dropped),
            Observation(3, "recovery", 500)
        ]).Single();
        Assert.Equal((ResiliencePattern.Timeout, ResilienceFindingOutcome.Failed, RecommendationSeverity.High),
            (finding.Pattern, finding.Outcome, finding.Severity));
        var interpretation = finding.Evidence.RootElement.GetProperty("interpretation");
        Assert.Contains("500", interpretation.GetProperty("observed").GetString());
        Assert.False(string.IsNullOrEmpty(interpretation.GetProperty("suggests").GetString()));
        Assert.False(string.IsNullOrEmpty(interpretation.GetProperty("notProven").GetString()));
        Assert.Equal(3, finding.Evidence.RootElement.GetProperty("facts").GetArrayLength());
    }

    [Fact]
    public void Judges_duplicates_by_how_many_copies_were_accepted()
    {
        ResilienceFindingOutcome? Duplicates(string key, params int?[] statuses) => Evaluator.Evaluate(
            ScenarioType.DuplicateRequest, Parameters(new { copies = statuses.Length, key, delayMs = 0 }), [],
            statuses.Select((status, index) => Observation(index + 1, "fault", status)).ToList()).SingleOrDefault()?.Outcome;

        Assert.Equal(ResilienceFindingOutcome.Passed, Duplicates("same", 201, 409));
        // Both accepted: one record or two? The responses don't say.
        Assert.Equal(ResilienceFindingOutcome.Inconclusive, Duplicates("same", 201, 201));
        Assert.Equal(ResilienceFindingOutcome.Failed, Duplicates("same", 201, 500));
        Assert.Equal(ResilienceFindingOutcome.Inconclusive, Duplicates("none", 400, 400));
        Assert.Equal(ResilienceFindingOutcome.Passed, Duplicates("different", 201, 201));
        Assert.Equal(ResilienceFindingOutcome.Degraded, Duplicates("different", 201, 409));
    }

    [Fact]
    public void A_held_record_count_decides_accepted_duplicates_and_a_violated_invariant_fails()
    {
        var count = new Invariant(InvariantType.MaxRecordsAfterDuplicates, new(Max: 1), new("GET", "/orders"));
        ResilienceFindingOutcome? After(string readBackBody) => Evaluator.Evaluate(
            ScenarioType.DuplicateRequest, Parameters(new { copies = 2, key = "same", delayMs = 0 }), [count],
            [Observation(1, "fault", 201), Observation(2, "fault", 201), Observation(3, "readBack", 200, body: readBackBody, invariant: 0)]).SingleOrDefault()?.Outcome;

        Assert.Equal(ResilienceFindingOutcome.Passed, After("[{}]"));
        Assert.Equal(ResilienceFindingOutcome.Failed, After("{\"data\":[{},{}]}"));
        // Not a list: the invariant is not evaluated, so the finding stays where the responses left it.
        Assert.Equal(ResilienceFindingOutcome.Inconclusive, After("{\"total\":1}"));
    }

    [Theory]
    [InlineData(InvariantType.StatusCodeIn, true)]
    [InlineData(InvariantType.FieldEquals, true)]
    [InlineData(InvariantType.FieldNotBelow, true)]
    [InlineData(InvariantType.MaxDurationMs, true)]
    [InlineData(InvariantType.HeaderPresent, true)]
    [InlineData(InvariantType.StatusCodeIn, false)]
    [InlineData(InvariantType.FieldEquals, false)]
    [InlineData(InvariantType.FieldNotBelow, false)]
    [InlineData(InvariantType.MaxDurationMs, false)]
    [InlineData(InvariantType.HeaderPresent, false)]
    public void Checks_each_invariant_type_on_the_faulted_response(InvariantType type, bool holds)
    {
        var parameters = type switch
        {
            InvariantType.StatusCodeIn => new InvariantParams(Codes: [holds ? 200 : 204]),
            InvariantType.FieldEquals => new InvariantParams(Path: "$.order.status", Value: holds ? "paid" : "void"),
            InvariantType.FieldNotBelow => new InvariantParams(Path: "$.order.stock", Min: holds ? 0 : 5),
            InvariantType.MaxDurationMs => new InvariantParams(Ms: holds ? 100 : 10),
            _ => new InvariantParams(Name: holds ? "etag" : "Retry-After")
        };
        var fault = Observation(2, "fault", 200, body: "{\"order\":{\"status\":\"paid\",\"stock\":2}}");

        Assert.Equal(holds ? ResilienceFindingOutcome.Passed : ResilienceFindingOutcome.Failed,
            Latency(Observation(1, "baseline", 200), fault, new Invariant(type, parameters, null)));
    }

    [Fact]
    public void An_invariant_without_a_read_back_is_not_evaluated_on_a_timeout_or_drop()
    {
        // The runner ended the faulted request itself: its missing status must not count as a violation.
        var finding = Evaluator.Evaluate(ScenarioType.TimeoutDrop, Parameters(new { mode = "drop" }),
            [new Invariant(InvariantType.StatusCodeIn, new(Codes: [200]), null)],
            [
                Observation(1, "baseline", 200), Observation(2, "fault", null, outcome: ObservationOutcomeType.Dropped),
                Observation(3, "recovery", 200)
            ]).Single();
        Assert.Equal(ResilienceFindingOutcome.Passed, finding.Outcome);
        Assert.Equal("NotEvaluated", finding.Evidence.RootElement.GetProperty("invariants")[0].GetProperty("result").GetString());
    }

    [Fact]
    public void Fills_read_back_templates_from_the_request_body_or_gives_up()
    {
        var body = "{\"reference\":\"a b\",\"order\":{\"id\":7}}"u8.ToArray();
        Assert.Equal("/payments?reference=a%20b&order=7",
            Evaluator.ResolveReadBack("/payments?reference={{request.body.reference}}&order={{ request.body.order.id }}", body));
        Assert.Equal("/payments", Evaluator.ResolveReadBack("/payments", null));
        Assert.Null(Evaluator.ResolveReadBack("/payments?reference={{request.body.missing}}", body));
        Assert.Null(Evaluator.ResolveReadBack("/payments/{{response.body.id}}", body));
    }
}
