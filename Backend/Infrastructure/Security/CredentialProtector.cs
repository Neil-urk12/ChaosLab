using System.Security.Cryptography;
using System.Text;

namespace Backend.Infrastructure.Security;

// Encrypts target credentials and secret header values with AES-256-GCM.
// The key comes from Credentials:Key (base64, 32 bytes) and must survive restarts, so it is configuration
// rather than the ephemeral ASP.NET Data Protection key ring. The owning row's id is the associated data,
// so a ciphertext copied onto another row does not decrypt.
// A missing or invalid key fails only when a secret is encrypted or decrypted, so endpoints that never touch a
// secret (plain headers, AuthType None) keep working and still run their access checks first.
public sealed class CredentialProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[]? key;

    public CredentialProtector(IConfiguration configuration)
    {
        var configured = configuration["Credentials:Key"];
        try
        {
            key = configured is null ? null : Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
        }
        if (key is not { Length: 32 })
            key = null;
    }

    private byte[] Key => key ?? throw new InvalidOperationException(
        "Credentials:Key must be a base64-encoded 32-byte key (generate one with `openssl rand -base64 32`).");

    public byte[] Protect(string plaintext, Guid owner)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + TagSize + data.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(Key, TagSize);
        aes.Encrypt(nonce, data, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), owner.ToByteArray());
        return output;
    }

    // Null when the ciphertext does not decrypt with the current key, e.g. after Credentials:Key changed.
    public string? TryUnprotect(byte[] cipherText, Guid owner)
    {
        if (cipherText.Length < NonceSize + TagSize)
            return null;
        var data = new byte[cipherText.Length - NonceSize - TagSize];
        using var aes = new AesGcm(Key, TagSize);
        try
        {
            aes.Decrypt(cipherText.AsSpan(0, NonceSize), cipherText.AsSpan(NonceSize + TagSize),
                cipherText.AsSpan(NonceSize, TagSize), data, owner.ToByteArray());
        }
        catch (CryptographicException)
        {
            return null;
        }
        return Encoding.UTF8.GetString(data);
    }

    // Last four characters, the only part of a secret ever shown.
    public static string Hint(string secret) => secret.Length <= 4 ? "" : secret[^4..];
}
