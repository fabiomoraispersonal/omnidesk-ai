namespace omniDesk.Api.Domain.Tickets;

public enum TicketPriority { Low, Normal, High, Urgent }

public static class TicketPriorityExtensions
{
    public static string ToWireValue(this TicketPriority p) => p switch
    {
        TicketPriority.Low    => "low",
        TicketPriority.Normal => "normal",
        TicketPriority.High   => "high",
        TicketPriority.Urgent => "urgent",
        _ => throw new ArgumentOutOfRangeException(nameof(p))
    };
}
