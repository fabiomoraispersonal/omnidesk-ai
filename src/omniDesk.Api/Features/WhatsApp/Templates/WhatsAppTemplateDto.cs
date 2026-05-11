using omniDesk.Api.Domain.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Templates;

/// <summary>
/// Spec 008 US5 — payload de saída para GET /api/whatsapp/templates*. Inclui
/// estado completo do template + <c>variable_count</c> derivado para o frontend.
/// contracts/whatsapp-templates-api.md §1.
/// </summary>
public sealed record WhatsAppTemplateDto(
    Guid Id,
    string Type,
    string Name,
    string Category,
    string Language,
    string Status,
    string BodyTemplate,
    IReadOnlyList<string> VariableLabels,
    int VariableCount,
    string? RejectionReason,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    string? MetaTemplateId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static WhatsAppTemplateDto From(WhatsAppTemplate t) => new(
        Id:              t.Id,
        Type:            t.Type.ToWire(),
        Name:            t.Name,
        Category:        t.Category.ToWire(),
        Language:        t.Language,
        Status:          t.Status.ToWire(),
        BodyTemplate:    t.BodyTemplate,
        VariableLabels:  t.VariableLabels,
        VariableCount:   t.VariableCount,
        RejectionReason: t.RejectionReason,
        SubmittedAt:     t.SubmittedAt,
        ApprovedAt:      t.ApprovedAt,
        RejectedAt:      t.RejectedAt,
        MetaTemplateId:  t.MetaTemplateId,
        CreatedAt:       t.CreatedAt,
        UpdatedAt:       t.UpdatedAt);
}
