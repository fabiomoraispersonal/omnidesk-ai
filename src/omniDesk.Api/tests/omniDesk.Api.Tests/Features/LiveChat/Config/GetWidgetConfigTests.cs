using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Config;

/// <summary>
/// Spec 007 T096 — placeholder. Full coverage requires lifting the JWT auth flow into
/// Spec007WebFactory; the auth pipeline registration uses RSA keys + tenant_id claims
/// minted by the Spec 002 issuer, which is non-trivial to wire here. Tracked as a
/// follow-up: see specs/007-live-chat-widget/follow-up-issues.md (T184).
/// </summary>
public class GetWidgetConfigTests
{
    [Fact(Skip = "T096 — pending JWT-aware Spec007WebFactory; tracked in follow-up-issues.md")]
    public void Returns_config_widget_token_and_installation_snippet()
    {
        // - GET /api/widget/config with tenant_admin JWT
        // - 200 { widget_token, installation_snippet, config{is_enabled,..} }
    }
}
