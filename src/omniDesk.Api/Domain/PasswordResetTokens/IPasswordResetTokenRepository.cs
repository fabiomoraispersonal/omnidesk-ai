namespace omniDesk.Api.Domain.PasswordResetTokens;

public interface IPasswordResetTokenRepository
{
    Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<PasswordResetToken> CreateAsync(PasswordResetToken token, CancellationToken ct = default);
    Task MarkUsedAsync(PasswordResetToken token, CancellationToken ct = default);
}
