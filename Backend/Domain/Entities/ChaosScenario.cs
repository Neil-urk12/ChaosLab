using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ChaosScenario
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }
    public ApiEndpoint Endpoint { get; set; } = null!;
    public ScenarioType ScenarioType { get; set; }
    public JsonDocument Parameters { get; set; } = null!;
    public bool IsDefaultEnabled { get; set; } = true;
    public string? DeveloperInvariant { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ScenarioExecution> ScenarioExecutions { get; set; } = [];
}
