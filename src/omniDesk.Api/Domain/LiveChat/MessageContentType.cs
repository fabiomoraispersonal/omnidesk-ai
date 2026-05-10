namespace omniDesk.Api.Domain.LiveChat;

public enum MessageContentType
{
    Text,
    Image,
    File,
    SystemEvent,
}

public static class MessageContentTypeExtensions
{
    public static string ToWire(this MessageContentType value) => value switch
    {
        MessageContentType.Text         => "text",
        MessageContentType.Image        => "image",
        MessageContentType.File         => "file",
        MessageContentType.SystemEvent  => "system_event",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static MessageContentType ParseWire(string value) => value switch
    {
        "text"         => MessageContentType.Text,
        "image"        => MessageContentType.Image,
        "file"         => MessageContentType.File,
        "system_event" => MessageContentType.SystemEvent,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown content type."),
    };
}
