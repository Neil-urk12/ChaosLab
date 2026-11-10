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
    // Who removed the member; null while active and for removals recorded before this was tracked.
    public Guid? RemovedById { get; set; }

    // Navigation properties
    public Project Project { get; set; } = null!;
    public User User { get; set; } = null!;
    public User? RemovedBy { get; set; }

    // Methods
    // Re-adding a removed member updates the existing (ProjectId, UserId) record.
    public void Reactivate(ProjectRole role)
    {
        Role = role;
        RemovedAt = null;
        RemovedById = null;
    }
}
