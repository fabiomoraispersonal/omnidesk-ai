using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Hubs.Handlers;

/// <summary>
/// Spec 007 — visitor signals "I've read up to here". Marks all unread messages of the
/// conversation as read in a single UPDATE. Idempotent.
/// </summary>
public class MessagesReadHandler
{
    private readonly AppDbContext _db;
    public MessagesReadHandler(AppDbContext db) => _db = db;

    public async Task<int> HandleAsync(Guid conversationId, CancellationToken ct)
    {
        return await _db.Messages
            .Where(m => m.ConversationId == conversationId && !m.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, true), ct);
    }
}
