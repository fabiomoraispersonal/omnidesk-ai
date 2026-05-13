using System.Diagnostics.Metrics;

namespace omniDesk.Api.Infrastructure.Metrics;

/// <summary>
/// Spec 011 T140 — counters and histograms for the agenda/appointments subsystem.
/// Uses System.Diagnostics.Metrics (built-in .NET 10) for OpenTelemetry / Prometheus compatibility.
/// </summary>
public sealed class AgendaMetrics : IDisposable
{
    public const string MeterName = "omnidesk.agenda";

    private readonly Meter _meter;

    /// <summary>appointments_created_total{tenant, status, client_type, created_by}</summary>
    public Counter<long> AppointmentsCreated { get; }

    /// <summary>appointment_cancellations_total{tenant, by, channel}</summary>
    public Counter<long> AppointmentCancellations { get; }

    /// <summary>reminder_response_no_total{tenant, outcome}</summary>
    public Counter<long> ReminderResponseNo { get; }

    /// <summary>availability_query_duration_seconds (histogram)</summary>
    public Histogram<double> AvailabilityQueryDuration { get; }

    /// <summary>appointment_slot_conflict_total{tenant, layer}</summary>
    public Counter<long> SlotConflicts { get; }

    /// <summary>appointment_no_show_total{tenant}</summary>
    public Counter<long> NoShows { get; }

    public AgendaMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        AppointmentsCreated = _meter.CreateCounter<long>(
            "appointments_created_total",
            unit: "{appointment}",
            description: "Total appointments created, tagged by status, client_type and created_by.");

        AppointmentCancellations = _meter.CreateCounter<long>(
            "appointment_cancellations_total",
            unit: "{cancellation}",
            description: "Total appointment cancellations, tagged by who cancelled (attendant/client/system) and channel.");

        ReminderResponseNo = _meter.CreateCounter<long>(
            "reminder_response_no_total",
            unit: "{message}",
            description: "Total WhatsApp 'NÃO' responses detected, tagged by outcome (cancelled/outside_window/no_match).");

        AvailabilityQueryDuration = _meter.CreateHistogram<double>(
            "availability_query_duration_seconds",
            unit: "s",
            description: "Elapsed time for availability slot computation.");

        SlotConflicts = _meter.CreateCounter<long>(
            "appointment_slot_conflict_total",
            unit: "{conflict}",
            description: "Total slot conflict errors, tagged by layer (redis/for_update/unique_violation).");

        NoShows = _meter.CreateCounter<long>(
            "appointment_no_show_total",
            unit: "{no_show}",
            description: "Total appointments marked no-show.");
    }

    public void Dispose() => _meter.Dispose();
}
