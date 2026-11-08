using Backend.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace Backend.Tests.Integration;

public sealed class CredentialProtectorTests
{
    private static CredentialProtector With(string? key) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Credentials:Key"] = key }).Build());

    private static string NewKey() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    [Theory]
    [InlineData(null)]
    [InlineData("change_me_base64_32_byte_key")]
    [InlineData("AAAA")]
    public void Missing_or_invalid_key_fails_only_when_a_secret_is_used(string? key)
    {
        var protector = With(key);
        Assert.Throws<InvalidOperationException>(() => protector.Protect("secret", Guid.NewGuid()));
    }

    [Fact]
    public void Ciphertext_decrypts_only_with_its_key_and_owner()
    {
        var key = NewKey();
        var owner = Guid.NewGuid();
        var cipherText = With(key).Protect("token-1234", owner);

        Assert.Equal("token-1234", With(key).TryUnprotect(cipherText, owner));
        Assert.Null(With(key).TryUnprotect(cipherText, Guid.NewGuid()));
        Assert.Null(With(NewKey()).TryUnprotect(cipherText, owner));
        Assert.Null(With(key).TryUnprotect(new byte[8], owner));
    }
}
