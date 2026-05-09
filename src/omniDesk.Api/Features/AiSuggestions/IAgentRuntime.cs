namespace omniDesk.Api.Features.AiSuggestions;

/// <summary>
/// Abstraction owned by Spec 002 (Agentes de IA) — Spec 005 consumes it via DI without
/// taking a hard reference on the concrete implementation. A null-object fallback is used
/// when the Spec 002 module is not yet wired (returns a generic system prompt).
/// </summary>
public interface IAgentRuntime
{
    Task<SubAgentContext?> GetSubAgentForDepartmentAsync(Guid departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(
        Guid conversationId, int maxCount, CancellationToken ct = default);
    Task<string?> GetClientNameAsync(Guid conversationId, CancellationToken ct = default);
}

public record SubAgentContext(Guid Id, string Name, string Prompt);

public record ConversationMessage(string Role, string Content, DateTimeOffset Timestamp);

/// <summary>
/// Default no-op implementation used when Spec 002 has not been wired yet.
/// Returns a generic system prompt and empty conversation context.
/// </summary>
public class FallbackAgentRuntime : IAgentRuntime
{
    public Task<SubAgentContext?> GetSubAgentForDepartmentAsync(Guid departmentId, CancellationToken ct = default)
        => Task.FromResult<SubAgentContext?>(null);

    public Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(
        Guid conversationId, int maxCount, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());

    public Task<string?> GetClientNameAsync(Guid conversationId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
