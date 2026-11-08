using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ApiEnvironment
{
    // Keys
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }

    // Table properties
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool VerifyTls { get; set; } = true;
    public bool IsDefault { get; set; }
    public EnvironmentKind Kind { get; set; } = EnvironmentKind.Test;
    // Upper bound per scenario request, so a hung target cannot stall a run.
    public int RequestTimeoutSeconds { get; set; } = 30;
    // Who confirmed they own or may test this target (and that it is not production), and when.
    public Guid? AuthorizationConfirmedById { get; set; }
    public DateTimeOffset? AuthorizationConfirmedAt { get; set; }

    // Metadata
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Project Project { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public User? AuthorizationConfirmedBy { get; set; }
    public ICollection<AuthProfile> AuthProfiles { get; set; } = [];
    public ICollection<EnvironmentHeader> Headers { get; set; } = [];
    public ICollection<TestRun> TestRuns { get; set; } = [];
}
