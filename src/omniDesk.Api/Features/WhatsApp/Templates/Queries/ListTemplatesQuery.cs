using omniDesk.Api.Domain.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Templates.Queries;

/// <summary>
/// Spec 008 US5 — GET /api/whatsapp/templates. Suporta filtros (status, type) e paginação.
/// Para role <c>Attendant</c>, força <c>status=approved</c> server-side (FR-027/FR-031).
/// </summary>
public class ListTemplatesQuery
{
    private readonly IWhatsAppTemplateRepository _repo;

    public ListTemplatesQuery(IWhatsAppTemplateRepository repo) => _repo = repo;

    public async Task<ListTemplatesResult> ExecuteAsync(
        Guid tenantId,
        string? status,
        string? type,
        int page,
        int perPage,
        bool forceApprovedOnly,
        CancellationToken ct)
    {
        TemplateStatus? statusFilter = null;
        if (forceApprovedOnly)
        {
            statusFilter = TemplateStatus.Approved;
        }
        else if (!string.IsNullOrEmpty(status))
        {
            try { statusFilter = TemplateStatusExtensions.ParseWire(status); }
            catch { /* invalid filter — no-op */ }
        }

        TemplateType? typeFilter = null;
        if (!string.IsNullOrEmpty(type))
        {
            try { typeFilter = TemplateTypeExtensions.ParseWire(type); }
            catch { /* invalid filter — no-op */ }
        }

        var filter = new TemplateListFilter(statusFilter, typeFilter, page, perPage);
        var result = await _repo.ListAsync(tenantId, filter, ct);

        return new ListTemplatesResult(
            Items: result.Items.Select(WhatsAppTemplateDto.From).ToList(),
            Total: result.Total,
            Page:  result.Page,
            PerPage: result.PerPage);
    }
}

public sealed record ListTemplatesResult(
    IReadOnlyList<WhatsAppTemplateDto> Items,
    int Total,
    int Page,
    int PerPage);
