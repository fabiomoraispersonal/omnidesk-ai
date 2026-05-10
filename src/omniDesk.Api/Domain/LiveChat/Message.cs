namespace omniDesk.Api.Domain.LiveChat;

/// <summary>
/// Single message inside a conversation. Immutable after insert (only is_read may flip).
/// Idempotent inserts via (conversation_id, client_message_id) — see ux_messages_idempotency.
/// </summary>
public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public MessageSenderType SenderType { get; set; }
    public Guid? SenderId { get; set; }
    public Guid? ClientMessageId { get; set; }
    public MessageContentType ContentType { get; set; } = MessageContentType.Text;
    public string? Content { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? AttachmentName { get; set; }
    public int? AttachmentSizeBytes { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Spec 008 — Meta WhatsApp message ID (wamid.HBgL...). NULL para conversas live_chat.</summary>
    public string? WaMessageId { get; set; }
}

public interface IMessageRepository
{
    Task<IReadOnlyList<Message>> GetByConversationAsync(Guid conversationId, int limit, Guid? before, CancellationToken ct);
    Task<IReadOnlyList<Message>> GetRecentByConversationAsync(Guid conversationId, int limit, CancellationToken ct);
    Task<Message?> GetByClientMessageIdAsync(Guid conversationId, Guid clientMessageId, CancellationToken ct);
    Task<Message> CreateAsync(Message message, CancellationToken ct);
    Task MarkAllReadAsync(Guid conversationId, CancellationToken ct);
}
