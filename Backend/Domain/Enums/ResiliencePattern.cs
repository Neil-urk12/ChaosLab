namespace Backend.Domain.Enums;

public enum ResiliencePattern
{
    Retry,
    Timeout,
    CircuitBreaker,
    Fallback,
    Idempotency
}
