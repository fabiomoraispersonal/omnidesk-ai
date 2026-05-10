namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// Template de mensagem WhatsApp aprovado pela Meta. Necessário para envio fora da
/// janela de 24h. Spec 008 §2.2 / data-model §1.2.
/// </summary>
public class WhatsAppTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>ID do template na Meta. Preenchido após submissão/aprovação.</summary>
    public string? MetaTemplateId { get; set; }

    public TemplateType Type { get; set; }

    /// <summary>Nome único por tenant. Gerado por <see cref="TemplateNameGenerator"/>.</summary>
    public string Name { get; set; } = string.Empty;

    public TemplateCategory Category { get; set; } = TemplateCategory.Utility;

    /// <summary>Idioma do template. V1 fixo em <c>pt_BR</c>.</summary>
    public string Language { get; set; } = "pt_BR";

    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;

    /// <summary>Corpo com placeholders <c>{{1}}..{{N}}</c>.</summary>
    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>Descrição de cada variável, em ordem.</summary>
    public IReadOnlyList<string> VariableLabels { get; set; } = Array.Empty<string>();

    /// <summary>Motivo de rejeição retornado pela Meta. Preenchido apenas se Status = Rejected.</summary>
    public string? RejectionReason { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    public int VariableCount => VariableLabels.Count;
}

public sealed record TemplateListFilter(
    TemplateStatus? Status = null,
    TemplateType? Type = null,
    int Page = 1,
    int PerPage = 20);

public sealed record TemplateListResult(
    IReadOnlyList<WhatsAppTemplate> Items,
    int Total,
    int Page,
    int PerPage);

public interface IWhatsAppTemplateRepository
{
    Task<TemplateListResult> ListAsync(Guid tenantId, TemplateListFilter filter, CancellationToken ct);
    Task<WhatsAppTemplate?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<WhatsAppTemplate?> GetByNameAsync(string name, Guid tenantId, CancellationToken ct);
    Task<WhatsAppTemplate?> GetByMetaIdAsync(string metaTemplateId, Guid tenantId, CancellationToken ct);
    Task<WhatsAppTemplate> CreateAsync(WhatsAppTemplate template, CancellationToken ct);
    Task UpdateAsync(WhatsAppTemplate template, CancellationToken ct);
    Task SoftDeleteAsync(Guid id, Guid tenantId, CancellationToken ct);
}
