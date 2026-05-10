namespace omniDesk.Api.Hubs.Events;

/// <summary>
/// WebSocket event names for the attendant CRM channel (/ws/crm).
/// Constants — sem magic strings (Constitution §VII).
/// </summary>
public static class CrmEvents
{
    // backend → CRM
    public const string ChatNewConversation     = "chat.new_conversation";
    public const string ChatMessageReceived     = "chat.message_received";
    public const string ChatVisitorTyping       = "chat.visitor_typing";
    public const string ChatBrowserNotify       = "chat.browser_notify";
    public const string ChatConversationResolved = "chat.conversation_resolved";

    // CRM → backend
    public const string AttendantTyping        = "attendant.typing";
    public const string ConversationSend       = "conversation.send";
    public const string ConversationResolve    = "conversation.resolve";
    public const string MessagesRead           = "messages.read";

    // browser_notify triggers
    public const string TriggerNewConversation = "new_conversation";
    public const string TriggerNewMessage      = "new_message";
    public const string TriggerTransferred     = "transferred";
}
