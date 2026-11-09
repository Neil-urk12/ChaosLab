namespace Backend.Domain.Enums;

public enum ResilienceFindingOutcome
{
    Passed,
    Failed,
    Inconclusive,
    NotApplicable,
    // Appended last so the stored integer values of the other outcomes don't change.
    Degraded
}
