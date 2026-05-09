using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.AiSuggestions;

public record SuggestionRequestContext(
    Guid ConversationId,
    Guid AttendantId,
    Guid DepartmentId,
    Guid? TicketId,
    int MaxContextMessages);

public record SuggestionResponse(
    string SuggestionId,
    string Text,
    string Model,
    long ElapsedMs,
    int InputTokens,
    int OutputTokens,
    Guid? SubAgentId,
    string? SubAgentName,
    int MessagesUsed);

public enum SuggestionFailure { Timeout, ProviderError, RateLimit, ConversationNotFound }

public record SuggestionOutcome(SuggestionResponse? Response, SuggestionFailure? Failure);

/// <summary>
/// Spec 005 / US8 (FR-036–040, SC-007).
/// Builds the prompt per research §R6, calls the OpenAI client, truncates to 1000 chars,
/// and persists telemetry to Mongo. **Never** sends a message to the customer — the response
/// is returned to the caller for human approval.
/// </summary>
public class SuggestReplyService
{
    public const int MaxSuggestionLength = 1000;
    public const int HardCapContextMessages = 50;

    private readonly IAgentRuntime _agents;
    private readonly IOpenAiSuggestionClient _openai;
    private readonly AiSuggestionLogger _logger;
    private readonly AppDbContext _db;
    private readonly ILogger<SuggestReplyService> _log;

    public SuggestReplyService(
        IAgentRuntime agents,
        IOpenAiSuggestionClient openai,
        AiSuggestionLogger logger,
        AppDbContext db,
        ILogger<SuggestReplyService> log)
    {
        _agents = agents;
        _openai = openai;
        _logger = logger;
        _db = db;
        _log = log;
    }

    public async Task<SuggestionOutcome> SuggestAsync(
        string tenantSlug, SuggestionRequestContext ctx, CancellationToken cancel)
    {
        var maxMessages = Math.Min(Math.Max(ctx.MaxContextMessages, 1), HardCapContextMessages);

        var subAgent = await _agents.GetSubAgentForDepartmentAsync(ctx.DepartmentId, cancel);
        var messages = await _agents.GetRecentMessagesAsync(ctx.ConversationId, maxMessages, cancel);

        var prompt = BuildPrompt(subAgent, messages);
        var sw = Stopwatch.StartNew();
        var result = await _openai.CompleteAsync(prompt, TimeSpan.FromSeconds(10), cancel);
        sw.Stop();

        if (result.Error != AiProviderError.None || result.Suggestion is null)
        {
            var failure = result.Error switch
            {
                AiProviderError.Timeout => SuggestionFailure.Timeout,
                AiProviderError.RateLimit => SuggestionFailure.RateLimit,
                _ => SuggestionFailure.ProviderError,
            };
            _log.LogWarning("AI suggestion failed conversation={ConversationId} error={Error}",
                ctx.ConversationId, result.Error);
            return new SuggestionOutcome(null, failure);
        }

        var truncated = result.Suggestion.Text.Length > MaxSuggestionLength
            ? result.Suggestion.Text.Substring(0, MaxSuggestionLength)
            : result.Suggestion.Text;

        var entry = new SuggestionLogEntry(
            ctx.ConversationId, ctx.TicketId, ctx.AttendantId, ctx.DepartmentId,
            subAgent?.Id, messages.Count, truncated, result.Suggestion.Model,
            result.Suggestion.InputTokens, result.Suggestion.OutputTokens,
            sw.ElapsedMilliseconds, DateTimeOffset.UtcNow);

        var suggestionId = await _logger.LogGenerationAsync(tenantSlug, entry, cancel);

        return new SuggestionOutcome(
            new SuggestionResponse(
                suggestionId,
                truncated,
                result.Suggestion.Model,
                sw.ElapsedMilliseconds,
                result.Suggestion.InputTokens,
                result.Suggestion.OutputTokens,
                subAgent?.Id,
                subAgent?.Name,
                messages.Count),
            null);
    }

    internal static IReadOnlyList<(string role, string content)> BuildPrompt(
        SubAgentContext? subAgent,
        IReadOnlyList<ConversationMessage> messages)
    {
        var prompt = new List<(string role, string content)>();

        if (subAgent is not null)
            prompt.Add(("system", subAgent.Prompt));
        else
            prompt.Add(("system",
                "Você é um assistente que sugere respostas para um atendente humano. " +
                "Responda em PT-BR. Não invente dados que não estão no contexto."));

        prompt.Add(("system",
            "Você está sugerindo uma resposta para um atendente humano enviar manualmente. " +
            "Não inclua despedidas se a conversa não está terminando. " +
            "Não invente dados que não estão no contexto. " +
            "Limite-se a uma resposta direta e objetiva."));

        foreach (var msg in messages)
            prompt.Add((msg.Role, msg.Content));

        prompt.Add(("user",
            "Sugira uma resposta adequada que o atendente humano possa enviar agora."));

        return prompt;
    }
}
