using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.LiveChat.Inbox.Commands;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.LiveChat.Inbox;

/// <summary>
/// Spec 007 US3 — attendant-facing conversation surface. Mounted under <c>/api/conversations</c>
/// (alongside Spec 005's SuggestReplyEndpoint) with JWT auth.
///
/// Routes:
///   GET    /              → active conversations for the caller
///   GET    /{id}/messages → paginated history (ownership enforced)
///   POST   /{id}/messages → attendant sends a message
///   POST   /{id}/resolve  → attendant ends the conversation
/// </summary>
public static class InboxConversationEndpoints
{
    public static RouteGroupBuilder MapInboxConversationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}/livechat-messages", GetMessagesAsync);
        group.MapPost("/{id:guid}/livechat-messages", SendAsync);
        group.MapPost("/{id:guid}/resolve", ResolveAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        AppDbContext db,
        CancellationToken ct)
    {
        if (!TryResolveCurrentAttendant(http, out var attendantId, out var departmentIds))
            return Results.Json(Error("ATTENDANT_REQUIRED", "Caller is not an attendant."), statusCode: 403);

        var conversations = await db.Conversations.AsNoTracking()
            .Where(c => c.Status == ConversationStatus.Open
                     && (c.AttendantId == attendantId
                      || (c.AttendantId == null && c.DepartmentId != null && departmentIds.Contains(c.DepartmentId.Value))))
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => new
            {
                id = c.Id,
                visitor_id = c.VisitorId,
                department_id = c.DepartmentId,
                attendant_id = c.AttendantId,
                last_message_at = c.LastMessageAt,
                created_at = c.CreatedAt,
                channel = c.Channel.ToWire(),
            })
            .ToListAsync(ct);

        return Results.Ok(new { success = true, data = conversations });
    }

    private static async Task<IResult> GetMessagesAsync(
        Guid id,
        HttpContext http,
        AppDbContext db,
        IMessageRepository messages,
        CancellationToken ct,
        int limit = 50,
        Guid? before = null)
    {
        if (!TryResolveCurrentAttendant(http, out var attendantId, out var departmentIds))
            return Results.Json(Error("ATTENDANT_REQUIRED", "Caller is not an attendant."), statusCode: 403);

        var conv = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conv is null)
            return Results.Json(Error("CONVERSATION_NOT_FOUND", "Conversation not found."), statusCode: 404);
        if (!CanAccess(conv, attendantId, departmentIds))
            return Results.Json(Error("FORBIDDEN", "Not allowed."), statusCode: 403);

        if (limit < 1 || limit > 100) limit = 50;
        var rows = await messages.GetByConversationAsync(id, limit, before, ct);
        var hasMore = rows.Count == limit;

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                messages = rows.Select(m => new
                {
                    id = m.Id,
                    sender_type = m.SenderType.ToWire(),
                    sender_id = m.SenderId,
                    content_type = m.ContentType.ToWire(),
                    content = m.Content,
                    attachment_url = m.AttachmentUrl,
                    attachment_name = m.AttachmentName,
                    attachment_size_bytes = m.AttachmentSizeBytes,
                    created_at = m.CreatedAt,
                }),
                has_more = hasMore,
                next_cursor = hasMore && rows.Count > 0 ? rows[0].Id : (Guid?)null,
            },
        });
    }

    private static async Task<IResult> SendAsync(
        Guid id,
        SendMessageRequest body,
        HttpContext http,
        SendAttendantMessageCommand command,
        CancellationToken ct)
    {
        if (!TryResolveCurrentAttendant(http, out var attendantId, out _))
            return Results.Json(Error("ATTENDANT_REQUIRED", "Caller is not an attendant."), statusCode: 403);
        if (string.IsNullOrWhiteSpace(body.Content))
            return Results.Json(Error("CONTENT_REQUIRED", "content is required."), statusCode: 400);
        if (body.Content.Length > 10_000)
            return Results.Json(Error("CONTENT_TOO_LONG", "content exceeds 10000 chars."), statusCode: 400);

        var result = await command.ExecuteAsync(id, attendantId, body.Content, ct);
        return result.Outcome switch
        {
            "accepted" => Results.Ok(new { success = true, data = new { message_id = result.MessageId } }),
            _ => MapError(result.ErrorCode!),
        };
    }

    private static async Task<IResult> ResolveAsync(
        Guid id,
        HttpContext http,
        ResolveConversationCommand command,
        CancellationToken ct)
    {
        if (!TryResolveCurrentAttendant(http, out var attendantId, out _))
            return Results.Json(Error("ATTENDANT_REQUIRED", "Caller is not an attendant."), statusCode: 403);

        var result = await command.ExecuteAsync(id, attendantId, ct);
        return result.Outcome switch
        {
            "resolved" => Results.Ok(new { success = true, data = new { conversation_id = id, status = "resolved" } }),
            _ => MapError(result.ErrorCode!),
        };
    }

    private static bool TryResolveCurrentAttendant(HttpContext http, out Guid attendantId, out HashSet<Guid> departmentIds)
    {
        attendantId = Guid.Empty;
        departmentIds = new HashSet<Guid>();

        var sub = http.User.FindFirst("sub")?.Value
                  ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(sub, out attendantId)) return false;

        // Department ids are populated as repeated claims by Spec 005's claims transformer.
        foreach (var claim in http.User.FindAll("dept_id"))
        {
            if (Guid.TryParse(claim.Value, out var d)) departmentIds.Add(d);
        }
        return true;
    }

    private static bool CanAccess(Conversation conv, Guid attendantId, HashSet<Guid> departmentIds)
    {
        if (conv.AttendantId == attendantId) return true;
        if (conv.AttendantId is null && conv.DepartmentId is { } d && departmentIds.Contains(d)) return true;
        return false;
    }

    private static IResult MapError(string code) => code switch
    {
        "CONVERSATION_NOT_FOUND" => Results.Json(Error(code, "Conversation not found."), statusCode: 404),
        "CONVERSATION_CLOSED" => Results.Json(Error(code, "Conversation is closed."), statusCode: 409),
        "FORBIDDEN" => Results.Json(Error(code, "Not allowed."), statusCode: 403),
        _ => Results.Json(Error(code, "Unknown error."), statusCode: 500),
    };

    private static object Error(string code, string message)
        => new { success = false, error = new { code, message } };
}

public record SendMessageRequest(string Content);
