namespace omniDesk.Api.Domain.Tickets;

public enum TicketChannel { LiveChat, WhatsApp, Manual }

public static class TicketChannelExtensions
{
    public static string ToWireValue(this TicketChannel c) => c switch
    {
        TicketChannel.LiveChat  => "live_chat",
        TicketChannel.WhatsApp  => "whatsapp",
        TicketChannel.Manual    => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(c))
    };
}
