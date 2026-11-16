using System.Text.Json.Serialization;

namespace Backend.Domain.Enums;

// The eight scoring categories (PROJECT.md 14.2). Stored by name inside jsonb, so renaming one changes stored data,
// and as an integer on ResilienceFinding, so new values are appended.
[JsonConverter(typeof(JsonStringEnumConverter<ScoringCategory>))]
public enum ScoringCategory
{
    Timeout,
    Retry,
    GracefulDegradation,
    RateLimit,
    Idempotency,
    AuthenticationFailure,
    ResponseValidation,
    DependencyFailure
}
