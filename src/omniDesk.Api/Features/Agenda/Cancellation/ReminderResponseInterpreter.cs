using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Metrics;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Agenda.Cancellation;

/// <summary>
/// Spec 011 T123 — pure interpreter: decides whether an incoming WhatsApp text is a
/// "NÃO" reminder response cancelling a confirmed appointment (research §R11).
/// Stateless except for a single DB read.
/// </summary>
public sealed class ReminderResponseInterpreter(AppDbContext db, AgendaMetrics metrics)
{
    private const string NaoNormalized = "nao";
    private static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(26);

    public async Task<Outcome> TryInterpretAsync(
        Guid conversationId,
        string messageText,
        CancellationToken ct)
    {
        if (Normalize(messageText) != NaoNormalized)
            return Outcome.NotApplicable;

        var windowStart = DateTimeOffset.UtcNow - ReminderWindow;

        var appointment = await db.Appointments
            .Where(a => a.ConversationId == conversationId
                     && a.Status == AppointmentStatus.Confirmed
                     && a.ReminderSentAt != null
                     && a.ReminderSentAt >= windowStart)
            .OrderBy(a => a.StartAt)
            .FirstOrDefaultAsync(ct);

        if (appointment is null)
        {
            metrics.ReminderResponseNo.Add(1, new KeyValuePair<string, object?>("outcome", "outside_window"));
            return Outcome.OutsideWindow;
        }

        metrics.ReminderResponseNo.Add(1, new KeyValuePair<string, object?>("outcome", "cancelled"));
        return new Outcome.Cancelled(appointment);
    }

    private static string Normalize(string text)
    {
        var trimmed = text.Trim().ToLowerInvariant();
        var normalized = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>Result of interpreting an incoming WhatsApp message.</summary>
public abstract class Outcome
{
    private Outcome() { }

    /// <summary>Message is not a "NÃO" response — process as normal.</summary>
    public static readonly Outcome NotApplicable = new NotApplicableOutcome();

    /// <summary>Message is "NÃO" but no eligible appointment found in the 26h window.</summary>
    public static readonly Outcome OutsideWindow = new OutsideWindowOutcome();

    public sealed class NotApplicableOutcome : Outcome { }
    public sealed class OutsideWindowOutcome : Outcome { }

    /// <summary>Confirmed appointment found — cancellation should be executed.</summary>
    public sealed class Cancelled(omniDesk.Api.Domain.Agenda.Appointment appointment) : Outcome
    {
        public omniDesk.Api.Domain.Agenda.Appointment Appointment { get; } = appointment;
    }
}
