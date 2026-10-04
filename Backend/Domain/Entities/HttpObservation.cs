
using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class HttpObservation
{
    // Keys
    public Guid Id { get; set; }
    public Guid ScenarioExecutionId { get; set; }

    // Table properties
    public int AttemptNo { get; set; }
    public Guid? CorrelationId { get; set; }
    public string RequestMethod { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public DateTimeOffset RequestStartedAt { get; set; }
    public DateTimeOffset? RequestCompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int? UpstreamStatusCode { get; set; }
    public int? ClientStatusCode { get; set; }
    public ObservationOutcomeType OutcomeType { get; set; }
    public long? RequestBytes { get; set; }
    public long? ResponseBytes { get; set; }
    public string? ErrorType { get; set; }
    public JsonDocument Metadata { get; set; } = null!;

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ScenarioExecution ScenarioExecution { get; set; } = null!;
}
