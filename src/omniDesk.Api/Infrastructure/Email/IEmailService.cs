namespace omniDesk.Api.Infrastructure.Email;

public interface IEmailService
{
    Task SendInviteAsync(string to, string tenantName, string inviteLink, CancellationToken ct = default);
    Task SendPasswordResetAsync(string to, string resetLink, CancellationToken ct = default);
    Task SendTenantWelcomeAsync(string to, string recipientName, string slug, string email, string password, CancellationToken ct = default);
    Task SendSuperAdminPasswordResetAsync(string to, string recipientName, string newPassword, CancellationToken ct = default);
}
