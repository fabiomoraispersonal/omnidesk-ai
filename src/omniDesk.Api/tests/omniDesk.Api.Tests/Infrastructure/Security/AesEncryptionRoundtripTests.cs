using System.Security.Cryptography;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Security;

/// <summary>
/// Spec 008 T050 — sanity tests para o <see cref="AesEncryptionService"/> reusado
/// (research R3 — REUSO confirmado de Spec 003). Valida que o serviço cumpre
/// os requisitos da Spec 008 para criptografar access_token e app_secret WhatsApp:
/// roundtrip lossless, nonce único por chamada, tampering detection.
/// </summary>
public class AesEncryptionRoundtripTests
{
    private readonly AesEncryptionService _aes = WhatsAppTestHelpers.CreateAesService();

    [Fact]
    public void Roundtrip_returns_original_plaintext()
    {
        const string token = "EAAFakeAccessToken1234567890ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var cipher = _aes.Encrypt(token);
        var back = _aes.Decrypt(cipher);
        Assert.Equal(token, back);
    }

    [Fact]
    public void Each_encryption_uses_distinct_nonce()
    {
        const string plain = "EAAToken";
        var seen = new HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            var cipher = _aes.Encrypt(plain);
            var nonce = cipher.Split(':')[0];
            Assert.True(seen.Add(nonce), "Duplicate nonce detected");
        }
    }

    [Fact]
    public void Tampered_ciphertext_throws()
    {
        const string token = "AppSecret123";
        var cipher = _aes.Encrypt(token);
        var parts = cipher.Split(':');
        // Flip the first nibble of the ciphertext part — invalidates the auth tag.
        var ctHex = parts[1];
        var firstChar = ctHex[0] == '0' ? '1' : '0';
        var tampered = $"{parts[0]}:{firstChar}{ctHex[1..]}:{parts[2]}";

        Assert.ThrowsAny<CryptographicException>(() => _aes.Decrypt(tampered));
    }

    [Fact]
    public void Tampered_tag_throws()
    {
        const string token = "AppSecret123";
        var cipher = _aes.Encrypt(token);
        var parts = cipher.Split(':');
        var tagHex = parts[2];
        var firstChar = tagHex[0] == '0' ? '1' : '0';
        var tampered = $"{parts[0]}:{parts[1]}:{firstChar}{tagHex[1..]}";

        Assert.ThrowsAny<CryptographicException>(() => _aes.Decrypt(tampered));
    }

    [Fact]
    public void Empty_string_roundtrips()
    {
        var cipher = _aes.Encrypt(string.Empty);
        Assert.Equal(string.Empty, _aes.Decrypt(cipher));
    }

    [Fact]
    public void Long_payload_roundtrips()
    {
        var token = new string('x', 4096);
        var cipher = _aes.Encrypt(token);
        Assert.Equal(token, _aes.Decrypt(cipher));
    }
}
