using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Domain.Enums;

namespace Backend.Domain.Entities;

public class AuthProfile
{
    public Guid Id { get; set; }
    public Guid EnvironmentId { get; set; }
    public ApiEnvironment Environment { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public AuthType AuthType { get; set; }
    public JsonDocument PublicConfig { get; set; } = null!;
    // Protected target credentials belong here, never in PublicConfig.
    [JsonIgnore]
    public byte[]? SecretCipherText { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
