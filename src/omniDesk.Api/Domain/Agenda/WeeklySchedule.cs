namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — turno recorrente por dia da semana de um profissional. Múltiplas entradas por
/// <c>(professional_id, day_of_week)</c> permitidas (turnos da manhã + tarde, por exemplo) —
/// validação de overlap entre turnos do mesmo dia é feita em aplicação (FR-013).
/// </summary>
public class WeeklySchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfessionalId { get; set; }

    /// <summary>0 = Domingo, 1 = Segunda, ..., 6 = Sábado. CHECK 0..6 no banco.</summary>
    public short DayOfWeek { get; set; }

    /// <summary>Início do turno. Ex.: 08:00.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Fim do turno. CHECK <c>start_time &lt; end_time</c>.</summary>
    public TimeOnly EndTime { get; set; }
}
