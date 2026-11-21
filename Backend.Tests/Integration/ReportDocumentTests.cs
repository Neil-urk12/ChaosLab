using System.Text.Json;
using Backend.Domain.Enums;
using Backend.Features.Findings;
using Backend.Features.Reports;
using Backend.Features.TestRuns;

namespace Backend.Tests.Integration;

// Renders the sections a real run in ReportEndpointTests does not reach (scores, endpoints, timelines, the previous run,
// runner-only faults), so a layout error in any of them fails here rather than in a user's request.
public sealed class ReportDocumentTests
{
    [Fact]
    public void Renders_a_scored_run_with_every_section()
    {
        var at = new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(1, 3).Select(attempt => new GetRun.Event(
            attempt, "GET", "/orders/1", at.AddSeconds(attempt), 120, 503, attempt == 3 ? null : 200,
            ObservationOutcomeType.ModifiedResponse, null)).ToList();
        var run = new GetRun.Response(
            Guid.NewGuid(), 12, TestRunStatus.Completed, "Staging", "Test User", at, at, at.AddMinutes(2), 2, 2, 0,
            62.5m, 80m,
            [
                new(ScoringCategory.Timeout, 20, 100m, 1, 0),
                new(ScoringCategory.Retry, 15, 0m, 1, 1),
                new(ScoringCategory.RateLimit, 10, null, 0, 0)
            ],
            [new("GET", "/orders/{id}", 50m)], "1.0", "One scenario hit a runner error.",
            new GetRun.Previous(Guid.NewGuid(), 11, 70m),
            [new(Guid.NewGuid(), "GET", "/orders/{id}", ScenarioType.ServerErrorStorm, ScenarioOutcome.Failed, null, at, events)],
            null);
        var evidence = JsonSerializer.SerializeToElement(new
        {
            expected = "The target recovers after the storm.",
            observed = "The target answered 500.",
            interpretation = new { suggests = "No recovery observed.", notProven = "How a client retries." },
            facts = new[] { new { label = "Attempt 1 · fault", value = "500 in 12 ms" } }
        });
        List<ListFindings.Row> findings =
        [
            new(Guid.NewGuid(), RecommendationSeverity.High, ResilienceFindingOutcome.Failed, ResiliencePattern.Retry,
                ScoringCategory.Retry, "GET", "/orders/{id}", ScenarioType.ServerErrorStorm, "The target failed during the storm",
                evidence, new ListFindings.RecommendationInfo("Limit retries", "Use bounded retries with backoff and jitter.")),
            // A finding saved before evidence had a fixed shape.
            new(Guid.NewGuid(), RecommendationSeverity.Info, ResilienceFindingOutcome.Passed, ResiliencePattern.Timeout,
                null, "GET", "/orders/{id}", null, "Answered normally", JsonSerializer.SerializeToElement("legacy"), null)
        ];
        var data = new ReportData("Orders & Billing", "Orders", "1.0", "3.0.3", run,
            JsonSerializer.SerializeToElement(new { concurrency = 2, target = new { baseUrl = "https://staging.example.test" } }),
            findings, [new(ScenarioExecutionStatus.Failed, null, 1)], "Test User", at);

        var pdf = ReportDocument.Render(data);

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }
}
