using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Infrastructure.OpenAi;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// In-process fake of <see cref="IAssistantsApi"/> for orchestrator integration tests.
/// Lets each test script the run progression (queued → completed | requires_action |
/// failed | exception) without touching HTTP or the real OpenAI SDK.
/// </summary>
public class FakeAssistantsApi : IAssistantsApi
{
    public List<string> CreatedThreadIds { get; } = new();
    public List<string> AppendedMessages { get; } = new();
    public List<(string ThreadId, string AssistantId, string? Instructions)> CreatedRuns { get; } = new();
    public List<(string ThreadId, string RunId, IReadOnlyList<ToolOutput> Outputs)> SubmittedToolOutputs { get; } = new();

    /// <summary>
    /// Each call to CreateRunAsync consumes one scripted run from this queue.
    /// If empty, returns a default completed run.
    /// </summary>
    public Queue<AssistantRun> ScriptedRuns { get; } = new();

    /// <summary>Optional exception to throw on the next CreateRun/PollRun. Null = no fault.</summary>
    public Exception? ThrowOnNextRun { get; set; }

    /// <summary>Per-run latest assistant message text.</summary>
    public Dictionary<string, string> LatestAssistantMessages { get; } = new();

    public Task<string> EnsureAssistantAsync(AiAgent agent, OpenAiCredentials cred, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(agent.OpenAiAssistantId))
            return Task.FromResult(agent.OpenAiAssistantId);
        return Task.FromResult($"asst_fake_{agent.Id:n}");
    }

    public Task UpdateAssistantAsync(string assistantId, AiAgent agent, OpenAiCredentials cred, CancellationToken ct)
        => Task.CompletedTask;

    public Task<string> CreateThreadAsync(OpenAiCredentials cred, CancellationToken ct)
    {
        var id = $"thread_{Guid.NewGuid():n}";
        CreatedThreadIds.Add(id);
        return Task.FromResult(id);
    }

    public Task DeleteThreadAsync(string threadId, OpenAiCredentials cred, CancellationToken ct)
        => Task.CompletedTask;

    public Task AppendUserMessageAsync(string threadId, string content, OpenAiCredentials cred, CancellationToken ct)
    {
        AppendedMessages.Add(content);
        return Task.CompletedTask;
    }

    public Task<AssistantRun> CreateRunAsync(string threadId, string assistantId, string? instructionsOverride, OpenAiCredentials cred, CancellationToken ct)
    {
        if (ThrowOnNextRun is { } ex)
        {
            ThrowOnNextRun = null;
            throw ex;
        }
        CreatedRuns.Add((threadId, assistantId, instructionsOverride));
        var run = ScriptedRuns.Count > 0
            ? ScriptedRuns.Dequeue()
            : DefaultCompleted();
        return Task.FromResult(run);
    }

    public Task<AssistantRun> PollRunAsync(string threadId, string runId, TimeSpan timeout, OpenAiCredentials cred, CancellationToken ct)
    {
        if (ThrowOnNextRun is { } ex)
        {
            ThrowOnNextRun = null;
            throw ex;
        }
        // For simplicity, polling returns whatever script step is next, or the same `runId` completed.
        var run = ScriptedRuns.Count > 0
            ? ScriptedRuns.Dequeue() with { Id = runId }
            : DefaultCompleted() with { Id = runId };
        return Task.FromResult(run);
    }

    public Task<AssistantRun> SubmitToolOutputsAsync(string threadId, string runId, IReadOnlyList<ToolOutput> outputs, OpenAiCredentials cred, CancellationToken ct)
    {
        SubmittedToolOutputs.Add((threadId, runId, outputs));
        var run = ScriptedRuns.Count > 0
            ? ScriptedRuns.Dequeue() with { Id = runId }
            : DefaultCompleted() with { Id = runId };
        return Task.FromResult(run);
    }

    public Task<string?> GetLatestAssistantMessageAsync(string threadId, string runId, OpenAiCredentials cred, CancellationToken ct)
    {
        return Task.FromResult<string?>(LatestAssistantMessages.GetValueOrDefault(runId,
            "Esta é uma resposta padrão do agente."));
    }

    public static AssistantRun Completed(string runId = "run_done", int inputTokens = 100, int outputTokens = 25)
        => new(runId, "completed", inputTokens, outputTokens, "gpt-4o", Array.Empty<ToolCall>(), null);

    public static AssistantRun RequiresAction(string runId, params ToolCall[] calls)
        => new(runId, "requires_action", null, null, "gpt-4o", calls, null);

    public static AssistantRun Failed(string runId = "run_fail", string message = "boom")
        => new(runId, "failed", null, null, "gpt-4o", Array.Empty<ToolCall>(),
            new OpenAiErrorDetails(500, "server_error", message));

    private static AssistantRun DefaultCompleted() => Completed();
}
