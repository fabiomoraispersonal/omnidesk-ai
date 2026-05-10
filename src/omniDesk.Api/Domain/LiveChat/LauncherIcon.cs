namespace omniDesk.Api.Domain.LiveChat;

public enum LauncherIcon
{
    Chat,
    Message,
    Support,
}

public static class LauncherIconExtensions
{
    public static string ToWire(this LauncherIcon value) => value switch
    {
        LauncherIcon.Chat    => "chat",
        LauncherIcon.Message => "message",
        LauncherIcon.Support => "support",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static LauncherIcon ParseWire(string value) => value switch
    {
        "chat"    => LauncherIcon.Chat,
        "message" => LauncherIcon.Message,
        "support" => LauncherIcon.Support,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown launcher icon."),
    };
}
