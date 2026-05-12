namespace omniDesk.Api.Domain.Contacts;

public enum ContactSourceChannel { LiveChat, WhatsApp, Manual }

public static class ContactSourceChannelExtensions
{
    public static string ToWireValue(this ContactSourceChannel c) => c switch
    {
        ContactSourceChannel.LiveChat  => "live_chat",
        ContactSourceChannel.WhatsApp  => "whatsapp",
        ContactSourceChannel.Manual    => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(c))
    };
}
