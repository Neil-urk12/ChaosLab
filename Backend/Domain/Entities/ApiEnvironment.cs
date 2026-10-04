namespace Backend.Domain.Entities;

public class ApiEnvironment
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool VerifyTls { get; set; } = true;
    public bool IsDefault { get; set; }
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<AuthProfile> AuthProfiles { get; set; } = [];
    public ICollection<TestRun> TestRuns { get; set; } = [];
}
