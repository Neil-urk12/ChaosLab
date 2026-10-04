using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class Recommendation
{
    // Keys
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public Guid? EndpointId { get; set; }
    public Guid? ScenarioExecutionId { get; set; }
    public Guid? ResilienceFindingId { get; set; }

    // Table properties
    public string Category { get; set; } = string.Empty;
    public RecommendationSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Remediation { get; set; }
    public JsonDocument Evidence { get; set; } = null!;

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public TestRun TestRun { get; set; } = null!;
    public ApiEndpoint? Endpoint { get; set; }
    public ScenarioExecution? ScenarioExecution { get; set; }
    public ResilienceFinding? ResilienceFinding { get; set; }
}
