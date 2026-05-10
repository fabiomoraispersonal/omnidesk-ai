namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// Tipos de mensagem WhatsApp suportados pelo MVP (Spec 008 §5).
/// Tipos não listados aqui são silenciosamente ignorados — ver <see cref="WaUnsupportedTypes"/>.
/// </summary>
public enum WaSupportedMessageType
{
    Text,
    Image,
    Document,
    Audio,
}

public static class WaSupportedMessageTypeExtensions
{
    public static string ToWire(this WaSupportedMessageType value) => value switch
    {
        WaSupportedMessageType.Text     => "text",
        WaSupportedMessageType.Image    => "image",
        WaSupportedMessageType.Document => "document",
        WaSupportedMessageType.Audio    => "audio",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static WaSupportedMessageType? TryParseWire(string value) => value switch
    {
        "text"     => WaSupportedMessageType.Text,
        "image"    => WaSupportedMessageType.Image,
        "document" => WaSupportedMessageType.Document,
        "audio"    => WaSupportedMessageType.Audio,
        _ => null,
    };
}
