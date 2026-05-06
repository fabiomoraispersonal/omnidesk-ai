using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.InviteTokens;

namespace omniDesk.Api.Infrastructure.Persistence.Repositories;

public class InviteTokenRepository(AppDbContext db) : IInviteTokenRepository
{
    public Task<InviteToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => db.InviteTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<InviteToken> CreateAsync(InviteToken token, CancellationToken ct = default)
    {
        token.CreatedAt = DateTimeOffset.UtcNow;
        db.InviteTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task InvalidatePendingByEmailAsync(string email, CancellationToken ct = default)
    {
        var pending = await db.InviteTokens
            .Where(t => t.Email == email.ToLowerInvariant()
                     && t.AcceptedAt == null
                     && t.InvalidatedAt == null
                     && t.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var t in pending)
            t.InvalidatedAt = now;

        await db.SaveChangesAsync(ct);
    }

    public async Task AcceptAsync(InviteToken token, CancellationToken ct = default)
    {
        token.AcceptedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
