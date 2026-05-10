namespace omniDesk.Api.Domain.LiveChat;

/// <summary>
/// Widget configuration per tenant (1:1). Created automatically on tenant provisioning
/// with safe defaults (Spec 007 FR-027).
/// </summary>
public class WidgetConfig
{
    public Guid TenantId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string PrimaryColor { get; set; } = "#2563EB";
    public LauncherIcon LauncherIcon { get; set; } = LauncherIcon.Chat;
    public string CompanyName { get; set; } = "Atendimento";
    public string WelcomeMessage { get; set; } = "Olá! Como posso ajudar?";
    public string? InputPlaceholder { get; set; }
    public WidgetPosition Position { get; set; } = WidgetPosition.BottomRight;
    public bool RequireIdentification { get; set; }
    public IReadOnlyList<IdentificationField>? IdentificationFields { get; set; }
    public IReadOnlyList<string>? AllowedDomains { get; set; }
    public string? PrivacyPolicyText { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public int AbandonmentTimeoutHours { get; set; } = 8;
    public int InactivityCloseHours { get; set; } = 24;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public interface IWidgetConfigRepository
{
    Task<WidgetConfig?> GetByTenantAsync(Guid tenantId, CancellationToken ct);
    Task UpdateAsync(WidgetConfig config, CancellationToken ct);
    Task SetEnabledAsync(Guid tenantId, bool isEnabled, CancellationToken ct);
}
