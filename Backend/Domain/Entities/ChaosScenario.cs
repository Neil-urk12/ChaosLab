using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ChaosScenario
{
    // Keys
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }

    // Table properties
    public ScenarioType ScenarioType { get; set; }
    public JsonDocument Parameters { get; set; } = null!;
    public bool IsDefaultEnabled { get; set; } = true;
    // Why the generator chose this fault for the endpoint, shown to users.
    public string Reason { get; set; } = string.Empty;
    // Structured assertions: a JSON array of { "type", "params", "readBack"? } objects.
    public JsonDocument Invariants { get; set; } = JsonDocument.Parse("[]");

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ApiEndpoint Endpoint { get; set; } = null!;
    public ICollection<ScenarioExecution> ScenarioExecutions { get; set; } = [];
}
