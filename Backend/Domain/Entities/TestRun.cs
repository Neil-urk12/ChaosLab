using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class TestRun
{
    // Keys
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ApiSpecId { get; set; }
    public Guid EnvironmentId { get; set; }
    public Guid? AuthProfileId { get; set; }

    // Table properties
    public TestRunStatus Status { get; set; } = TestRunStatus.Queued;
    public JsonDocument RunConfig { get; set; } = null!;
    public int TotalScenarios { get; set; }
    public int CompletedScenarios { get; set; }
    public decimal? ResilienceScore { get; set; }
    public string ScoringVersion { get; set; } = "1.0";
    public string? FailureMessage { get; set; }

    // Metadata
    public Guid RequestedById { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Project Project { get; set; } = null!;
    public ApiSpec ApiSpec { get; set; } = null!;
    public ApiEnvironment Environment { get; set; } = null!;
    public AuthProfile? AuthProfile { get; set; }
    public User RequestedBy { get; set; } = null!;
    public ICollection<ScenarioExecution> ScenarioExecutions { get; set; } = [];
    public ICollection<EndpointResult> EndpointResults { get; set; } = [];
    public ICollection<ResilienceFinding> ResilienceFindings { get; set; } = [];
    public ICollection<Recommendation> Recommendations { get; set; } = [];
    public ICollection<Report> Reports { get; set; } = [];
}
