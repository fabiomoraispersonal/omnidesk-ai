# Contract: Push Subscriptions REST API

**Spec**: 010-notifications
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer (CRM session).

---

## `GET /api/push/vapid-public-key` — fetch VAPID public key

Returns the server's VAPID public key. Frontend uses it as the `applicationServerKey` when calling `pushManager.subscribe(...)`.

### Response — `200 OK`

```json
{
  "success": true,
  "data": { "vapid_public_key": "BLc4xRzKlKORKWlbdgFaBrrPK3ydWAH..." }
}
```

The key is base64url-encoded.

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | |
| 500 | `VAPID_NOT_CONFIGURED` | Server missing `Push:VapidPublicKey` config — operator action required. |

---

## `POST /api/push/subscribe` — register a push subscription

Registers (or upserts) a push subscription for the caller. Called by the frontend after `pushManager.subscribe()` returns successfully.

### Request body

```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/abc123...",
  "p256dh": "BPo7v8...",
  "auth": "k7tR9...",
  "user_agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 13_5) AppleWebKit/..."
}
```

### Response — `201 Created`

```json
{
  "success": true,
  "data": { "id": "f0e1d2c3-..." }
}
```

If a subscription with the same `endpoint` already exists for the caller, the row's `p256dh`, `auth`, and `user_agent` are updated and `200 OK` is returned.

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | |
| 422 | `INVALID_SUBSCRIPTION_PAYLOAD` | Missing one of the required crypto fields. |

### Side effects

- The subscription is now eligible for delivery on subsequent events.

---

## `DELETE /api/push/unsubscribe` — remove a subscription

### Request body

```json
{ "endpoint": "https://fcm.googleapis.com/fcm/send/abc123..." }
```

### Response — `204 No Content`

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | |
| 404 | `SUBSCRIPTION_NOT_FOUND` | The endpoint isn't associated with the caller. |

---

## Implementation notes

- `PushEndpoints.cs` exposes the three routes under `app.MapGroup("/api/push").MapPushEndpoints().RequireAuthorization()`.
- `vapid-public-key` is the only endpoint that returns server-side config; the others operate on `push_subscriptions`.
- `WebPushDispatcher` consumes these rows and auto-deletes when push returns `410 Gone`. Frontend isn't required to call `/unsubscribe` for this reason — but should on logout for cleanliness.
