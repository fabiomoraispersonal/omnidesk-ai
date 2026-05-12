using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.LiveChat;

public class VisitorRepository(AppDbContext db) : IVisitorRepository
{
    public Task<Visitor?> GetByAnonymousIdAsync(Guid anonymousId, CancellationToken ct)
        => db.Visitors.FirstOrDefaultAsync(v => v.AnonymousId == anonymousId, ct);

    public async Task<Visitor> CreateAsync(Visitor visitor, CancellationToken ct)
    {
        if (visitor.Id == Guid.Empty) visitor.Id = Guid.NewGuid();
        db.Visitors.Add(visitor);
        await db.SaveChangesAsync(ct);
        return visitor;
    }

    public async Task UpdateIdentificationAsync(Guid visitorId, string? name, string? email, string? phone, CancellationToken ct)
    {
        var visitor = await db.Visitors.FirstOrDefaultAsync(v => v.Id == visitorId, ct)
            ?? throw new InvalidOperationException($"Visitor {visitorId} not found");
        visitor.Name = name;
        visitor.Email = email;
        visitor.Phone = phone;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetContactIdAsync(Guid visitorId, Guid contactId, CancellationToken ct)
    {
        var visitor = await db.Visitors.FirstOrDefaultAsync(v => v.Id == visitorId, ct)
            ?? throw new InvalidOperationException($"Visitor {visitorId} not found");
        visitor.ContactId = contactId;
        await db.SaveChangesAsync(ct);
    }
}
