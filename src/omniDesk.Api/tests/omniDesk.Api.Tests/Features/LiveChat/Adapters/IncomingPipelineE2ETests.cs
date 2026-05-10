using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Adapters;

/// <summary>
/// Spec 007 T058 — placeholder. End-to-end test from visitor message via WS to the AI
/// orchestrator running with a mocked OpenAI client and back to the widget channel.
/// Requires both Spec007WebFactory WS support and the Spec 006 FakeAssistantsApi seam,
/// which are non-trivial to wire here. Tracked as follow-up.
/// </summary>
public class IncomingPipelineE2ETests
{
    [Fact(Skip = "T058 — depends on T057 + FakeAssistantsApi composition; tracked in follow-up-issues.md")]
    public void Visitor_message_round_trips_through_orchestrator()
    {
        // - WS message.send → LiveChatIncomingAdapter persists row + enqueues IncomingMessage
        // - Hangfire IncomingMessageWorker fires the orchestrator with mocked OpenAI
        // - Orchestrator hits LiveChatConversationGateway.EnqueueOutgoingAsync
        // - LiveChatOutgoingAdapter persists agent message + publishes message.new
        // - WebSocketTestClient receives the message
    }
}
