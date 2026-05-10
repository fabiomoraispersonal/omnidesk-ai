using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.WhatsApp.Webhook;
using omniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Features.WhatsApp.Config.Commands;

/// <summary>
/// Spec 008 — PUT /api/whatsapp/config. Aplica apenas campos não-empty;
/// strings vazias significam "manter o existente" (frontend só envia novos
/// valores quando o usuário clicar "Alterar"). Cifra access_token e app_secret
/// via <see cref="AesEncryptionService"/> antes de persistir (FR-003).
/// Invalida cache Redis do <see cref="WaWebhookTenantResolver"/>.
/// </summary>
public class UpdateWhatsAppConfigCommand
{
    private readonly IWhatsAppConfigRepository _repo;
    private readonly AesEncryptionService _aes;
    private readonly WaWebhookTenantResolver _resolver;

    public UpdateWhatsAppConfigCommand(
        IWhatsAppConfigRepository repo,
        AesEncryptionService aes,
        WaWebhookTenantResolver resolver)
    {
        _repo = repo;
        _aes = aes;
        _resolver = resolver;
    }

    public async Task<WhatsAppConfig> ExecuteAsync(
        Guid tenantId,
        string tenantSlug,
        UpdateWhatsAppConfigRequest request,
        CancellationToken ct)
    {
        var config = await _repo.GetByTenantIdAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"WhatsAppConfig not found for tenant {tenantId}");

        // Apply only non-empty fields. Trim where applicable.
        if (request.PhoneNumber is not null) config.PhoneNumber = NullIfEmpty(request.PhoneNumber);
        if (request.DisplayName is not null) config.DisplayName = NullIfEmpty(request.DisplayName);
        if (request.WabaId is not null)      config.WabaId      = NullIfEmpty(request.WabaId);
        if (request.PhoneNumberId is not null) config.PhoneNumberId = NullIfEmpty(request.PhoneNumberId);

        // Encrypt + persist secrets only when caller provided new values.
        if (!string.IsNullOrEmpty(request.AccessToken))
            config.AccessTokenCiphertext = _aes.Encrypt(request.AccessToken);

        if (!string.IsNullOrEmpty(request.AppSecret))
            config.AppSecretCiphertext = _aes.Encrypt(request.AppSecret);

        if (request.BusinessHoursEnabled is { } bhe)
            config.BusinessHoursEnabled = bhe;

        config.UpdatedAt = DateTimeOffset.UtcNow;
        await _repo.UpdateAsync(config, ct);

        // Bust webhook resolver cache so next webhook reflects the new app_secret etc.
        await _resolver.InvalidateAsync(tenantSlug);

        return config;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
