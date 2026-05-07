using System.Security.Cryptography;
using System.Text;

namespace omniDesk.Api.Infrastructure.Security;

public sealed class AesEncryptionService
{
    private readonly byte[] _key;

    public AesEncryptionService()
    {
        var keyBase64 = Environment.GetEnvironmentVariable("AES_ENCRYPTION_KEY")
            ?? throw new InvalidOperationException("AES_ENCRYPTION_KEY is not configured.");

        _key = Convert.FromBase64String(keyBase64);

        if (_key.Length != 32)
            throw new InvalidOperationException("AES_ENCRYPTION_KEY must be 32 bytes (256-bit) when decoded from Base64.");
    }

    public string Encrypt(string plaintext)
    {
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var nonceHex = Convert.ToHexString(nonce).ToLowerInvariant();
        var ciphertextHex = Convert.ToHexString(ciphertext).ToLowerInvariant();
        var tagHex = Convert.ToHexString(tag).ToLowerInvariant();

        return $"{nonceHex}:{ciphertextHex}:{tagHex}";
    }

    public string Decrypt(string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 3)
            throw new FormatException("Invalid encrypted value format.");

        var nonce = Convert.FromHexString(parts[0]);
        var ciphertext = Convert.FromHexString(parts[1]);
        var tag = Convert.FromHexString(parts[2]);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
