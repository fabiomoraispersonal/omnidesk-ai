using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.WhatsApp.Send.Commands;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Tickets.Commands;

public enum SendManualTemplateError
{
    None,
    TicketNotFound,
    NotAuthorized,
    TicketHasNoConversation,
    DelegationFailed,
}

public sealed record SendManualTemplateOutcome(
    SendManualTemplateError Error,
    SendResult? Delegated = null,
    string? Detail = null);

/// <summary>
/// Spec 010 US5 T081 — manual WhatsApp template send from the ticket screen.
/// Thin orchestrator: resolves ticket → linked conversation, enforces caller
/// authorization, delegates to Spec 008's <see cref="SendWhatsAppMessageCommand.ExecuteTemplateAsync"/>
/// for all WhatsApp-side validation (template approved, variables, session, AI guard),
/// then resets <c>has_reminder_alert</c> when the manual send is an
/// <c>appointment_reminder</c> on a ticket that had a previous failure (FR-021).
/// </summary>
public class SendManualTemplateCommand(
    AppDbContext db,
    SendWhatsAppMessageCommand whatsAppSend,
    ICurrentUser currentUser)
{
    public async Task<SendManualTemplateOutcome> ExecuteAsync(
        Guid ticketId,
        Guid templateId,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken ct)
    {
        var tenantId = currentUser.TenantId
            ?? throw new InvalidOperationException("Authenticated user without tenant id.");

        var ticket = await db.Tickets
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);
        if (ticket is null) return new(SendManualTemplateError.TicketNotFound);

        if (!IsAuthorized(ticket))
            return new(SendManualTemplateError.NotAuthorized);

        if (!ticket.ConversationId.HasValue)
            return new(SendManualTemplateError.TicketHasNoConversation);

        var userId = currentUser.UserId
            ?? throw new InvalidOperationException("Authenticated user without user id.");

        // Look up the template to (a) read its name for the alert-reset side-effect
        // and (b) translate named variables to the positional "1".."N" form Spec 008 expects.
        var template = await db.WhatsAppTemplates
            .AsNoTracking()
            .Where(t => t.Id == templateId && t.TenantId == tenantId && t.DeletedAt == null)
            .Select(t => new { t.Name, t.VariableLabels })
            .FirstOrDefaultAsync(ct);

        var positionalVars = TranslateVariables(template?.VariableLabels, variables);

        var delegated = await whatsAppSend.ExecuteTemplateAsync(
            tenantId, userId, ticket.ConversationId.Value, templateId, positionalVars, ct);

        if (delegated.Status != SendResultStatus.Sent)
        {
            return new(SendManualTemplateError.DelegationFailed,
                Delegated: delegated,
                Detail: delegated.Detail);
        }

        // FR-021: a successful appointment_reminder dispatch resets the badge.
        if (string.Equals(template?.Name, "appointment_reminder", StringComparison.OrdinalIgnoreCase)
            && ticket.HasReminderAlert)
        {
            ticket.HasReminderAlert = false;
            ticket.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new(SendManualTemplateError.None, Delegated: delegated);
    }

    /// <summary>
    /// Accepts variables keyed by either name (matching <c>VariableLabels</c>) or positional
    /// index ("1".."N"). Returns the positional form Spec 008 expects. Unknown keys are passed
    /// through so the WhatsApp send layer can reject mismatches uniformly.
    /// </summary>
    private static IReadOnlyDictionary<string, string> TranslateVariables(
        IReadOnlyList<string>? labels,
        IReadOnlyDictionary<string, string> input)
    {
        if (labels is null || labels.Count == 0) return input;

        var result = new Dictionary<string, string>(input.Count);
        for (var i = 0; i < labels.Count; i++)
        {
            var positional = (i + 1).ToString();
            if (input.TryGetValue(labels[i], out var byName))
                result[positional] = byName;
            else if (input.TryGetValue(positional, out var byIndex))
                result[positional] = byIndex;
        }

        // Preserve any extra keys the caller sent so VariableCount mismatch surfaces from Spec 008.
        foreach (var (k, v) in input)
        {
            if (!result.ContainsKey(k) && !labels.Contains(k)) result[k] = v;
        }
        return result;
    }

    private bool IsAuthorized(Ticket ticket)
    {
        // Tenant admins can send manually on any ticket; otherwise only the assigned attendant.
        if (currentUser.Role == Roles.TenantAdmin) return true;
        if (!currentUser.UserId.HasValue) return false;
        if (ticket.AttendantId is null) return false;
        // ticket.AttendantId is an attendant_id, not a user_id; we cross-reference via DB.
        return db.Attendants
            .Where(a => a.Id == ticket.AttendantId.Value && a.UserId == currentUser.UserId.Value)
            .Any();
    }
}
