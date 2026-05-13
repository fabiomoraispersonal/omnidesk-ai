using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Features.Agenda.Appointments;
using omniDesk.Api.Features.Agenda.Appointments.Commands;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Agenda.Tools;

/// <summary>
/// Spec 011 T115 — implements create_appointment AI tool call.
/// Backend discards AI-provided client_type; ClientTypeResolver is authoritative (research §R5).
/// </summary>
public sealed class CreateAppointmentTool(
    CreateAppointmentCommand createCommand,
    ClientTypeResolver clientTypeResolver,
    IContactRepository contacts,
    AppDbContext db)
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    public async Task<string> ExecuteAsync(string argumentsJson, ToolDispatchContext context, CancellationToken ct)
    {
        JsonElement args;
        try { args = JsonDocument.Parse(argumentsJson).RootElement; }
        catch { return Err("INVALID_ARGUMENTS"); }

        if (!TryGetGuid(args, "professional_id", out var professionalId))
            return Err(AgendaErrorCodes.ProfessionalNotFound);
        if (!TryGetGuid(args, "service_id", out var serviceId))
            return Err(AgendaErrorCodes.ServiceNotFound);
        if (!args.TryGetProperty("start_at", out var startEl)
            || !DateTimeOffset.TryParse(startEl.GetString(), out var startAt))
            return Err(AgendaErrorCodes.InvalidDateFormat);

        var clientName = args.TryGetProperty("client_name", out var nameEl) ? nameEl.GetString() : null;
        var clientPhone = args.TryGetProperty("client_phone", out var phoneEl) ? phoneEl.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(clientPhone)) return Err("INVALID_ARGUMENTS");

        // Validate professional is active
        var prof = await db.Professionals.AsNoTracking()
            .Where(p => p.Id == professionalId)
            .Select(p => new { p.IsActive, p.Name })
            .FirstOrDefaultAsync(ct);
        if (prof is null || !prof.IsActive) return Err(AgendaErrorCodes.ProfessionalNotFound);

        // Validate service is active
        var svc = await db.Services.AsNoTracking()
            .Where(s => s.Id == serviceId)
            .Select(s => new { s.DurationMinutes, s.RequiresConfirmation, s.IsActive })
            .FirstOrDefaultAsync(ct);
        if (svc is null || !svc.IsActive) return Err(AgendaErrorCodes.ServiceNotFound);

        // Validate professional offers the service
        var offersService = await db.ProfessionalServices.AsNoTracking()
            .AnyAsync(ps => ps.ProfessionalId == professionalId && ps.ServiceId == serviceId, ct);
        if (!offersService)
            return ErrMsg(AgendaErrorCodes.ProfessionalDoesNotOfferService, "Este profissional não oferece este serviço.");

        // Find or create contact by phone (E.164 normalized)
        var phoneNorm = clientPhone.TrimStart('+');
        var contact = await contacts.FindByPhoneNormalizedAsync(phoneNorm, ct);
        if (contact is null)
        {
            contact = await contacts.AddAsync(new Contact
            {
                Name = clientName,
                Phone = clientPhone,
                PhoneNormalized = phoneNorm,
                SourceChannels = ["whatsapp"],
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }, ct);
        }

        var resolvedClientType = await clientTypeResolver.ResolveAsync(contact.Id, ct);

        var result = await createCommand.ExecuteAsync(
            professionalId, serviceId, contact.Id,
            ticketId: null, conversationId: context.ThreadId,
            startAt, notes: null,
            resolvedClientType, svc.RequiresConfirmation,
            svc.DurationMinutes, AppointmentCreatedBy.Ai, context.TenantSlug, ct);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                AgendaErrorCodes.AppointmentSlotConflict =>
                    ErrMsg("APPOINTMENT_SLOT_CONFLICT", "Slot já reservado — consulte disponibilidade novamente."),
                AgendaErrorCodes.AppointmentOutsideAvailability =>
                    ErrMsg("APPOINTMENT_OUTSIDE_AVAILABILITY", "Horário fora da disponibilidade do profissional."),
                _ => Err(result.ErrorCode ?? "UNKNOWN_ERROR"),
            };
        }

        var appt = result.Appointment!;

        var tenantTz = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == context.TenantId)
            .Select(t => t.Timezone)
            .FirstOrDefaultAsync(ct) ?? "America/Sao_Paulo";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tenantTz);
        var localStart = TimeZoneInfo.ConvertTime(appt.StartAt, tz);
        var dateStr = localStart.ToString("dd/MM/yyyy 'às' HH:mm");

        return JsonSerializer.Serialize(new
        {
            appointment_id = appt.Id,
            status = appt.Status,
            client_type = resolvedClientType,
            start_at = appt.StartAt,
            end_at = appt.EndAt,
            requires_confirmation = svc.RequiresConfirmation,
            message_to_client = $"Agendamento criado para {dateStr} com {prof.Name}.",
        }, Opts);
    }

    private static bool TryGetGuid(JsonElement root, string property, out Guid value)
    {
        value = Guid.Empty;
        return root.TryGetProperty(property, out var el)
               && el.ValueKind == JsonValueKind.String
               && Guid.TryParse(el.GetString(), out value);
    }

    private static string Err(string code) => JsonSerializer.Serialize(new { error = code }, Opts);
    private static string ErrMsg(string code, string message) => JsonSerializer.Serialize(new { error = code, message }, Opts);
}
