# Contract: Agenda WebSocket Events

**Spec**: 011-agenda-services
**WS endpoint**: `/ws/crm` (existing, from Spec 007). No new endpoint.
**Channels**: existing `{slug}:ws:crm:dept:{id}` (department fan-out) and `{slug}:ws:attendant:{id}` (per-attendant fan-out).
**Auth**: JWT Bearer at WebSocket handshake (existing mechanism).

Spec 011 adds **one** new event type. Frontend uses it to refresh the agenda grid, pending tab and detail view in real time.

---

## Event: `appointment.changed`

Emitted after any successful state transition of an appointment.

### Payload

```json
{
  "type": "appointment.changed",
  "payload": {
    "id": "a1...",
    "professional_id": "p1...",
    "service_id": "s1...",
    "contact_id": "c1...",
    "ticket_id": "t1...",
    "start_at": "2026-06-10T09:00:00-03:00",
    "end_at": "2026-06-10T09:45:00-03:00",
    "status": "confirmed",
    "action": "created",
    "actor": {
      "type": "attendant",
      "id": "u1...",
      "name": "Maria Recepção"
    }
  },
  "timestamp": "2026-05-12T14:30:01Z"
}
```

### `action` values

| Action | When emitted | Triggered by |
|---|---|---|
| `created` | New appointment row inserted. | CRM `POST /api/appointments` OR AI tool `create_appointment`. |
| `confirmed` | Status changed pending → confirmed. | CRM `PATCH .../confirm` OR automatic transition (returning client). |
| `cancelled` | Status changed → cancelled. | CRM `PATCH .../cancel` OR WhatsApp "NÃO" interpreter. |
| `no_show` | Status changed → no_show. | CRM `PATCH .../no-show`. |
| `rescheduled` | `start_at` or `service_id` changed via `PUT`. | CRM `PUT /api/appointments/{id}`. |
| `reminder_sent` | `reminder_sent_at` populated by `AppointmentReminderJob` (Spec 010). | Hangfire job. |
| `reminder_resent` | `reminder_sent_at` updated via manual resend. | CRM `POST .../resend-reminder`. |

### Channel routing

Emitted on **both** channels:

1. `{slug}:ws:crm:dept:{department_id}` — where `department_id` resolves from `professional.department_id`. If `null` (professional has no department), uses `ticket.department_id`; if also null, emits only on attendant channel.
2. `{slug}:ws:attendant:{attendant_id}` — only if the professional has `attendant_id` (so the linked attendant sees their personal agenda update even if they belong to no department of relevance).

Frontend subscriptions remain unchanged (already subscribe to both via `CrmWebSocketEndpoint`).

### Idempotency

Frontend MUST treat receipt of duplicate `appointment.changed` (same `id`, same `action`) as no-op. Each transition can be emitted at most once per second from the backend, but transient network conditions may produce duplicates.

---

## Frontend usage (Angular CRM)

```ts
// features/agenda/appointments.service.ts
this.notificationStream.events$
    .pipe(filter(e => e.type === 'appointment.changed'))
    .subscribe(evt => this.handleAppointmentChanged(evt.payload));

private handleAppointmentChanged(payload: AppointmentChangedPayload) {
    switch (payload.action) {
        case 'created':
            this.upsertInLocalSignal(payload);
            break;
        case 'cancelled':
        case 'no_show':
            this.removeFromGrid(payload.id);  // grid + pending tab
            this.updateInList(payload);        // list tab keeps it
            break;
        case 'rescheduled':
            this.upsertInLocalSignal(payload);
            break;
        // ...etc
    }
}
```

---

## Implementation notes

- Backend publisher: `Infrastructure/WebSockets/AppointmentEventPublisher.cs`.
- Each appointment mutation handler calls `IAppointmentEventPublisher.PublishAsync(appointment, action, actor, ct)` AFTER the DB transaction commits.
- Constants: `Hubs/Events/AppointmentEvents.cs` (`Type`, `Action` consts).
- No new Redis pub/sub channel — reuses existing `RedisKeys.WsCrmDept(slug, deptId)` / `RedisKeys.WsAttendant(slug, attendantId)`.
