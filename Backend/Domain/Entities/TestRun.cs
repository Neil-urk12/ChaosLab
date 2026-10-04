using System.Text.Json;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class TestRun
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid ApiSpecId { get; set; }
    public ApiSpec ApiSpec { get; set; } = null!;
    public Guid EnvironmentId { get; set; }
    public ApiEnvironment Environment { get; set; } = null!;
    public Guid? AuthProfileId { get; set; }
    public AuthProfile? AuthProfile { get; set; }
    public Guid RequestedById { get; set; }
    public User RequestedBy { get; set; } = null!;
    public TestRunStatus Status { get; set; } = TestRunStatus.Queued;
    public JsonDocument RunConfig { get; set; } = null!;
    public int TotalScenarios { get; set; }
    public int CompletedScenarios { get; set; }
    public decimal? ResilienceScore { get; set; }
    public string ScoringVersion { get; set; } = "1.0";
    public string? FailureMessage { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ScenarioExecution> ScenarioExecutions { get; set; } = [];
    public ICollection<EndpointResult> EndpointResults { get; set; } = [];
    public ICollection<ResilienceFinding> ResilienceFindings { get; set; } = [];
    public ICollection<Recommendation> Recommendations { get; set; } = [];
    public ICollection<Report> Reports { get; set; } = [];
}
