using System.Security.Claims;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.Push;

namespace omniDesk.Api.Features.Notifications;

/// <summary>Spec 010 US2 T056 — REST surface for browser push subscriptions.</summary>
public static class PushEndpoints
{
    public record SubscribeRequest(
        string Endpoint,
        string P256dh,
        string Auth,
        string? UserAgent);

    public record UnsubscribeRequest(string Endpoint);

    public static RouteGroupBuilder MapPushEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/vapid-public-key", VapidPublicKeyAsync).WithName("Push_VapidPublicKey");
        group.MapPost("/subscribe", SubscribeAsync).WithName("Push_Subscribe");
        group.MapDelete("/unsubscribe", UnsubscribeAsync).WithName("Push_Unsubscribe");
        return group;
    }

    private static IResult VapidPublicKeyAsync(VapidKeyProvider vapid)
    {
        if (!vapid.IsConfigured)
        {
            return Results.Json(new
            {
                success = false,
                error = new { code = "VAPID_NOT_CONFIGURED", message = "Server is missing VAPID configuration." },
            }, statusCode: 500);
        }
        return Results.Ok(new
        {
            success = true,
            data = new { vapid_public_key = vapid.PublicKey },
        });
    }

    private static async Task<IResult> SubscribeAsync(
        SubscribeRequest request,
        ClaimsPrincipal principal,
        IAttendantRepository attendantRepo,
        PushSubscriptionRepository pushRepo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint)
            || string.IsNullOrWhiteSpace(request.P256dh)
            || string.IsNullOrWhiteSpace(request.Auth))
        {
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new
                {
                    code = "INVALID_SUBSCRIPTION_PAYLOAD",
                    message = "endpoint, p256dh and auth are required.",
                },
            });
        }

        var attendant = await ResolveAttendantAsync(principal, attendantRepo, ct);
        if (attendant is null) return Results.Forbid();

        var saved = await pushRepo.UpsertAsync(
            attendant.Id, request.Endpoint, request.P256dh, request.Auth, request.UserAgent, ct);

        return Results.Created($"/api/push/subscriptions/{saved.Id}", new
        {
            success = true,
            data = new { id = saved.Id },
        });
    }

    private static async Task<IResult> UnsubscribeAsync(
        UnsubscribeRequest request,
        ClaimsPrincipal principal,
        IAttendantRepository attendantRepo,
        PushSubscriptionRepository pushRepo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "INVALID_SUBSCRIPTION_PAYLOAD", message = "endpoint required." },
            });

        var attendant = await ResolveAttendantAsync(principal, attendantRepo, ct);
        if (attendant is null) return Results.Forbid();

        var removed = await pushRepo.DeleteByEndpointForAttendantAsync(attendant.Id, request.Endpoint, ct);
        if (!removed)
        {
            return Results.NotFound(new
            {
                success = false,
                error = new { code = "SUBSCRIPTION_NOT_FOUND", message = "Endpoint not registered for this user." },
            });
        }
        return Results.NoContent();
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
