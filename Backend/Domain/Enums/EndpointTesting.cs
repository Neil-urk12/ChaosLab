namespace Backend.Domain.Enums;

public enum EndpointTesting
{
    Included,
    // Skipped in every run.
    Excluded,
    // Unsafe to call; hidden from run setup entirely.
    NeverTest
}
