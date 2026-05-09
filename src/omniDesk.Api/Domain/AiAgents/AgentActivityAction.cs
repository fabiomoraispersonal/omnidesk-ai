namespace omniDesk.Api.Domain.AiAgents;

public enum AgentActivityAction
{
    Respond,
    HandoffToAgent,
    TransferToHuman,
    ApiError,
}

public static class AgentActivityActions
{
    public const string Respond = "respond";
    public const string HandoffToAgent = "handoff_to_agent";
    public const string TransferToHuman = "transfer_to_human";
    public const string ApiError = "api_error";

    public static string ToWire(AgentActivityAction action) => action switch
    {
        AgentActivityAction.Respond => Respond,
        AgentActivityAction.HandoffToAgent => HandoffToAgent,
        AgentActivityAction.TransferToHuman => TransferToHuman,
        AgentActivityAction.ApiError => ApiError,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}
