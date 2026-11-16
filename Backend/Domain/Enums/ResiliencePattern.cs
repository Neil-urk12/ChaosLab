namespace Backend.Domain.Enums;

public enum ResiliencePattern
{
    Retry,
    Timeout,
    CircuitBreaker,
    Fallback,
    Idempotency,
    // Appended, like every later value: patterns are stored as integers.
    RateLimit,
    AuthenticationFailure,
    ResponseValidation,
    DependencyFailure
}
