using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Hubs.Handlers;

/// <summary>
/// Spec 007 — handles <c>messages.replay {since_message_id}</c>. Returns every message
/// posted to the conversation strictly after the given message id, in chronological order.
/// Used by the widget after a transient disconnect to backfill anything it missed (R6).
/// </summary>
public class MessagesReplayHandler
{
    private readonly AppDbContext _db;
    public MessagesReplayHandler(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MessageReplayItem>> HandleAsync(
        Guid conversationId,
        Guid? sinceMessageId,
        CancellationToken ct)
    {
        var query = _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId);

        if (sinceMessageId is not null)
        {
            var pivot = await _db.Messages.AsNoTracking()
                .Where(m => m.Id == sinceMessageId.Value)
                .Select(m => new { m.CreatedAt, m.Id })
                .FirstOrDefaultAsync(ct);
            if (pivot is not null)
            {
                query = query.Where(m =>
                    m.CreatedAt > pivot.CreatedAt ||
                    (m.CreatedAt == pivot.CreatedAt && m.Id.CompareTo(pivot.Id) > 0));
            }
        }

        var rows = await query
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Take(500)
            .ToListAsync(ct);

        return rows.Select(m => new MessageReplayItem(
            m.Id,
            m.SenderType.ToWire(),
            m.SenderId,
            m.ContentType.ToWire(),
            m.Content,
            m.AttachmentUrl,
            m.CreatedAt)).ToList();
    }
}

public record MessageReplayItem(
    Guid Id,
    string SenderType,
    Guid? SenderId,
    string ContentType,
    string? Content,
    string? AttachmentUrl,
    DateTimeOffset CreatedAt);
