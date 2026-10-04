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

    // Metadata
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Project Project { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public ICollection<AuthProfile> AuthProfiles { get; set; } = [];
    public ICollection<TestRun> TestRuns { get; set; } = [];
}
