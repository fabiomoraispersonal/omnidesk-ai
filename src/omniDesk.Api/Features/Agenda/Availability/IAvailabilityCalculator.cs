namespace omniDesk.Api.Features.Agenda.Availability;

public interface IAvailabilityCalculator
{
    Task<IReadOnlyList<Slot>> GetSlotsAsync(
        Guid professionalId,
        Guid serviceId,
        DateOnly date,
        string tenantTimezone,
        CancellationToken ct);
}
