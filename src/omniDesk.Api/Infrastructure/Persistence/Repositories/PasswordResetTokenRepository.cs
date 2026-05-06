using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.PasswordResetTokens;

namespace omniDesk.Api.Infrastructure.Persistence.Repositories;

public class PasswordResetTokenRepository(AppDbContext db) : IPasswordResetTokenRepository
{
    public Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<PasswordResetToken> CreateAsync(PasswordResetToken token, CancellationToken ct = default)
    {
        token.CreatedAt = DateTimeOffset.UtcNow;
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task MarkUsedAsync(PasswordResetToken token, CancellationToken ct = default)
    {
        token.UsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
