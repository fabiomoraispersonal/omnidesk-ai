# Feature Specification: Notification System (Spec 010)

**Feature Branch**: `010-notifications`
**Created**: 2026-05-12
**Status**: Draft

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — In-App Notification Bell (Priority: P1)

An attendant logs into the CRM and sees a bell icon in the header showing the count of unread notifications. When a ticket is assigned to them, a new notification appears in the badge in real time. They click the bell, see the list with the most recent at the top, click a notification, are taken to the relevant ticket, and the notification is marked as read.

**Why this priority**: Core delivery channel — every CRM actor depends on the in-app feed to stay aware of work. Must work before push or WhatsApp notifications are layered on top.

**Independent Test**: An attendant can receive, view, click, and dismiss in-app notifications entirely within the CRM without any browser or WhatsApp integration enabled.

**Acceptance Scenarios**:

1. **Given** a ticket is assigned to an attendant, **When** the `ticket.assigned` event fires, **Then** a new notification is persisted and pushed to the attendant's open CRM via WebSocket, and the unread badge increments.
2. **Given** the attendant has 5 unread notifications, **When** they open the notification panel, **Then** notifications appear ordered newest-first, unread items have a highlighted background, and each shows title, body (truncated at 80 chars), and relative time.
3. **Given** an attendant clicks a notification, **When** the click is handled, **Then** the notification is marked as read, the badge decrements, and the browser navigates to the related ticket or conversation.
4. **Given** an attendant clicks "Mark all as read", **When** the action completes, **Then** all notifications are marked read, the badge resets to zero, and the badge is hidden.
5. **Given** the attendant has more than 20 notifications, **When** they scroll to the bottom of the panel, **Then** the next 20 notifications load automatically (infinite scroll).
6. **Given** an attendant has 100 or more unread notifications, **When** the badge is rendered, **Then** it displays "99+" rather than the exact count.

---

### User Story 2 — Browser Push Notifications (Priority: P2)

An attendant grants browser push permission on first login. When a new message arrives on their ticket while the CRM tab is in the background, they receive a push notification with title and snippet. Clicking the push opens or focuses the CRM and navigates to the ticket. They can later disable push or fine-tune which events trigger a push in their profile preferences.

**Why this priority**: Enables attendants to act without keeping the CRM tab in the foreground — critical for operational responsiveness.

**Independent Test**: With CRM minimised, trigger a `ticket.new_message` event and verify the OS shows a push notification that navigates correctly on click.

**Acceptance Scenarios**:

1. **Given** an attendant logs in for the first time, **When** the CRM loads, **Then** the browser's native permission prompt is displayed.
2. **Given** permission was previously denied, **When** the attendant revisits the CRM, **Then** no automatic re-prompt appears — only a manual link in Preferences is shown.
3. **Given** permission is granted and the attendant has the ticket open (active tab), **When** a `ticket.new_message` arrives for that ticket, **Then** no browser push is sent — only the real-time WebSocket update is applied.
4. **Given** the attendant has multiple browsers registered, **When** a notification-triggering event fires, **Then** all registered push subscriptions for that attendant receive the push.
5. **Given** a push subscription endpoint returns HTTP 410 Gone, **When** the push is attempted, **Then** that subscription record is removed from the database automatically.
6. **Given** an attendant disables push for `ticket.queued` in Preferences, **When** a queued ticket event fires, **Then** an in-app notification is still created but no browser push is sent for that event type.

---

### User Story 3 — SLA Breach and Queue Alerts (Priority: P2)

When a ticket's SLA timer is breached, both the responsible attendant and all supervisors of the department receive an in-app and browser push notification. When a ticket has been queued without an attendant for more than 5 minutes, all supervisors of that department are notified.

**Why this priority**: Operational escalation path — supervisors need automated alerts for SLA and queue overflow without manual monitoring.

**Independent Test**: Simulate an SLA breach on a ticket with an assigned attendant and two supervisors in the same department; verify all three receive notifications.

**Acceptance Scenarios**:

1. **Given** a ticket's SLA deadline passes, **When** the breach is detected, **Then** the responsible attendant and all supervisors of that department each receive a `ticket.sla_breached` notification (in-app + push).
2. **Given** a `ticket.sla_warning` event fires, **When** it is processed, **Then** only the responsible attendant receives the warning notification.
3. **Given** a ticket has been in `New` status with no attendant for exactly 5 minutes, **When** the queue monitor fires, **Then** all supervisors of that department receive a `ticket.queued` notification.
4. **Given** the 5-minute queue threshold, **When** any supervisor checks settings, **Then** there is no per-tenant configuration for this value — it is fixed at 5 minutes.

---

### User Story 4 — WhatsApp Appointment Reminder Job (Priority: P3)

The system sends a WhatsApp template message to each patient/contact 24 hours before their scheduled appointment. The job runs daily at the time configured by the tenant (default 20:00). If sending fails, the attendant responsible for the linked ticket receives an in-app alert with a warning badge on the ticket card.

**Why this priority**: Reduces no-shows but depends on the appointment (Spec 11) and WhatsApp (Spec 08) modules being operational.

**Independent Test**: Configure a test appointment for the following day, enable the reminder toggle, run the job manually, and verify the WhatsApp message is sent and logged.

**Acceptance Scenarios**:

1. **Given** a tenant has reminders enabled, a WhatsApp channel active, an approved `appointment_reminder` template, and an appointment scheduled for tomorrow with a contact who has a phone number, **When** the daily job runs, **Then** the template message is sent and the send is recorded so it is not resent.
2. **Given** the same appointment, **When** the job has already sent a reminder for it today, **Then** no second reminder is sent (idempotency check).
3. **Given** the contact has no phone number, **When** the job processes that appointment, **Then** no message is attempted; if the appointment is linked to a ticket, a `reminder_failed` event is appended to the ticket's event log and the attendant receives an in-app notification.
4. **Given** a reminder failure on a ticket-linked appointment, **When** the ticket card is rendered, **Then** a ⚠️ badge appears on the card.
5. **Given** the attendant corrects the contact's phone and manually sends the template, **When** the send succeeds, **Then** `has_reminder_alert` is reset to `false` and the badge disappears.
6. **Given** a tenant configures the reminder time to 09:00, **When** the Hangfire job scheduler evaluates, **Then** the job fires at 09:00 tenant-local time rather than the 20:00 default.

---

### User Story 5 — Manual WhatsApp Template Send (Priority: P3)

An attendant whose 24-hour WhatsApp window has expired opens the ticket, clicks "Send template", selects from approved templates, fills in variables, reviews the preview, and confirms sending.

**Why this priority**: Required for conversation reactivation — without this attendants have no way to re-engage contacts after the session window closes.

**Independent Test**: Open a ticket with an expired WhatsApp session, trigger the modal, select a template, fill variables, preview, and confirm; verify the message appears in the conversation.

**Acceptance Scenarios**:

1. **Given** a ticket with a linked WhatsApp conversation, **When** the attendant clicks "Send template", **Then** a modal lists only `approved` templates for the tenant.
2. **Given** the attendant selects a template with variables, **When** they fill the variable fields, **Then** a live preview of the rendered message is displayed.
3. **Given** the attendant confirms the send, **When** the message is dispatched, **Then** it appears in the conversation thread, the WhatsApp session window resets, and the ticket remains in its current status.

---

### User Story 6 — Attendant Notification Preferences (Priority: P3)

An attendant opens their profile preferences, disables browser push globally, or selectively turns off push for low-priority event types like `ticket.queued` while keeping `ticket.sla_breached` enabled.

**Why this priority**: Reduces notification fatigue without removing operational visibility — supervisors may want queue alerts suppressed while keeping breach alerts active.

**Independent Test**: Disable `ticket.queued` push in preferences; trigger a queued event and verify no push is sent but the in-app notification still appears.

**Acceptance Scenarios**:

1. **Given** an attendant disables push globally, **When** any notification event fires, **Then** no browser push is sent to any of their devices (in-app notifications still persist).
2. **Given** an attendant unchecks `ticket.queued` push but leaves others checked, **When** a `ticket.queued` event fires, **Then** no push for that event type; a `ticket.assigned` event still sends a push.

---

### Edge Cases

- What happens when a notification's related ticket is deleted before the attendant clicks it? → Navigation lands on a 404 page; the notification is still marked as read.
- What happens when a push subscription's endpoint is unreachable (non-410)? → The send attempt fails silently; no retry; the subscription record is kept.
- What happens to notifications older than 90 days? → The Hangfire cleanup job soft-deletes them (marks archived); they no longer appear in the UI.
- What happens when a tenant's WhatsApp channel is disabled mid-day while the reminder job is running? → The job skips that tenant's remaining appointments for that run.
- What happens when `ticket.sla_breached` fires but the ticket has no assigned attendant? → Only supervisors receive the notification.
- What happens if an attendant has no department? → `ticket.queued` and `ticket.sla_breached` supervisor alerts are skipped for that attendant.

---

## Requirements *(mandatory)*

### Functional Requirements

**In-App Notifications**

- **FR-001**: The system MUST persist a notification record in the tenant's database for each event listed in the notification event table, addressed to the correct recipient(s).
- **FR-002**: The system MUST deliver new notification records to connected attendants in real time via WebSocket (`notification.new` event).
- **FR-003**: The system MUST expose an unread-count value via WebSocket (`notification.unread_count` event) and a REST endpoint, calculated from live data (not cached), capped at 99 for display purposes.
- **FR-004**: Attendants MUST be able to retrieve their notifications paginated (20 per page) in descending creation order.
- **FR-005**: Attendants MUST be able to mark a single notification as read.
- **FR-006**: Attendants MUST be able to mark all their notifications as read in a single action.
- **FR-007**: The system MUST automatically archive (soft-delete) notifications older than 90 days via a scheduled background job.
- **FR-008**: `ticket.sla_breached` notifications MUST be sent to both the ticket's assigned attendant and all supervisors of the ticket's department.
- **FR-009**: `ticket.queued` notifications MUST fire exactly after 5 minutes (fixed, non-configurable) of a ticket remaining in `New` status without an assigned attendant, addressed to all supervisors of that department.
- **FR-010**: When an attendant has a ticket actively open (active browser tab), browser push for `ticket.new_message` and `ticket.client_replied` for that specific ticket MUST NOT be sent.

**Browser Push**

- **FR-011**: The CRM MUST request browser push permission once at first login; it MUST NOT re-request if the user has already denied.
- **FR-012**: Attendants MUST be able to register push subscriptions (endpoint, p256dh, auth) and remove them.
- **FR-013**: The system MUST deliver browser push notifications to all active subscriptions of the recipient attendant when a notification event fires and the attendant has push enabled for that event type.
- **FR-014**: The system MUST remove a push subscription record when its endpoint returns HTTP 410 Gone during a push attempt.
- **FR-015**: Each attendant MUST be able to disable browser push globally and selectively per event type in their profile preferences.

**WhatsApp — Appointment Reminders (Job)**

- **FR-016**: The system MUST run a daily Hangfire job to send `appointment_reminder` WhatsApp templates to contacts with appointments scheduled for the following day.
- **FR-017**: The job MUST send reminders only when all four conditions are met: tenant's WhatsApp channel active, contact has a phone number, tenant has an approved `appointment_reminder` template, and the tenant's reminder toggle is enabled.
- **FR-018**: The system MUST NOT send the same reminder twice in the same calendar day for the same appointment.
- **FR-019**: When a reminder fails and the appointment is linked to a ticket, the system MUST append a `reminder_failed` event to the ticket's event log, set `tickets.has_reminder_alert = true`, and create an in-app notification for the responsible attendant.
- **FR-020**: When a reminder fails on a standalone (non-ticket) appointment, the failure MUST be logged in MongoDB and an in-app notification sent to supervisors of the responsible department.
- **FR-021**: When a reminder succeeds or the ticket is closed, `tickets.has_reminder_alert` MUST be reset to `false`.
- **FR-022**: The job execution time MUST be configurable per tenant (default 20:00 local time).

**WhatsApp — Manual Template Send**

- **FR-023**: Attendants MUST be able to open a modal from the ticket screen listing all `approved` templates for the tenant.
- **FR-024**: The modal MUST display a live preview of the rendered message as the attendant fills template variables.
- **FR-025**: On confirmation, the template message MUST be dispatched via the WhatsApp outgoing channel and appear in the conversation thread.

**Tenant Notification Settings**

- **FR-026**: Tenant admins MUST be able to toggle automatic follow-up message on ticket close (on/off).
- **FR-027**: Tenant admins MUST be able to toggle the appointment reminder job (on/off) and set its daily execution time.

---

### Key Entities

- **Notification** (`notifications` table in tenant schema): Represents a single in-app notification addressed to one attendant. Attributes: id, attendant_id, event_type, title, body, entity_type, entity_id, is_read, created_at. Archived (soft-deleted) after 90 days.

- **PushSubscription** (`push_subscriptions` table in tenant schema): One record per browser/device per attendant. Attributes: id, attendant_id, endpoint, p256dh, auth, user_agent, created_at. Removed when endpoint returns 410.

- **NotificationEventType** (enumerated set): `ticket.assigned`, `ticket.new_message`, `ticket.transferred_to_me`, `ticket.sla_warning`, `ticket.sla_breached`, `ticket.client_replied`, `ticket.queued`, `ticket.reminder_failed`.

- **AttendantNotificationPreferences** (stored per attendant): Global push enabled flag + per-event-type push enabled flags.

- **TenantNotificationSettings** (stored per tenant): follow_up_enabled (boolean), reminder_enabled (boolean), reminder_time (HH:mm, default "20:00").

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Attendants receive in-app notifications within 2 seconds of the triggering event for users with an active WebSocket connection.
- **SC-002**: Browser push notifications are delivered within 5 seconds of the triggering event on devices with an active push subscription and granted permission.
- **SC-003**: The unread-count badge is accurate at all times — zero discrepancy between displayed count and database unread count (verified by automated test).
- **SC-004**: The appointment reminder job processes 100% of eligible appointments for a tenant within 5 minutes of its scheduled run time.
- **SC-005**: No appointment receives more than one reminder per day per appointment ID (idempotency verified by automated test).
- **SC-006**: Stale push subscriptions (410 Gone) are removed within a single push cycle — no dead endpoint accumulates past one delivery attempt.
- **SC-007**: Notifications older than 90 days are archived within 24 hours of the archival job run (max one day lag).
- **SC-008**: Attendants can update push preferences and have new behavior take effect on the next notification event without reloading the CRM.

---

## Assumptions

- Spec 08 (WhatsApp) is operational and the `OutgoingMessageWorker` pipeline accepts outgoing template messages — Spec 010 does not re-implement this pipeline.
- Spec 09 (Tickets) is operational; `tickets.has_reminder_alert` column exists (added in Spec 09 data model or as a migration in this spec).
- Spec 11 (Agenda) provides the appointment data queried by the reminder job; the reminder job in Spec 010 reads appointments but does not own the agenda domain.
- SLA timers are computed and emitted as events by the ticket/SLA subsystem (Spec 09); Spec 010 only consumes those events to create notifications.
- `ticket.queued` monitoring uses a Hangfire recurring job polling every minute; the 5-minute threshold is hardcoded.
- Browser Service Worker for push is registered as part of the Angular CRM build — this spec covers the backend push dispatch and the frontend subscription management; Service Worker implementation is part of the Angular task set.
- Attendant supervisor role is identified by `roles.name = 'tenant_admin'` scoped to the same department as the ticket — no separate "supervisor" entity exists in V1.
- Follow-up WhatsApp template (`follow_up`) is assumed to already be approved in the tenant's template list when the feature is used.
- E-mail notifications for attendants are out of scope for V1.
- Customer notifications other than WhatsApp (e.g., SMS, email to customer) are out of scope for V1.
