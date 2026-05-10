using System.Text.Json;
using omniDesk.Api.Features.LiveChat.Adapters;

namespace omniDesk.Api.Hubs.Handlers;

/// <summary>
/// Spec 007 — handles <c>message.send</c> events from the visitor widget. Dedupes by
/// <c>(conversation_id, client_message_id)</c>, validates the conversation is still
/// <c>open</c>, then enqueues for the AI pipeline (or no-op when handed off to a human).
/// </summary>
public class MessageSendHandler
{
    private readonly LiveChatIncomingAdapter _incoming;

    public MessageSendHandler(LiveChatIncomingAdapter incoming) => _incoming = incoming;

    public async Task<HandlerResult> HandleAsync(
        Guid conversationId,
        JsonElement payload,
        CancellationToken ct)
    {
        if (!payload.TryGetProperty("client_message_id", out var clientIdEl)
            || !Guid.TryParse(clientIdEl.GetString(), out var clientMessageId))
            return HandlerResult.Error("client_message_id missing or invalid");

        if (!payload.TryGetProperty("content", out var contentEl)
            || string.IsNullOrWhiteSpace(contentEl.GetString()))
            return HandlerResult.Error("content missing");

        var content = contentEl.GetString()!;
        if (content.Length > 10_000)
            return HandlerResult.Error("content too long (>10000 chars)");

        var result = await _incoming.EnqueueAsync(conversationId, clientMessageId, content, ct);

        return result.Outcome switch
        {
            "accepted" => HandlerResult.Ok(new { message_id = result.MessageId, client_message_id = clientMessageId, accepted = true }),
            "duplicate" => HandlerResult.Ok(new { message_id = result.MessageId, client_message_id = clientMessageId, accepted = false, duplicate = true }),
            "rejected" => HandlerResult.Error(result.ErrorCode!),
            _ => HandlerResult.Error("unknown outcome"),
        };
    }
}

public record HandlerResult(bool IsSuccess, object? Payload, string? ErrorMessage)
{
    public static HandlerResult Ok(object payload) => new(true, payload, null);
    public static HandlerResult Error(string message) => new(false, null, message);
}
