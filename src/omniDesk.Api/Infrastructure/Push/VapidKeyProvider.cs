namespace omniDesk.Api.Infrastructure.Push;

/// <summary>
/// Spec 010 US2 T054 — reads VAPID keypair from <see cref="IConfiguration"/> and validates shape.
///
/// Configuration keys (set via user-secrets in dev, env vars in prod):
///   - <c>Push:VapidSubject</c>      — MUST start with <c>mailto:</c> or <c>https://</c>.
///   - <c>Push:VapidPublicKey</c>    — base64url-encoded P-256 public key (typical length: 87 chars).
///   - <c>Push:VapidPrivateKey</c>   — base64url-encoded private key (typical length: 43 chars).
///
/// In dev/Development mode, missing keys are tolerated — <see cref="IsConfigured"/> returns false
/// and the dispatcher becomes a no-op. In Production, callers should fail fast at startup if
/// <see cref="IsConfigured"/> is false. Validation of subject/key shape happens lazily on first use.
/// </summary>
public class VapidKeyProvider
{
    private readonly string? _subject;
    private readonly string? _publicKey;
    private readonly string? _privateKey;

    public VapidKeyProvider(IConfiguration configuration)
    {
        _subject    = configuration["Push:VapidSubject"];
        _publicKey  = configuration["Push:VapidPublicKey"];
        _privateKey = configuration["Push:VapidPrivateKey"];
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_subject)
        && !string.IsNullOrWhiteSpace(_publicKey)
        && !string.IsNullOrWhiteSpace(_privateKey);

    public string Subject =>
        _subject ?? throw new InvalidOperationException(
            "VAPID subject not configured (Push:VapidSubject).");

    public string PublicKey =>
        _publicKey ?? throw new InvalidOperationException(
            "VAPID public key not configured (Push:VapidPublicKey).");

    public string PrivateKey =>
        _privateKey ?? throw new InvalidOperationException(
            "VAPID private key not configured (Push:VapidPrivateKey).");

    /// <summary>
    /// Validates the configured values match the expected shape (subject scheme, key length).
    /// Call from the dispatcher's first use so config errors surface as actionable exceptions.
    /// </summary>
    public void Validate()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "VAPID keys not configured. Set Push:VapidSubject/VapidPublicKey/VapidPrivateKey.");

        if (!(_subject!.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
              || _subject.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Push:VapidSubject must begin with 'mailto:' or 'https://' (got '{_subject}').");
        }

        // Permissive base64url length sanity check; the WebPush lib does full parsing.
        if (_publicKey!.Length < 60)
            throw new InvalidOperationException(
                "Push:VapidPublicKey appears too short — expected ~87 base64url chars.");
        if (_privateKey!.Length < 30)
            throw new InvalidOperationException(
                "Push:VapidPrivateKey appears too short — expected ~43 base64url chars.");
    }
}
