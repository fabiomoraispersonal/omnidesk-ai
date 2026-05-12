namespace omniDesk.Api.Features.WhatsApp.Config;

/// <summary>
/// Spec 008 — payload de saída do GET /api/whatsapp/config. NUNCA inclui access_token
/// ou app_secret em texto plano (FR-003 / SC-004) — apenas flags <c>access_token_configured</c>
/// e <c>app_secret_configured</c>. Webhook URL é derivada da config.
/// </summary>
public sealed record WhatsAppConfigDto(
    bool IsEnabled,
    string? PhoneNumber,
    string? DisplayName,
    string? WabaId,
    string? PhoneNumberId,
    bool AccessTokenConfigured,
    bool AppSecretConfigured,
    string WebhookVerifyToken,
    string WebhookUrl,
    bool BusinessHoursEnabled,
    string ChannelStatus,
    DateTimeOffset UpdatedAt);

public sealed record ToggleWhatsAppChannelRequest(bool IsEnabled);

public sealed record ToggleWhatsAppChannelResult(bool IsEnabled, string ChannelStatus);

/// <summary>
/// Body do PUT /api/whatsapp/config. Strings vazias para <c>access_token</c> e
/// <c>app_secret</c> significam "manter o existente" — frontend só envia novos valores
/// quando o usuário clicar "Alterar".
/// </summary>
public sealed record UpdateWhatsAppConfigRequest(
    string? PhoneNumber,
    string? DisplayName,
    string? WabaId,
    string? PhoneNumberId,
    string? AccessToken,
    string? AppSecret,
    bool? BusinessHoursEnabled);
