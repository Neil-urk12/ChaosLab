namespace Backend.Domain.Entities;

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }

    public ICollection<ProjectMember> Members { get; set; } = [];
    public ICollection<ApiSpec> ApiSpecs { get; set; } = [];
    public ICollection<ApiEnvironment> Environments { get; set; } = [];
    public ICollection<TestRun> TestRuns { get; set; } = [];
}
