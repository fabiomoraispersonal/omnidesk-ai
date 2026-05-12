namespace omniDesk.Api.Domain.Tickets;

public enum TicketStatus { New, InProgress, WaitingClient, Resolved, Cancelled }

public static class TicketStatusExtensions
{
    public const string New           = "new";
    public const string InProgress    = "in_progress";
    public const string WaitingClient = "waiting_client";
    public const string Resolved      = "resolved";
    public const string Cancelled     = "cancelled";

    public static string ToWireValue(this TicketStatus s) => s switch
    {
        TicketStatus.New           => New,
        TicketStatus.InProgress    => InProgress,
        TicketStatus.WaitingClient => WaitingClient,
        TicketStatus.Resolved      => Resolved,
        TicketStatus.Cancelled     => Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(s))
    };

    public static bool IsTerminal(this TicketStatus s) =>
        s == TicketStatus.Resolved || s == TicketStatus.Cancelled;

    public static bool IsActive(this TicketStatus s) =>
        s == TicketStatus.New || s == TicketStatus.InProgress || s == TicketStatus.WaitingClient;
}
