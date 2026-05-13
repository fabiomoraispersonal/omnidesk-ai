namespace omniDesk.Api.Features.Agenda.Availability;

/// <summary>Spec 011 T080 — value object representing a free time slot.</summary>
public readonly record struct Slot(DateTimeOffset StartAt, DateTimeOffset EndAt);
