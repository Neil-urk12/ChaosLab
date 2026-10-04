using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ResilienceFinding
{
    // Keys
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public Guid EndpointId { get; set; }
    public Guid? ScenarioExecutionId { get; set; }

    // Table properties
    public ResiliencePattern Pattern { get; set; }
    public ResilienceFindingOutcome Outcome { get; set; } = ResilienceFindingOutcome.Inconclusive;
    public string Summary { get; set; } = string.Empty;
    public JsonDocument Evidence { get; set; } = null!;

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public TestRun TestRun { get; set; } = null!;
    public ApiEndpoint Endpoint { get; set; } = null!;
    public ScenarioExecution? ScenarioExecution { get; set; }
}
