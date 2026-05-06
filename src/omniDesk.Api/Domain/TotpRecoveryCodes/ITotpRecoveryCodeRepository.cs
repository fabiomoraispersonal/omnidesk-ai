namespace omniDesk.Api.Domain.TotpRecoveryCodes;

public interface ITotpRecoveryCodeRepository
{
    Task<TotpRecoveryCode?> GetByHashAsync(string codeHash, CancellationToken ct = default);
    Task CreateAllAsync(IEnumerable<TotpRecoveryCode> codes, CancellationToken ct = default);
    Task MarkUsedAsync(TotpRecoveryCode code, CancellationToken ct = default);
    Task DeleteAllByUserIdAsync(Guid userId, CancellationToken ct = default);
}
