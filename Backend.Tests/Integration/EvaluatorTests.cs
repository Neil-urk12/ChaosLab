using System.Text.Json;
using Backend.Domain.Entities;
using Backend.Domain.Enums;
using Backend.Features.Scenarios;
using Backend.Features.TestRuns;

namespace Backend.Tests.Integration;

// The evaluator is pure, so these need no database: observations in, finding out.
public sealed class EvaluatorTests
{
    private static JsonElement Parameters(object value) => JsonSerializer.SerializeToElement(value);

    private static HttpObservation Observation(
        int attempt, string phase, int? status, long durationMs = 20, string body = "{}", string? error = null,
        ObservationOutcomeType outcome = ObservationOutcomeType.Forwarded, int? invariant = null) => new()
    {
        AttemptNo = attempt, UpstreamStatusCode = status, ClientStatusCode = status, DurationMs = durationMs, ErrorType = error,
        OutcomeType = outcome,
        Metadata = JsonSerializer.SerializeToDocument(new Dictionary<string, object?>
        {
            ["phase"] = phase, ["invariant"] = invariant, ["body"] = body, ["headers"] = new Dictionary<string, string> { ["ETag"] = "1" }
        })
    };

    private static ResilienceFindingOutcome? Latency(HttpObservation? baseline, HttpObservation fault, params Invariant[] invariants) =>
        Evaluator.Evaluate(ScenarioType.Latency, Parameters(new { delayMs = 3000 }), invariants,
            baseline is null ? [fault] : [baseline, fault])?.Outcome;

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
    public void Words_findings_as_observation_suggestion_and_what_is_not_proven()
    {
        var finding = Evaluator.Evaluate(ScenarioType.TimeoutDrop, Parameters(new { mode = "drop" }), [],
        [
            Observation(1, "baseline", 200), Observation(2, "fault", null, outcome: ObservationOutcomeType.Dropped),
            Observation(3, "recovery", 500)
        ])!;
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
            statuses.Select((status, index) => Observation(index + 1, "fault", status)).ToList())?.Outcome;

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
            [Observation(1, "fault", 201), Observation(2, "fault", 201), Observation(3, "readBack", 200, body: readBackBody, invariant: 0)])?.Outcome;

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
            ])!;
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
