namespace omniDesk.Api.Domain.InviteTokens;

public interface IInviteTokenRepository
{
    Task<InviteToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<InviteToken> CreateAsync(InviteToken token, CancellationToken ct = default);
    Task InvalidatePendingByEmailAsync(string email, CancellationToken ct = default);
    Task AcceptAsync(InviteToken token, CancellationToken ct = default);
}
