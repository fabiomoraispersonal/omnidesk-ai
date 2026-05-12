# Contract: Notifications REST API

**Spec**: 010-notifications
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer (CRM session). Role: any authenticated attendant.
**Tenant scope**: derived from JWT `tenant_slug` claim. Notifications outside the caller's `attendant_id` are never returned.

---

## `GET /api/notifications` — list paginated feed

Lists the caller's notifications, newest first, excluding archived.

### Query params

| Param | Type | Default | Notes |
|---|---|---|---|
| `page` | int ≥ 1 | 1 | Pagination cursor. |
| `per_page` | int 1..50 | 20 | Page size. |
| `unread_only` | bool | false | If true, filter `is_read = false`. |

### Response — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "9d7c6c7e-...",
      "event_type": "ticket.new_message",
      "title": "Nova mensagem — TK-20260512-00042",
      "body": "João Silva: Olá, preciso confirmar o agendamento de quinta.",
      "entity_type": "ticket",
      "entity_id": "ab12...",
      "is_read": false,
      "created_at": "2026-05-12T14:30:01Z"
    }
  ],
  "meta": { "page": 1, "per_page": 20, "total": 47 }
}
```

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | Missing/expired JWT. |
| 422 | `INVALID_PAGINATION` | `per_page` out of range. |

---

## `GET /api/notifications/unread-count` — badge counter

Returns the live unread count (no caching). Capped at 99 for UI consumption.

### Response — `200 OK`

```json
{
  "success": true,
  "data": { "count": 7 }
}
```

If count > 99, returns `99` (UI renders as `"99+"`).

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |

---

## `PATCH /api/notifications/{id}/read` — mark one as read

### Path params

- `id` (uuid) — notification id; MUST belong to the caller.

### Request body

Empty.

### Response — `200 OK`

```json
{ "success": true, "data": { "id": "9d7c6c7e-...", "is_read": true } }
```

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | |
| 403 | `NOT_OWNER` | Notification belongs to a different attendant. |
| 404 | `NOTIFICATION_NOT_FOUND` | id not found or already archived. |

### Side effects

- Updates `is_read = true`.
- Publishes `notification.unread_count` WS event to the caller with the new count.

---

## `POST /api/notifications/read-all` — mark all as read

Marks every unread, non-archived notification for the caller as read.

### Request body

Empty.

### Response — `200 OK`

```json
{ "success": true, "data": { "marked": 12 } }
```

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |

### Side effects

- Publishes a single `notification.unread_count` event with `count = 0`.

---

## Implementation notes

- The endpoints are mapped in `Features/Notifications/NotificationsEndpoints.cs` via `app.MapGroup("/api/notifications").MapNotificationsEndpoints().RequireAuthorization();`.
- All handlers resolve `attendantId` from `ICurrentUser` (the existing service). Cross-attendant access is impossible because queries filter `WHERE attendant_id = @current`.
- `ListNotificationsQuery` and `UnreadCountQuery` are read-side classes; `MarkAsReadCommand` and `MarkAllAsReadCommand` are write-side. Same separation pattern as Spec 009 endpoints.
