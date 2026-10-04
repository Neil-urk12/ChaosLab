namespace Backend.Domain.Entities;

public class Project
{
    // Keys
    public Guid Id { get; set; }

    // Table properties
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Metadata
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }

    // Navigation properties
    public User CreatedBy { get; set; } = null!;
    public ICollection<ProjectMember> Members { get; set; } = [];
    public ICollection<ApiSpec> ApiSpecs { get; set; } = [];
    public ICollection<ApiEnvironment> Environments { get; set; } = [];
    public ICollection<TestRun> TestRuns { get; set; } = [];
}
