using System.Security.Cryptography;
using System.Text;
using omniDesk.Api.Infrastructure.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Webhook;

/// <summary>
/// Valida o header <c>X-Hub-Signature-256</c> de webhooks Meta WhatsApp.
/// Format: <c>sha256={hex_hmac_sha256(rawBody, app_secret)}</c>.
/// Comparação em tempo constante via <see cref="CryptographicOperations.FixedTimeEquals"/>
/// para mitigar timing attacks contra <c>app_secret</c>.
///
/// Spec 008 FR-006 / contracts/whatsapp-webhook.md §4 / research R4.
/// </summary>
public sealed class MetaWebhookSignatureValidator
{
    /// <param name="headerSignature">Conteúdo de <c>X-Hub-Signature-256</c> (com prefixo <c>sha256=</c>).</param>
    /// <param name="rawBody">Bytes do body como recebidos da Meta — antes de qualquer parse.</param>
    /// <param name="appSecret">App Secret do tenant (decifrado em memória).</param>
    /// <returns><c>true</c> se a assinatura confere; <c>false</c> em qualquer caso de falha.</returns>
    public bool Validate(string? headerSignature, byte[] rawBody, byte[] appSecret)
    {
        if (string.IsNullOrEmpty(headerSignature)) return false;
        if (!headerSignature.StartsWith(MetaApi.Headers.SignaturePrefix, StringComparison.Ordinal)) return false;
        if (rawBody is null || appSecret is null || appSecret.Length == 0) return false;

        var providedHex = headerSignature.AsSpan(MetaApi.Headers.SignaturePrefix.Length).ToString();
        if (providedHex.Length != 64) return false; // sha256 hex sempre 64 chars

        Span<byte> computed = stackalloc byte[32];
        if (!HMACSHA256.TryHashData(appSecret, rawBody, computed, out var written) || written != 32)
            return false;

        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        // Comparação UTF-8 byte-a-byte em tempo constante.
        var providedBytes = Encoding.ASCII.GetBytes(providedHex.ToLowerInvariant());
        var computedBytes = Encoding.ASCII.GetBytes(computedHex);
        return CryptographicOperations.FixedTimeEquals(providedBytes, computedBytes);
    }
}
