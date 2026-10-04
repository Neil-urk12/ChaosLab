
using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class HttpObservation
{
    public Guid Id { get; set; }
    public Guid ScenarioExecutionId { get; set; }
    public ScenarioExecution ScenarioExecution { get; set; } = null!;
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
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
