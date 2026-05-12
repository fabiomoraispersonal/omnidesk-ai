# Contract: Attendant Notification Preferences REST API

**Spec**: 010-notifications
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer. Operates on the caller's own preferences only.

---

## `GET /api/notifications/preferences` — get caller's preferences

Returns the caller's notification preferences. If no row exists, returns the default (push enabled, no event overrides).

### Response — `200 OK`

```json
{
  "success": true,
  "data": {
    "push_enabled": true,
    "event_push_flags": {
      "ticket.assigned": true,
      "ticket.new_message": true,
      "ticket.transferred_to_me": true,
      "ticket.sla_warning": true,
      "ticket.sla_breached": true,
      "ticket.client_replied": true,
      "ticket.queued": true,
      "ticket.reminder_failed": true
    }
  }
}
```

The server always returns the **full event map** in the response (filling in `true` for absent keys), even if the DB row only stores deviations. This keeps the frontend simple.

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |

---

## `PUT /api/notifications/preferences` — save preferences

Replaces the caller's preferences row (upsert).

### Request body

```json
{
  "push_enabled": true,
  "event_push_flags": {
    "ticket.queued": false,
    "ticket.sla_warning": false
  }
}
```

The frontend MAY send only the keys that deviate from `true` (server treats missing keys as `true`). The frontend MAY send all 8 keys explicitly — both are valid.

### Response — `200 OK`

Returns the full effective preferences (same shape as `GET`).

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | |
| 422 | `INVALID_EVENT_TYPE` | A key in `event_push_flags` is not in `NotificationEventTypes.AllowedValues`. |

### Side effects

- Updates `attendant_notification_preferences` (upsert by `attendant_id`).
- New behavior applies to the **next** notification event (no cache, prefs are read live per dispatch).

---

## Implementation notes

- Single record per attendant; UPSERT via `ON CONFLICT (attendant_id) DO UPDATE`.
- Validation: `event_push_flags` keys MUST be one of the 8 allowed strings. Values MUST be boolean.
- Endpoint paths intentionally namespaced under `/api/notifications/...` to keep them grouped.
