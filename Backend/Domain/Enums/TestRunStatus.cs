
namespace Backend.Domain.Enums;

public enum TestRunStatus
{
    Queued,
    Running,
    Completed,
    PartiallyCompleted,
    Failed,
    Cancelled
}
