namespace omniDesk.Api.Infrastructure.Email;

public interface IEmailService
{
    Task SendInviteAsync(string to, string tenantName, string inviteLink, CancellationToken ct = default);
    Task SendPasswordResetAsync(string to, string resetLink, CancellationToken ct = default);
}
