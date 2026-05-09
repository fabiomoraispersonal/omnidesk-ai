namespace omniDesk.Api.Domain.LiveChat;

public enum WidgetPosition
{
    BottomRight,
    BottomLeft,
}

public static class WidgetPositionExtensions
{
    public static string ToWire(this WidgetPosition value) => value switch
    {
        WidgetPosition.BottomRight => "bottom_right",
        WidgetPosition.BottomLeft  => "bottom_left",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static WidgetPosition ParseWire(string value) => value switch
    {
        "bottom_right" => WidgetPosition.BottomRight,
        "bottom_left"  => WidgetPosition.BottomLeft,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown widget position."),
    };
}
