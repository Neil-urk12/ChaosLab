using System.Text.Json;

namespace Backend.Domain.Entities;

public class ApiEndpoint
{
    // Keys
    public Guid Id { get; set; }
    public Guid ApiSpecId { get; set; }

    // Table properties
    public string Path { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    // Optional OpenAPI label; ApiSpecId + Path + HttpMethod remains the canonical identity.
    public string? OperationId { get; set; }
    public JsonDocument Tags { get; set; } = null!;
    public JsonDocument Parameters { get; set; } = null!;
    public JsonDocument? RequestBodySchema { get; set; }
    public JsonDocument Responses { get; set; } = null!;
    public JsonDocument? SecurityRequirements { get; set; }
    public bool IsDeprecated { get; set; }

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ApiSpec ApiSpec { get; set; } = null!;
    public ICollection<ChaosScenario> ChaosScenarios { get; set; } = [];
    public ICollection<ScenarioExecution> ScenarioExecutions { get; set; } = [];
    public ICollection<ResilienceFinding> ResilienceFindings { get; set; } = [];
    public ICollection<EndpointResult> Results { get; set; } = [];
}
