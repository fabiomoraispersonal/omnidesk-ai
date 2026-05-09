namespace omniDesk.Api.Domain.AiAgents;

public enum AgentType
{
    Orchestrator,
    SubAgent,
}

public static class AgentTypes
{
    public const string Orchestrator = "orchestrator";
    public const string SubAgent = "sub_agent";

    public static AgentType Parse(string value) => value switch
    {
        Orchestrator => AgentType.Orchestrator,
        SubAgent => AgentType.SubAgent,
        _ => throw new ArgumentException($"Unknown agent type '{value}'.", nameof(value)),
    };

    public static string ToWire(AgentType type) => type switch
    {
        AgentType.Orchestrator => Orchestrator,
        AgentType.SubAgent => SubAgent,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
