using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Features.Notifications.Commands;
using omniDesk.Api.Features.Notifications.Queries;

namespace omniDesk.Api.Features.Notifications;

/// <summary>Spec 010 US1 (T039) — REST surface for the in-app notification feed.</summary>
public static class NotificationsEndpoints
{
    public static RouteGroupBuilder MapNotificationsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync).WithName("Notifications_List");
        group.MapGet("/unread-count", UnreadCountAsync).WithName("Notifications_UnreadCount");
        group.MapPatch("/{id:guid}/read", MarkAsReadAsync).WithName("Notifications_MarkAsRead");
        group.MapPost("/read-all", MarkAllAsReadAsync).WithName("Notifications_MarkAllAsRead");
        return group;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        IAttendantRepository attendantRepo,
        ListNotificationsQuery query,
        [FromQuery] int page,
        [FromQuery(Name = "per_page")] int perPage,
        [FromQuery(Name = "unread_only")] bool unreadOnly,
        CancellationToken ct)
    {
        var attendant = await ResolveAttendantAsync(principal, attendantRepo, ct);
        if (attendant is null) return AttendantRequired();

        if (page <= 0) page = 1;
        if (perPage <= 0) perPage = 20;

        var (items, total) = await query.ExecuteAsync(attendant.Id, page, perPage, unreadOnly, ct);

        return Results.Ok(new
        {
            success = true,
            data = items.Select(n => new
            {
                id = n.Id,
                event_type = n.EventType,
                title = n.Title,
                body = n.Body,
                entity_type = n.EntityType,
                entity_id = n.EntityId,
                is_read = n.IsRead,
                created_at = n.CreatedAt,
            }),
            meta = new { page, per_page = perPage, total },
        });
    }

    private static async Task<IResult> UnreadCountAsync(
        ClaimsPrincipal principal,
        IAttendantRepository attendantRepo,
        UnreadCountQuery query,
        CancellationToken ct)
    {
        var attendant = await ResolveAttendantAsync(principal, attendantRepo, ct);
        if (attendant is null) return AttendantRequired();

        var count = await query.ExecuteAsync(attendant.Id, ct);
        return Results.Ok(new { success = true, data = new { count } });
    }

    private static async Task<IResult> MarkAsReadAsync(
        Guid id,
        ClaimsPrincipal principal,
        IAttendantRepository attendantRepo,
        MarkAsReadCommand command,
        CancellationToken ct)
    {
        var attendant = await ResolveAttendantAsync(principal, attendantRepo, ct);
        if (attendant is null) return AttendantRequired();

        var result = await command.ExecuteAsync(id, attendant.Id, ct);
        return result switch
        {
            MarkAsReadResult.Ok       => Results.Ok(new { success = true, data = new { id, is_read = true } }),
            MarkAsReadResult.NotFound => Results.NotFound(new { success = false,
                error = new { code = "NOTIFICATION_NOT_FOUND", message = "Notification not found." } }),
            _                         => Results.StatusCode(500),
        };
    }

    private static async Task<IResult> MarkAllAsReadAsync(
        ClaimsPrincipal principal,
        IAttendantRepository attendantRepo,
        MarkAllAsReadCommand command,
        CancellationToken ct)
    {
        var attendant = await ResolveAttendantAsync(principal, attendantRepo, ct);
        if (attendant is null) return AttendantRequired();

        var marked = await command.ExecuteAsync(attendant.Id, ct);
        return Results.Ok(new { success = true, data = new { marked } });
    }

    // -----------------------------------------------------------------
    private static async Task<Attendant?> ResolveAttendantAsync(
        ClaimsPrincipal principal, IAttendantRepository repo, CancellationToken ct)
    {
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId)) return null;
        return await repo.GetByUserIdAsync(userId, ct);
    }

    private static IResult AttendantRequired() =>
        Results.Forbid();
}
