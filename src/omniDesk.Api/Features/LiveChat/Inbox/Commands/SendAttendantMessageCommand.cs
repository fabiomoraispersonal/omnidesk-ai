using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.LiveChat.Adapters;

namespace omniDesk.Api.Features.LiveChat.Inbox.Commands;

/// <summary>
/// Spec 007 US3 — attendant sends a message into a conversation they own.
/// Validates ownership, persists the Message row, then re-uses
/// <see cref="LiveChatOutgoingAdapter"/> to publish on the visitor's widget channel
/// (and the CRM channel — though the same attendant just sent it, multi-tab UX
/// benefits from the echo).
/// </summary>
public class SendAttendantMessageCommand
{
    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly LiveChatOutgoingAdapter _outgoing;

    public SendAttendantMessageCommand(
        IConversationRepository conversations,
        IMessageRepository messages,
        LiveChatOutgoingAdapter outgoing)
    {
        _conversations = conversations;
        _messages = messages;
        _outgoing = outgoing;
    }

    public async Task<SendResult> ExecuteAsync(
        Guid conversationId,
        Guid attendantId,
        string content,
        CancellationToken ct)
    {
        var conv = await _conversations.GetByIdAsync(conversationId, ct);
        if (conv is null) return SendResult.NotFound();
        if (conv.Status != ConversationStatus.Open) return SendResult.Closed();
        if (conv.AttendantId is null || conv.AttendantId != attendantId)
            return SendResult.Forbidden();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderType = MessageSenderType.Attendant,
            SenderId = attendantId,
            ContentType = MessageContentType.Text,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _messages.CreateAsync(message, ct);

        // The outgoing adapter publishes message.new on widget channel +
        // chat.message_received on CRM channel.
        await _outgoing.DispatchAsync(
            conversationId,
            new omniDesk.Api.Features.AgentRuntime.OutgoingMessage(content, "attendant", attendantId),
            ct);

        return SendResult.Accepted(message.Id);
    }

    public record SendResult(string Outcome, Guid? MessageId, string? ErrorCode)
    {
        public static SendResult Accepted(Guid id) => new("accepted", id, null);
        public static SendResult NotFound() => new("rejected", null, "CONVERSATION_NOT_FOUND");
        public static SendResult Closed() => new("rejected", null, "CONVERSATION_CLOSED");
        public static SendResult Forbidden() => new("rejected", null, "FORBIDDEN");
    }
}
