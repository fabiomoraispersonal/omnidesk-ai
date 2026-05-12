# Data Model: Notifications (Spec 010)

**Branch**: `010-notifications` | **Phase**: 1 | **Date**: 2026-05-12

This document defines the persistent schema for Spec 010. All tenant-scoped tables live in `tenant_{slug}.*`. The settings table lives in `public.*` because it stores **about-tenant** configuration (same pattern as `public.tenants`).

DDL is illustrative; the canonical SQL is captured in:

- `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Notifications_Push_Preferences.sql` (tenant-scoped)
- `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_TenantNotificationSettings.sql` (public)

---

## Entity Overview

| Entity | Schema | Purpose | Lifecycle |
|---|---|---|---|
| **Notification** | `tenant_{slug}.notifications` | A single in-app alert addressed to one attendant. | Created by handler; `is_read` flipped by user; soft-archived after 90 days. |
| **PushSubscription** | `tenant_{slug}.push_subscriptions` | One per browser/device per attendant. | Created when attendant grants permission; deleted on `410` or manual unsubscribe. |
| **AttendantNotificationPreferences** | `tenant_{slug}.attendant_notification_preferences` | Per-attendant global push toggle + per-event flags (JSONB). | Lazy-created on first preference save; default behavior if absent = all-on. |
| **TenantNotificationSettings** | `public.tenant_notification_settings` | Per-tenant follow-up toggle, reminder toggle, reminder time. | Lazy-upsert on first settings save; default `false`/`false`/`'20:00'`. |
| **NotificationEventType** | (enum, C# const class) | Closed set of event-type strings. | Code-only. |

---

## Notification

```sql
CREATE TABLE tenant_{slug}.notifications (
    id              uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    attendant_id    uuid          NOT NULL,
    event_type      varchar(64)   NOT NULL,
    title           varchar(255)  NOT NULL,
    body            text          NOT NULL,
    entity_type     varchar(50)   NOT NULL,                -- 'ticket' | 'conversation'
    entity_id       uuid          NOT NULL,
    is_read         boolean       NOT NULL DEFAULT false,
    created_at      timestamptz   NOT NULL DEFAULT now(),
    archived_at     timestamptz   NULL,

    CONSTRAINT fk_notifications_attendant
        FOREIGN KEY (attendant_id) REFERENCES tenant_{slug}.attendants(id)
        ON DELETE CASCADE
);

-- Hot read path: attendant's unread/recent feed.
CREATE INDEX idx_notifications_feed
    ON tenant_{slug}.notifications (attendant_id, is_read, created_at DESC)
    WHERE archived_at IS NULL;

-- Archiver job sweep.
CREATE INDEX idx_notifications_archive_candidates
    ON tenant_{slug}.notifications (created_at)
    WHERE archived_at IS NULL;

-- Entity lookup (rare; navigation from notification to source).
CREATE INDEX idx_notifications_entity
    ON tenant_{slug}.notifications (entity_type, entity_id);
```

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK, server-generated. |
| `attendant_id` | `uuid` | FK to `attendants.id`. Cascade on delete (if an attendant is removed, their notifications go too). |
| `event_type` | `varchar(64)` | One of `NotificationEventTypes.*`. Validated at insert. |
| `title` | `varchar(255)` | Server-rendered, locale-aware (V1: PT-BR only). |
| `body` | `text` | Untrimmed. The 80-char truncation is a UI concern. |
| `entity_type` | `varchar(50)` | `'ticket'` or `'conversation'` in V1. |
| `entity_id` | `uuid` | Target row id (ticket_id or conversation_id). No FK across both options (poly). |
| `is_read` | `boolean` | Default false; flipped by user. |
| `created_at` | `timestamptz` | Insert time. |
| `archived_at` | `timestamptz?` | Set by `NotificationArchiverJob` after 90 days. UI filters `IS NULL`. |

**State transitions**:

```text
[created] → is_read=false, archived_at=null
       │
       ├─→ user marks as read → is_read=true   (still visible)
       │
       └─→ archiver job → archived_at=NOW()    (hidden from feed)
```

**Validation rules** (FluentValidation at API boundary):

- `event_type` MUST be in `NotificationEventTypes.AllowedValues`.
- `entity_type` MUST be in `{ "ticket", "conversation" }`.
- `title.Length <= 255` AND `title.Length > 0`.
- `body.Length > 0` (no upper limit at DB).

---

## PushSubscription

```sql
CREATE TABLE tenant_{slug}.push_subscriptions (
    id              uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    attendant_id    uuid          NOT NULL,
    endpoint        text          NOT NULL,
    p256dh          text          NOT NULL,
    auth            text          NOT NULL,
    user_agent      varchar(255)  NULL,
    created_at      timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT fk_push_subscriptions_attendant
        FOREIGN KEY (attendant_id) REFERENCES tenant_{slug}.attendants(id)
        ON DELETE CASCADE,

    -- Endpoint is globally unique (assigned by browser/push service per browser install).
    CONSTRAINT uq_push_subscriptions_endpoint UNIQUE (endpoint)
);

CREATE INDEX idx_push_subscriptions_attendant
    ON tenant_{slug}.push_subscriptions (attendant_id);
```

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK. |
| `attendant_id` | `uuid` | FK to `attendants`. Cascade delete. |
| `endpoint` | `text` | URL of push endpoint (`https://fcm.googleapis.com/...`, `https://updates.push.services.mozilla.com/...`, etc.). UNIQUE because the browser assigns one per install. |
| `p256dh` | `text` | Base64url-encoded ECDH P-256 public key (subscriber side). |
| `auth` | `text` | Base64url-encoded auth secret. |
| `user_agent` | `varchar(255)?` | Optional; captured at registration for display ("Chrome on macOS"). |
| `created_at` | `timestamptz` | Registration time. |

**Validation rules**:

- All three crypto fields (`endpoint`, `p256dh`, `auth`) MUST be present and non-empty at registration.
- `endpoint` MUST be HTTPS.
- Duplicate `endpoint` triggers UPSERT semantics (replace subscription metadata; the row stays the same).

**Deletion triggers**:

- Manual: `DELETE /api/push/unsubscribe` with the endpoint.
- Automatic: `WebPushDispatcher` deletes on `HTTP 410 Gone` from push service.

---

## AttendantNotificationPreferences

```sql
CREATE TABLE tenant_{slug}.attendant_notification_preferences (
    attendant_id      uuid          PRIMARY KEY,
    push_enabled      boolean       NOT NULL DEFAULT true,
    event_push_flags  jsonb         NOT NULL DEFAULT '{}'::jsonb,
    updated_at        timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT fk_anp_attendant
        FOREIGN KEY (attendant_id) REFERENCES tenant_{slug}.attendants(id)
        ON DELETE CASCADE
);
```

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `attendant_id` | `uuid` | PK + FK (one row per attendant). |
| `push_enabled` | `boolean` | Global toggle. If `false`, no push to any device. In-app still persists. |
| `event_push_flags` | `jsonb` | Map of `event_type → bool`. **Absent key = `true` (default-on)**. |
| `updated_at` | `timestamptz` | Last save time. |

**Default semantics**:

- If no row exists for an attendant → treat as `push_enabled=true, event_push_flags={}` → all pushes enabled.
- This avoids requiring a row to exist before push starts working on a fresh tenant.

**Example `event_push_flags` value**:

```json
{
  "ticket.queued": false,
  "ticket.sla_warning": false
}
```

(Means: don't push for `ticket.queued` or `ticket.sla_warning`; push for the other 6.)

**Validation rules**:

- Keys in `event_push_flags` MUST be in `NotificationEventTypes.AllowedValues`. Unknown keys are silently ignored by the dispatcher but rejected by the PUT endpoint validator.

---

## TenantNotificationSettings

```sql
CREATE TABLE public.tenant_notification_settings (
    tenant_id           uuid          PRIMARY KEY,
    follow_up_enabled   boolean       NOT NULL DEFAULT false,
    reminder_enabled    boolean       NOT NULL DEFAULT false,
    reminder_time       time          NOT NULL DEFAULT '20:00',
    updated_at          timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT fk_tns_tenant
        FOREIGN KEY (tenant_id) REFERENCES public.tenants(id)
        ON DELETE CASCADE
);
```

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `tenant_id` | `uuid` | PK + FK (one row per tenant). |
| `follow_up_enabled` | `boolean` | Toggle for automatic `follow_up` template send on ticket close. Default false (opt-in). |
| `reminder_enabled` | `boolean` | Toggle for daily `AppointmentReminderJob`. Default false (opt-in). |
| `reminder_time` | `time` | Local time (tenant TZ) at which the reminder job fires. Default `'20:00'`. |
| `updated_at` | `timestamptz` | Last save time. |

**Lifecycle**:

- Lazy upsert. On first `GET /api/notification-settings`, if no row exists, return defaults (don't create row). On first `PUT`, INSERT or UPDATE.

**Side effect of `PUT`**:

- After save, `AppointmentReminderScheduler.ApplyAsync(tenant_id, settings)` is called:
  - If `reminder_enabled = true` → `RecurringJob.AddOrUpdate<AppointmentReminderJob>("appointment-reminder:{slug}", ...)` with cron derived from `reminder_time` in tenant TZ.
  - If `reminder_enabled = false` → `RecurringJob.RemoveIfExists("appointment-reminder:{slug}")`.

---

## NotificationEventType (C# constants)

```csharp
// src/omniDesk.Api/Domain/Notifications/NotificationEventTypes.cs
namespace omniDesk.Api.Domain.Notifications;

public static class NotificationEventTypes
{
    public const string TicketAssigned         = "ticket.assigned";
    public const string TicketNewMessage       = "ticket.new_message";
    public const string TicketTransferredToMe  = "ticket.transferred_to_me";
    public const string TicketSlaWarning       = "ticket.sla_warning";
    public const string TicketSlaBreached      = "ticket.sla_breached";
    public const string TicketClientReplied    = "ticket.client_replied";
    public const string TicketQueued           = "ticket.queued";
    public const string TicketReminderFailed   = "ticket.reminder_failed";

    public static readonly IReadOnlySet<string> AllowedValues = new HashSet<string>
    {
        TicketAssigned, TicketNewMessage, TicketTransferredToMe,
        TicketSlaWarning, TicketSlaBreached, TicketClientReplied,
        TicketQueued, TicketReminderFailed,
    };
}
```

---

## Related Existing Tables (reused, no new columns from this spec)

| Table | Used For | Spec Owner |
|---|---|---|
| `tenant_{slug}.tickets` | `has_reminder_alert` column already added in Spec 009 — flipped to `true` by `ReminderFailedHandler`, back to `false` by `SendTemplateEndpoint` (on success) or by ticket close handler. | Spec 009 |
| `tenant_{slug}.ticket_events` (MongoDB collection: `{slug}_ticket_events`) | `reminder_failed` events appended when a reminder fails on a ticket-linked appointment. | Spec 009 |
| `{slug}_agent_activity_logs` (MongoDB) | `reminder_failed` for standalone (non-ticket) appointments. | Spec 006 |
| `tenant_{slug}.attendants`, `tenant_{slug}.attendant_departments` | Used by `SupervisorLookupService` to resolve supervisor recipients. | Spec 005 |
| `public.tenants` (`timezone`, `slug`) | Reminder job uses `timezone` to compute next run; `slug` is the schema/key prefix. | Spec 003 |
| `tenant_{slug}.appointments` (read-only via `IAppointmentReadRepository`) | Reminder job iterates appointments scheduled for tomorrow. | Spec 11 (or Null stub) |
| `tenant_{slug}.wa_message_statuses` | Outbound template messages from reminder job get a status row, reused as-is. | Spec 008 |
| `tenant_{slug}.whatsapp_templates` | Reminder job queries for `appointment_reminder` template; manual send queries for any `approved` template. | Spec 008 |

---

## EF Core Configurations

```csharp
// src/omniDesk.Api/Infrastructure/Notifications/NotificationsConfiguration.cs
public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasKey(n => n.Id);
        b.Property(n => n.EventType).HasMaxLength(64).IsRequired();
        b.Property(n => n.Title).HasMaxLength(255).IsRequired();
        b.Property(n => n.Body).IsRequired();
        b.Property(n => n.EntityType).HasMaxLength(50).IsRequired();
        b.Property(n => n.IsRead).HasDefaultValue(false);
        b.Property(n => n.CreatedAt).HasDefaultValueSql("now()");
        b.HasIndex(n => new { n.AttendantId, n.IsRead, n.CreatedAt }).HasFilter("archived_at IS NULL");
    }
}

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> b)
    {
        b.ToTable("push_subscriptions");
        b.HasKey(p => p.Id);
        b.Property(p => p.Endpoint).IsRequired();
        b.HasIndex(p => p.Endpoint).IsUnique();
        b.HasIndex(p => p.AttendantId);
    }
}

public class AttendantNotificationPreferencesConfiguration : IEntityTypeConfiguration<AttendantNotificationPreferences>
{
    public void Configure(EntityTypeBuilder<AttendantNotificationPreferences> b)
    {
        b.ToTable("attendant_notification_preferences");
        b.HasKey(a => a.AttendantId);
        b.Property(a => a.PushEnabled).HasDefaultValue(true);
        b.Property(a => a.EventPushFlags).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
    }
}
```

`TenantNotificationSettings` is configured against the `public` schema explicitly via `b.ToTable("tenant_notification_settings", "public")` and registered in the `PublicDbContext` (not `AppDbContext`, which is tenant-scoped) — same pattern used for `Tenant` in Spec 003.

---

## Migration Notes

### `Add_Notifications_Push_Preferences.sql` (tenant migration)

Runs once per tenant schema. Order:

1. `CREATE TABLE notifications (...)` + indexes.
2. `CREATE TABLE push_subscriptions (...)` + indexes.
3. `CREATE TABLE attendant_notification_preferences (...)`.

No data backfill needed — fresh tables.

### `Add_TenantNotificationSettings.sql` (public migration)

Runs once globally:

1. `CREATE TABLE public.tenant_notification_settings (...)`.

No backfill — defaults apply lazily.

### Existing column check

`tenant_{slug}.tickets.has_reminder_alert` was added in Spec 009 migration `Add_Tickets_FullModel.sql`. Verified present in `src/omniDesk.Api/Domain/Tickets/Ticket.cs` and the migration script. No change here.

---

## Read patterns (for index validation)

| Query | Index used | Frequency |
|---|---|---|
| `GET /api/notifications` (attendant feed) | `idx_notifications_feed` | High — every CRM session, on bell open and periodic refresh. |
| `GET /api/notifications/unread-count` | `idx_notifications_feed` (covering for `is_read=false`) | Very high — every WS reconnect + every event handler. |
| `PATCH /api/notifications/{id}/read` | PK lookup | Medium. |
| `POST /api/notifications/read-all` | `idx_notifications_feed` filter | Low. |
| Push dispatch: SELECT subscriptions for attendant | `idx_push_subscriptions_attendant` | Per event firing. |
| Archiver: WHERE created_at < NOW() - 90d | `idx_notifications_archive_candidates` | Once per day. |
