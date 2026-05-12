# Research: Notifications (Spec 010)

**Branch**: `010-notifications` | **Phase**: 0 | **Date**: 2026-05-12

This document resolves the open technical decisions for Spec 010. Each entry follows: **Decision → Rationale → Alternatives considered**.

---

## R1. Web Push library for .NET

**Decision**: Use the [`WebPush` NuGet package](https://www.nuget.org/packages/WebPush) (originally `web-push-csharp`, MIT, ~3M downloads).

**Rationale**:
- Implements RFC 8030 + RFC 8291 (Web Push Protocol with AES128GCM) + VAPID.
- Handles payload encryption, JWT signing for VAPID, and HTTP delivery to FCM/Mozilla/Apple endpoints uniformly.
- Stable API (`WebPushClient.SendNotification(subscription, payload, vapidDetails)`) with explicit `WebPushException.StatusCode` we can match on for `410 Gone` / `404 Not Found` (both → remove sub).
- No active maintenance concern: protocol is stable; library has been unchanged for ~3 years without breaking and is widely used in production .NET CRMs.
- Pure managed code, no native deps — works on ARM64 (Constitution platform target).

**Alternatives considered**:
- **Hand-rolled implementation** — would require implementing ECDH P-256, AES128GCM, and VAPID JWT signing from scratch. Out of scope for V1 (Constitution §V Simplicity).
- **`Lib.Net.Http.WebPush`** — less mature, smaller user base, last update older than `WebPush`.
- **External service (OneSignal, Pusher Beams)** — adds a paid third-party dep with PII passing through them. Conflicts with Constitution §IV (LGPD — Brazilian residency).
- **Firebase Cloud Messaging direct** — Google-specific; doesn't work for Safari/Firefox subscriptions. Web Push protocol is the standard that all browsers honor.

**Implementation notes**:
- Inject `WebPushClient` as singleton, configure VAPID once at startup.
- `WebPushDispatcher.SendAsync(...)`: try/catch on `WebPushException` — if `StatusCode in {410, 404}` → call `_subscriptions.DeleteByEndpointAsync(endpoint)` and log info; on `429` → log warning and skip retry (sub still valid, just rate-limited); otherwise log warning.
- Send is fire-and-forget from the perspective of the originating event handler — we await all subs for a single notification to bound latency, but exceptions don't propagate up.

---

## R2. Per-tenant recurring job scheduling (configurable reminder time)

**Decision**: One Hangfire `RecurringJob` per tenant with `Id = "appointment-reminder:{slug}"`. Re-scheduled via `RecurringJob.AddOrUpdate(...)` whenever `PUT /api/notification-settings` changes `reminder_time` or toggles `reminder_enabled`. When `reminder_enabled = false`, the job is **removed** (`RecurringJob.RemoveIfExists`).

**Rationale**:
- Hangfire natively supports thousands of recurring jobs (each is a Redis hash entry); per-tenant jobs scale fine for V1's tenant count (< 100).
- Cron expression derived from `reminder_time` HH:mm at tenant local timezone. We treat the tenant timezone as a tenant config (already exists per Spec 004) and pass it to `AddOrUpdate` as `TimeZoneInfo` — Hangfire handles DST automatically.
- Re-scheduling on settings change is atomic from the API perspective (just an extra Hangfire call) and idempotent (same job id replaces previous schedule).
- Each job execution iterates only its tenant's appointments — no cross-tenant scan, simpler authorization.

**Alternatives considered**:
- **Single global cron job that scans all tenants** — would have to compute "now matches tenant.reminder_time" for every tenant on every minute tick. Wasteful and harder to test. Loses per-tenant scheduling granularity.
- **APScheduler-style dynamic in-process scheduler** — would lose Hangfire's persistence (job survives restart). Not aligned with ADR-004.
- **Cron in the database driving a polling worker** — reinvents Hangfire.

**Implementation notes**:
- `TenantNotificationSettingsService.UpdateAsync(...)` returns the new settings; controller layer then calls a helper `AppointmentReminderScheduler.ApplyAsync(tenant_id, settings)` which calls `RecurringJob.AddOrUpdate<AppointmentReminderJob>(...)` with the right cron.
- At deploy time, a one-shot `BackfillReminderJobsJob` iterates `tenant_notification_settings` and registers/removes recurring jobs to match DB state (idempotent).

---

## R3. Dependency on Spec 11 (Agenda) — appointment data access

**Decision**: Introduce `IAppointmentReadRepository` in `Infrastructure/Appointments/` with the interface signature `GetUpcomingAppointmentsAsync(string slug, DateOnly date, CancellationToken ct)`. V1 implementation reads from `tenant_{slug}.appointments` via **raw SQL** (no EF Core navigation) — this works whether Spec 11 ships its EF model first or not. If Spec 11 hasn't landed when Spec 010 begins, ship a `NullAppointmentReadRepository` that returns `[]` and a manual checklist item to swap the implementation when Spec 11 merges.

**Rationale**:
- Spec 010 must not block on Spec 11 — they're parallel-eligible per `docs/DEPENDENCIES.md` group G6.
- Raw SQL against a known column list (`id, contact_id, scheduled_for, status, ticket_id`) decouples Spec 010 from Spec 11's EF model evolution.
- A null implementation makes the system function even before agendas exist; the reminder job will run, find nothing, and log "no appointments today" without errors.

**Alternatives considered**:
- **Block Spec 010 on Spec 11 completion** — violates the parallelization decision in `docs/DEPENDENCIES.md`.
- **Define `Appointment` domain entity inside Spec 010** — would create ownership conflict when Spec 11 ships its own entity. Read-only adapter avoids it.
- **Cross-spec import** — would require Spec 11's API to be merged. Same blocker.

**Implementation notes**:
- Column contract: `id (uuid)`, `scheduled_for (timestamptz)`, `contact_id (uuid)`, `ticket_id (uuid?)`, `status (text)`. If Spec 11 changes the column names, swap is at the repository SQL level only.
- Test fixture for `AppointmentReminderJobTests` inserts raw rows into `appointments` via `dotnet ef migrations script` or direct DDL in `TenantSchemaFixture.ApplySchemaAsync` (mirrors Spec 009 pattern).

---

## R4. `notifications.event_push_flags` schema (JSONB vs separate table)

**Decision**: Use a single `jsonb` column `event_push_flags` on `attendant_notification_preferences`. Format: `{ "ticket.queued": false, ... }`. Keys absent = `true` (default-on).

**Rationale**:
- Set of event types is closed (8 entries in V1, possibly 12 by V2). A child table `attendant_event_push_flag` would be 8 rows per attendant — query overhead and 3 extra joins on every notification dispatch.
- JSONB is queryable in Postgres (`event_push_flags ->> 'ticket.queued' = 'false'`) and indexable if needed (we don't need to).
- Default-on semantics keep migration simple: new event types added later are auto-enabled for existing attendants.

**Alternatives considered**:
- **Boolean column per event type** — schema churn each time the spec adds an event type.
- **Child table `event_push_flag (attendant_id, event_type, enabled)`** — 8× row count, slower reads, no obvious benefit.

**Implementation notes**:
- Read pattern in handlers: SELECT `event_push_flags` once per attendant, cast to `Dictionary<string, bool>` once per request batch. For high-volume events (e.g., new message storms) this lookup is read-only and cacheable per request scope.

---

## R5. Tenant-local timezone for reminder job

**Decision**: Use `tenants.timezone` (existing column from Spec 003 — IANA tz string, e.g. `America/Sao_Paulo`) as the basis for Hangfire's `TimeZoneInfo`. The reminder job runs at the tenant's local `reminder_time`.

**Rationale**:
- Tenants in V1 are clinics in Brazil; most are in `America/Sao_Paulo`, some in `America/Manaus`. They expect "8 PM" to mean 8 PM local.
- Hangfire's `RecurringJobOptions.TimeZone` parameter natively accepts `TimeZoneInfo` and handles DST.
- Tenant timezone already exists in `public.tenants.timezone` (Spec 003 Tenant data model).

**Alternatives considered**:
- **Always UTC** — would require admins to compute local→UTC offsets manually. Violates principle of least surprise.
- **Per-attendant timezone** — irrelevant; the job is per-tenant, not per-attendant.

**Implementation notes**:
- `AppointmentReminderScheduler.ApplyAsync(tenantId, settings)` reads `tenants.timezone`, converts it via `TimeZoneInfo.FindSystemTimeZoneById(tz)`, and passes to `RecurringJob.AddOrUpdate`.
- DateTimeOffset handling inside the job: `now()` is UTC; the "tomorrow" range for appointments is computed in tenant TZ then converted to UTC for SQL.

---

## R6. Supervisor identification (Constitution gap)

**Decision**: A "supervisor" of department D = a user with `role = Roles.Supervisor` (string `"supervisor"`) **AND** an `attendant_departments` row pointing to D. `Roles.TenantAdmin` users also receive supervisor-class notifications **for every department** (no scoping).

**Rationale**:
- The constitution preamble (drafted before Spec 005) only mentions `tenant_admin` / `tenant_attendant`. The actual codebase (Spec 005) introduced `Roles.Supervisor` as a distinct role with hierarchy `Admin > Supervisor > Attendant`. Spec 010 must use the real role model, not the outdated preamble.
- `TenantAdmin` is tenant-wide; treating them as cross-department supervisors aligns with their unrestricted access to the CRM and matches how admins use the SLA dashboard.
- Spec assumptions §A7 mentioned `tenant_admin` because the spec author wasn't aware of `Roles.Supervisor` — this decision corrects it without changing functional intent.

**Alternatives considered**:
- **Notify only TenantAdmin (no Supervisor)** — would miss the role specifically introduced to receive these alerts.
- **Notify everyone with read access to the department** — too noisy; attendants don't need supervisor-class alerts.

**Implementation notes**:
- `SupervisorLookupService.GetDepartmentSupervisorsAsync(deptId, ct)` returns:
  ```sql
  SELECT u.id FROM users u
  WHERE u.role = 'tenant_admin'
  UNION
  SELECT u.id FROM users u
    JOIN attendants a ON a.user_id = u.id
    JOIN attendant_departments ad ON ad.attendant_id = a.id
  WHERE u.role = 'supervisor' AND ad.department_id = @dept_id;
  ```
- A `Notification` row is created per recipient (not a multi-recipient row). Simpler reads, simpler RBAC.

---

## R7. In-app silence rule "tab active" — heartbeat protocol

**Decision**: Frontend `TicketDetailComponent` sends a WS message `{ type: "attendant.viewing_ticket", ticket_id: "<uuid>" }` on mount and every 30s (heartbeat). Backend stores `{slug}:attendant_active_ticket:{attendant_id}` = ticket_id with TTL 60s. On unmount, frontend sends `{ type: "attendant.viewing_ticket", ticket_id: null }` and backend `DEL`s the key.

**Rationale**:
- 60s TTL with 30s heartbeat tolerates one missed heartbeat (network jitter) without falsely "exiting" the active state.
- Push silence rule (FR-010) checks this key inline in `TicketNewMessageHandler` / `TicketClientRepliedHandler` — single Redis GET, very cheap.
- Falling back to "send push" if the key is absent is the correct conservative behavior.

**Alternatives considered**:
- **Postgres `attendant_session` table** — overkill, polling, latency.
- **WS-only presence (no Redis)** — would tie silence rule to WS server's local memory; doesn't survive node failover.
- **Frontend-only decision (suppress push at SW level)** — SW can't know which ticket is open in the UI without extra IPC; backend gate is simpler.

**Implementation notes**:
- `CrmWebSocketEndpoint.HandleMessage` adds a case for `attendant.viewing_ticket`. Wraps Redis `SET ... EX 60`.
- If `ticket_id == null` or empty, `DEL` instead.
- The check is conditional on event type — only `ticket.new_message` and `ticket.client_replied` for the *same* ticket are suppressed. All other event types push normally.

---

## R8. Re-using `INotificationService` interface from Spec 009

**Decision**: Keep `INotificationService` as the dispatch entry point. Add 6 new methods to match the 8 event types (some already covered). Replace `NoOpNotificationService` with `NotificationService` (real impl) in DI. Update tests that referenced `NoOpNotificationService` to construct the real one with appropriate fixtures (or keep `NoOp` as a `Tests/Helpers` class for cases where notifications aren't under test).

**Rationale**:
- The interface was introduced in Spec 009 exactly to be replaced here. Keeping the type identity minimizes churn in `TicketCreationGateway`, `TransferTicketCommand`, etc.
- Renaming `NoOpNotificationService` to `NotificationServiceStub` and moving to `tests/Helpers/` keeps the no-op available for unrelated integration tests without resurrecting the production stub.

**Alternatives considered**:
- **Delete `INotificationService` and introduce per-event handlers** — would mean changes in 3+ Spec 009 call sites. Refactor not justified.
- **Keep `NoOp` registered as fallback** — defeats the purpose; we want real notifications.

**Implementation notes**:
- New interface signature draft:
  ```csharp
  public interface INotificationService
  {
      Task NotifyTicketAssignedAsync(Guid attendantId, Guid ticketId, string protocol, string contactName, CancellationToken ct);
      Task NotifyTicketTransferredAsync(Guid toAttendantId, Guid ticketId, string protocol, Guid fromAttendantId, string fromName, CancellationToken ct);
      Task NotifyNewMessageAsync(Guid attendantId, Guid ticketId, string protocol, string contactName, string snippet, CancellationToken ct);
      Task NotifyClientRepliedAsync(Guid attendantId, Guid ticketId, string protocol, string contactName, CancellationToken ct);
      Task NotifySlaWarningAsync(Guid attendantId, Guid ticketId, string protocol, string slaType, CancellationToken ct);
      Task NotifySlaBreachedAsync(Guid ticketId, string protocol, Guid departmentId, Guid? attendantId, CancellationToken ct); // fan-out to attendant + supervisors
      Task NotifyTicketQueuedAsync(Guid ticketId, string protocol, Guid departmentId, CancellationToken ct); // fan-out to supervisors only
      Task NotifyReminderFailedAsync(Guid attendantId, Guid ticketId, string protocol, string contactName, string reason, CancellationToken ct);
  }
  ```
- The fan-out methods (sla_breached, queued) compute the recipient list internally and call the persist+push pipeline per recipient.

---

## R9. Service Worker — separate from Angular SW (cache)

**Decision**: Ship a single hand-written Service Worker at `src/sw-notifications.js`. Do NOT enable Angular's built-in `@angular/service-worker` (PWA caching) in V1 — they conflict on the SW registration.

**Rationale**:
- V1 doesn't need offline caching; the CRM is online-only by design.
- Angular SW (`ngsw-worker.js`) takes the SW slot and doesn't accept arbitrary `push` event handlers without configuration acrobatics.
- A hand-written 40-line SW is simpler, easier to debug, and explicitly limited to push.

**Alternatives considered**:
- **Enable Angular SW with custom routes for push** — possible via `ngswConfig` extensions but adds build-time complexity and risks accidental caching of API responses.
- **Use Workbox** — overkill for one event handler.

**Implementation notes**:
- SW file lives in `src/sw-notifications.js` (alongside `_redirects`, `_headers`). Registered as an `angular.json` asset.
- Registration in `WebPushService.register()`:
  ```ts
  navigator.serviceWorker.register('/sw-notifications.js', { scope: '/' });
  ```
- Cloudflare Pages headers (`_headers`):
  ```
  /sw-notifications.js
    Service-Worker-Allowed: /
  ```

---

## R10. Push payload size and content

**Decision**: Payload JSON with `title` (≤ 64 chars), `body` (≤ 120 chars), `icon`, `badge`, `data.url`. Sender truncates `body` server-side before push to stay well under the 4 KB encrypted payload limit. Cleaning is *display* only — the in-app notification stores the full body without truncation.

**Rationale**:
- Web Push encrypted payload caps at ~4 KB; with overhead, safe target is ~3.5 KB. 120 chars of UTF-8 body + envelope = ~300 bytes. Plenty of margin.
- Truncating only for push keeps the in-app feed informative.
- Body truncation rule mirrors the spec's "truncated at 80 chars" UI rule for the bell list, but uses 120 for push (slight more room since OS notifications are wider than a list item).

**Alternatives considered**:
- **Push entire body** — risks 413 errors on rare long bodies.
- **No body, just title + URL** — loses useful preview info.

**Implementation notes**:
- `WebPushPayloadBuilder.Build(notification)` returns the JSON object; `Body` is `notification.Body.SafeTruncate(120)`.
- `data.url`: relative path `/tickets/{ticket_id}` or `/conversations/{conv_id}`. SW combines with current origin on click.

---

## R11. Idempotency for queue monitor (5-minute fixed)

**Decision**: Job runs cron `* * * * *`. For each ticket where `status='new' AND attendant_id IS NULL AND created_at <= NOW() - INTERVAL '5 minutes'`, attempt `SET NX {slug}:queue_alert:{ticket_id} 1 EX 3600`. If `NX` succeeded, fan-out `ticket.queued` to supervisors. If it failed, the ticket was already notified this hour — skip.

**Rationale**:
- The TTL (1h) is generous enough to prevent re-notification within a normal workday but short enough to re-notify if the ticket is somehow still queued the next day (extreme edge case; should be picked up by then).
- Cron at 1-minute cadence guarantees the first notification fires within 1 minute of the 5-minute mark.
- `SETNX` is atomic and tenant-scoped.

**Alternatives considered**:
- **One-shot delayed job scheduled at ticket creation** — would require canceling the job on assignment. More moving parts; explicit scheduled state.
- **Notify every cycle (no idempotency)** — supervisors would get a notification per minute. Unacceptable.

**Implementation notes**:
- If ticket is picked up after the alert was sent, no "queue cleared" notification is sent in V1 (the bell entry remains as a historical alert; the supervisor sees in the kanban that it's now assigned).

---

## R12. Manual template send — concurrency and validation

**Decision**: `POST /api/tickets/{id}/send-template` is **synchronous from the API's perspective** (200 = enqueued for outbound, not yet sent). Validation chain: ticket exists + attendant is the assigned attendant OR `Roles.TenantAdmin` + template exists in tenant + template `status = approved` + all variables filled. On success, message is enqueued via `OutgoingMessagePublisher` with `sender_type=attendant`, `message_type=template`. The `WaOutgoingGuard` already allows attendant+template (Spec 008 FR-016 explicitly permits attendant-driven template sends).

**Rationale**:
- Synchronous enqueue is fast (Redis RPUSH), so latency is acceptable.
- Permission check uses existing role logic — no new permission required for V1.
- Renders preview server-side using the same engine as the WhatsApp templates page (template variable substitution `{{name}}` → `value`). Frontend can show the preview either by calling a separate `POST /api/whatsapp/templates/{id}/render` (Spec 008) or by re-rendering client-side with the same substitution rule. Decision: client-side rendering for the preview (no extra round-trip), server-side render is what actually goes out.

**Alternatives considered**:
- **Async with job ID + polling for status** — overkill for a manual send.
- **Skip preview, just send** — bad UX; spec explicitly requires preview.

**Implementation notes**:
- Validation errors return 400 with semantic codes: `TEMPLATE_NOT_APPROVED`, `TEMPLATE_VARIABLES_MISSING`, `TICKET_NOT_ASSIGNED_TO_USER`, etc.
- Endpoint emits no notification (the attendant is the one sending; they don't need to notify themselves).

---

## R13. WS event delivery to attendant — channel granularity

**Decision**: New events `notification.new` and `notification.unread_count` are published to `{slug}:ws:attendant:{attendant_id}` (per-attendant channel). The existing `CrmWebSocketEndpoint` already subscribes to the attendant's own channel by default — extension only requires adding handlers for the new event types in `NotificationEventPublisher`.

**Rationale**:
- Per-attendant channel guarantees only the recipient sees the notification (privacy + traffic minimization).
- Reuses existing pub/sub topology; no new channels, no new subscriptions.

**Alternatives considered**:
- **Publish to department channel and let client filter by `attendant_id`** — leaks notification metadata to other attendants. Rejected.

**Implementation notes**:
- `NotificationEventPublisher.PublishNewAsync(slug, attendantId, payload)` → `redis.PublishAsync(RedisKeys.WsAttendant(slug, attendantId), envelope)`.
- Same for `PublishUnreadCountAsync(slug, attendantId, count)`.

---

## R14. Archiver semantics — soft delete

**Decision**: `notifications.archived_at` (nullable timestamptz). Archiver sets it to `NOW()` for rows older than 90 days. UI feed filters `archived_at IS NULL`. No physical delete in V1.

**Rationale**:
- Constitution §IV mandates soft delete in production paths.
- Archived rows enable audit queries ("did this attendant ever see X?") if needed.
- A V1.1 cold-storage job can move archived rows to MongoDB cold collection if volume warrants — outside V1 scope.

**Alternatives considered**:
- **Physical delete** — violates constitution.
- **Move to MongoDB cold immediately** — premature optimization.

**Implementation notes**:
- Index: `CREATE INDEX idx_notifications_active ON notifications (attendant_id, is_read, created_at DESC) WHERE archived_at IS NULL;` — partial index optimizes the feed query.

---

## R15. Frontend WS reconnect strategy — already covered

**Decision**: Reuse the existing reconnect logic in `CrmWebSocketService` (Spec 007). On reconnect, fetch `/api/notifications/unread-count` to refresh the badge in case events were missed during disconnect.

**Rationale**:
- Backend doesn't queue WS events for offline clients (per Spec 007 design).
- A one-shot REST call on reconnect is enough to reconcile state.

**Alternatives considered**:
- **Server-side event log replay** — adds complexity not needed in V1.

**Implementation notes**:
- `NotificationStreamService` listens for the existing `ws.reconnected` Signal/Event and calls `unreadCount$` refresh.

---

## Open Questions (resolved → tracked in plan/data-model/contracts)

- ✅ R1: WebPush lib chosen
- ✅ R2: Per-tenant cron implemented via Hangfire AddOrUpdate
- ✅ R3: Appointment access via raw-SQL adapter (decoupled from Spec 11)
- ✅ R4: `event_push_flags` jsonb
- ✅ R5: Tenant TZ from `public.tenants.timezone`
- ✅ R6: Supervisor = `Roles.Supervisor` scoped via `attendant_departments` + `Roles.TenantAdmin` global
- ✅ R7: Heartbeat 30s / TTL 60s for active ticket
- ✅ R8: `INotificationService` expanded; `NoOp` demoted to test helper
- ✅ R9: Hand-written SW; Angular SW disabled
- ✅ R10: Push payload truncation (120 chars body)
- ✅ R11: Queue monitor idempotency via Redis NX 1h
- ✅ R12: Manual template send — sync enqueue, validation chain documented
- ✅ R13: WS per-attendant channel
- ✅ R14: Soft delete via `archived_at`
- ✅ R15: WS reconnect → refresh unread count
