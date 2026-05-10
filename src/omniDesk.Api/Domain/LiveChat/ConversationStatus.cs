namespace omniDesk.Api.Domain.LiveChat;

public enum ConversationStatus
{
    Open,
    Resolved,
    Abandoned,
}

public static class ConversationStatusExtensions
{
    public static string ToWire(this ConversationStatus value) => value switch
    {
        ConversationStatus.Open      => "open",
        ConversationStatus.Resolved  => "resolved",
        ConversationStatus.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static ConversationStatus ParseWire(string value) => value switch
    {
        "open"      => ConversationStatus.Open,
        "resolved"  => ConversationStatus.Resolved,
        "abandoned" => ConversationStatus.Abandoned,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown conversation status."),
    };
}
