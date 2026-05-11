using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Infrastructure.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Webhook;

/// <summary>
/// Spec 008 US5 T117 — handler de <c>message_template_status_update</c> via webhook Meta.
/// Atualiza <c>WhatsAppTemplate.Status</c> conforme <c>change.Value.Event</c>:
/// <list type="bullet">
///   <item><c>APPROVED</c> → status=Approved, approved_at=now, meta_template_id set.</item>
///   <item><c>REJECTED</c> → status=Rejected, rejected_at=now, rejection_reason set.</item>
/// </list>
/// Templates não encontrados (ex.: o backend nunca submeteu) são ignorados com log.
/// </summary>
public sealed class WaTemplateStatusHandler
{
    private readonly IWhatsAppTemplateRepository _repo;
    private readonly TimeProvider _clock;
    private readonly ILogger<WaTemplateStatusHandler> _logger;

    public WaTemplateStatusHandler(
        IWhatsAppTemplateRepository repo,
        TimeProvider clock,
        ILogger<WaTemplateStatusHandler> logger)
    {
        _repo = repo;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(
        Guid tenantId,
        string tenantSlug,
        WaMessagesValue value,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(value.Event))
        {
            _logger.LogWarning("WaTemplateStatusUpdate: missing event field. tenant={Slug}", tenantSlug);
            return;
        }

        WhatsAppTemplate? template = null;

        if (value.MessageTemplateId is { } metaIdLong)
        {
            template = await _repo.GetByMetaIdAsync(metaIdLong.ToString(), tenantId, ct);
        }

        if (template is null && !string.IsNullOrEmpty(value.MessageTemplateName))
        {
            template = await _repo.GetByNameAsync(value.MessageTemplateName!, tenantId, ct);
        }

        if (template is null)
        {
            _logger.LogInformation(
                "WaTemplateStatusUpdate: template not found locally. tenant={Slug} event={Event} name={Name} meta_id={MetaId}",
                tenantSlug, value.Event, value.MessageTemplateName, value.MessageTemplateId);
            return;
        }

        var now = _clock.GetUtcNow();

        switch (value.Event.ToUpperInvariant())
        {
            case MetaApi.TemplateEvents.Approved:
                template.Status = TemplateStatus.Approved;
                template.ApprovedAt = now;
                template.RejectedAt = null;
                template.RejectionReason = null;
                if (value.MessageTemplateId is { } approvedMetaId)
                    template.MetaTemplateId = approvedMetaId.ToString();
                await _repo.UpdateAsync(template, ct);
                _logger.LogInformation(
                    "WaTemplateStatusUpdate: approved. tenant={Slug} template={Name}",
                    tenantSlug, template.Name);
                break;

            case MetaApi.TemplateEvents.Rejected:
                template.Status = TemplateStatus.Rejected;
                template.RejectedAt = now;
                template.RejectionReason = string.IsNullOrEmpty(value.Reason) ? "Rejeitado pela Meta." : value.Reason;
                if (value.MessageTemplateId is { } rejectedMetaId)
                    template.MetaTemplateId = rejectedMetaId.ToString();
                await _repo.UpdateAsync(template, ct);
                _logger.LogInformation(
                    "WaTemplateStatusUpdate: rejected. tenant={Slug} template={Name} reason={Reason}",
                    tenantSlug, template.Name, template.RejectionReason);
                break;

            default:
                _logger.LogInformation(
                    "WaTemplateStatusUpdate: unhandled event '{Event}'. tenant={Slug} template={Name}",
                    value.Event, tenantSlug, template.Name);
                break;
        }
    }
}
