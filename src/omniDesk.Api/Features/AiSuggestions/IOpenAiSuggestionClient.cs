namespace omniDesk.Api.Features.AiSuggestions;

public record OpenAiSuggestion(string Text, string Model, int InputTokens, int OutputTokens);

public enum AiProviderError { None, Timeout, RateLimit, ServerError }

public record AiCallResult(OpenAiSuggestion? Suggestion, AiProviderError Error);

/// <summary>
/// Thin wrapper around the OpenAI SDK that the SuggestReplyService consumes via DI.
/// The default implementation calls `gpt-4o`; tests inject a stub.
/// </summary>
public interface IOpenAiSuggestionClient
{
    Task<AiCallResult> CompleteAsync(
        IReadOnlyList<(string role, string content)> messages,
        TimeSpan timeout,
        CancellationToken ct);
}
