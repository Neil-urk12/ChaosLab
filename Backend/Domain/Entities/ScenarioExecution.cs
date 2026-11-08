using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ScenarioExecution
{
    // Keys
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public Guid EndpointId { get; set; }
    public Guid ScenarioId { get; set; }

    // Table properties
    public int SequenceNo { get; set; }
    // Execution-time snapshots preserve historical TestRuns when a generated scenario is edited or regenerated.
    public ScenarioType ScenarioType { get; set; }
    public JsonDocument Parameters { get; set; } = null!;
    public JsonDocument Invariants { get; set; } = JsonDocument.Parse("[]");
    public ScenarioExecutionStatus Status { get; set; } = ScenarioExecutionStatus.Queued;
    public ScenarioOutcome? Outcome { get; set; }
    public decimal? Score { get; set; }
    public string? FailureReason { get; set; }

    // Metadata
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public TestRun TestRun { get; set; } = null!;
    public ChaosScenario Scenario { get; set; } = null!;
    public ApiEndpoint Endpoint { get; set; } = null!;
    public ICollection<HttpObservation> Observations { get; set; } = [];
}
