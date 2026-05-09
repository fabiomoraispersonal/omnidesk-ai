namespace omniDesk.Api.Domain.AiAgents;

/// <summary>
/// Mongo document — one per agent run or api error. Never contains client message content (LGPD §IV).
/// </summary>
public class AgentActivityLog
{
    public string TenantSlug { get; set; } = string.Empty;
    public Guid? ConversationId { get; set; }
    public Guid? AgentId { get; set; }
    public string? AgentName { get; set; }
    public string? AgentType { get; set; }
    public string Action { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? Model { get; set; }
    public long LatencyMs { get; set; }
    public string? OpenAiRunId { get; set; }
    public string? OpenAiThreadId { get; set; }
    public Guid? HandoffTargetAgentId { get; set; }
    public Guid? HandoffTargetDepartmentId { get; set; }
    public AgentActivityError? Error { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public class AgentActivityError
{
    public string Type { get; set; } = string.Empty;   // timeout | http_5xx | http_4xx | rate_limit | tool_loop | config_missing
    public int? Status { get; set; }
    public string Message { get; set; } = string.Empty;
}
