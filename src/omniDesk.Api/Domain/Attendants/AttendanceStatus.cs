namespace omniDesk.Api.Domain.Attendants;

public enum AttendanceStatus
{
    Online,
    Away,
    Offline
}

public static class AttendanceStatusExtensions
{
    public const string Online = "online";
    public const string Away = "away";
    public const string Offline = "offline";

    public static string ToWireValue(this AttendanceStatus status) => status switch
    {
        AttendanceStatus.Online => Online,
        AttendanceStatus.Away => Away,
        AttendanceStatus.Offline => Offline,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown AttendanceStatus")
    };

    public static AttendanceStatus FromWireValue(string value) => value switch
    {
        Online => AttendanceStatus.Online,
        Away => AttendanceStatus.Away,
        Offline => AttendanceStatus.Offline,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown AttendanceStatus value")
    };
}

public enum AttendanceStatusChangedBy
{
    Manual,
    System
}

public static class AttendanceStatusChangedByExtensions
{
    public const string Manual = "manual";
    public const string System = "system";

    public static string ToWireValue(this AttendanceStatusChangedBy by) => by switch
    {
        AttendanceStatusChangedBy.Manual => Manual,
        AttendanceStatusChangedBy.System => System,
        _ => throw new ArgumentOutOfRangeException(nameof(by), by, null)
    };
}
