using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Agenda.Appointments.Commands;
using omniDesk.Api.Features.Agenda.Appointments.Queries;
using omniDesk.Api.Features.Agenda.Validators;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Agenda.Appointments;

/// <summary>
/// Spec 011 T098 — REST surface for appointments.
/// Mounted as <c>app.MapGroup("/api/appointments").MapAppointmentsEndpoints().RequireAuthorization()</c>.
/// Visibility filtered per <see cref="IAppointmentVisibilityPolicy"/>.
/// </summary>
public static class AppointmentsEndpoints
{
    public record CreateRequest(
        Guid ProfessionalId,
        Guid ServiceId,
        Guid? ContactId,
        Guid? TicketId,
        Guid? ConversationId,
        DateTimeOffset StartAt,
        string? Notes);

    public record UpdateRequest(
        Guid ProfessionalId,
        Guid ServiceId,
        Guid? ContactId,
        DateTimeOffset StartAt,
        string? Notes);

    public record CancelRequest(string? CancellationReason);

    public static RouteGroupBuilder MapAppointmentsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/",                           ListAsync).WithName("Appointments_List");
        group.MapGet("/{id:guid}",                  GetAsync).WithName("Appointments_Get");
        group.MapPost("/",                          CreateAsync).WithName("Appointments_Create");
        group.MapPut("/{id:guid}",                  UpdateAsync).WithName("Appointments_Update");
        group.MapPatch("/{id:guid}/confirm",        ConfirmAsync).WithName("Appointments_Confirm");
        group.MapPatch("/{id:guid}/cancel",         CancelAsync).WithName("Appointments_Cancel");
        group.MapPatch("/{id:guid}/no-show",        NoShowAsync).WithName("Appointments_NoShow");
        group.MapPost("/{id:guid}/resend-reminder", ResendReminderAsync).WithName("Appointments_ResendReminder");
        return group;
    }

    // ── GET /api/appointments ─────────────────────────────────────────

    private static async Task<IResult> ListAsync(
        ListAppointmentsQuery query,
        IAppointmentVisibilityPolicy visibility,
        ICurrentUser caller,
        Guid? professional_id,
        Guid? service_id,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? per_page,
        string? sort,
        string? order,
        CancellationToken ct)
    {
        var (items, total) = await query.ExecuteAsync(
            professional_id, service_id, status, from, to,
            sort, order,
            page ?? 1, per_page ?? 20,
            "start_at", "asc", ct);

        var visible = items.Where(a => visibility.CanView(caller, a)).ToList();

        return Results.Ok(new
        {
            success = true,
            data    = visible.Select(AppointmentDto),
            meta    = new { page = page ?? 1, per_page = per_page ?? 20, total },
        });
    }

    // ── GET /api/appointments/{id} ────────────────────────────────────

    private static async Task<IResult> GetAsync(
        Guid id,
        GetAppointmentQuery query,
        IAppointmentVisibilityPolicy visibility,
        ICurrentUser caller,
        CancellationToken ct)
    {
        var appt = await query.ExecuteAsync(id, ct);
        if (appt is null || !visibility.CanView(caller, appt))
            return Results.NotFound(Error(AgendaErrorCodes.AppointmentNotFound, "Appointment not found."));

        return Results.Ok(new { success = true, data = AppointmentDto(appt) });
    }

    // ── POST /api/appointments ────────────────────────────────────────

    private static async Task<IResult> CreateAsync(
        CreateRequest request,
        ICurrentUser caller,
        CreateAppointmentCommand command,
        CreateAppointmentValidator validator,
        ClientTypeResolver clientTypeResolver,
        ServiceRepository serviceRepo,
        AppDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        var validation = validator.Validate(new CreateAppointmentValidator.Request(
            request.ProfessionalId, request.ServiceId, request.ContactId,
            request.StartAt, request.Notes));
        if (!validation.IsValid)
            return ValidationErrors(validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }));

        var service = await serviceRepo.GetByIdAsync(request.ServiceId, ct);
        if (service is null || !service.IsActive)
            return Results.NotFound(Error(AgendaErrorCodes.ServiceNotFound, "Service not found."));

        var offersService = await db.ProfessionalServices.AsNoTracking()
            .AnyAsync(ps => ps.ProfessionalId == request.ProfessionalId && ps.ServiceId == request.ServiceId, ct);
        if (!offersService)
            return Results.UnprocessableEntity(Error(
                AgendaErrorCodes.ProfessionalDoesNotOfferService,
                "Professional does not offer this service."));

        var clientType = await clientTypeResolver.ResolveAsync(request.ContactId, ct);
        var createdBy  = caller.Role is Roles.TenantAdmin or Roles.Attendant or Roles.Supervisor
            ? AppointmentCreatedBy.Attendant
            : AppointmentCreatedBy.Ai;

        var result = await command.ExecuteAsync(
            request.ProfessionalId, request.ServiceId, request.ContactId,
            request.TicketId, request.ConversationId, request.StartAt,
            request.Notes, clientType, service.RequiresConfirmation,
            service.DurationMinutes, createdBy, caller.TenantSlug, ct);

        if (!result.Success)
        {
            return result.ErrorCode == AgendaErrorCodes.AppointmentSlotConflict
                ? Results.Conflict(Error(result.ErrorCode, "The requested slot is not available."))
                : Results.UnprocessableEntity(Error(result.ErrorCode!, "Could not create appointment."));
        }

        audit.Log(caller.TenantSlug, caller.TenantId ?? Guid.Empty, AuditEventNames.AppointmentCreated,
            AuditActorFactory.FromCurrentUser(caller),
            AuditTargetFactory.Appointment(result.Appointment!.Id));

        return Results.Created($"/api/appointments/{result.Appointment!.Id}",
            new { success = true, data = AppointmentDto(result.Appointment) });
    }

    // ── PUT /api/appointments/{id} ────────────────────────────────────

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateRequest request,
        UpdateAppointmentCommand command,
        ServiceRepository serviceRepo,
        CancellationToken ct)
    {
        var service = await serviceRepo.GetByIdAsync(request.ServiceId, ct);
        if (service is null)
            return Results.NotFound(Error(AgendaErrorCodes.ServiceNotFound, "Service not found."));

        var result = await command.ExecuteAsync(
            id, request.ProfessionalId, request.ServiceId, request.ContactId,
            request.StartAt, service.DurationMinutes, request.Notes, ct);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                AgendaErrorCodes.AppointmentNotFound => Results.NotFound(Error(result.ErrorCode, "Appointment not found.")),
                AgendaErrorCodes.AppointmentSlotConflict => Results.Conflict(Error(result.ErrorCode, "The requested slot is not available.")),
                _ => Results.UnprocessableEntity(Error(result.ErrorCode!, "Cannot update appointment.")),
            };
        }

        return Results.Ok(new { success = true, data = AppointmentDto(result.Appointment!) });
    }

    // ── PATCH /api/appointments/{id}/confirm ──────────────────────────

    private static async Task<IResult> ConfirmAsync(
        Guid id,
        ICurrentUser caller,
        ConfirmAppointmentCommand command,
        IAuditService audit,
        CancellationToken ct)
    {
        var result = await command.ExecuteAsync(id, caller.UserId.GetValueOrDefault(), ct);
        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                AgendaErrorCodes.AppointmentNotFound => Results.NotFound(Error(result.ErrorCode, "Appointment not found.")),
                _ => Results.UnprocessableEntity(Error(result.ErrorCode!, "Cannot confirm appointment.")),
            };
        }

        audit.Log(caller.TenantSlug, caller.TenantId ?? Guid.Empty, AuditEventNames.AppointmentConfirmed,
            AuditActorFactory.FromCurrentUser(caller),
            AuditTargetFactory.Appointment(result.Appointment!.Id));

        return Results.Ok(new { success = true, data = AppointmentDto(result.Appointment!) });
    }

    // ── PATCH /api/appointments/{id}/cancel ───────────────────────────

    private static async Task<IResult> CancelAsync(
        Guid id,
        CancelRequest request,
        ICurrentUser caller,
        CancelAppointmentCommand command,
        CancelAppointmentValidator validator,
        IAuditService audit,
        CancellationToken ct)
    {
        var validation = validator.Validate(new CancelAppointmentValidator.Request(request.CancellationReason));
        if (!validation.IsValid)
            return ValidationErrors(validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }));

        var result = await command.ExecuteAsync(
            id, AppointmentCancelledBy.Attendant, request.CancellationReason, caller.UserId.GetValueOrDefault(), ct);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                AgendaErrorCodes.AppointmentNotFound => Results.NotFound(Error(result.ErrorCode, "Appointment not found.")),
                _ => Results.UnprocessableEntity(Error(result.ErrorCode!, "Cannot cancel appointment.")),
            };
        }

        audit.Log(caller.TenantSlug, caller.TenantId ?? Guid.Empty, AuditEventNames.AppointmentCancelled,
            AuditActorFactory.FromCurrentUser(caller),
            AuditTargetFactory.Appointment(result.Appointment!.Id),
            metadata: new { cancelled_by = "attendant" });

        return Results.Ok(new { success = true, data = AppointmentDto(result.Appointment!) });
    }

    // ── PATCH /api/appointments/{id}/no-show ──────────────────────────

    private static async Task<IResult> NoShowAsync(
        Guid id,
        ICurrentUser caller,
        MarkNoShowCommand command,
        IAuditService audit,
        CancellationToken ct)
    {
        var result = await command.ExecuteAsync(id, caller.UserId.GetValueOrDefault(), ct);
        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                AgendaErrorCodes.AppointmentNotFound => Results.NotFound(Error(result.ErrorCode, "Appointment not found.")),
                _ => Results.UnprocessableEntity(Error(result.ErrorCode!, "Cannot mark appointment as no-show.")),
            };
        }

        audit.Log(caller.TenantSlug, caller.TenantId ?? Guid.Empty, AuditEventNames.AppointmentNoShow,
            AuditActorFactory.FromCurrentUser(caller),
            AuditTargetFactory.Appointment(result.Appointment!.Id));

        return Results.Ok(new { success = true, data = AppointmentDto(result.Appointment!) });
    }

    // ── POST /api/appointments/{id}/resend-reminder ───────────────────

    private static async Task<IResult> ResendReminderAsync(
        Guid id,
        ResendReminderCommand command,
        CancellationToken ct)
    {
        var result = await command.ExecuteAsync(id, ct);
        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                AgendaErrorCodes.AppointmentNotFound => Results.NotFound(Error(result.ErrorCode, "Appointment not found.")),
                AgendaErrorCodes.ContactHasNoPhone   => Results.UnprocessableEntity(Error(result.ErrorCode, "Contact has no phone number.")),
                _ => Results.UnprocessableEntity(Error(result.ErrorCode!, "Cannot resend reminder.")),
            };
        }
        return Results.Ok(new
        {
            success = true,
            data    = new { reminder_sent_at = result.ReminderSentAt },
        });
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static object AppointmentDto(Appointment a) => new
    {
        id                  = a.Id,
        professional_id     = a.ProfessionalId,
        service_id          = a.ServiceId,
        contact_id          = a.ContactId,
        ticket_id           = a.TicketId,
        conversation_id     = a.ConversationId,
        start_at            = a.StartAt,
        end_at              = a.EndAt,
        status              = a.Status,
        client_type         = a.ClientType,
        created_by          = a.CreatedBy,
        notes               = a.Notes,
        reminder_sent_at    = a.ReminderSentAt,
        cancelled_by        = a.CancelledBy,
        cancelled_at        = a.CancelledAt,
        cancellation_reason = a.CancellationReason,
        created_at          = a.CreatedAt,
        updated_at          = a.UpdatedAt,
        professional = a.Professional is null ? null : (object)new
        {
            id   = a.Professional.Id,
            name = a.Professional.Name,
        },
        service = a.Service is null ? null : (object)new
        {
            id               = a.Service.Id,
            name             = a.Service.Name,
            duration_minutes = a.Service.DurationMinutes,
            price            = a.Service.Price,
        },
    };

    private static object Error(string code, string message) => new
    {
        success = false,
        error   = new { code, message },
    };

    private static IResult ValidationErrors(IEnumerable<object> errors) =>
        Results.UnprocessableEntity(new
        {
            success = false,
            error   = new { code = AgendaErrorCodes.ValidationFailed, message = "Validation failed.", details = errors },
        });
}
