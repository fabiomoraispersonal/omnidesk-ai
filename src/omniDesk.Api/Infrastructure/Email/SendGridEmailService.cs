using SendGrid;
using SendGrid.Helpers.Mail;

namespace omniDesk.Api.Infrastructure.Email;

public class SendGridEmailService : IEmailService
{
    private readonly SendGridClient _client;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public SendGridEmailService()
    {
        var apiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY")
            ?? throw new InvalidOperationException("SENDGRID_API_KEY not set");
        _fromEmail = Environment.GetEnvironmentVariable("SENDGRID_FROM_EMAIL") ?? "noreply@omnideskcrm.com.br";
        _fromName = "OmniDesk";
        _client = new SendGridClient(apiKey);
    }

    public async Task SendInviteAsync(string to, string tenantName, string inviteLink, CancellationToken ct = default)
    {
        var msg = MailHelper.CreateSingleEmail(
            from: new EmailAddress(_fromEmail, _fromName),
            to: new EmailAddress(to),
            subject: $"Você foi convidado para o OmniDesk — {tenantName}",
            plainTextContent: $"Acesse o link para criar sua conta: {inviteLink}",
            htmlContent: $"""
                <p>Você foi convidado para acessar o OmniDesk.</p>
                <p><a href="{inviteLink}">Criar minha conta</a></p>
                <p>Este link expira em 72 horas.</p>
                """);

        await _client.SendEmailAsync(msg, ct);
    }

    public async Task SendPasswordResetAsync(string to, string resetLink, CancellationToken ct = default)
    {
        var msg = MailHelper.CreateSingleEmail(
            from: new EmailAddress(_fromEmail, _fromName),
            to: new EmailAddress(to),
            subject: "Redefinir sua senha OmniDesk",
            plainTextContent: $"Acesse o link para redefinir sua senha: {resetLink}",
            htmlContent: $"""
                <p>Recebemos uma solicitação para redefinir a senha da sua conta OmniDesk.</p>
                <p><a href="{resetLink}">Redefinir minha senha</a></p>
                <p>Este link expira em 1 hora. Se você não solicitou, ignore este e-mail.</p>
                """);

        await _client.SendEmailAsync(msg, ct);
    }

    public async Task SendTenantWelcomeAsync(string to, string recipientName, string slug, string email, string password, CancellationToken ct = default)
    {
        var crmUrl = $"https://{slug}.omnideskcrm.com.br";
        var msg = MailHelper.CreateSingleEmail(
            from: new EmailAddress(_fromEmail, _fromName),
            to: new EmailAddress(to),
            subject: $"Seu ambiente OmniDesk está pronto",
            plainTextContent: $"Olá, {recipientName}!\n\nSeu ambiente OmniDesk foi configurado com sucesso.\n\nAcesse em: {crmUrl}\nUsuário: {email}\nSenha: {password}\n\nRecomendamos que você altere sua senha no primeiro acesso.",
            htmlContent: $"""
                <p>Olá, {recipientName}!</p>
                <p>Seu ambiente OmniDesk foi configurado com sucesso.</p>
                <p>Acesse em: <a href="{crmUrl}">{crmUrl}</a></p>
                <p><strong>Usuário:</strong> {email}<br/>
                   <strong>Senha:</strong> {password}</p>
                <p>Recomendamos que você altere sua senha no primeiro acesso.</p>
                """);

        await _client.SendEmailAsync(msg, ct);
    }

    public async Task SendSuperAdminPasswordResetAsync(string to, string recipientName, string newPassword, CancellationToken ct = default)
    {
        var msg = MailHelper.CreateSingleEmail(
            from: new EmailAddress(_fromEmail, _fromName),
            to: new EmailAddress(to),
            subject: "Sua senha OmniDesk foi redefinida",
            plainTextContent: $"Olá, {recipientName}!\n\nSua senha foi redefinida pelo operador.\n\nNova senha: {newPassword}\n\nRecomendamos que você altere sua senha no próximo acesso.",
            htmlContent: $"""
                <p>Olá, {recipientName}!</p>
                <p>Sua senha foi redefinida pelo operador OmniDesk.</p>
                <p><strong>Nova senha:</strong> {newPassword}</p>
                <p>Recomendamos que você altere sua senha no próximo acesso.</p>
                """);

        await _client.SendEmailAsync(msg, ct);
    }
}
