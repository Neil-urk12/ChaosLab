using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Domain.Entities;

// A header (or test value) sent with scenario requests to an environment.
public class EnvironmentHeader
{
    // Keys
    public Guid Id { get; set; }
    public Guid EnvironmentId { get; set; }

    // Table properties
    public string Name { get; set; } = string.Empty;
    // Plain value; null when the header is secret.
    public string? Value { get; set; }
    // Secret value, encrypted like credentials; only Hint (the last 4 characters) is ever shown.
    [JsonIgnore]
    public byte[]? SecretCipherText { get; set; }
    public string? Hint { get; set; }
    // JSON array of "METHOD /path" operations; empty means all requests.
    public JsonDocument AppliesTo { get; set; } = JsonDocument.Parse("[]");

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ApiEnvironment Environment { get; set; } = null!;
}
