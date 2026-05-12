using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Infrastructure.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Templates.Commands;

/// <summary>
/// Spec 008 US5 — POST /api/whatsapp/templates/{id}/submit. Chama Meta
/// <c>POST /message_templates</c> com o body + components.parameters.example.
/// Em sucesso: status → <c>pending_meta</c>, persiste <c>meta_template_id</c>,
/// <c>submitted_at = now</c>. Em <c>4xx</c> Meta: status → <c>rejected</c> com
/// <c>rejection_reason = meta error message</c> (rejeição síncrona — Meta às vezes
/// rejeita imediatamente em vez de via webhook).
///
/// Requer <c>whatsapp_config</c> completo (waba_id + access_token).
/// </summary>
public class SubmitTemplateCommand
{
    private readonly IWhatsAppTemplateRepository _templateRepo;
    private readonly IWhatsAppConfigRepository _configRepo;
    private readonly WhatsAppMetaClient _meta;
    private readonly AesEncryptionService _aes;
    private readonly TimeProvider _clock;
    private readonly ILogger<SubmitTemplateCommand> _logger;

    public SubmitTemplateCommand(
        IWhatsAppTemplateRepository templateRepo,
        IWhatsAppConfigRepository configRepo,
        WhatsAppMetaClient meta,
        AesEncryptionService aes,
        TimeProvider clock,
        ILogger<SubmitTemplateCommand> logger)
    {
        _templateRepo = templateRepo;
        _configRepo = configRepo;
        _meta = meta;
        _aes = aes;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SubmitTemplateResult> ExecuteAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct)
    {
        var template = await _templateRepo.GetByIdAsync(id, tenantId, ct);
        if (template is null) return SubmitTemplateResult.NotFound();

        if (!TemplateStateMachine.CanSubmit(template.Status))
            return SubmitTemplateResult.NotSubmittable(template.Status);

        var config = await _configRepo.GetByTenantIdAsync(tenantId, ct);
        if (config is null || !config.HasAccessToken || string.IsNullOrEmpty(config.WabaId))
            return SubmitTemplateResult.NotConfigured();

        string accessToken;
        try
        {
            accessToken = _aes.Decrypt(config.AccessTokenCiphertext!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubmitTemplate: failed to decrypt access_token for tenant {TenantId}.", tenantId);
            return SubmitTemplateResult.NotConfigured();
        }

        var payload = BuildSubmissionPayload(template);

        try
        {
            var resp = await _meta.SubmitTemplateAsync(config.WabaId!, accessToken, payload, ct);

            template.Status = TemplateStatus.PendingMeta;
            template.SubmittedAt = _clock.GetUtcNow();
            template.MetaTemplateId = resp.MetaTemplateId;
            await _templateRepo.UpdateAsync(template, ct);

            _logger.LogInformation(
                "SubmitTemplate: tenant={TenantId} template={Name} → pending_meta (meta_id={MetaId}).",
                tenantId, template.Name, resp.MetaTemplateId);

            return SubmitTemplateResult.Submitted(template);
        }
        catch (MetaApiException ex)
        {
            // Rejeição síncrona da Meta — alguns erros são imediatos (nome inválido,
            // body com termos proibidos, etc.). Marcamos como rejected diretamente.
            template.Status = TemplateStatus.Rejected;
            template.RejectedAt = _clock.GetUtcNow();
            template.RejectionReason = $"[{ex.Code}] {ex.Message}";
            await _templateRepo.UpdateAsync(template, ct);

            _logger.LogWarning(
                "SubmitTemplate: Meta rejected synchronously — tenant={TenantId} template={Name} code={Code}.",
                tenantId, template.Name, ex.Code);

            return SubmitTemplateResult.MetaRejected(template, ex.Code.ToString(), ex.Message);
        }
    }

    private static TemplateSubmissionPayload BuildSubmissionPayload(WhatsAppTemplate template)
    {
        // example.body_text: parâmetros fictícios para a Meta avaliar (1 row por exemplo).
        // Usamos os variable_labels como amostra de texto.
        var sampleRow = template.VariableLabels.Count > 0
            ? template.VariableLabels.ToList()
            : Array.Empty<string>().ToList();

        var component = new TemplateComponent(
            Type: "BODY",
            Text: template.BodyTemplate,
            Example: sampleRow.Count > 0
                ? new TemplateComponentExample(BodyText: new List<IReadOnlyList<string>> { sampleRow })
                : null);

        return new TemplateSubmissionPayload(
            Name: template.Name,
            Category: template.Category.ToMetaWire(),
            Language: template.Language,
            Components: new[] { component });
    }
}

public sealed record SubmitTemplateResult(
    SubmitTemplateResultStatus Status,
    WhatsAppTemplate? Template = null,
    TemplateStatus? CurrentStatus = null,
    string? MetaErrorCode = null,
    string? MetaErrorMessage = null)
{
    public static SubmitTemplateResult Submitted(WhatsAppTemplate t) =>
        new(SubmitTemplateResultStatus.Submitted, t);

    public static SubmitTemplateResult NotFound() =>
        new(SubmitTemplateResultStatus.NotFound);

    public static SubmitTemplateResult NotSubmittable(TemplateStatus current) =>
        new(SubmitTemplateResultStatus.NotSubmittable, CurrentStatus: current);

    public static SubmitTemplateResult NotConfigured() =>
        new(SubmitTemplateResultStatus.NotConfigured);

    public static SubmitTemplateResult MetaRejected(WhatsAppTemplate t, string code, string message) =>
        new(SubmitTemplateResultStatus.MetaRejected, t, MetaErrorCode: code, MetaErrorMessage: message);
}

public enum SubmitTemplateResultStatus
{
    Submitted,
    NotFound,
    NotSubmittable,
    NotConfigured,
    MetaRejected,
}
