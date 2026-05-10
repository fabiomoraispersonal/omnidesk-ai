using Xunit;

namespace omniDesk.Api.Tests.Hubs;

/// <summary>
/// Spec 007 T057 — placeholder. Full coverage requires lifting the test host's WebSocket
/// client into a flow that authenticates via the WidgetToken scheme on a query string.
/// Tracked as a follow-up: see specs/007-live-chat-widget/follow-up-issues.md (T184).
/// </summary>
public class WidgetWebSocketEndpointTests
{
    [Fact(Skip = "T057 — pending Spec007WebFactory WS-handshake support; tracked in follow-up-issues.md")]
    public void Handshake_happy_path_replay_close_codes()
    {
        // Cases to cover when implemented:
        //  - happy: connect with valid token + matching origin + open conv → 1000
        //  - 4401 INVALID_WIDGET_TOKEN, 4403 ORIGIN_NOT_ALLOWED, 4404 CONVERSATION_NOT_FOUND
        //  - 4422 LGPD_CONSENT_REQUIRED, 4409 CONVERSATION_CLOSED
        //  - message.send persists row + enqueues IncomingMessage
        //  - messages.replay returns events posted after `since_message_id`
    }
}
