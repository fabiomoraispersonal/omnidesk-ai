using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Notifications;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Infrastructure.Metrics;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Agenda.Cancellation;

/// <summary>
/// Spec 011 T126 — executes WhatsApp "NÃO" reminder cancellation flow.
/// Returns the rendered response text to send to the client.
/// </summary>
public sealed class CancelAppointmentByClientCommand(
    AppDbContext db,
    IAppointmentEventStore eventStore,
    INotificationService notifications,
    AgendaMetrics agendaMetrics,
    ILogger<CancelAppointmentByClientCommand> logger)
{
    public async Task<string?> ExecuteAsync(
        Appointment appointment,
        string tenantSlug,
        CancellationToken ct)
    {
        // Guard: appointment must still be confirmed (race condition protection)
        var current = await db.Appointments
            .Include(a => a.Contact)
            .FirstOrDefaultAsync(a => a.Id == appointment.Id, ct);
        if (current is null || current.Status != AppointmentStatus.Confirmed)
            return null;

        var now = DateTimeOffset.UtcNow;
        current.Status = AppointmentStatus.Cancelled;
        current.CancelledBy = AppointmentCancelledBy.Client;
        current.CancelledAt = now;
        current.CancellationReason = "Cliente cancelou via WhatsApp respondendo NÃO";
        current.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Cancellation via WhatsApp executed. Tenant={Slug}, Appointment={Id}, Contact={ContactId}",
            tenantSlug, current.Id, current.ContactId);

        agendaMetrics.AppointmentCancellations.Add(1,
            new("tenant", tenantSlug), new("by", "client"), new("channel", "whatsapp"));

        // Append audit event
        await eventStore.AppendAsync(new AppointmentEvent
        {
            TenantSlug = tenantSlug,
            AppointmentId = current.Id,
            Action = "cancelled",
            ActorType = "client",
            ConversationId = current.ConversationId,
            TicketId = current.TicketId,
            Metadata = new BsonDocument
            {
                ["channel"] = "whatsapp",
                ["reason"] = current.CancellationReason,
            },
        }, ct);

        // Load agenda settings for policy texts
        var settings = await db.AgendaSettings.AsNoTracking().FirstOrDefaultAsync(ct)
                       ?? new AgendaSettings();

        var isLateCancel = (current.StartAt - now).TotalHours < settings.LateCancelWindowHours;

        // Notify attendant in-app
        var contactName = current.Contact?.Name ?? "Cliente";
        try
        {
            await notifications.NotifyAppointmentCancelledByClientAsync(
                current.TicketId, current.Id, contactName, current.StartAt, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to dispatch in-app notification for appointment cancellation {Id}.", current.Id);
        }

        // Render response text
        var dateStr = current.StartAt.ToString("dd/MM/yyyy HH:mm");
        var sb = new System.Text.StringBuilder();
        sb.Append($"Seu agendamento de {dateStr} foi cancelado.");
        if (!string.IsNullOrWhiteSpace(settings.CancellationPolicyText))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(settings.CancellationPolicyText);
        }
        if (isLateCancel && !string.IsNullOrWhiteSpace(settings.LateCancelText))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(settings.LateCancelText);
        }
        return sb.ToString();
    }
}
