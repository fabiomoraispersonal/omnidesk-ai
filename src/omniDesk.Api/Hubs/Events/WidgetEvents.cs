namespace omniDesk.Api.Hubs.Events;

/// <summary>
/// WebSocket event names for the visitor widget channel (/ws/widget/{conversation_id}).
/// Constants — sem magic strings (Constitution §VII).
/// </summary>
public static class WidgetEvents
{
    // backend → widget
    public const string MessageNew            = "message.new";
    public const string AgentTyping           = "agent.typing";
    public const string ConversationAssigned  = "conversation.assigned";
    public const string ConversationResolved  = "conversation.resolved";
    public const string Ping                  = "ping";

    // widget → backend
    public const string MessageSend     = "message.send";
    public const string VisitorTyping   = "visitor.typing";
    public const string MessagesRead    = "messages.read";
    public const string MessagesReplay  = "messages.replay";
    public const string Pong            = "pong";
}
