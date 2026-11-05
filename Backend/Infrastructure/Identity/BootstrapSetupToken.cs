using System.Security.Cryptography;
using System.Text;

namespace Backend.Infrastructure.Identity;

public sealed class BootstrapSetupToken(TimeProvider timeProvider)
{
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly DateTimeOffset _expiresAt = timeProvider.GetUtcNow().AddHours(1);

    public string SetupUrl(string publicUrl) => $"{publicUrl.TrimEnd('/')}/install#token={_token}";

    public bool IsValid(string? token) =>
        token is { Length: 64 }
        && timeProvider.GetUtcNow() < _expiresAt
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_token), Encoding.UTF8.GetBytes(token));
}
