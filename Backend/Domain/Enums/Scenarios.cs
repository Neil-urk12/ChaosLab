
namespace Backend.Domain.Enums;

public enum ScenarioType
{
    Baseline,
    Latency,
    ServerErrorStorm,
    Throttle429,
    TimeoutDrop,
    MalformedJson,
    EmptyResponse,
    SlowDrip,
    AuthExpiry,
    DnsFailure,
    DuplicateRequest
}

public enum ScenarioExecutionStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Skipped
}

public enum ScenarioOutcome
{
    Passed,
    Failed,
    Inconclusive
}
