namespace omniDesk.Api.Domain.LiveChat;

public enum EndedBy
{
    Attendant,
    AiAgent,
    SystemInactivity,
    SystemDisable,
}

public static class EndedByExtensions
{
    public static string ToWire(this EndedBy value) => value switch
    {
        EndedBy.Attendant         => "attendant",
        EndedBy.AiAgent           => "ai_agent",
        EndedBy.SystemInactivity  => "system_inactivity",
        EndedBy.SystemDisable     => "system_disable",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static EndedBy ParseWire(string value) => value switch
    {
        "attendant"         => EndedBy.Attendant,
        "ai_agent"          => EndedBy.AiAgent,
        "system_inactivity" => EndedBy.SystemInactivity,
        "system_disable"    => EndedBy.SystemDisable,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown ended_by value."),
    };
}
