namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// Regras de transição de estado para <see cref="WhatsAppTemplate"/> (data-model §1.2).
/// <list type="bullet">
///   <item><c>Draft</c> → editável, deletável, submetível.</item>
///   <item><c>PendingMeta</c> → imutável; transição automática via webhook ou poller.</item>
///   <item><c>Approved</c> → imutável; permanece selecionável no envio.</item>
///   <item><c>Rejected</c> → deletável (usuário recria como draft); apenas leitura.</item>
/// </list>
/// </summary>
public static class TemplateStateMachine
{
    public static bool CanEdit(TemplateStatus s) => s == TemplateStatus.Draft;

    public static bool CanDelete(TemplateStatus s) =>
        s == TemplateStatus.Draft || s == TemplateStatus.Rejected;

    public static bool CanSubmit(TemplateStatus s) => s == TemplateStatus.Draft;

    public static bool CanBeUsedForSending(TemplateStatus s) => s == TemplateStatus.Approved;
}
