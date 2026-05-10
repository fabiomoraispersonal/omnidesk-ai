namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// WhatsApp Business channel configuration per tenant (1:1 — PK = TenantId).
/// Provisioned empty (<c>IsEnabled = false</c>) by <c>TenantProvisioningJob</c>; activated by
/// <c>tenant_admin</c> after entering Meta credentials. Spec 008 §2.1.
/// </summary>
public class WhatsAppConfig
{
    public Guid TenantId { get; set; }
    public bool IsEnabled { get; set; }
    public string? PhoneNumber { get; set; }
    public string? DisplayName { get; set; }
    public string? WabaId { get; set; }
    public string? PhoneNumberId { get; set; }

    /// <summary>
    /// Access Token Meta criptografado at-rest (AES-256-GCM via <c>AesEncryptionService</c>).
    /// Nunca retornado em texto plano por nenhum endpoint.
    /// </summary>
    public string? AccessTokenCiphertext { get; set; }

    /// <summary>
    /// App Secret Meta para HMAC-SHA256 do webhook. Mesma criptografia do access_token.
    /// Spec 008 research R4.
    /// </summary>
    public string? AppSecretCiphertext { get; set; }

    /// <summary>
    /// Token gerado no provisioning para o handshake inicial do webhook Meta. Imutável.
    /// </summary>
    public string WebhookVerifyToken { get; set; } = string.Empty;

    public bool BusinessHoursEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    public bool HasAccessToken => !string.IsNullOrEmpty(AccessTokenCiphertext);
    public bool HasAppSecret  => !string.IsNullOrEmpty(AppSecretCiphertext);
    public bool IsFullyConfigured =>
        !string.IsNullOrEmpty(PhoneNumberId) &&
        !string.IsNullOrEmpty(WabaId) &&
        HasAccessToken &&
        HasAppSecret;
}

public interface IWhatsAppConfigRepository
{
    Task<WhatsAppConfig?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct);
    Task<WhatsAppConfig?> GetByTenantSlugAsync(string slug, CancellationToken ct);
    Task UpdateAsync(WhatsAppConfig config, CancellationToken ct);
    Task SetEnabledAsync(Guid tenantId, bool isEnabled, CancellationToken ct);
}
