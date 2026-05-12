using omniDesk.Api.Domain.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Templates.Commands;

/// <summary>
/// Spec 008 US5 — DELETE /api/whatsapp/templates/{id} (apenas draft ou rejected).
/// Soft delete (sets deleted_at). Templates approved/pending_meta NÃO podem ser
/// deletados (regra Meta + auditoria).
/// </summary>
public class DeleteTemplateCommand
{
    private readonly IWhatsAppTemplateRepository _repo;

    public DeleteTemplateCommand(IWhatsAppTemplateRepository repo) => _repo = repo;

    public async Task<DeleteTemplateResult> ExecuteAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        var template = await _repo.GetByIdAsync(id, tenantId, ct);
        if (template is null) return DeleteTemplateResult.NotFound();

        if (!TemplateStateMachine.CanDelete(template.Status))
            return DeleteTemplateResult.NotDeletable(template.Status);

        await _repo.SoftDeleteAsync(id, tenantId, ct);
        return DeleteTemplateResult.Deleted();
    }
}

public sealed record DeleteTemplateResult(
    DeleteTemplateResultStatus Status,
    TemplateStatus? CurrentStatus = null)
{
    public static DeleteTemplateResult Deleted() => new(DeleteTemplateResultStatus.Deleted);
    public static DeleteTemplateResult NotFound() => new(DeleteTemplateResultStatus.NotFound);
    public static DeleteTemplateResult NotDeletable(TemplateStatus current) =>
        new(DeleteTemplateResultStatus.NotDeletable, CurrentStatus: current);
}

public enum DeleteTemplateResultStatus
{
    Deleted,
    NotFound,
    NotDeletable,
}
