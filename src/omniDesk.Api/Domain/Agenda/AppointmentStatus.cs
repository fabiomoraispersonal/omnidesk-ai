namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — conjunto fechado de status de agendamento. Persistido como <c>varchar(24)</c>
/// no banco para simplicidade multi-tenant (mesma estratégia da Spec 010 com event_type).
/// </summary>
public static class AppointmentStatus
{
    public const string PendingConfirmation = "pending_confirmation";
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
    public const string NoShow = "no_show";

    /// <summary>Todos os valores permitidos. Validar contra esta lista no INSERT.</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { PendingConfirmation, Confirmed, Cancelled, NoShow };

    /// <summary>
    /// Status que ocupam o slot para fins de disponibilidade e detecção de conflito
    /// (UNIQUE parcial no Postgres usa este mesmo conjunto).
    /// </summary>
    public static readonly IReadOnlySet<string> ActiveForSlot =
        new HashSet<string> { PendingConfirmation, Confirmed };

    /// <summary>Status terminais — não podem transicionar para nada (FR-031).</summary>
    public static readonly IReadOnlySet<string> Terminal =
        new HashSet<string> { Cancelled, NoShow };
}
