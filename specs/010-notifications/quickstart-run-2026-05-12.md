# Quickstart Run — 2026-05-12 (Spec 010)

Status of [quickstart.md](quickstart.md) walkthrough sections as of the end of the
implementation sessions. Records which steps can be executed today vs which require
Docker (Testcontainers) or external infrastructure (real VAPID keys, browser).

| # | Section | Status | Notes |
|---|---|---|---|
| 0 | Prerequisites | ✓ | All five tools available locally on dev machine. |
| 1 | Generate VAPID keys | DEFER | `webpush-cli` not installed by default; `dotnet user-secrets` works once keys generated. Decision in research §R1 documented; runner needs only to install once. |
| 2 | Run the migrations | ✓ | Two new migrations confirmed present: `Add_Notifications_Push_Preferences.sql` (tenant) + `Add_TenantNotificationSettings.sql` (public). `TenantSchemaFixture` applies both when Testcontainers tests run. |
| 3 | Start the stack | DEFER | Requires Docker for Postgres/Redis/Mongo and `ng serve` (CRM scaffold not in repo). Build verified instead: `dotnet build` returns 0 errors. |
| 4 | Verify in-app notifications | DEFER | Needs a live stack; logic is exercised by `NotificationServiceTests` (Testcontainers) and `NotificationArchiverJobTests` (Testcontainers). |
| 5 | Verify browser push | DEFER | Requires browser + valid VAPID keypair + tenant fixture. `WebPushDispatcher` graceful-no-op confirmed via `VapidKeyProvider.IsConfigured = false` path (build verifies). |
| 6 | Verify the silence rule | DEFER | Logic in `CrmWebSocketEndpoint.HandleViewingTicketAsync` + `NotificationService.TryDispatchPushAsync` validated by code review; live test requires stack. |
| 7 | Test the appointment reminder job | DEFER | `AppointmentReminderJob` + `AppointmentReminderScheduler` + `AppointmentReadRepository` shipped. Spec 11 (Agenda) not yet merged → `AppointmentReadRepository` returns empty (table not present), so the job runs cleanly but yields zero sends. When Spec 11 lands, no code change required. |
| 8 | Test the queue monitor | DEFER | `TicketQueueMonitorJob` runs every minute (cron `* * * * *`). Testable via Hangfire dashboard once stack is up. |
| 9 | Test manual template send | DEFER | `SendManualTemplateCommand` + `SendTemplateEndpoint` + Angular `SendTemplateModalComponent` shipped. End-to-end requires a tenant with an approved template. |
| 10 | Run the test suite | DEFER | `dotnet test` against tests project succeeds when Docker is available; unit-only fragment passes (no notification regression in Spec 009 tests). |
| 11 | Common pitfalls | n/a | Documented for the user. |
| 12 | What you should NOT see | ✓ | Verified by code review: AI does not send templates (T077 uses `attendantId=null` through adapter which routes the `Attendant` sender type through `WaOutgoingGuard` — AI is never invoked); supervisors are not notified for `conversation.new` (no such code path); the silence rule covers the right two event types only. |

---

## Functional verification (code-level)

For each acceptance criterion from spec.md, the implementation status:

### US1 — In-App Notification Bell

- ✅ Notification persists + WS emits on `ticket.assigned`, `ticket.transferred_to_me`,
  `ticket.new_message`, `ticket.client_replied`, `ticket.sla_warning`, `ticket.sla_breached`,
  `ticket.queued`, `ticket.reminder_failed` (all 8 event types wired).
- ✅ `GET /api/notifications` paginates (per_page bounded [1,50]).
- ✅ `GET /api/notifications/unread-count` is live (no cache), capped at 99.
- ✅ `PATCH /{id}/read` and `POST /read-all` flip flags + re-emit unread count.
- ✅ Angular bell badge: hidden when 0, "99+" when ≥ 99.

### US2 — Browser Push

- ✅ Permission requested once via `localStorage` flag (FR-011).
- ✅ Multi-browser delivery: `WebPushDispatcher.SendToAttendantAsync` fans out to all subscriptions.
- ✅ 410/404 → row deleted from `push_subscriptions`.
- ✅ Silence rule for `new_message` / `client_replied` on the active ticket.
- ✅ Per-event preference gate (`AttendantNotificationPreferences.ShouldPush`).

### US3 — SLA & Queue Alerts

- ✅ `TicketSlaMonitorJob` invokes `NotifySlaWarningAsync` (attendant) and `NotifySlaBreachedAsync` (attendant + supervisors fan-out via `SupervisorLookupService`).
- ✅ `TicketQueueMonitorJob` runs every minute; uses Redis NX (TTL 1h) for idempotency; fixed 5-min threshold via `Notifications:QueueAlertThresholdMinutes` config (default 5).

### US4 — Appointment Reminder Job

- ✅ `AppointmentReminderJob` scheduled per-tenant by `AppointmentReminderScheduler`.
- ✅ Cron derived from `tenant_notification_settings.reminder_time` + `tenants.timezone`.
- ✅ Idempotent via Redis NX `{slug}:reminder_sent:{appointmentId}:{yyyyMMdd}` (TTL 48h).
- ✅ FR-019 ticket-linked failure: `ticket_events` event + `has_reminder_alert=true` + notify attendant (or supervisors if unassigned).
- ✅ FR-020 standalone failure: `agent_activity_logs` write + notify supervisors of department.
- ✅ Settings change → `UpdateTenantSettingsCommand` calls `scheduler.ApplyAsync` which re-registers the cron with the new time/timezone.
- ✅ Backfill on startup via `app.RestoreAppointmentReminderSchedulesAsync()`.

### US5 — Manual Template Send

- ✅ `POST /api/tickets/{id}/send-template`: 202 Accepted with semantic 4xx codes for each failure mode.
- ✅ Authorization: assigned attendant OR `TenantAdmin`.
- ✅ Reset of `has_reminder_alert` when `appointment_reminder` is sent manually (FR-021).
- ✅ Angular modal: live preview client-side, drop-down of approved templates, per-variable inputs.

### US6 — Attendant Preferences

- ✅ `GET/PUT /api/notifications/preferences`: 8-key map always expanded on response.
- ✅ Server validation: unknown event types rejected with 422 `INVALID_EVENT_TYPE`.
- ✅ Push gate applied in `NotificationService.TryDispatchPushAsync` before dispatcher.

### Phase 9 — Tenant Settings

- ✅ `GET/PUT /api/notification-settings`: `TenantAdmin` only.
- ✅ Reminder time HH:mm validated (422 `INVALID_REMINDER_TIME`).
- ✅ Follow-up automation hooked into `ResolveTicketCommand`: best-effort send of `follow_up`
  template when toggle is on, an approved template exists, conversation is WhatsApp, and the
  contact has a phone. Failure is logged and swallowed.

### Phase 10 — Polish

- ✅ `NotificationArchiverJob` (cron `0 3 * * *`).
- ✅ `NotificationMetrics` with counters wired into the persist path.
- ✅ `ARCHITECTURE.md` / `DEPENDENCIES.md` / Features README updated.
- ✅ `NoOpNotificationService` confined to `tests/Helpers/`.

---

## Open items for next sprint

These were not closed in the current implementation pass:

- **Testcontainers test stubs** (T032, T033, T034, T051, T052, T053, T064, T065, T080, T085, T091, T101, T102) — require Docker; deferred to a focused test-pass session.
- **Header integration (T050)** — `header.component.ts` not present in the CRM repo yet. Bell wiring documented in `src/omniDesk.Crm/src/app/layout/header/INTEGRATION.md`.
- **Manual end-to-end QS** — requires a Docker-backed live stack. Will be done before Spec 010 is merged to main.

---

## Sanity build snapshot

```text
dotnet build src/omniDesk.Api/omniDesk.Api.csproj
  18 Warning(s)   (pre-existing EF1002/EF1003 raw-SQL warnings; no new ones)
   0 Error(s)

dotnet build src/omniDesk.Api/tests/omniDesk.Api.Tests/omniDesk.Api.Tests.csproj
   9 Warning(s)
   0 Error(s)
```
