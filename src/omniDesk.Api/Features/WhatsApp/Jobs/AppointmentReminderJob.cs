using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.Notifications.Handlers;
using omniDesk.Api.Features.WhatsApp.Adapters;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Appointments;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Metrics;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Features.WhatsApp.Jobs;

/// <summary>
/// Spec 010 US4 T077 — daily WhatsApp <c>appointment_reminder</c> sender.
///
/// Scheduled per tenant via <see cref="omniDesk.Api.Features.Notifications.Schedulers.IAppointmentReminderScheduler"/>.
/// The cron expression and timezone are computed from <c>tenant_notification_settings.reminder_time</c>
/// and <c>public.tenants.timezone</c>.
///
/// Flow per appointment (FR-017 / FR-018 / FR-019 / FR-020):
///   1. Resolve tenant context + verify reminder_enabled and channel active.
///   2. Look up the approved <c>appointment_reminder</c> template (skip whole run if missing).
///   3. Iterate tomorrow's appointments. For each:
///      a. Idempotency check via Redis NX <c>{slug}:reminder_sent:{appointment_id}:{yyyyMMdd}</c>.
///      b. Resolve contact (phone, name); fail if absent.
///      c. Resolve conversation (open WhatsApp conversation for this contact); fail if absent.
///      d. Dispatch template via <see cref="WhatsAppOutgoingAdapter.DispatchTemplateAsync"/>
///         with attendantId=null (system-initiated). Adapter routes through Meta + records
///         status + emits WS events.
///      e. On any failure, route through <see cref="ReminderFailedHandler"/>.
/// </summary>
public class AppointmentReminderJob(
    AppDbContext db,
    IConnectionMultiplexer redis,
    IAppointmentReadRepository appointments,
    WhatsAppOutgoingAdapter outgoingAdapter,
    ReminderFailedHandler failureHandler,
    TenantContextHolder tenantContext,
    NotificationMetrics metrics,
    ILogger<AppointmentReminderJob> logger)
{
    private const int IdempotencyTtlHours = 48;

    /// <summary>Hangfire entry point — invoked per tenant via its dedicated recurring job.</summary>
    public async Task RunAsync(string tenantSlug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            logger.LogWarning("AppointmentReminderJob: empty tenant slug; skipping.");
            return;
        }

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == tenantSlug, ct);
        if (tenant is null)
        {
            logger.LogWarning(
                "AppointmentReminderJob: tenant {Slug} not found; skipping.", tenantSlug);
            return;
        }

        tenantContext.Set(tenant.Slug, tenant.Id);

        // (1) Settings gate (reminder toggle).
        var settings = await db.TenantNotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null || !settings.ReminderEnabled)
        {
            logger.LogDebug(
                "AppointmentReminderJob: reminder disabled for tenant {Slug}; skipping.", tenantSlug);
            return;
        }

        // (2) Channel + template preconditions.
        var config = await db.WhatsAppConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenant.Id, ct);
        if (config is null || !config.IsEnabled)
        {
            logger.LogInformation(
                "AppointmentReminderJob: tenant {Slug} has no active WhatsApp channel; skipping run.",
                tenantSlug);
            return;
        }

        var template = await db.WhatsAppTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t =>
                t.TenantId == tenant.Id
                && t.Name == "appointment_reminder"
                && t.Status == TemplateStatus.Approved
                && t.DeletedAt == null, ct);
        if (template is null)
        {
            logger.LogInformation(
                "AppointmentReminderJob: tenant {Slug} has no approved appointment_reminder template; skipping run.",
                tenantSlug);
            return;
        }

        // (3) Compute "tomorrow" in tenant local time. We use the tenant timezone if available,
        // otherwise UTC. Hangfire's cron + TimeZone option ensures the job fires at the right
        // local hour; we still compute the date in the same zone to avoid off-by-one bugs.
        var localTomorrow = ComputeLocalTomorrow(tenant.Timezone);
        var dateTag = localTomorrow.ToString("yyyyMMdd");

        var appts = await appointments.GetForDateAsync(tenant.Slug, localTomorrow, ct);
        if (appts.Count == 0)
        {
            logger.LogDebug(
                "AppointmentReminderJob: no appointments for {Slug} on {Date}.",
                tenantSlug, localTomorrow);
            return;
        }

        logger.LogInformation(
            "AppointmentReminderJob: processing {Count} appointment(s) for {Slug} on {Date}.",
            appts.Count, tenantSlug, localTomorrow);

        var redisDb = redis.GetDatabase();
        var sent = 0;
        var failed = 0;

        foreach (var appt in appts)
        {
            // Idempotency: only one attempt per day per appointment (FR-018).
            var idemKey = RedisKeys.ReminderSent(tenant.Slug, appt.Id, dateTag);
            try
            {
                var won = await redisDb.StringSetAsync(
                    idemKey, "1",
                    TimeSpan.FromHours(IdempotencyTtlHours),
                    When.NotExists);
                if (!won) continue;
            }
            catch (Exception ex)
            {
                // Redis hiccup → log and continue conservatively; a duplicate is preferred over a miss.
                logger.LogWarning(ex,
                    "AppointmentReminderJob: Redis NX failed for appointment {ApptId}; proceeding without idempotency.",
                    appt.Id);
            }

            try
            {
                var dispatched = await TryDispatchAsync(tenant, appt, template, ct);
                if (dispatched) sent++;
                else failed++;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex,
                    "AppointmentReminderJob: unexpected error processing appointment {ApptId} for tenant {Slug}.",
                    appt.Id, tenantSlug);

                await RecordFailureAsync(tenant.Slug, appt, "unexpected_error", "Cliente", ct);
            }
        }

        if (sent > 0)
            metrics.RemindersSent.Add(sent, new KeyValuePair<string, object?>("tenant_slug", tenant.Slug));
        if (failed > 0)
            metrics.RemindersFailed.Add(failed,
                new KeyValuePair<string, object?>("tenant_slug", tenant.Slug),
                new KeyValuePair<string, object?>("reason", "mixed"));

        logger.LogInformation(
            "AppointmentReminderJob: tenant {Slug} sent={Sent} failed={Failed}.",
            tenant.Slug, sent, failed);
    }

    /// <summary>
    /// Returns true if a reminder was successfully enqueued; false when a precondition failed
    /// (no contact, no phone, no open conversation, etc.). In all failure cases, the failure
    /// handler has already been invoked.
    /// </summary>
    private async Task<bool> TryDispatchAsync(
        Tenant tenant,
        AppointmentReminderDto appt,
        WhatsAppTemplate template,
        CancellationToken ct)
    {
        // Contact + phone required.
        if (!appt.ContactId.HasValue)
        {
            await RecordFailureAsync(tenant.Slug, appt, "no_contact", "Cliente", ct);
            return false;
        }

        var contact = await db.Contacts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == appt.ContactId.Value, ct);

        var contactName = contact?.Name ?? "Cliente";

        if (contact is null || string.IsNullOrWhiteSpace(contact.Phone))
        {
            await RecordFailureAsync(tenant.Slug, appt, "no_phone", contactName, ct);
            return false;
        }

        // Resolve a conversation to attach the outgoing message to.
        var conv = await db.Conversations.AsNoTracking()
            .Where(c => c.Channel == ChannelType.WhatsApp
                        && c.ContactId == appt.ContactId.Value
                        && c.Status != ConversationStatus.Resolved)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (conv is null)
        {
            await RecordFailureAsync(tenant.Slug, appt, "no_open_conversation", contactName, ct);
            return false;
        }

        // Build template variables. The template has positional labels {{1}}..{{N}}.
        // We provide best-effort values from appointment + contact; missing labels become "—".
        var variables = BuildVariables(template, contact, appt);
        if (variables.Count != template.VariableCount)
        {
            await RecordFailureAsync(tenant.Slug, appt, "variable_count_mismatch", contactName, ct);
            return false;
        }

        try
        {
            // attendantId=null marks this as a system-initiated send.
            await outgoingAdapter.DispatchTemplateAsync(conv.Id, template, variables, attendantId: null, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "AppointmentReminderJob: adapter dispatch failed for appointment {ApptId}.", appt.Id);
            await RecordFailureAsync(tenant.Slug, appt,
                reason: $"dispatch_error: {ex.GetType().Name}",
                contactName, ct);
            return false;
        }
    }

    private async Task RecordFailureAsync(
        string tenantSlug,
        AppointmentReminderDto appt,
        string reason,
        string contactName,
        CancellationToken ct)
    {
        try
        {
            await failureHandler.HandleAsync(
                tenantSlug, appt.Id, appt.TicketId, appt.ContactId,
                appt.DepartmentId, contactName, reason, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "AppointmentReminderJob: failure-handler crashed for appointment {ApptId}.", appt.Id);
        }
    }

    /// <summary>
    /// Default variable layout when the tenant uses the predefined <c>appointment_reminder</c>
    /// template. The template carries N positional variables; we fill the first N with
    /// (name, time, professional, date), padding with "—" if the template has more.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildVariables(
        WhatsAppTemplate template,
        Domain.Contacts.Contact contact,
        AppointmentReminderDto appt)
    {
        var values = new[]
        {
            contact.Name ?? "Cliente",
            appt.ScheduledFor.ToString("HH:mm"),
            appt.ProfessionalName ?? "—",
            appt.ScheduledFor.ToString("dd/MM"),
        };

        var result = new Dictionary<string, string>(template.VariableCount);
        for (var i = 0; i < template.VariableCount; i++)
        {
            result[(i + 1).ToString()] = i < values.Length ? values[i] : "—";
        }
        return result;
    }

    private static DateOnly ComputeLocalTomorrow(string? tenantTimezone)
    {
        var tz = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(tenantTimezone))
        {
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(tenantTimezone); }
            catch (TimeZoneNotFoundException) { /* fall back to UTC */ }
            catch (InvalidTimeZoneException)  { /* fall back to UTC */ }
        }

        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        return DateOnly.FromDateTime(local.DateTime.AddDays(1));
    }
}
