using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Queues;

namespace omniDesk.Api.Features.LiveChat.Adapters;

/// <summary>
/// Spec 007 — entry point for visitor messages arriving via the widget. Persists the message
/// in <c>tenant_{slug}.messages</c>, idempotent on (conversation_id, client_message_id),
/// then enqueues an <see cref="IncomingMessage"/> to the Spec 006 AI pipeline only when the
/// conversation is still under AI control (<c>attendant_id IS NULL</c>).
///
/// Bypassing the pipeline once a human has taken over honors FR-015 (AI does not process
/// while a human owns the conversation).
/// </summary>
public class LiveChatIncomingAdapter
{
    private readonly AppDbContext _db;
    private readonly IncomingMessagePublisher _publisher;
    private readonly ITenantSlugAccessor _slug;
    private readonly ILogger<LiveChatIncomingAdapter> _logger;

    public LiveChatIncomingAdapter(
        AppDbContext db,
        IncomingMessagePublisher publisher,
        ITenantSlugAccessor slug,
        ILogger<LiveChatIncomingAdapter> logger)
    {
        _db = db;
        _publisher = publisher;
        _slug = slug;
        _logger = logger;
    }

    public async Task<EnqueueResult> EnqueueAsync(
        Guid conversationId,
        Guid? clientMessageId,
        string content,
        CancellationToken ct)
    {
        var conv = await _db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conv is null) return EnqueueResult.Rejected("CONVERSATION_NOT_FOUND");
        if (conv.Status != ConversationStatus.Open) return EnqueueResult.Rejected("CONVERSATION_CLOSED");

        // Idempotency: if a message with the same client_message_id already exists, return it.
        if (clientMessageId is not null)
        {
            var existing = await _db.Messages.FirstOrDefaultAsync(
                m => m.ConversationId == conversationId && m.ClientMessageId == clientMessageId, ct);
            if (existing is not null) return EnqueueResult.Duplicate(existing.Id);
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderType = MessageSenderType.Visitor,
            ClientMessageId = clientMessageId,
            ContentType = MessageContentType.Text,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(ct);

        // FR-015: only feed the AI pipeline while the conversation is unowned by a human.
        if (conv.AttendantId is null)
        {
            var tenantId = await _db.Tenants
                .Where(t => t.Slug == _slug.Slug)
                .Select(t => t.Id)
                .FirstAsync(ct);

            _publisher.Enqueue(new IncomingMessage(
                TenantId: tenantId,
                TenantSlug: _slug.Slug,
                ExternalConversationRef: conversationId.ToString(),
                MessageId: message.Id.ToString(),
                Content: content,
                SentAt: message.CreatedAt));
        }
        else
        {
            _logger.LogDebug(
                "Conversation {ConvId} owned by attendant {AttendantId}; skipping AI pipeline.",
                conversationId, conv.AttendantId);
        }

        return EnqueueResult.Accepted(message.Id);
    }

    public sealed record EnqueueResult(string Outcome, Guid? MessageId, string? ErrorCode)
    {
        public static EnqueueResult Accepted(Guid id) => new("accepted", id, null);
        public static EnqueueResult Duplicate(Guid id) => new("duplicate", id, null);
        public static EnqueueResult Rejected(string code) => new("rejected", null, code);
    }
}
