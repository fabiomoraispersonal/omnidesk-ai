using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.TotpRecoveryCodes;

namespace omniDesk.Api.Infrastructure.Persistence.Repositories;

public class TotpRecoveryCodeRepository(AppDbContext db) : ITotpRecoveryCodeRepository
{
    public Task<TotpRecoveryCode?> GetByHashAsync(string codeHash, CancellationToken ct = default)
        => db.TotpRecoveryCodes.FirstOrDefaultAsync(c => c.CodeHash == codeHash && c.UsedAt == null, ct);

    public async Task CreateAllAsync(IEnumerable<TotpRecoveryCode> codes, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var code in codes)
            code.CreatedAt = now;

        db.TotpRecoveryCodes.AddRange(codes);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkUsedAsync(TotpRecoveryCode code, CancellationToken ct = default)
    {
        code.UsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAllByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        await db.TotpRecoveryCodes
            .Where(c => c.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }
}
