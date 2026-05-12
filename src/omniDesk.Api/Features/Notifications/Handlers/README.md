# Notification Handlers — Spec 010

Each event type listed in `NotificationEventTypes` is materialized into a `Notification` row by
`NotificationService.NotifyXxxAsync` (in-app + WS). This folder is intentionally **light** in V1:
no separate event-bus subscribers, because the existing call sites already invoke
`INotificationService` directly. The handlers below document where each event originates so the
wiring is explicit.

## Hooks

| Event | Triggered by | Call site |
|---|---|---|
| `ticket.assigned` | `TicketCreationGateway` (Spec 009) on handoff with attendant resolved; `TicketAssignmentService` round-robin pickup. | `Features/Tickets/TicketCreationGateway.cs:151` and `Features/Distribution/TicketAssignmentService.cs` (US3 task — adds call). |
| `ticket.transferred_to_me` | `TransferTicketCommand` (Spec 009) | Hooked in US3 task T042. |
| `ticket.new_message` | `SendVisitorMessageCommand` (Spec 007) + `WhatsAppIncomingAdapter` (Spec 008) — when message arrives in a ticket with `attendant_id`. | Hooked in US1 task T043. |
| `ticket.client_replied` | `WaitingClientResumerJob` (Spec 009) — `waiting_client → in_progress` transition. | Hooked in US1 task T044. |
| `ticket.sla_warning` | `TicketSlaMonitorJob` (Spec 009) | Hooked in US3 task T066. |
| `ticket.sla_breached` | `TicketSlaMonitorJob` (Spec 009) | Hooked in US3 task T067. |
| `ticket.queued` | `TicketQueueMonitorJob` (Spec 010 US3) | Hooked in US3 task T069. |
| `ticket.reminder_failed` | `AppointmentReminderJob` (Spec 010 US4) | Hooked in US4 task T076. |

## Silence rule (US2)

The silence rule (FR-010) is enforced **inside** the push dispatch path in `NotificationService`
(added in US2), not at the handler boundary — in-app is always persisted regardless. See research
§R7 and `NotificationService.cs` US2 changes.
