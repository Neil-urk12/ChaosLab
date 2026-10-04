using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ProjectRole Role { get; set; }
    // Original membership creation time; removal and reactivation do not reset it.
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }

    // Re-adding a removed member updates the existing (ProjectId, UserId) record.
    public void Reactivate(ProjectRole role)
    {
        Role = role;
        RemovedAt = null;
    }
}
