using omniDesk.Api.Features.Notifications;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Test-only no-op implementation of <see cref="INotificationService"/>. Used by integration
/// tests that exercise upstream behavior (TicketCreationGateway, etc.) and don't want
/// notification side-effects. Spec 010 retired the production NoOp in favor of the real service
/// (research §R8); this stub lives in the test assembly so legacy tests keep working without
/// resurrecting the prod stub.
/// </summary>
public sealed class NoOpNotificationService : INotificationService
{
    public Task NotifyTicketAssignedAsync(Guid attendantId, Guid ticketId, string protocol, CancellationToken ct)
        => Task.CompletedTask;

    public Task NotifyNewUnassignedTicketAsync(Guid departmentId, Guid ticketId, string protocol, CancellationToken ct)
        => Task.CompletedTask;

    public Task NotifyTicketTransferredAsync(
        Guid toAttendantId, Guid ticketId, string protocol,
        Guid? fromAttendantId, string? fromAttendantName, CancellationToken ct)
        => Task.CompletedTask;

    public Task NotifyNewMessageAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string contactName, string snippet, CancellationToken ct)
        => Task.CompletedTask;

    public Task NotifyClientRepliedAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string contactName, CancellationToken ct)
        => Task.CompletedTask;

    public Task NotifySlaWarningAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string slaType, CancellationToken ct)
        => Task.CompletedTask;

    public Task NotifySlaBreachedAsync(
        Guid ticketId, string protocol, Guid departmentId, Guid? attendantId,
        CancellationToken ct)
        => Task.CompletedTask;

    public Task NotifyTicketQueuedAsync(
        Guid ticketId, string protocol, Guid departmentId, CancellationToken ct)
        => Task.CompletedTask;

    public Task NotifyReminderFailedAsync(
        Guid attendantId, Guid ticketId, string protocol,
        string contactName, string reason, CancellationToken ct)
        => Task.CompletedTask;
}
