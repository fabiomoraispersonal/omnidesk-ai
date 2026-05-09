using omniDesk.Api.Domain.AiAgents;

namespace omniDesk.Api.Infrastructure.OpenAi;

/// <summary>
/// Thin wrapper over OpenAI Assistants v2 — abstracts authentication-per-call (tenant key vs global)
/// and lets tests replace it with a deterministic mock. Real impl wraps `openai-dotnet`.
/// </summary>
public interface IAssistantsApi
{
    Task<string> EnsureAssistantAsync(AiAgent agent, OpenAiCredentials credentials, CancellationToken ct);
    Task UpdateAssistantAsync(string assistantId, AiAgent agent, OpenAiCredentials credentials, CancellationToken ct);
    Task<string> CreateThreadAsync(OpenAiCredentials credentials, CancellationToken ct);
    Task DeleteThreadAsync(string threadId, OpenAiCredentials credentials, CancellationToken ct);
    Task AppendUserMessageAsync(string threadId, string content, OpenAiCredentials credentials, CancellationToken ct);
    Task<AssistantRun> CreateRunAsync(string threadId, string assistantId, string? instructionsOverride, OpenAiCredentials credentials, CancellationToken ct);
    Task<AssistantRun> PollRunAsync(string threadId, string runId, TimeSpan timeout, OpenAiCredentials credentials, CancellationToken ct);
    Task<AssistantRun> SubmitToolOutputsAsync(string threadId, string runId, IReadOnlyList<ToolOutput> outputs, OpenAiCredentials credentials, CancellationToken ct);
    Task<string?> GetLatestAssistantMessageAsync(string threadId, string runId, OpenAiCredentials credentials, CancellationToken ct);
}

public record OpenAiCredentials(string ApiKey, string? Organization, string? Project, string Source);

public record AssistantRun(
    string Id,
    string Status,                   // queued | in_progress | requires_action | completed | failed | cancelled | expired
    int? InputTokens,
    int? OutputTokens,
    string? Model,
    IReadOnlyList<ToolCall> ToolCalls,
    OpenAiErrorDetails? Error);

public record ToolCall(string CallId, string ToolName, string ArgumentsJson);

public record ToolOutput(string CallId, string OutputJson);

public record OpenAiErrorDetails(int? Status, string? Type, string Message);
