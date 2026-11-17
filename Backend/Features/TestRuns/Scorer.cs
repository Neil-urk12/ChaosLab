using Backend.Domain.Enums;

namespace Backend.Features.TestRuns;

// Turns findings into the Resilience Score (FeaturesFlow 2.19, PROJECT.md 14.3). Pure: no database.
// The same method scores a whole run or one endpoint; the caller chooses which findings to pass.
// The only copy of the method: pages show what this stored (GetRun, GetProject) and do not score findings themselves.
public static class Scorer
{
    // Locked for scoring version 1.0, like the points below: changing either needs a new scoring version.
    private static readonly (ScoringCategory Category, int Weight)[] Weights =
    [
        (ScoringCategory.Timeout, 20),
        (ScoringCategory.Retry, 15),
        (ScoringCategory.GracefulDegradation, 15),
        (ScoringCategory.RateLimit, 10),
        (ScoringCategory.Idempotency, 10),
        (ScoringCategory.AuthenticationFailure, 10),
        (ScoringCategory.ResponseValidation, 10),
        (ScoringCategory.DependencyFailure, 10)
    ];

    // Score is null when the category has no scored findings. Findings counts every finding in the category.
    public sealed record CategoryScore(ScoringCategory Category, int Weight, decimal? Score, int Findings, int Inconclusive);

    // Overall is null when nothing was scored; Coverage is null when there is no finding that counts toward it.
    public sealed record Result(decimal? Overall, decimal? Coverage, IReadOnlyList<CategoryScore> Categories);

    // Only for findings saved before the category was stored on the finding itself (ResilienceFinding.Category).
    public static ScoringCategory? CategoryOf(ScenarioType? type, ResiliencePattern pattern) => type switch
    {
        ScenarioType.Latency or ScenarioType.TimeoutDrop or ScenarioType.SlowDrip => ScoringCategory.Timeout,
        ScenarioType.ServerErrorStorm => ScoringCategory.Retry,
        ScenarioType.Throttle429 => ScoringCategory.RateLimit,
        ScenarioType.MalformedJson or ScenarioType.EmptyResponse => ScoringCategory.ResponseValidation,
        ScenarioType.AuthExpiry => ScoringCategory.AuthenticationFailure,
        ScenarioType.DnsFailure => ScoringCategory.DependencyFailure,
        ScenarioType.DuplicateRequest => ScoringCategory.Idempotency,
        // A finding without a scenario execution falls back to its pattern.
        null => pattern switch
        {
            ResiliencePattern.Timeout => ScoringCategory.Timeout,
            ResiliencePattern.Retry => ScoringCategory.Retry,
            ResiliencePattern.Idempotency => ScoringCategory.Idempotency,
            _ => ScoringCategory.GracefulDegradation
        },
        // Baseline is not a fault.
        _ => null
    };

    // Inconclusive and Not Applicable are not scored.
    private static decimal? Points(ResilienceFindingOutcome outcome) => outcome switch
    {
        ResilienceFindingOutcome.Passed => 100,
        ResilienceFindingOutcome.Degraded => 50,
        ResilienceFindingOutcome.Failed => 0,
        _ => null
    };

    public static Result Score(IReadOnlyCollection<(ScoringCategory? Category, ResilienceFindingOutcome Outcome)> findings)
    {
        var categories = Weights.Select(entry =>
        {
            var mine = findings.Where(finding => finding.Category == entry.Category).ToList();
            var points = mine.Select(finding => Points(finding.Outcome)).OfType<decimal>().ToList();
            return (entry.Category, entry.Weight, Mean: points.Count == 0 ? (decimal?)null : points.Average(),
                Findings: mine.Count, Inconclusive: mine.Count(finding => finding.Outcome == ResilienceFindingOutcome.Inconclusive));
        }).ToList();

        // A category with no scored findings is left out, which shares its weight out proportionally among the rest.
        var scoredCategories = categories.Where(category => category.Mean is not null).ToList();
        var weight = scoredCategories.Sum(category => category.Weight);
        var scored = findings.Count(finding => Points(finding.Outcome) is not null);
        // Not Applicable counts toward neither the score nor coverage.
        var counted = scored + findings.Count(finding => finding.Outcome == ResilienceFindingOutcome.Inconclusive);

        return new Result(
            weight == 0 ? null : Round(scoredCategories.Sum(category => category.Mean!.Value * category.Weight) / weight),
            counted == 0 ? null : Round(100m * scored / counted),
            categories.Select(category => new CategoryScore(category.Category, category.Weight,
                category.Mean is { } mean ? Round(mean) : null, category.Findings, category.Inconclusive)).ToList());
    }

    // Score columns are numeric(5,2).
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
