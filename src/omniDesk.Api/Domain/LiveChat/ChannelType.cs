namespace omniDesk.Api.Domain.LiveChat;

public enum ChannelType
{
    LiveChat,
    WhatsApp,
}

public static class ChannelTypeExtensions
{
    public static string ToWire(this ChannelType value) => value switch
    {
        ChannelType.LiveChat => "live_chat",
        ChannelType.WhatsApp => "whatsapp",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static ChannelType ParseWire(string value) => value switch
    {
        "live_chat" => ChannelType.LiveChat,
        "whatsapp"  => ChannelType.WhatsApp,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown channel type."),
    };
}
