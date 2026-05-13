namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — item do catálogo de serviços do tenant (consulta, procedimento, exame, avaliação).
/// Vive em <c>tenant_{slug}.services</c>. Soft delete via <see cref="IsActive"/>.
/// </summary>
public class Service
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nome do serviço. Ex.: "Consulta de Avaliação". 1–100 chars.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Descrição detalhada opcional para exibição ao cliente.</summary>
    public string? Description { get; set; }

    /// <summary>Categoria livre. Ex.: "Consulta", "Procedimento", "Exame", "Avaliação". ≤100 chars.</summary>
    public string? Category { get; set; }

    /// <summary>Duração padrão em minutos — define o tamanho do slot na agenda. &gt; 0.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Preço opcional. <c>null</c> = a combinar.</summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Se <c>true</c>, força <c>pending_confirmation</c> mesmo para clientes de retorno (FR-021).
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>Soft delete. <c>false</c> esconde de novos agendamentos; preserva os existentes.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
