using System.Text.Json;
using Backend.Domain.Entities;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
public sealed class CheckConstraintTests(PostgreSqlFixture postgres) : PersistenceTest(postgres)
{
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task TestRun_score_outside_zero_to_one_hundred_is_rejected(int score)
    {
        var run = await Db.TestRuns.SingleAsync(x => x.Id == Data.Run.Id);
        run.ResilienceScore = score;
        await AssertViolationAsync(PostgresErrorCodes.CheckViolation, "CK_TestRuns_ResilienceScore");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task ScenarioExecution_score_outside_zero_to_one_hundred_is_rejected(int score)
    {
        var execution = await Db.ScenarioExecutions.SingleAsync(x => x.Id == Data.Execution.Id);
        execution.Score = score;
        await AssertViolationAsync(PostgresErrorCodes.CheckViolation, "CK_ScenarioExecutions_Score");
    }

    [Theory]
    [InlineData(nameof(EndpointResult.RetryScore), -1)]
    [InlineData(nameof(EndpointResult.RetryScore), 101)]
    [InlineData(nameof(EndpointResult.TimeoutScore), -1)]
    [InlineData(nameof(EndpointResult.TimeoutScore), 101)]
    [InlineData(nameof(EndpointResult.CircuitBreakerScore), -1)]
    [InlineData(nameof(EndpointResult.CircuitBreakerScore), 101)]
    [InlineData(nameof(EndpointResult.FallbackScore), -1)]
    [InlineData(nameof(EndpointResult.FallbackScore), 101)]
    [InlineData(nameof(EndpointResult.IdempotencyScore), -1)]
    [InlineData(nameof(EndpointResult.IdempotencyScore), 101)]
    [InlineData(nameof(EndpointResult.OverallScore), -1)]
    [InlineData(nameof(EndpointResult.OverallScore), 101)]
    public async Task Each_endpoint_result_score_enforces_zero_to_one_hundred(string property, int score)
    {
        var result = Data.NewEndpointResult();
        Db.EndpointResults.Add(result);
        Db.Entry(result).Property(property).CurrentValue = (decimal)score;
        await AssertViolationAsync(PostgresErrorCodes.CheckViolation, "CK_EndpointResults_Scores");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(null)]
    public async Task Score_boundaries_and_optional_null_scores_are_persisted(int? score)
    {
        var run = await Db.TestRuns.SingleAsync(x => x.Id == Data.Run.Id);
        var execution = await Db.ScenarioExecutions.SingleAsync(x => x.Id == Data.Execution.Id);
        run.ResilienceScore = score;
        execution.Score = score;
        var result = Data.NewEndpointResult();
        result.RetryScore = result.TimeoutScore = result.CircuitBreakerScore =
            result.FallbackScore = result.IdempotencyScore = score;
        result.OverallScore = score ?? 50;
        Db.EndpointResults.Add(result);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        Assert.Equal((decimal?)score, (await Db.TestRuns.SingleAsync(x => x.Id == run.Id)).ResilienceScore);
        Assert.Equal((decimal?)score, (await Db.ScenarioExecutions.SingleAsync(x => x.Id == execution.Id)).Score);
        var saved = await Db.EndpointResults.SingleAsync(x => x.Id == result.Id);
        Assert.Equal((decimal?)score, saved.RetryScore);
        Assert.Equal((decimal?)score, saved.TimeoutScore);
        Assert.Equal((decimal?)score, saved.CircuitBreakerScore);
        Assert.Equal((decimal?)score, saved.FallbackScore);
        Assert.Equal((decimal?)score, saved.IdempotencyScore);
        Assert.Equal((decimal)(score ?? 50), saved.OverallScore);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 2)]
    public async Task Scenario_counts_reject_negative_or_completed_above_total(int total, int completed)
    {
        var run = await Db.TestRuns.SingleAsync(x => x.Id == Data.Run.Id);
        run.TotalScenarios = total;
        run.CompletedScenarios = completed;
        await AssertViolationAsync(PostgresErrorCodes.CheckViolation, "CK_TestRuns_ScenarioCounts");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public async Task Scenario_counts_allow_nonnegative_completed_at_or_below_total(int total, int completed)
    {
        var run = await Db.TestRuns.SingleAsync(x => x.Id == Data.Run.Id);
        run.TotalScenarios = total;
        run.CompletedScenarios = completed;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var saved = await Db.TestRuns.SingleAsync(x => x.Id == run.Id);
        Assert.Equal(total, saved.TotalScenarios);
        Assert.Equal(completed, saved.CompletedScenarios);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ScenarioExecution_sequence_number_must_start_at_one(int sequence)
    {
        var execution = await Db.ScenarioExecutions.SingleAsync(x => x.Id == Data.Execution.Id);
        execution.SequenceNo = sequence;
        await AssertViolationAsync(PostgresErrorCodes.CheckViolation, "CK_ScenarioExecutions_SequenceNo");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HttpObservation_attempt_number_must_start_at_one(int attempt)
    {
        Db.HttpObservations.Add(NewObservation(attempt));
        await AssertViolationAsync(PostgresErrorCodes.CheckViolation, "CK_HttpObservations_AttemptNo");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Report_version_must_start_at_one(int version)
    {
        Db.Reports.Add(new Report { Id = Guid.NewGuid(), TestRunId = Data.Run.Id, Version = version });
        await AssertViolationAsync(PostgresErrorCodes.CheckViolation, "CK_Reports_Version");
    }

    [Fact]
    public async Task Observation_and_report_ordinals_accept_one()
    {
        Db.HttpObservations.Add(NewObservation(1));
        Db.Reports.Add(new Report { Id = Guid.NewGuid(), TestRunId = Data.Run.Id, Version = 1 });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        Assert.Equal(1, (await Db.HttpObservations.SingleAsync()).AttemptNo);
        Assert.Equal(1, (await Db.Reports.SingleAsync()).Version);
    }

    private HttpObservation NewObservation(int attempt) => new()
    {
        Id = Guid.NewGuid(), ScenarioExecutionId = Data.Execution.Id, AttemptNo = attempt,
        RequestStartedAt = DateTimeOffset.UtcNow, Metadata = JsonDocument.Parse("{}")
    };
}
