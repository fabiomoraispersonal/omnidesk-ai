# Contract: Availability REST API

**Spec**: 011-agenda-services
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer. Role: `tenant_attendant`+.

This endpoint is the **single source of truth** for free slot calculation. The AI tool call `check_availability` (see `ai-tool-calls.md`) uses the same internal service (`AvailabilityCalculator`) — guaranteeing FR-018 parity.

---

## `GET /api/availability` — compute free slots

Returns the list of free start/end timestamps for a professional on a given local date, given a service that defines slot duration.

### Query params

| Param | Type | Required | Notes |
|---|---|---|---|
| `professional_id` | uuid | yes | |
| `service_id` | uuid | yes | Determines `duration_minutes`. |
| `date` | ISO date (`YYYY-MM-DD`) | yes | Interpreted in tenant timezone. |

### Response — `200 OK`

```json
{
  "success": true,
  "data": [
    { "start_at": "2026-06-10T09:00:00-03:00", "end_at": "2026-06-10T09:45:00-03:00" },
    { "start_at": "2026-06-10T09:45:00-03:00", "end_at": "2026-06-10T10:30:00-03:00" },
    { "start_at": "2026-06-10T10:30:00-03:00", "end_at": "2026-06-10T11:15:00-03:00" }
  ],
  "meta": {
    "professional_id": "p1...",
    "service_id": "s1...",
    "date": "2026-06-10",
    "duration_minutes": 45,
    "timezone": "America/Sao_Paulo"
  }
}
```

### Empty result conditions (returns `data: []`)

- Professional is inactive (`is_active = false`).
- Service is inactive (`is_active = false`).
- Professional does NOT offer the service (no row in `professional_services`).
- No weekly shifts on this `day_of_week`.
- All shifts fully consumed by blocks and/or existing appointments.

### Errors

| Code | Error code | Notes |
|---|---|---|
| 400 | `INVALID_DATE_FORMAT` | `date` not parseable. |
| 401 | `UNAUTHENTICATED` | |
| 404 | `PROFESSIONAL_NOT_FOUND` | |
| 404 | `SERVICE_NOT_FOUND` | |

> Note: returning empty list when professional/service are inactive is intentional (FR-017). Returning 404 only when the ID does not exist at all.

---

## Algorithm (reference)

Implemented in `Features/Agenda/Availability/AvailabilityCalculator.cs`:

```text
function GetSlots(professional_id, service_id, date, tenant_timezone):
    if professional.is_active = false or service.is_active = false:
        return []
    if not exists professional_services(professional_id, service_id):
        return []

    day_of_week = date.day_of_week_in(tenant_timezone)
    shifts = SELECT * FROM weekly_schedules
             WHERE professional_id = @p AND day_of_week = @d
             ORDER BY start_time

    if shifts is empty:
        return []

    day_start_utc = (date 00:00 in tenant_timezone) → UTC
    day_end_utc   = (date 23:59:59 in tenant_timezone) → UTC

    blocks = SELECT start_at, end_at FROM schedule_blocks
             WHERE professional_id = @p
               AND tstzrange(start_at, end_at, '[)') && tstzrange(@day_start, @day_end, '[)')

    busy = SELECT start_at, end_at FROM appointments
           WHERE professional_id = @p
             AND status IN ('pending_confirmation', 'confirmed')
             AND start_at >= @day_start AND start_at < @day_end

    occupied = merge_intervals(blocks ∪ busy)

    slots = []
    for shift in shifts:
        shift_start_utc = (date + shift.start_time in tenant_timezone) → UTC
        shift_end_utc   = (date + shift.end_time in tenant_timezone) → UTC

        cursor = shift_start_utc
        while cursor + service.duration <= shift_end_utc:
            candidate = (cursor, cursor + service.duration)
            if not overlaps(candidate, occupied):
                slots.append(candidate)
            cursor += service.duration

    return slots
```

**Performance**: O(s × b) where s = shifts (≤10), b = blocks+busy (≤30 typical). Single-digit milliseconds.

---

## Edge cases

- **Date is today**: slots whose `start_at <= now()` are filtered out (cannot book in the past).
- **Service duration > shift length**: shift produces 0 slots silently.
- **Block partially covers a slot**: slot is removed (any overlap eliminates it).
- **Timezone DST transition**: rare in Brazil (no DST since 2019) but handled by `TimeZoneInfo.ConvertTime` round-trip.

---

## Implementation notes

- Mapped in `Features/Agenda/Availability/AvailabilityEndpoint.cs` as `app.MapGet("/api/availability", ...).RequireAuthorization()`.
- Calculator is `IAvailabilityCalculator` injected via DI; concrete `AvailabilityCalculator` is stateless and thread-safe.
- Same calculator instance used by `CheckAvailabilityTool` (see `ai-tool-calls.md`).
- Tenant timezone resolved via existing `ITenantContext.Timezone` (Spec 005).
