using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.WhatsApp.Webhook;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Infrastructure.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Config.Commands;

/// <summary>
/// Spec 008 — PATCH /api/whatsapp/config/toggle. Quando <c>is_enabled = true</c>:
/// valida configuração completa (waba_id, phone_number_id, access_token, app_secret),
/// chama Meta <c>GET /me</c> com o access_token decifrado para confirmar validade.
/// Em <c>401</c> retorna <see cref="ToggleResultStatus.InvalidToken"/>.
///
/// Quando <c>is_enabled = false</c>: apenas update local; sem chamada Meta.
///
/// Invalida cache Redis em ambos os casos.
/// </summary>
public class ToggleWhatsAppChannelCommand
{
    private readonly IWhatsAppConfigRepository _repo;
    private readonly AesEncryptionService _aes;
    private readonly WhatsAppMetaClient _meta;
    private readonly WaWebhookTenantResolver _resolver;
    private readonly ILogger<ToggleWhatsAppChannelCommand> _logger;

    public ToggleWhatsAppChannelCommand(
        IWhatsAppConfigRepository repo,
        AesEncryptionService aes,
        WhatsAppMetaClient meta,
        WaWebhookTenantResolver resolver,
        ILogger<ToggleWhatsAppChannelCommand> logger)
    {
        _repo = repo;
        _aes = aes;
        _meta = meta;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<ToggleResult> ExecuteAsync(
        Guid tenantId,
        string tenantSlug,
        bool isEnabled,
        CancellationToken ct)
    {
        var config = await _repo.GetByTenantIdAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"WhatsAppConfig not found for tenant {tenantId}");

        if (isEnabled)
        {
            var missing = CheckMissingFields(config);
            if (missing.Count > 0)
                return ToggleResult.NotConfigured(missing);

            string accessToken;
            try
            {
                accessToken = _aes.Decrypt(config.AccessTokenCiphertext!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt access_token for tenant {Slug}.", tenantSlug);
                return ToggleResult.InvalidToken();
            }

            bool valid;
            try
            {
                valid = await _meta.ValidateAccessTokenAsync(accessToken, ct);
            }
            catch (MetaApiException ex)
            {
                _logger.LogWarning("Meta token probe rejected (tenant={Slug} code={Code}).", tenantSlug, ex.Code);
                valid = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Meta token probe failed (tenant={Slug}).", tenantSlug);
                valid = false;
            }

            if (!valid) return ToggleResult.InvalidToken();
        }

        config.IsEnabled = isEnabled;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        await _repo.UpdateAsync(config, ct);
        await _resolver.InvalidateAsync(tenantSlug);

        var status = isEnabled ? "active" : "configured_inactive";
        return ToggleResult.Ok(isEnabled, status);
    }

    private static List<string> CheckMissingFields(WhatsAppConfig config)
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(config.WabaId))         missing.Add("waba_id");
        if (string.IsNullOrEmpty(config.PhoneNumberId))  missing.Add("phone_number_id");
        if (!config.HasAccessToken)                       missing.Add("access_token");
        if (!config.HasAppSecret)                         missing.Add("app_secret");
        return missing;
    }
}

public sealed record ToggleResult(
    ToggleResultStatus Status,
    bool IsEnabled,
    string ChannelStatus,
    IReadOnlyList<string>? MissingFields)
{
    public static ToggleResult Ok(bool isEnabled, string channelStatus) =>
        new(ToggleResultStatus.Ok, isEnabled, channelStatus, null);

    public static ToggleResult NotConfigured(IReadOnlyList<string> missing) =>
        new(ToggleResultStatus.NotConfigured, false, "not_configured", missing);

    public static ToggleResult InvalidToken() =>
        new(ToggleResultStatus.InvalidToken, false, "configured_inactive", null);
}

public enum ToggleResultStatus
{
    Ok,
    NotConfigured,
    InvalidToken,
}
