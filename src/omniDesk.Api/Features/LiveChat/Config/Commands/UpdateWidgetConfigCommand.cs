using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Features.LiveChat.Config.Commands;

/// <summary>
/// Spec 007 — atomic widget_config update. Validation has already run upstream; this
/// just maps the request onto the entity and persists.
/// </summary>
public class UpdateWidgetConfigCommand
{
    private readonly IWidgetConfigRepository _repo;

    public UpdateWidgetConfigCommand(IWidgetConfigRepository repo) => _repo = repo;

    public async Task<WidgetConfig> ExecuteAsync(
        Guid tenantId,
        UpdateWidgetConfigRequest request,
        CancellationToken ct)
    {
        var config = await _repo.GetByTenantAsync(tenantId, ct)
            ?? throw new InvalidOperationException($"WidgetConfig not found for tenant {tenantId}");

        config.PrimaryColor = request.PrimaryColor;
        config.LauncherIcon = request.LauncherIcon;
        config.CompanyName = request.CompanyName;
        config.WelcomeMessage = request.WelcomeMessage;
        config.InputPlaceholder = request.InputPlaceholder;
        config.Position = request.Position;
        config.RequireIdentification = request.RequireIdentification;
        config.IdentificationFields = request.IdentificationFields;
        config.AllowedDomains = request.AllowedDomains;
        config.PrivacyPolicyText = request.PrivacyPolicyText;
        config.PrivacyPolicyUrl = request.PrivacyPolicyUrl;
        config.AbandonmentTimeoutHours = request.AbandonmentTimeoutHours;
        config.InactivityCloseHours = request.InactivityCloseHours;
        config.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.UpdateAsync(config, ct);
        return config;
    }
}
