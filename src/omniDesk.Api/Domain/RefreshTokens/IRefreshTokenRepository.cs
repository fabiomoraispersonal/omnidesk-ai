namespace omniDesk.Api.Domain.RefreshTokens;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default);
    Task RevokeAsync(RefreshToken token, CancellationToken ct = default);
    Task RevokeAllByUserIdAsync(Guid userId, Guid? exceptTokenId = null, CancellationToken ct = default);
}
