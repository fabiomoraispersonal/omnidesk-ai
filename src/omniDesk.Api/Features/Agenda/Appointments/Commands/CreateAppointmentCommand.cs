using omniDesk.Api.Domain.Agenda;

using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Appointments.Commands;

public record CreateResult(bool Success, string? ErrorCode, string? ErrorLayer, Appointment? Appointment);

/// <summary>
/// Spec 011 T088 — creates an appointment with 3-layer race protection (research §R2):
/// 1. Redis SETNX lock via AppointmentSlotLockService.TryAcquireAsync.
/// 2. Revalidation (SELECT FOR UPDATE) handled by AppointmentRepository.GetConflictingSlotIdsAsync.
/// 3. UNIQUE constraint violation (23505) → APPOINTMENT_SLOT_CONFLICT.
/// </summary>
public sealed class CreateAppointmentCommand(
    AppointmentRepository repository,
    AppointmentSlotLockService? slotLock,
    object? notificationSvc)
{
    /// <summary>Full 3-layer execution used by endpoints.</summary>
    public async Task<CreateResult> ExecuteAsync(
        Guid professionalId, Guid serviceId, Guid? contactId,
        Guid? ticketId, Guid? conversationId, DateTimeOffset startAt,
        string? notes, string clientType, bool requiresConfirmation,
        int durationMinutes, string createdBy, string tenantSlug,
        CancellationToken ct)
    {
        // Layer 1 — Redis SETNX
        IAsyncDisposable? lease = null;
        if (slotLock is not null)
        {
            lease = await slotLock.TryAcquireAsync(tenantSlug, professionalId, startAt,
                Guid.NewGuid().ToString(), ct);
            if (lease is null)
                return new CreateResult(false, AgendaErrorCodes.AppointmentSlotConflict, "redis", null);
        }

        try
        {
            // Layer 2 — revalidate inside implicit transaction
            var endAt     = startAt.AddMinutes(durationMinutes);
            var conflicts = await repository.GetConflictingSlotIdsAsync(
                professionalId, startAt, endAt, null, ct);
            if (conflicts.Count > 0)
                return new CreateResult(false, AgendaErrorCodes.AppointmentSlotConflict, "for_update", null);

            var status = DetermineStatus(clientType, requiresConfirmation);
            var appointment = new Appointment
            {
                ProfessionalId = professionalId,
                ServiceId      = serviceId,
                ContactId      = contactId,
                TicketId       = ticketId,
                ConversationId = conversationId,
                StartAt        = startAt,
                EndAt          = endAt,
                Status         = status,
                ClientType     = clientType,
                CreatedBy      = createdBy,
                Notes          = notes,
            };

            // Layer 3 — UNIQUE constraint catches concurrent inserts that race past layers 1+2
            var (saved, errorCode) = await repository.TryAddAsync(appointment, ct);
            if (saved is null)
                return new CreateResult(false, errorCode!, "unique_violation", null);

            return new CreateResult(true, null, null, saved);
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync();
        }
    }

    /// <summary>Direct insert without Redis lock — used by tests and seeding helpers.</summary>
    public async Task<Appointment> ExecuteDirectAsync(
        Guid professionalId, Guid serviceId, Guid? contactId,
        Guid? ticketId, Guid? conversationId, DateTimeOffset startAt,
        string? notes, string clientType, bool requiresConfirmation,
        int durationMinutes, string createdBy, CancellationToken ct)
    {
        var endAt  = startAt.AddMinutes(durationMinutes);
        var status = DetermineStatus(clientType, requiresConfirmation);

        var appointment = new Appointment
        {
            ProfessionalId = professionalId,
            ServiceId      = serviceId,
            ContactId      = contactId,
            TicketId       = ticketId,
            ConversationId = conversationId,
            StartAt        = startAt,
            EndAt          = endAt,
            Status         = status,
            ClientType     = clientType,
            CreatedBy      = createdBy,
            Notes          = notes,
        };

        return await repository.AddAsync(appointment, ct);
    }

    /// <summary>TryExecute variant used by concurrent creation tests.</summary>
    public async Task<CreateResult> TryExecuteDirectAsync(
        Guid professionalId, Guid serviceId, Guid? contactId,
        Guid? ticketId, Guid? conversationId, DateTimeOffset startAt,
        string? notes, string clientType, bool requiresConfirmation,
        int durationMinutes, string createdBy, CancellationToken ct)
    {
        var endAt  = startAt.AddMinutes(durationMinutes);
        var status = DetermineStatus(clientType, requiresConfirmation);

        var appointment = new Appointment
        {
            ProfessionalId = professionalId,
            ServiceId      = serviceId,
            ContactId      = contactId,
            TicketId       = ticketId,
            ConversationId = conversationId,
            StartAt        = startAt,
            EndAt          = endAt,
            Status         = status,
            ClientType     = clientType,
            CreatedBy      = createdBy,
            Notes          = notes,
        };

        var (saved, errorCode) = await repository.TryAddAsync(appointment, ct);
        return saved is not null
            ? new CreateResult(true, null, null, saved)
            : new CreateResult(false, errorCode!, "unique_violation", null);
    }

    private static string DetermineStatus(string clientType, bool requiresConfirmation)
    {
        if (requiresConfirmation || clientType == ClientType.NewClient)
            return AppointmentStatus.PendingConfirmation;
        return AppointmentStatus.Confirmed;
    }
}
