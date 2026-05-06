using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.RefreshTokens;

namespace omniDesk.Api.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
        => db.RefreshTokens
            .Where(t => t.UserId == userId && !t.Revoked && t.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<RefreshToken>)t.Result);

    public async Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.CreatedAt = DateTimeOffset.UtcNow;
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.Revoked = true;
        token.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllByUserIdAsync(Guid userId, Guid? exceptTokenId = null, CancellationToken ct = default)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == userId && !t.Revoked)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var t in tokens.Where(t => t.Id != exceptTokenId))
        {
            t.Revoked = true;
            t.RevokedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
