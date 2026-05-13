namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — bloqueio pontual (férias, congresso, doença) de um profissional. Subtrai
/// da disponibilidade efetiva calculada pelo <c>AvailabilityCalculator</c>. Criação rejeitada
/// se overlap com agendamentos existentes (FR-015).
/// </summary>
public class ScheduleBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfessionalId { get; set; }

    /// <summary>Início do bloqueio (timestamptz, UTC armazenado).</summary>
    public DateTimeOffset StartAt { get; set; }

    /// <summary>Fim do bloqueio. CHECK <c>start_at &lt; end_at</c>.</summary>
    public DateTimeOffset EndAt { get; set; }

    /// <summary>Motivo interno opcional. Ex.: "Férias", "Congresso CBO 2026". ≤255 chars.</summary>
    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
