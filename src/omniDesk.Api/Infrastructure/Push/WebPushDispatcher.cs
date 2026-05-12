using System.Net;
using omniDesk.Api.Infrastructure.Notifications;
using WebPush;
using DomainPushSubscription = omniDesk.Api.Domain.Notifications.PushSubscription;
using WebPushSubscription = WebPush.PushSubscription;

namespace omniDesk.Api.Infrastructure.Push;

/// <summary>
/// Spec 010 US2 T055 — wraps the WebPush NuGet library to deliver encrypted payloads to a
/// browser push endpoint. Auto-deletes subscriptions on HTTP 410 Gone / 404 Not Found (FR-014).
///
/// V1 behavior:
///   - If <see cref="VapidKeyProvider.IsConfigured"/> returns false, send is a no-op (warn log).
///   - On 410/404, the subscription row is deleted from <c>push_subscriptions</c>.
///   - On other transient errors (429, 5xx), we log a warning and DO NOT retry (research §R1).
///   - On unexpected exceptions, we log warning + swallow (handler-level fan-out must not crash).
/// </summary>
public class WebPushDispatcher(
    VapidKeyProvider vapid,
    PushSubscriptionRepository subscriptions,
    ILogger<WebPushDispatcher> logger)
{
    private readonly Lazy<WebPushClient> _client = new(() => new WebPushClient());
    private VapidDetails? _vapidDetails;

    public bool IsEnabled => vapid.IsConfigured;

    /// <summary>
    /// Sends one push payload to one subscription. Returns <c>true</c> on success,
    /// <c>false</c> if dispatch was skipped (not configured) or the subscription was cleaned up.
    /// Exceptions are logged and swallowed.
    /// </summary>
    public async Task<bool> SendAsync(
        DomainPushSubscription sub, string payloadJson, CancellationToken ct)
    {
        if (!vapid.IsConfigured)
        {
            logger.LogDebug("WebPushDispatcher: VAPID not configured; skipping push to {Endpoint}.",
                sub.Endpoint);
            return false;
        }

        _vapidDetails ??= BuildVapidDetails();

        var webSub = new WebPushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);

        try
        {
            await _client.Value.SendNotificationAsync(webSub, payloadJson, _vapidDetails, ct);
            return true;
        }
        catch (WebPushException ex) when (
            ex.StatusCode == HttpStatusCode.Gone || ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Subscription is permanently invalid (FR-014). Remove it.
            try
            {
                var removed = await subscriptions.DeleteByEndpointAsync(sub.Endpoint, ct);
                logger.LogInformation(
                    "WebPushDispatcher: removed dead subscription (status {Status}, rows {Rows}) for {Endpoint}.",
                    (int)ex.StatusCode, removed, sub.Endpoint);
            }
            catch (Exception delEx)
            {
                logger.LogWarning(delEx,
                    "WebPushDispatcher: failed to delete dead subscription {Endpoint}.", sub.Endpoint);
            }
            return false;
        }
        catch (WebPushException ex)
        {
            // 429 rate-limit, 5xx server errors, etc. — log and move on.
            logger.LogWarning(ex,
                "WebPushDispatcher: send failed status {Status} for {Endpoint}; no retry.",
                (int?)ex.StatusCode ?? -1, sub.Endpoint);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "WebPushDispatcher: unexpected error sending to {Endpoint}; swallowed.", sub.Endpoint);
            return false;
        }
    }

    /// <summary>Fan-out helper: send the same payload to every subscription of an attendant.</summary>
    public async Task<int> SendToAttendantAsync(
        Guid attendantId, string payloadJson, CancellationToken ct)
    {
        if (!vapid.IsConfigured) return 0;

        var subs = await subscriptions.GetByAttendantAsync(attendantId, ct);
        if (subs.Count == 0) return 0;

        int delivered = 0;
        foreach (var s in subs)
        {
            if (await SendAsync(s, payloadJson, ct)) delivered++;
        }
        return delivered;
    }

    private VapidDetails BuildVapidDetails()
    {
        vapid.Validate();
        return new VapidDetails(vapid.Subject, vapid.PublicKey, vapid.PrivateKey);
    }
}
