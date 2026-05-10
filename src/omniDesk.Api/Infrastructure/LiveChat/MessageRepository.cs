using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.LiveChat;

public class MessageRepository(AppDbContext db) : IMessageRepository
{
    public async Task<IReadOnlyList<Message>> GetByConversationAsync(
        Guid conversationId, int limit, Guid? before, CancellationToken ct)
    {
        var query = db.Messages.Where(m => m.ConversationId == conversationId);

        if (before is not null)
        {
            var cursor = await db.Messages
                .Where(m => m.Id == before.Value)
                .Select(m => new { m.CreatedAt, m.Id })
                .FirstOrDefaultAsync(ct);
            if (cursor is not null)
            {
                query = query.Where(m =>
                    m.CreatedAt < cursor.CreatedAt ||
                    (m.CreatedAt == cursor.CreatedAt && m.Id.CompareTo(cursor.Id) < 0));
            }
        }

        var page = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(ct);

        // Caller expects ASC chronological order.
        page.Reverse();
        return page;
    }

    public async Task<IReadOnlyList<Message>> GetRecentByConversationAsync(
        Guid conversationId, int limit, CancellationToken ct)
    {
        var page = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(ct);

        page.Reverse();
        return page;
    }

    public Task<Message?> GetByClientMessageIdAsync(
        Guid conversationId, Guid clientMessageId, CancellationToken ct)
        => db.Messages.FirstOrDefaultAsync(
            m => m.ConversationId == conversationId
              && m.ClientMessageId == clientMessageId,
            ct);

    public async Task<Message> CreateAsync(Message message, CancellationToken ct)
    {
        if (message.Id == Guid.Empty) message.Id = Guid.NewGuid();
        if (message.CreatedAt == default) message.CreatedAt = DateTimeOffset.UtcNow;
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
        return message;
    }

    public async Task MarkAllReadAsync(Guid conversationId, CancellationToken ct)
    {
        await db.Messages
            .Where(m => m.ConversationId == conversationId && !m.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true), ct);
    }
}
