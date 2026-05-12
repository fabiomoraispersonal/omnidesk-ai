# Features/Notifications — Spec 010

Internal alerts (in-app bell + browser push) and customer-facing WhatsApp template automations.

## Quick links

- [spec.md](../../../../../specs/010-notifications/spec.md) — user stories + FRs
- [plan.md](../../../../../specs/010-notifications/plan.md) — technical plan
- [research.md](../../../../../specs/010-notifications/research.md) — R1–R15 decisions
- [data-model.md](../../../../../specs/010-notifications/data-model.md) — entities + DDL
- [contracts/](../../../../../specs/010-notifications/contracts/) — REST + WS + SW
- [quickstart.md](../../../../../specs/010-notifications/quickstart.md) — local setup

## Layout

```
Features/Notifications/
├── INotificationService.cs       — 8 dispatch methods (one per event type)
├── NotificationService.cs        — production impl (persist + WS + push)
├── SupervisorLookupService.cs    — TenantAdmin + Supervisor of dept (R6)
├── NotificationsEndpoints.cs     — GET/PATCH/POST on /api/notifications
├── PushEndpoints.cs              — VAPID + subscribe + unsubscribe
├── PreferencesEndpoints.cs       — per-attendant push prefs
├── TenantSettingsEndpoints.cs    — admin-only follow-up/reminder toggles
├── Queries/                      — read side
├── Commands/                     — write side
├── Handlers/                     — event hooks from Spec 005/007/008/009
└── Schedulers/                   — AppointmentReminderScheduler (Hangfire)
```

## VAPID setup (dev)

See [quickstart.md §1](../../../../../specs/010-notifications/quickstart.md). TL;DR:

```bash
dotnet tool install -g webpush-cli
webpush-cli generate-vapid
dotnet user-secrets set "Push:VapidSubject"    "mailto:devops@omnicare.ia.br"
dotnet user-secrets set "Push:VapidPublicKey"  "<public>"
dotnet user-secrets set "Push:VapidPrivateKey" "<private>"
```

## Event types

`NotificationEventTypes` (`Domain/Notifications/`) defines the 8 closed-set strings:
`ticket.assigned`, `ticket.new_message`, `ticket.transferred_to_me`, `ticket.sla_warning`,
`ticket.sla_breached`, `ticket.client_replied`, `ticket.queued`, `ticket.reminder_failed`.
