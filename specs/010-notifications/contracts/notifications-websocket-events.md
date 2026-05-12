# Contract: Notifications WebSocket Events

**Spec**: 010-notifications
**Endpoint**: `/ws/crm` (existing, Spec 007)
**Auth**: JWT in `Sec-WebSocket-Protocol` header or `?token=` query (existing scheme).

Notifications introduces 2 new server→client events and 1 new client→server message. No new endpoints.

---

## Channels

- **Per-attendant**: `{slug}:ws:attendant:{attendant_id}` — existing channel; the `CrmWebSocketEndpoint` already subscribes to it on connect. Notification events publish here.

---

## Server → Client events

### `notification.new`

Sent when a new notification has been persisted for the connected attendant.

```json
{
  "type": "notification.new",
  "payload": {
    "id": "9d7c6c7e-...",
    "event_type": "ticket.new_message",
    "title": "Nova mensagem — TK-20260512-00042",
    "body": "João Silva: Olá, preciso confirmar o agendamento de quinta.",
    "entity_type": "ticket",
    "entity_id": "ab12...",
    "created_at": "2026-05-12T14:30:01Z"
  },
  "timestamp": "2026-05-12T14:30:01Z",
  "tenant_slug": "clinica-abc"
}
```

**Frontend handling**: prepend to in-memory list (if bell panel is open) and increment local unread counter.

### `notification.unread_count`

Sent whenever the unread count for the attendant changes. Includes:

- After persist of a new notification (counter went up).
- After `PATCH .../read` or `POST .../read-all` (counter went down).

```json
{
  "type": "notification.unread_count",
  "payload": { "count": 7 },
  "timestamp": "2026-05-12T14:30:01Z",
  "tenant_slug": "clinica-abc"
}
```

`count` is capped at `99` for the UI (matches the REST endpoint).

**Frontend handling**: update the badge.

---

## Client → Server messages

### `attendant.viewing_ticket`

Tells the server which ticket (if any) the attendant currently has open in the foreground. Used to enforce the silence rule (FR-010).

Sent:

- On `TicketDetailComponent` mount (initial assert).
- Every 30 seconds (heartbeat).
- On `TicketDetailComponent` destroy with `ticket_id: null` to clear.

```json
{
  "type": "attendant.viewing_ticket",
  "ticket_id": "ab12-..."   // null to clear
}
```

**Server handling**:

- If `ticket_id` is non-null: `SET {slug}:attendant_active_ticket:{attendant_id} = {ticket_id} EX 60`.
- If `ticket_id` is null/empty: `DEL {slug}:attendant_active_ticket:{attendant_id}`.

The 60-second TTL with 30-second heartbeat tolerates one missed heartbeat without false expiry.

---

## Silence rule application

`TicketNewMessageHandler` and `TicketClientRepliedHandler` apply the rule before dispatching push:

```text
let active_ticket = redis.GET("{slug}:attendant_active_ticket:{attendant_id}")
if active_ticket == event.ticket_id:
    // skip push (in-app already persisted earlier in the pipeline)
else:
    dispatch_push()
```

In-app notification is always persisted; only push is suppressed.

---

## Reconnect behavior

The frontend's existing WS service auto-reconnects with exponential backoff. On reconnect:

1. Reissue `attendant.viewing_ticket` if a ticket detail is currently mounted.
2. Call `GET /api/notifications/unread-count` to reconcile the badge (events emitted while disconnected are not replayed).
3. Optionally refresh the visible page of `GET /api/notifications`.

This is implemented in `core/services/notification-stream.service.ts` listening for the existing `ws.reconnected` Signal.
