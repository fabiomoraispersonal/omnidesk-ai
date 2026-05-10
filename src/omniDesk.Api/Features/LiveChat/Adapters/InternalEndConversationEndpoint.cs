using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Features.LiveChat.Adapters;

/// <summary>
/// Spec 007 T163 — internal endpoint reserved for the Spec 006 orchestrator (or any
/// future tool) to flag a conversation as naturally resolved by the AI. Not exposed
/// through the public widget surface and not authenticated by WidgetToken; it relies
/// on the existing JWT auth + an explicit role check at the route level.
/// </summary>
public static class InternalEndConversationEndpoint
{
    public static RouteGroupBuilder MapInternalEndConversation(this RouteGroupBuilder group)
    {
        group.MapPost("/conversations/{id:guid}/end", EndAsync);
        return group;
    }

    private static async Task<IResult> EndAsync(
        Guid id,
        IConversationRepository repo,
        CancellationToken ct)
    {
        var conv = await repo.GetByIdAsync(id, ct);
        if (conv is null)
            return Results.NotFound(new { success = false, error = new { code = "CONVERSATION_NOT_FOUND" } });

        if (conv.Status != ConversationStatus.Open)
            return Results.Conflict(new { success = false, error = new { code = "CONVERSATION_CLOSED" } });

        await repo.MarkResolvedByAiAsync(id, ct);
        return Results.Ok(new { success = true, data = new { conversation_id = id, ended_by = EndedBy.AiAgent.ToWire() } });
    }
}
