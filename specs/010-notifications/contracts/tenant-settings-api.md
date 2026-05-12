# Contract: Tenant Notification Settings REST API

**Spec**: 010-notifications
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer. **Restricted to `Roles.TenantAdmin`.**

These settings affect customer-facing WhatsApp automation (follow-up + reminders), so write access is admin-only.

---

## `GET /api/notification-settings` — get tenant settings

### Response — `200 OK`

```json
{
  "success": true,
  "data": {
    "follow_up_enabled": false,
    "reminder_enabled": true,
    "reminder_time": "20:00"
  }
}
```

If no row exists in `public.tenant_notification_settings` for the tenant, returns the default (`false`, `false`, `"20:00"`).

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | |
| 403 | `FORBIDDEN_ROLE` | Caller is not `TenantAdmin`. |

---

## `PUT /api/notification-settings` — update tenant settings

### Request body

```json
{
  "follow_up_enabled": true,
  "reminder_enabled": true,
  "reminder_time": "19:30"
}
```

`reminder_time` MUST match `^([01]\d|2[0-3]):[0-5]\d$` (HH:mm, 24h).

### Response — `200 OK`

```json
{
  "success": true,
  "data": {
    "follow_up_enabled": true,
    "reminder_enabled": true,
    "reminder_time": "19:30"
  }
}
```

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | |
| 403 | `FORBIDDEN_ROLE` | Caller is not `TenantAdmin`. |
| 422 | `INVALID_REMINDER_TIME` | `reminder_time` doesn't match HH:mm. |

### Side effects

- Upserts the row in `public.tenant_notification_settings`.
- Calls `AppointmentReminderScheduler.ApplyAsync(tenant_id, settings)`:
  - If `reminder_enabled = true` → `RecurringJob.AddOrUpdate` with cron derived from `reminder_time` and tenant timezone.
  - If `reminder_enabled = false` → `RecurringJob.RemoveIfExists("appointment-reminder:{slug}")`.
- Does NOT trigger a job run immediately; the schedule applies from the next tick.

---

## Implementation notes

- Endpoint is under `/api/notification-settings` (not under `/api/notifications/...`) because it's tenant config, distinct from per-attendant notification preferences.
- `[Authorize(Policy = "TenantAdmin")]` policy applies. Policy already exists in `Infrastructure/Authentication/`.
- The tenant's timezone comes from `public.tenants.timezone` (existing column from Spec 003) and is consulted by `AppointmentReminderScheduler`.
