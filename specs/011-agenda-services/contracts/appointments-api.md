# Contract: Appointments REST API

**Spec**: 011-agenda-services
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer. Role: `tenant_attendant`+ (visibility filtered by `IAppointmentVisibilityPolicy`).

---

## `GET /api/appointments` — list

### Query params

| Param | Type | Default | Notes |
|---|---|---|---|
| `page` | int ≥ 1 | 1 | |
| `per_page` | int 1..100 | 20 | |
| `professional_id` | uuid | (any) | |
| `service_id` | uuid | (any) | |
| `status` | string | (any) | One of `pending_confirmation`, `confirmed`, `cancelled`, `no_show`. |
| `from` | ISO datetime | (none) | `start_at >= from`. |
| `to` | ISO datetime | (none) | `start_at <= to`. |
| `sort` | string | `start_at` | One of `start_at`, `created_at`. |
| `order` | string | `asc` | |

Authorization: callers see only appointments allowed by `IAppointmentVisibilityPolicy` (research §R8).

### Response — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "a1...",
      "professional": { "id": "p1...", "name": "Dra. Ana Lima", "specialty": "Fisioterapeuta" },
      "service": { "id": "s1...", "name": "Consulta de Avaliação", "duration_minutes": 45, "price": 200.00 },
      "contact": { "id": "c1...", "name": "João Silva", "phone": "+5511999998888" },
      "ticket_id": "t1...",
      "conversation_id": "conv-1...",
      "start_at": "2026-06-10T09:00:00-03:00",
      "end_at": "2026-06-10T09:45:00-03:00",
      "status": "confirmed",
      "client_type": "new_client",
      "created_by": "ai",
      "notes": null,
      "reminder_sent_at": null,
      "cancelled_by": null,
      "cancelled_at": null,
      "cancellation_reason": null,
      "created_at": "2026-05-12T14:30:01Z",
      "updated_at": "2026-05-12T14:30:01Z"
    }
  ],
  "meta": { "page": 1, "per_page": 20, "total": 47 }
}
```

---

## `GET /api/appointments/{id}` — detail

### Response — `200 OK`

Same shape as a list item, plus:

```json
{
  "success": true,
  "data": {
    /* ... appointment fields ... */,
    "history": [
      { "action": "created", "actor_type": "ai", "actor_id": null, "at": "2026-05-12T14:30:01Z" },
      { "action": "confirmed", "actor_type": "attendant", "actor_id": "u1...", "at": "2026-05-12T14:35:00Z", "metadata": { "from_status": "pending_confirmation" } }
    ]
  }
}
```

`history` is loaded from `{slug}_appointment_events` (MongoDB), oldest-first.

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |
| 403 | `NOT_AUTHORIZED` |
| 404 | `APPOINTMENT_NOT_FOUND` |

---

## `POST /api/appointments` — create

Used by attendants via CRM. (AI uses the tool call `create_appointment` — see `ai-tool-calls.md`.)

### Request body

```json
{
  "professional_id": "p1...",
  "service_id": "s1...",
  "contact_id": "c1...",
  "start_at": "2026-06-10T09:00:00-03:00",
  "notes": "Trazer exames anteriores."
}
```

`contact_id` MUST exist. `end_at` is **never** accepted from the payload (FR-019).

### Response — `201 Created`

```json
{ "success": true, "data": { /* full appointment shape */ } }
```

Backend computes:

- `end_at = start_at + service.duration_minutes`
- `client_type` from contact history (FR-020, autoritative)
- `status` = `pending_confirmation` if `client_type = new_client` OR `service.requires_confirmation = true`; else `confirmed` (FR-021)
- `created_by = "attendant"` (taken from JWT identity)

### Side effects

- If `status = confirmed`: triggers `appointment_confirmation` WhatsApp via `INotificationService` (Spec 010) if contact has phone.
- WebSocket emits `appointment.changed` with `action: "created"`.
- Writes `{slug}_appointment_events` with `action: "created"`.

### Errors

| Code | Error code | Notes |
|---|---|---|
| 400 | `APPOINTMENT_OUTSIDE_AVAILABILITY` | `start_at` in past, outside weekly schedule, or inside a block. |
| 400 | `PROFESSIONAL_DOES_NOT_OFFER_SERVICE` | No row in `professional_services`. |
| 401 | `UNAUTHENTICATED` | |
| 403 | `NOT_AUTHORIZED` | |
| 404 | `PROFESSIONAL_NOT_FOUND` / `SERVICE_NOT_FOUND` / `CONTACT_NOT_FOUND` | |
| 409 | `APPOINTMENT_SLOT_CONFLICT` | Concurrent creation; one wins. Response includes `details.layer: "redis"|"unique_violation"`. |
| 422 | `VALIDATION_FAILED` | |

---

## `PUT /api/appointments/{id}` — edit

Allows editing `start_at`, `service_id`, `contact_id`, `notes`. Status changes go through dedicated endpoints below.

### Request body

```json
{
  "professional_id": "p1...",
  "service_id": "s2...",
  "contact_id": "c1...",
  "start_at": "2026-06-10T10:00:00-03:00",
  "notes": "Atualizado: trazer também receitas."
}
```

### Response — `200 OK`

Same shape as detail (without `history` — caller must re-fetch).

### Side effects

- Recomputes `end_at`.
- Revalidates availability **excluding the current appointment itself** from conflicts.
- Writes `{slug}_appointment_events` with `action: "rescheduled"` if `start_at` or `service_id` changed.
- Does NOT re-send `appointment_confirmation` (research §R7) — atendente é responsável por comunicar.
- WebSocket emits `appointment.changed` with `action: "rescheduled"`.

### Errors

Same as POST + `APPOINTMENT_INVALID_STATUS_TRANSITION` if trying to edit a `cancelled`/`no_show` appointment.

---

## `PATCH /api/appointments/{id}/confirm` — confirm pending

Confirms a `pending_confirmation` appointment. Triggers `appointment_confirmation` WhatsApp.

### Request body

Empty.

### Response — `200 OK`

```json
{ "success": true, "data": { "id": "a1...", "status": "confirmed" } }
```

### Side effects

- Status → `confirmed`.
- `INotificationService.NotifyAppointmentConfirmedAsync(...)` (Spec 010) — enfileira `appointment_confirmation`.
- `{slug}_appointment_events` append `action: "confirmed"`, `actor_type: "attendant"`, `metadata.from_status: "pending_confirmation"`.
- WS `appointment.changed action: "confirmed"`.

### Errors

| Code | Error code | Notes |
|---|---|---|
| 404 | `APPOINTMENT_NOT_FOUND` | |
| 409 | `APPOINTMENT_INVALID_STATUS_TRANSITION` | Already `confirmed`/`cancelled`/`no_show`. |

---

## `PATCH /api/appointments/{id}/cancel` — cancel (attendant)

Cancels from `pending_confirmation` or `confirmed`.

### Request body

```json
{ "cancellation_reason": "Cliente solicitou pelo telefone." }
```

`cancellation_reason` is optional (≤ 255 chars).

### Response — `200 OK`

```json
{ "success": true, "data": { "id": "a1...", "status": "cancelled" } }
```

### Side effects

- Status → `cancelled`.
- `cancelled_by = "attendant"`, `cancelled_at = now()`, `cancellation_reason` persisted.
- `{slug}_appointment_events` append `action: "cancelled"`, `actor_type: "attendant"`, `metadata.channel: "crm"`.
- WS `appointment.changed action: "cancelled"`.

### Errors

| Code | Error code |
|---|---|
| 404 | `APPOINTMENT_NOT_FOUND` |
| 409 | `APPOINTMENT_INVALID_STATUS_TRANSITION` |

---

## `PATCH /api/appointments/{id}/no-show` — mark no-show

Only allowed when `status = confirmed` AND `start_at <= now()`.

### Request body

Empty.

### Response — `200 OK`

```json
{ "success": true, "data": { "id": "a1...", "status": "no_show" } }
```

### Side effects

- Status → `no_show`.
- `{slug}_appointment_events` append `action: "no_show"`.
- WS `appointment.changed action: "no_show"`.

### Errors

| Code | Error code | Notes |
|---|---|---|
| 404 | `APPOINTMENT_NOT_FOUND` | |
| 409 | `APPOINTMENT_INVALID_STATUS_TRANSITION` | Not `confirmed` or `start_at` still in future. |

---

## `POST /api/appointments/{id}/resend-reminder` — resend WhatsApp reminder

Allowed only when `status = confirmed`.

### Request body

Empty.

### Response — `200 OK`

```json
{ "success": true, "data": { "id": "a1...", "reminder_sent_at": "2026-06-09T18:00:00Z" } }
```

### Side effects

- `reminder_sent_at = now()` (resets the 26h cancel window — research §R11).
- Enfileira o template `appointment_reminder` via `OutgoingMessagePublisher` (Spec 008).
- `{slug}_appointment_events` append `action: "reminder_resent"`.

### Errors

| Code | Error code | Notes |
|---|---|---|
| 400 | `CONTACT_HAS_NO_PHONE` | Contact lacks phone. |
| 404 | `APPOINTMENT_NOT_FOUND` | |
| 409 | `APPOINTMENT_INVALID_STATUS_TRANSITION` | Not `confirmed`. |
| 503 | `WHATSAPP_CHANNEL_INACTIVE` | Tenant has no active WA channel. |

---

## Implementation notes

- All endpoints in `Features/Agenda/Appointments/AppointmentsEndpoints.cs`.
- Authorization composed: `RequireAuthorization()` + per-endpoint `RequirePermission(Appointments.View)` or `RequirePermission(Appointments.Manage)`.
- Visibility filter applied in queries (LIST) and post-load check (DETAIL + write ops).
- All mutations write `{slug}_appointment_events` via `IAppointmentEventStore.AppendAsync(...)`.
- Race-condition protection on POST follows the 3-layer strategy in research §R2.
