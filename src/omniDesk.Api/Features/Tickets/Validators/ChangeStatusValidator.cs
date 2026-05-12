using FluentValidation;
using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Features.Tickets.Validators;

public record ChangeStatusRequest(string Status, string? Reason = null);

public class ChangeStatusValidator : AbstractValidator<ChangeStatusRequest>
{
    private static readonly string[] ValidStatuses =
    [
        "in_progress", "waiting_client", "new"
    ];

    public ChangeStatusValidator()
    {
        RuleFor(r => r.Status)
            .NotEmpty()
            .Must(s => ValidStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}");
    }
}

// Encodes the valid transition matrix from data-model.md §Transições
public static class TicketStatusTransitions
{
    private static readonly Dictionary<TicketStatus, HashSet<TicketStatus>> Allowed = new()
    {
        [TicketStatus.New]           = [TicketStatus.InProgress, TicketStatus.Cancelled],
        [TicketStatus.InProgress]    = [TicketStatus.WaitingClient, TicketStatus.Resolved, TicketStatus.Cancelled],
        [TicketStatus.WaitingClient] = [TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Cancelled],
        [TicketStatus.Resolved]      = [],
        [TicketStatus.Cancelled]     = [],
    };

    public static bool IsAllowed(TicketStatus from, TicketStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static TicketStatus? Parse(string wireValue) => wireValue switch
    {
        "new"            => TicketStatus.New,
        "in_progress"    => TicketStatus.InProgress,
        "waiting_client" => TicketStatus.WaitingClient,
        "resolved"       => TicketStatus.Resolved,
        "cancelled"      => TicketStatus.Cancelled,
        _                => null,
    };
}
