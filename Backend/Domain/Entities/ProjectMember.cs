using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ProjectMember
{
    // Keys
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }

    // Table properties
    public ProjectRole Role { get; set; }

    // Metadata
    // Original membership creation time; removal and reactivation do not reset it.
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }

    // Navigation properties
    public Project Project { get; set; } = null!;
    public User User { get; set; } = null!;

    // Methods
    // Re-adding a removed member updates the existing (ProjectId, UserId) record.
    public void Reactivate(ProjectRole role)
    {
        Role = role;
        RemovedAt = null;
    }
}
