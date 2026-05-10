namespace omniDesk.Api.Domain.LiveChat;

public enum MessageSenderType
{
    Visitor,
    AiAgent,
    Attendant,
    System,
}

public static class MessageSenderTypeExtensions
{
    public static string ToWire(this MessageSenderType value) => value switch
    {
        MessageSenderType.Visitor   => "visitor",
        MessageSenderType.AiAgent   => "ai_agent",
        MessageSenderType.Attendant => "attendant",
        MessageSenderType.System    => "system",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static MessageSenderType ParseWire(string value) => value switch
    {
        "visitor"   => MessageSenderType.Visitor,
        "ai_agent"  => MessageSenderType.AiAgent,
        "attendant" => MessageSenderType.Attendant,
        "system"    => MessageSenderType.System,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown sender type."),
    };
}
