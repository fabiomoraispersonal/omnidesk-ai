# Contract: Manual WhatsApp Template Send REST API

**Spec**: 010-notifications
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer. Attendant must be assigned to the ticket OR be `TenantAdmin`.

---

## `POST /api/tickets/{ticket_id}/send-template` — send a template manually

The attendant dispatches an approved WhatsApp template message to the ticket's contact, typically to reactivate a conversation outside the 24-hour session window.

### Path params

- `ticket_id` (uuid)

### Request body

```json
{
  "template_id": "f1e2d3c4-...",
  "variables": {
    "patient_name": "Maria Silva",
    "appointment_time": "14:30",
    "professional_name": "Dra. Ana"
  }
}
```

The `variables` keys MUST match exactly the template's defined variable names (no extras, no missing).

### Response — `202 Accepted`

```json
{
  "success": true,
  "data": {
    "message_id": "7c8d9e0f-...",
    "status": "enqueued",
    "rendered_body": "Olá Maria Silva, lembramos seu agendamento às 14:30 com Dra. Ana. Confirma?"
  }
}
```

`message_id` is the id of the row created in `tenant_{slug}.conversation_messages` (or equivalent). `status: "enqueued"` indicates the message is in the outbound queue; final delivery status is tracked via `wa_message_statuses` and the conversation WS events.

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | |
| 403 | `TICKET_NOT_ASSIGNED_TO_USER` | Caller is neither the assigned attendant nor `TenantAdmin`. |
| 404 | `TICKET_NOT_FOUND` | |
| 404 | `TEMPLATE_NOT_FOUND` | |
| 422 | `TEMPLATE_NOT_APPROVED` | Template `status != approved` (e.g., `pending`, `rejected`, `paused`). |
| 422 | `TEMPLATE_VARIABLES_MISSING` | At least one expected variable is empty or absent. Body includes `details: ["patient_name"]`. |
| 422 | `TEMPLATE_VARIABLES_UNKNOWN` | Body has variable keys not defined in the template. |
| 422 | `TICKET_HAS_NO_CONTACT` | Ticket has no linked contact (can't send WhatsApp). |
| 422 | `CONTACT_HAS_NO_PHONE` | Linked contact has no phone number. |
| 503 | `WHATSAPP_CHANNEL_DISABLED` | Tenant's WhatsApp channel is `is_enabled = false`. |

### Side effects

- Renders template body server-side using the same engine as Spec 008's template renderer.
- Enqueues an outbound message to `{slug}:outgoing_messages` with:
  - `sender_type = "attendant"` (caller's user id)
  - `message_type = "template"`
  - Template variables + ticket/contact context
- Passes `WaOutgoingGuard.Validate` (attendant+template is allowed; AI+template is blocked).
- If the failure reason was a reminder previously failing (i.e., `tickets.has_reminder_alert = true` for this ticket) AND this manual send is for the `appointment_reminder` template, sets `has_reminder_alert = false` (resolving the alert badge — FR-021).

---

## Implementation notes

- The endpoint is mapped under existing `/api/tickets` group: `group.MapPost("/{id}/send-template", SendTemplate);`.
- The `has_reminder_alert` reset condition is checked specifically: if `template.name == "appointment_reminder"` and the ticket has the flag set, reset it within the same transaction as the message enqueue. Use a Postgres advisory lock or transactional UPDATE.
- The endpoint does **not** create an in-app notification for the attendant — the attendant is the one acting.
