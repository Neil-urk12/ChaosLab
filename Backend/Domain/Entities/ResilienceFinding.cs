using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ResilienceFinding
{
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public TestRun TestRun { get; set; } = null!;
    public Guid EndpointId { get; set; }
    public ApiEndpoint Endpoint { get; set; } = null!;
    public Guid? ScenarioExecutionId { get; set; }
    public ScenarioExecution? ScenarioExecution { get; set; }
    public ResiliencePattern Pattern { get; set; }
    public ResilienceFindingOutcome Outcome { get; set; } = ResilienceFindingOutcome.Inconclusive;
    public string Summary { get; set; } = string.Empty;
    public JsonDocument Evidence { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
