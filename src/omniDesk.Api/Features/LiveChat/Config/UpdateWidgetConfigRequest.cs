using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Features.LiveChat.Config;

/// <summary>Spec 007 — body shape for PUT /api/widget/config (CRM admin).</summary>
public record UpdateWidgetConfigRequest(
    string PrimaryColor,
    LauncherIcon LauncherIcon,
    string CompanyName,
    string WelcomeMessage,
    string? InputPlaceholder,
    WidgetPosition Position,
    bool RequireIdentification,
    IReadOnlyList<IdentificationField>? IdentificationFields,
    IReadOnlyList<string>? AllowedDomains,
    string? PrivacyPolicyText,
    string? PrivacyPolicyUrl,
    int AbandonmentTimeoutHours,
    int InactivityCloseHours);

public record ToggleWidgetRequest(bool IsEnabled);

public record ToggleWidgetResult(bool IsEnabled, int AffectedConversations);
