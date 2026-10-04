using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class AuthProfile
{
    // Keys
    public Guid Id { get; set; }
    public Guid EnvironmentId { get; set; }

    // Table properties
    public string Name { get; set; } = string.Empty;
    public AuthType AuthType { get; set; }
    public JsonDocument PublicConfig { get; set; } = null!;
    // Protected target credentials belong here, never in PublicConfig.
    [JsonIgnore]
    public byte[]? SecretCipherText { get; set; }

    // Metadata
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public ApiEnvironment Environment { get; set; } = null!;
}
