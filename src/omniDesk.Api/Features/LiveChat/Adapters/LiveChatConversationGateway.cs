using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.LiveChat.Adapters;

/// <summary>
/// Spec 007 — real <see cref="IConversationGateway"/> implementation operating over
/// <c>tenant_{slug}.conversations</c> + <c>tenant_{slug}.messages</c>. Replaces
/// <see cref="ChannelStubGateway"/>'s <c>ai_threads</c> usage.
///
/// Identity convention: external_conversation_ref = Conversation.Id stringified.
/// AgentId on Conversation maps to CurrentAgentId on AiThreadDto. AttendantId presence ⇒ handed off.
/// </summary>
public class LiveChatConversationGateway : IConversationGateway
{
    private readonly AppDbContext _db;
    private readonly LiveChatOutgoingAdapter _outgoing;

    public LiveChatConversationGateway(AppDbContext db, LiveChatOutgoingAdapter outgoing)
    {
        _db = db;
        _outgoing = outgoing;
    }

    public async Task<AiThreadDto> GetOrCreateThreadAsync(
        string externalConversationRef,
        Func<Task<string>> openAiThreadFactory,
        CancellationToken ct)
    {
        if (!Guid.TryParse(externalConversationRef, out var convId))
            throw new InvalidOperationException(
                $"externalConversationRef must be a Guid (Conversation.Id). Got: '{externalConversationRef}'.");

        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == convId, ct)
            ?? throw new InvalidOperationException($"Conversation {convId} not found.");

        if (string.IsNullOrEmpty(conv.OpenAiThreadId))
        {
            conv.OpenAiThreadId = await openAiThreadFactory();
            conv.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Map(conv, externalConversationRef);
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(
        Guid threadId, int limit, CancellationToken ct)
    {
        var rows = await _db.Messages
            .Where(m => m.ConversationId == threadId
                     && m.ContentType != MessageContentType.SystemEvent)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(ct);

        rows.Reverse();
        return rows.Select(m => new ConversationMessage(
            Role: m.SenderType switch
            {
                MessageSenderType.Visitor => "user",
                MessageSenderType.AiAgent => "assistant",
                MessageSenderType.Attendant => "assistant",
                MessageSenderType.System => "system",
                _ => "user",
            },
            Content: m.Content ?? string.Empty,
            SentAt: m.CreatedAt)).ToList();
    }

    public async Task EnqueueOutgoingAsync(Guid threadId, OutgoingMessage message, CancellationToken ct)
    {
        // Persist + publish to widget WS channel + (optional) CRM channel.
        await _outgoing.DispatchAsync(threadId, message, ct);
    }

    public async Task MarkHandedOffAsync(Guid threadId, CancellationToken ct)
    {
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == threadId, ct)
            ?? throw new InvalidOperationException($"Conversation {threadId} not found.");

        // Release the AI agent — attendant_id is set by the human pickup flow (Spec 005/008).
        conv.AgentId = null;
        conv.UpdatedAt = DateTimeOffset.UtcNow;

        _db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = threadId,
            SenderType = MessageSenderType.System,
            ContentType = MessageContentType.SystemEvent,
            Content = "handoff_to_human",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task SetCurrentAgentAsync(Guid threadId, Guid? agentId, CancellationToken ct)
    {
        await _db.Conversations
            .Where(c => c.Id == threadId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(c => c.AgentId, agentId)
                 .SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task<bool> IsHandedOffAsync(Guid threadId, CancellationToken ct)
        => await _db.Conversations
            .Where(c => c.Id == threadId)
            .Select(c => c.AttendantId)
            .FirstOrDefaultAsync(ct) is not null;

    public async Task<AiThreadDto?> GetByExternalRefAsync(string externalConversationRef, CancellationToken ct)
    {
        if (!Guid.TryParse(externalConversationRef, out var convId))
            return null;
        var conv = await _db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == convId, ct);
        return conv is null ? null : Map(conv, externalConversationRef);
    }

    // Spec 007 FR-017 — pulls the tail of the visitor's last resolved conversation so the
    // orchestrator can seed the new OpenAI thread with continuity context. system_event
    // messages are filtered (FR-045) and rows come back in chronological (ASC) order.
    public async Task<IReadOnlyList<ConversationMessage>> GetResumedContextAsync(
        Guid visitorId, int limit, CancellationToken ct)
    {
        if (limit <= 0) return Array.Empty<ConversationMessage>();

        var lastResolved = await _db.Conversations.AsNoTracking()
            .Where(c => c.VisitorId == visitorId
                     && c.Channel == ChannelType.LiveChat
                     && c.Status == ConversationStatus.Resolved)
            .OrderByDescending(c => c.EndedAt ?? c.UpdatedAt)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);

        if (lastResolved == Guid.Empty) return Array.Empty<ConversationMessage>();

        var rows = await _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == lastResolved
                     && m.ContentType != MessageContentType.SystemEvent)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(ct);

        rows.Reverse();
        return rows.Select(m => new ConversationMessage(
            Role: m.SenderType switch
            {
                MessageSenderType.Visitor => "user",
                MessageSenderType.AiAgent => "assistant",
                MessageSenderType.Attendant => "assistant",
                MessageSenderType.System => "system",
                _ => "user",
            },
            Content: m.Content ?? string.Empty,
            SentAt: m.CreatedAt)).ToList();
    }

    private static AiThreadDto Map(Conversation conv, string externalRef)
        => new(
            Id: conv.Id,
            ExternalConversationRef: externalRef,
            OpenAiThreadId: conv.OpenAiThreadId ?? string.Empty,
            CurrentAgentId: conv.AgentId,
            HandedOffToHumanAt: conv.AttendantId.HasValue ? conv.UpdatedAt : null);
}
