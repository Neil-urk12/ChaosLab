
using System.Text.Json;

namespace Backend.Domain.Entities;

public class EndpointResult
{
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public TestRun TestRun { get; set; } = null!;
    public Guid EndpointId { get; set; }
    public ApiEndpoint Endpoint { get; set; } = null!;
    public decimal? RetryScore { get; set; }
    public decimal? TimeoutScore { get; set; }
    public decimal? CircuitBreakerScore { get; set; }
    public decimal? FallbackScore { get; set; }
    public decimal? IdempotencyScore { get; set; }
    public decimal OverallScore { get; set; }
    public string? Grade { get; set; }
    public JsonDocument Evidence { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
