using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.WhatsApp.Templates.Requests;

namespace omniDesk.Api.Features.WhatsApp.Templates.Commands;

/// <summary>
/// Spec 008 US5 — PUT /api/whatsapp/templates/{id} (apenas status=draft).
/// Tipo e nome são imutáveis após criação; apenas body + variable_labels mudam.
/// Para tipos pré-definidos, variable_count permanece fixo.
/// </summary>
public class UpdateTemplateCommand
{
    private readonly IWhatsAppTemplateRepository _repo;

    public UpdateTemplateCommand(IWhatsAppTemplateRepository repo) => _repo = repo;

    public async Task<UpdateTemplateResult> ExecuteAsync(
        Guid id,
        Guid tenantId,
        UpdateTemplateRequest request,
        CancellationToken ct)
    {
        var template = await _repo.GetByIdAsync(id, tenantId, ct);
        if (template is null) return UpdateTemplateResult.NotFound();

        if (!TemplateStateMachine.CanEdit(template.Status))
            return UpdateTemplateResult.NotEditable(template.Status);

        // Pre-defined types: variable count locked.
        if (PredefinedTemplates.IsPredefined(template.Type))
        {
            var expected = PredefinedTemplates.For(template.Type).VariableCount;
            if (request.VariableLabels.Count != expected)
                return UpdateTemplateResult.VariableMismatch(expected, request.VariableLabels.Count);
        }

        template.BodyTemplate = request.BodyTemplate;
        template.VariableLabels = request.VariableLabels;
        await _repo.UpdateAsync(template, ct);
        return UpdateTemplateResult.Updated(template);
    }
}

public sealed record UpdateTemplateResult(
    UpdateTemplateResultStatus Status,
    WhatsAppTemplate? Template = null,
    TemplateStatus? CurrentStatus = null,
    int? ExpectedVariableCount = null,
    int? ProvidedVariableCount = null)
{
    public static UpdateTemplateResult Updated(WhatsAppTemplate t) =>
        new(UpdateTemplateResultStatus.Updated, t);

    public static UpdateTemplateResult NotFound() =>
        new(UpdateTemplateResultStatus.NotFound);

    public static UpdateTemplateResult NotEditable(TemplateStatus current) =>
        new(UpdateTemplateResultStatus.NotEditable, CurrentStatus: current);

    public static UpdateTemplateResult VariableMismatch(int expected, int provided) =>
        new(UpdateTemplateResultStatus.VariableMismatch,
            ExpectedVariableCount: expected, ProvidedVariableCount: provided);
}

public enum UpdateTemplateResultStatus
{
    Updated,
    NotFound,
    NotEditable,
    VariableMismatch,
}
