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
    public string? DeveloperInvariant { get; set; }

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ApiEndpoint Endpoint { get; set; } = null!;
    public ICollection<ScenarioExecution> ScenarioExecutions { get; set; } = [];
}
