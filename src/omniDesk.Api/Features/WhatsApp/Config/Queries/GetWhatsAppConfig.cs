using omniDesk.Api.Domain.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Config.Queries;

/// <summary>
/// Spec 008 — GET /api/whatsapp/config. Mapeia <see cref="WhatsAppConfig"/> em
/// <see cref="WhatsAppConfigDto"/> com webhook URL derivada e channel_status.
/// NUNCA retorna access_token/app_secret em texto plano (FR-003 / SC-004).
/// </summary>
public class GetWhatsAppConfigQuery
{
    private readonly IWhatsAppConfigRepository _repo;
    private readonly IConfiguration _config;

    public GetWhatsAppConfigQuery(IWhatsAppConfigRepository repo, IConfiguration config)
    {
        _repo = repo;
        _config = config;
    }

    public async Task<WhatsAppConfigDto?> ExecuteAsync(Guid tenantId, string tenantSlug, CancellationToken ct)
    {
        var config = await _repo.GetByTenantIdAsync(tenantId, ct);
        if (config is null) return null;

        return new WhatsAppConfigDto(
            IsEnabled: config.IsEnabled,
            PhoneNumber: config.PhoneNumber,
            DisplayName: config.DisplayName,
            WabaId: config.WabaId,
            PhoneNumberId: config.PhoneNumberId,
            AccessTokenConfigured: config.HasAccessToken,
            AppSecretConfigured: config.HasAppSecret,
            WebhookVerifyToken: config.WebhookVerifyToken,
            WebhookUrl: BuildWebhookUrl(tenantSlug),
            BusinessHoursEnabled: config.BusinessHoursEnabled,
            ChannelStatus: DeriveChannelStatus(config),
            UpdatedAt: config.UpdatedAt);
    }

    private string BuildWebhookUrl(string slug)
    {
        // Frontend:CrmBaseUrl é tipicamente algo como https://crm.omnicare.ia.br ou
        // http://localhost:4201. A API correspondente vive em api.omnicare.ia.br;
        // em produção substituímos o subdomínio para api.; em dev usamos a config
        // diretamente para apontar para o host onde o backend está exposto.
        var baseUrl = _config["Frontend:CrmBaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            return $"https://api.omnicare.ia.br/api/public/whatsapp/webhook/{slug}";

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            // crm.* → api.*; preserva esquema/porta.
            var host = uri.Host.StartsWith("crm.", StringComparison.OrdinalIgnoreCase)
                ? "api." + uri.Host[4..]
                : uri.Host;

            var portPart = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            return $"{uri.Scheme}://{host}{portPart}/api/public/whatsapp/webhook/{slug}";
        }

        return $"{baseUrl}/api/public/whatsapp/webhook/{slug}";
    }

    private static string DeriveChannelStatus(WhatsAppConfig c)
    {
        if (string.IsNullOrEmpty(c.PhoneNumberId) || !c.HasAccessToken || !c.HasAppSecret)
            return "not_configured";
        return c.IsEnabled ? "active" : "configured_inactive";
    }
}
