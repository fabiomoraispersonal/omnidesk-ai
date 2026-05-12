using System.Security.Claims;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Features.Notifications.Commands;
using omniDesk.Api.Infrastructure.Notifications;

namespace omniDesk.Api.Features.Notifications;

/// <summary>Spec 010 US6 T087 — per-attendant push preferences REST surface.</summary>
public static class PreferencesEndpoints
{
    public record UpdatePreferencesRequest(
        bool PushEnabled,
        Dictionary<string, bool>? EventPushFlags);

    public static RouteGroupBuilder MapPreferencesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/preferences", GetAsync).WithName("Notifications_GetPreferences");
        group.MapPut("/preferences", PutAsync).WithName("Notifications_PutPreferences");
        return group;
    }

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal principal,
        IAttendantRepository attendantRepo,
        AttendantPreferencesRepository repo,
        CancellationToken ct)
    {
        var attendant = await ResolveAttendantAsync(principal, attendantRepo, ct);
        if (attendant is null) return Results.Forbid();

        var prefs = await repo.GetAsync(attendant.Id, ct);
        return Results.Ok(new
        {
            success = true,
            data = new
            {
                push_enabled = prefs.PushEnabled,
                event_push_flags = ExpandFlags(prefs.EventPushFlags),
            },
        });
    }

    private static async Task<IResult> PutAsync(
        UpdatePreferencesRequest request,
        ClaimsPrincipal principal,
        IAttendantRepository attendantRepo,
        UpdatePreferencesCommand command,
        CancellationToken ct)
    {
        var attendant = await ResolveAttendantAsync(principal, attendantRepo, ct);
        if (attendant is null) return Results.Forbid();

        var flags = request.EventPushFlags ?? new Dictionary<string, bool>();
        var result = await command.ExecuteAsync(attendant.Id, request.PushEnabled, flags, ct);

        if (result.Error == UpdatePreferencesError.InvalidEventType)
        {
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new
                {
                    code = "INVALID_EVENT_TYPE",
                    message = $"Unknown event type: {result.InvalidKey}",
                },
            });
        }

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                push_enabled = result.Preferences!.PushEnabled,
                event_push_flags = ExpandFlags(result.Preferences.EventPushFlags),
            },
        });
    }

    /// <summary>
    /// Server always returns the full 8-key map (filling absent keys with true), so the
    /// frontend can render checkboxes without inferring defaults.
    /// </summary>
    private static Dictionary<string, bool> ExpandFlags(Dictionary<string, bool> stored)
    {
        var result = new Dictionary<string, bool>(NotificationEventTypes.AllowedValues.Count);
        foreach (var eventType in NotificationEventTypes.AllowedValues)
        {
            result[eventType] = !stored.TryGetValue(eventType, out var v) || v;
        }
        return result;
    }

    private static async Task<Attendant?> ResolveAttendantAsync(
        ClaimsPrincipal principal, IAttendantRepository repo, CancellationToken ct)
    {
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId)) return null;
        return await repo.GetByUserIdAsync(userId, ct);
    }
}
