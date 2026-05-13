using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Domain.Agenda;

/// <summary>
/// Spec 011 — entidade central do módulo. 1 profissional × 1 cliente × 1 serviço por
/// agendamento na V1. <c>end_at</c> é calculado pelo backend (FR-019), nunca aceito do payload.
/// Vive em <c>tenant_{slug}.appointments</c>.
/// </summary>
public class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── Relacionais ─────────────────────────────────────────────────────────────────
    public Guid ProfessionalId { get; set; }
    public Guid ServiceId { get; set; }

    /// <summary>Contato vinculado. Opcional para suportar agendamentos avulsos.</summary>
    public Guid? ContactId { get; set; }

    /// <summary>Ticket de origem (quando criado via transbordo).</summary>
    public Guid? TicketId { get; set; }

    /// <summary>Conversa de origem (Live Chat ou WhatsApp).</summary>
    public Guid? ConversationId { get; set; }

    // ── Temporais ───────────────────────────────────────────────────────────────────
    public DateTimeOffset StartAt { get; set; }

    /// <summary>Calculado: <c>start_at + service.duration_minutes</c>. CHECK <c>end_at &gt; start_at</c>.</summary>
    public DateTimeOffset EndAt { get; set; }

    /// <summary>Preenchido pelo <c>AppointmentReminderJob</c> da Spec 010 ao enviar o lembrete WA.</summary>
    public DateTimeOffset? ReminderSentAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ── Estado ──────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Um de <see cref="AppointmentStatus"/>: pending_confirmation, confirmed, cancelled, no_show.
    /// </summary>
    public string Status { get; set; } = AppointmentStatus.PendingConfirmation;

    /// <summary>
    /// Autoritativo no backend (FR-020). Um de <see cref="Domain.Agenda.ClientType"/>.
    /// </summary>
    public string ClientType { get; set; } = Domain.Agenda.ClientType.NewClient;

    /// <summary>Um de <see cref="AppointmentCreatedBy"/>: ai, attendant.</summary>
    public string CreatedBy { get; set; } = AppointmentCreatedBy.Attendant;

    /// <summary>Um de <see cref="AppointmentCancelledBy"/>: client, attendant, system. Null se ativo.</summary>
    public string? CancelledBy { get; set; }

    public string? CancellationReason { get; set; }

    /// <summary>Anotações internas do atendente — não exibidas ao cliente.</summary>
    public string? Notes { get; set; }

    // Navigation (loaded explicitly)
    public Professional? Professional { get; set; }
    public Service? Service { get; set; }
    public Contact? Contact { get; set; }
    public Ticket? Ticket { get; set; }
}
