namespace omniDesk.Api.Features.Notifications;

/// <summary>
/// Spec 009 Polish T180 — V1 no-op stub.
/// Spec 010 will deliver the real implementation (email, in-app, push).
/// </summary>
public interface INotificationService
{
    /// <summary>Notify an attendant that a ticket has been assigned to them.</summary>
    Task NotifyTicketAssignedAsync(Guid attendantId, Guid ticketId, string protocol, CancellationToken ct);

    /// <summary>Notify the department supervisor that a new ticket arrived unassigned.</summary>
    Task NotifyNewUnassignedTicketAsync(Guid departmentId, Guid ticketId, string protocol, CancellationToken ct);
}

/// <summary>
/// V1 no-op: logs intent, does nothing. Replaced by Spec 010 real implementation.
/// </summary>
public class NoOpNotificationService : INotificationService
{
    public Task NotifyTicketAssignedAsync(Guid attendantId, Guid ticketId, string protocol, CancellationToken ct)
        => Task.CompletedTask;

    public Task NotifyNewUnassignedTicketAsync(Guid departmentId, Guid ticketId, string protocol, CancellationToken ct)
        => Task.CompletedTask;
}
