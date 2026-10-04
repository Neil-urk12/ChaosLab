
using System.Text.Json;

namespace Backend.Domain.Entities;

public class EndpointResult
{
    // Keys
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public Guid EndpointId { get; set; }

    // Table properties
    public decimal? RetryScore { get; set; }
    public decimal? TimeoutScore { get; set; }
    public decimal? CircuitBreakerScore { get; set; }
    public decimal? FallbackScore { get; set; }
    public decimal? IdempotencyScore { get; set; }
    public decimal OverallScore { get; set; }
    public string? Grade { get; set; }
    public JsonDocument Evidence { get; set; } = null!;

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public TestRun TestRun { get; set; } = null!;
    public ApiEndpoint Endpoint { get; set; } = null!;
}
