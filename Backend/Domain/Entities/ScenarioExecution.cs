using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ScenarioExecution
{
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public TestRun TestRun { get; set; } = null!;
    public Guid EndpointId { get; set; }
    public Guid ScenarioId { get; set; }
    public ChaosScenario Scenario { get; set; } = null!;
    public ApiEndpoint Endpoint { get; set; } = null!;
    public int SequenceNo { get; set; }
    // Execution-time snapshots preserve historical TestRuns when a generated scenario is edited or regenerated.
    public ScenarioType ScenarioType { get; set; }
    public JsonDocument Parameters { get; set; } = null!;
    public string? DeveloperInvariant { get; set; }
    public ScenarioExecutionStatus Status { get; set; } = ScenarioExecutionStatus.Queued;
    public ScenarioOutcome? Outcome { get; set; }
    public decimal? Score { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<HttpObservation> Observations { get; set; } = [];
}
