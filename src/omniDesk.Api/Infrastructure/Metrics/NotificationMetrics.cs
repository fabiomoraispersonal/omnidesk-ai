using System.Diagnostics.Metrics;

namespace omniDesk.Api.Infrastructure.Metrics;

/// <summary>
/// Spec 010 Polish T100 — counters/gauges for the notifications subsystem.
/// Uses <c>System.Diagnostics.Metrics</c> (built-in .NET 10) so values are exposed
/// to any OpenTelemetry / Prometheus exporter wired in later without code changes.
/// Counters are incremented at the call sites that own the side-effect.
/// </summary>
public sealed class NotificationMetrics : IDisposable
{
    public const string MeterName = "omnidesk.notifications";

    private readonly Meter _meter;

    public Counter<long> NotificationsDelivered { get; }
    public Counter<long> PushFailures { get; }
    public Counter<long> RemindersSent { get; }
    public Counter<long> RemindersFailed { get; }

    public NotificationMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        NotificationsDelivered = _meter.CreateCounter<long>(
            "notifications_delivered_total",
            unit: "{notification}",
            description: "Total in-app notifications persisted, tagged by event_type.");

        PushFailures = _meter.CreateCounter<long>(
            "notifications_push_failed_total",
            unit: "{push}",
            description: "Total Web Push deliveries that failed (status code or unknown).");

        RemindersSent = _meter.CreateCounter<long>(
            "reminders_sent_total",
            unit: "{reminder}",
            description: "Total WhatsApp appointment_reminder messages enqueued by AppointmentReminderJob.");

        RemindersFailed = _meter.CreateCounter<long>(
            "reminders_failed_total",
            unit: "{reminder}",
            description: "Total reminder failures, tagged by reason (no_phone, no_template, channel_disabled, etc.).");
    }

    public void Dispose() => _meter.Dispose();
}
