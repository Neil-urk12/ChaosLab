using Backend.Domain.Enums;
using Backend.Features.TestRuns;
using static Backend.Domain.Enums.ResilienceFindingOutcome;

namespace Backend.Tests.Integration;

// The scorer is pure, so these need no database: findings in, scores out.
public sealed class ScorerTests
{
    private static Scorer.Result Score(params (ScoringCategory? Category, ResilienceFindingOutcome Outcome)[] findings) =>
        Scorer.Score(findings);

    private static Scorer.CategoryScore Category(Scorer.Result result, ScoringCategory category) =>
        result.Categories.Single(score => score.Category == category);

    [Fact]
    public void Scores_categories_by_mean_points_and_shares_out_the_weight_of_empty_ones()
    {
        var result = Score(
            (ScoringCategory.Timeout, Passed), (ScoringCategory.Timeout, Passed), (ScoringCategory.Timeout, Degraded),
            (ScoringCategory.Timeout, Failed), (ScoringCategory.Timeout, NotApplicable),
            (ScoringCategory.Idempotency, Passed), (ScoringCategory.Idempotency, Failed), (ScoringCategory.Idempotency, Inconclusive));

        Assert.Equal(new Scorer.CategoryScore(ScoringCategory.Timeout, 20, 62.5m, 5, 0), Category(result, ScoringCategory.Timeout));
        Assert.Equal(new Scorer.CategoryScore(ScoringCategory.Idempotency, 10, 50m, 3, 1), Category(result, ScoringCategory.Idempotency));
        Assert.Equal(new Scorer.CategoryScore(ScoringCategory.Retry, 15, null, 0, 0), Category(result, ScoringCategory.Retry));
        // The locked weights of version 1.0, in order.
        Assert.Equal([20, 15, 15, 10, 10, 10, 10, 10], result.Categories.Select(score => score.Weight));
        // (62.5 × 20 + 50 × 10) over the 30 points of weight that were scored.
        Assert.Equal(58.33m, result.Overall);
        // Six scored out of seven; Not Applicable does not count.
        Assert.Equal(85.71m, result.Coverage);
    }

    [Fact]
    public void Leaves_the_score_empty_when_nothing_was_scored()
    {
        Assert.Equal((null, null), (Score().Overall, Score().Coverage));
        Assert.Equal((null, null), (Score((ScoringCategory.Retry, NotApplicable)).Overall, Score((ScoringCategory.Retry, NotApplicable)).Coverage));
        var inconclusive = Score((ScoringCategory.Retry, Inconclusive));
        Assert.Equal((null, 0m), (inconclusive.Overall, inconclusive.Coverage));
        Assert.Equal((100m, 100m), (Score((ScoringCategory.Retry, Passed)).Overall, Score((ScoringCategory.Retry, Passed)).Coverage));
        Assert.Equal(0m, Score((ScoringCategory.Retry, Failed)).Overall);
    }

    [Theory]
    [InlineData(ScenarioType.Latency, ScoringCategory.Timeout)]
    [InlineData(ScenarioType.TimeoutDrop, ScoringCategory.Timeout)]
    [InlineData(ScenarioType.SlowDrip, ScoringCategory.Timeout)]
    [InlineData(ScenarioType.ServerErrorStorm, ScoringCategory.Retry)]
    [InlineData(ScenarioType.Throttle429, ScoringCategory.RateLimit)]
    [InlineData(ScenarioType.MalformedJson, ScoringCategory.ResponseValidation)]
    [InlineData(ScenarioType.EmptyResponse, ScoringCategory.ResponseValidation)]
    [InlineData(ScenarioType.AuthExpiry, ScoringCategory.AuthenticationFailure)]
    [InlineData(ScenarioType.DnsFailure, ScoringCategory.DependencyFailure)]
    [InlineData(ScenarioType.DuplicateRequest, ScoringCategory.Idempotency)]
    public void Takes_the_category_from_the_scenarios_fault(ScenarioType type, ScoringCategory category) =>
        // The pattern is ignored once the fault is known.
        Assert.Equal(category, Scorer.CategoryOf(type, ResiliencePattern.Fallback));

    // A scenario type added later must be given a category here, or it would silently go unscored.
    [Fact]
    public void Only_baseline_is_left_without_a_category() =>
        Assert.Equal([ScenarioType.Baseline],
            Enum.GetValues<ScenarioType>().Where(type => Scorer.CategoryOf(type, ResiliencePattern.Timeout) is null));

    [Fact]
    public void Falls_back_to_the_pattern_for_a_finding_without_a_scenario()
    {
        Assert.Null(Scorer.CategoryOf(ScenarioType.Baseline, ResiliencePattern.Timeout));
        Assert.Equal(ScoringCategory.Retry, Scorer.CategoryOf(null, ResiliencePattern.Retry));
        Assert.Equal(ScoringCategory.GracefulDegradation, Scorer.CategoryOf(null, ResiliencePattern.CircuitBreaker));
        Assert.Equal(ScoringCategory.GracefulDegradation, Scorer.CategoryOf(null, ResiliencePattern.Fallback));
    }
}
