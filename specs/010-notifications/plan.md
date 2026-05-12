# Implementation Plan: Notifications (Spec 010)

**Branch**: `010-notifications` | **Data**: 2026-05-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/010-notifications/spec.md`

## Summary

Spec **Notifications** entrega o módulo de alertas operacionais do CRM. Cobre dois domínios distintos:

1. **Notificações internas (atendentes/supervisores)** — sino in-app (badge + lista paginada) + browser push (Web Push API). 8 tipos de evento: `ticket.assigned`, `ticket.new_message`, `ticket.transferred_to_me`, `ticket.sla_warning`, `ticket.sla_breached`, `ticket.client_replied`, `ticket.queued`, `ticket.reminder_failed`. Preferências por atendente (toggle global + por evento).
2. **Notificações para cliente (WhatsApp)** — automáticas (`appointment_confirmation` no agendar, `appointment_reminder` 24h antes via job Hangfire, `follow_up` ao encerrar ticket — toggle por tenant) + manuais (modal de template aprovado disparado pelo atendente).

Esta spec **substitui o stub `NoOpNotificationService`** instalado pela Spec 009 (`Features/Notifications/INotificationService.cs`) pela implementação real e amplia a interface para cobrir todos os 8 event types. Reaproveita o que 005–009 entregaram:

- **WebSocket `/ws/crm`** Spec 007 — ganha 2 eventos novos (`notification.new`, `notification.unread_count`). Sem novo endpoint.
- **`TicketSlaMonitorJob`** Spec 009 — já emite warnings/breaches no canal CRM; esta spec **consome** esses eventos para gerar notifications (não duplica a lógica de SLA).
- **`TicketCreationGateway`** Spec 009 — chama `INotificationService.NotifyTicketAssignedAsync` (já presente, hoje no-op); passa a criar notification real.
- **`TransferTicketCommand` / `PickupTicketEndpoint`** Spec 005/009 — emitem `ticket.assigned`/`ticket.transferred`; esta spec acopla um handler que materializa notifications correspondentes.
- **`OutgoingMessageWorker` + `WhatsAppOutgoingAdapter`** Spec 008 — pipeline reutilizada para enviar templates `appointment_reminder` em background. Esta spec **não** re-implementa envio WhatsApp.
- **`WaOutgoingGuard`** Spec 008 — continua bloqueando IA de enviar templates. Job de lembrete usa `sender_type=system` (não `ai_agent`), portanto passa pelo guard.
- **Roles** Spec 002/005 — `Roles.Supervisor` já existe (`Domain/Authorization/Roles.cs`). Supervisores de departamento = usuários `Supervisor` ligados ao depto via `AttendantDepartment` (mesmo grafo de atendentes). `Roles.TenantAdmin` recebe os mesmos alertas de supervisor por convenção (ver research §R8).

Backend novo entrega:

- **2 tabelas tenant-scoped novas**: `notifications`, `push_subscriptions`.
- **1 tabela tenant-scoped nova**: `attendant_notification_preferences` (toggle global + JSON com flags por event type).
- **1 tabela `public` nova**: `tenant_notification_settings` (follow_up_enabled, reminder_enabled, reminder_time HH:mm) — **uma linha por tenant**, FK → `public.tenants`.
- **2 endpoints REST Notifications** (list paginado + unread-count) + **2 endpoints REST mark-as-read** (single + bulk).
- **2 endpoints REST Push** (subscribe + unsubscribe).
- **2 endpoints REST Preferences** (GET/PUT — preferências do atendente logado).
- **2 endpoints REST Tenant Settings** (GET/PUT — restrito a `TenantAdmin`).
- **1 endpoint REST manual template send** (`POST /api/tickets/{id}/send-template`).
- **3 jobs Hangfire**:
  - `AppointmentReminderJob` (cron diário configurável por tenant) — envia `appointment_reminder` 24h antes.
  - `TicketQueueMonitorJob` (cron `* * * * *`, polling 1 min) — detecta tickets em `new` há ≥ 5min sem atendente, emite `ticket.queued`.
  - `NotificationArchiverJob` (cron `0 3 * * *`) — soft-deleta notifications > 90 dias.
- **6 event handlers** (em `Features/Notifications/Handlers/`) que assinam eventos do barramento interno e materializam linhas em `notifications` + disparam push:
  - `TicketAssignedHandler`, `TicketTransferredHandler`, `TicketNewMessageHandler`, `TicketClientRepliedHandler`, `TicketSlaWarningHandler`, `TicketSlaBreachedHandler`, `TicketQueuedHandler`, `ReminderFailedHandler`.
- **Web Push dispatcher** (`Infrastructure/Push/WebPushDispatcher.cs`) — usa biblioteca `WebPush` (.NET) com VAPID, batch para múltiplas subscriptions; remove subscriptions 410.
- **`AppointmentRepository`** (read-only) — lê agendamentos de `appointments` (Spec 11) ou — se Spec 11 ainda não estiver mergeada — placeholder com query stub documentado em research §R3.

Frontend CRM (Angular 21) entrega:

- `features/notifications/` — `NotificationBellComponent` (header), `NotificationListComponent` (painel deslizante com scroll infinito), `NotificationItemComponent` (linha clicável que marca como lida e navega).
- `features/notifications/preferences-page.component.ts` — Perfil → Preferências → Notificações (toggle global + checkboxes por evento).
- `features/whatsapp-templates/send-template-modal.component.ts` — modal acionado de `features/ticket-detail/`, lista templates `approved`, formulário variáveis, preview ao vivo.
- `features/notification-settings/` — CRM → Configurações → Notificações (3 controles: follow-up toggle, reminder toggle, reminder time picker). **Restrito a `TenantAdmin`** via guard existente.
- `core/services/web-push.service.ts` — registra Service Worker, solicita permissão (1× no login), submete subscription ao backend, gerencia revogação.
- `core/services/notification-stream.service.ts` — assina `/ws/crm` e expõe Signal `unreadCount` + Signal `recentNotifications` consumidos pelo bell.
- `src/sw-notifications.js` — Service Worker que recebe `push` events e exibe notificação nativa via `self.registration.showNotification(...)`. Service Worker é registrado apenas para push (não substitui Angular SW de cache — desativado).

Implementação faseada respeitando dependências:

1. **Fase A (Domain + Data + endpoints REST)**: migration `notifications`, `push_subscriptions`, `attendant_notification_preferences`, `tenant_notification_settings`. Domain entities. Endpoints CRUD básicos. Stub real do `INotificationService` (sem push ainda, só persist + WS).
2. **Fase B (Event handlers + WS)**: handlers que escutam eventos de ticket e materializam notifications. Publisher WS dos 2 eventos novos. Regra de silêncio para "tab ativa".
3. **Fase C (Web Push)**: VAPID keys provisionadas via user-secrets, `WebPushDispatcher`, integração com handlers (após persist, dispara push para todas as subscriptions ativas que tenham o event_type habilitado). Frontend Service Worker.
4. **Fase D (Queue Monitor + Cleanup)**: `TicketQueueMonitorJob` (5min fixed) + `NotificationArchiverJob` (90 dias).
5. **Fase E (Appointment Reminder Job)**: `AppointmentReminderJob` com agendamento per-tenant, idempotência por `appointment_id + date`, integração com `OutgoingMessageWorker`. Tratamento de `reminder_failed`: badge no ticket + notification ao atendente. Dependência: leitura de `appointments` da Spec 11. Se Spec 11 não estiver disponível na branch ainda, usar **adapter stub** com leitura via SQL bruto e documentação clara para troca (ver research §R3).
6. **Fase F (Frontend Notifications)**: Bell + lista + preferências. WS subscription. Service Worker.
7. **Fase G (Frontend WhatsApp manual + Tenant Settings)**: modal de envio de template manual; tela de configurações de notificação.
8. **Fase H (Polish)**: cleanup, métricas (`NotificationsDelivered`, `PushSubscriptionsActive`, `RemindersScheduled`), documentação operacional.

---

## Technical Context

**Backend**: C# .NET 10 — Minimal API + Endpoint Groups (continuação dos padrões 002–009).
**Frontend**: TypeScript — Angular 21 Standalone Components + Signals (CRM em `src/omniDesk.Crm/`). Service Worker próprio (não Angular SW) apenas para Web Push.
**ORM**: Entity Framework Core 10 + Migrations SQL tenant-scoped (padrão `Add_*` em `Infrastructure/Persistence/Migrations/`).

**Storage**:

- PostgreSQL tenant-scoped:
  - `tenant_{slug}.notifications` (id, attendant_id, event_type, title, body, entity_type, entity_id, is_read, created_at, archived_at). Index composto `(attendant_id, is_read, created_at DESC)` para o feed; index parcial em `archived_at IS NULL` para o archiver.
  - `tenant_{slug}.push_subscriptions` (id, attendant_id, endpoint UNIQUE, p256dh, auth, user_agent, created_at). Index em `attendant_id`.
  - `tenant_{slug}.attendant_notification_preferences` (attendant_id PK, push_enabled boolean, event_push_flags jsonb, updated_at). Um registro por atendente; default `push_enabled = true`, `event_push_flags = {}` (interpretado como "todos habilitados").
- PostgreSQL `public`:
  - `public.tenant_notification_settings` (tenant_id PK FK→tenants, follow_up_enabled boolean, reminder_enabled boolean, reminder_time time, updated_at). Defaults: `false`, `false`, `'20:00'`. Linha criada lazy no primeiro acesso (upsert).
- Redis:
  - `{slug}:queue_alert:{ticket_id}` — flag idempotente do queue monitor, TTL 1h. Evita notificar supervisores múltiplas vezes para o mesmo ticket.
  - `{slug}:reminder_sent:{appointment_id}:{yyyyMMdd}` — flag idempotente do AppointmentReminderJob, TTL 48h. Garante FR-018.
  - `{slug}:attendant_active_ticket:{attendant_id}` — set efêmero (TTL 60s, renovado por heartbeat WS) com `ticket_id` que o atendente tem aberto. Consultado pela regra de silêncio FR-010.
- MongoDB:
  - `{slug}_agent_activity_logs` — recebe entry `action="reminder_failed"` quando lembrete em agendamento avulso falha (FR-020).
  - `{slug}_ticket_events` — recebe entry `reminder_failed` quando lembrete em ticket falha (FR-019). Reutiliza store da Spec 009.

**Background jobs** (Hangfire):

| Job | Schedule | Responsabilidade |
|---|---|---|
| `TicketSlaMonitorJob` (Spec 009) | Cron `* * * * *` | **REUTILIZADO**. Já emite `ticket.sla_warning` / `ticket.sla_breached` em `{slug}:crm:dept:{id}`. Esta spec adiciona um `TicketSlaWarningHandler` / `TicketSlaBreachedHandler` que consomem o evento (via injeção direta — handlers são chamados in-process pelo monitor após publicar WS) e materializam notifications. |
| `TicketQueueMonitorJob` (novo) | Cron `* * * * *` | Varre tickets em `status='new'` e `attendant_id IS NULL` em todos os tenants. Para cada ticket com `created_at <= now() - 5min`, faz `SETNX` em `{slug}:queue_alert:{ticket_id}` com TTL 1h e — se conseguiu — emite notificações `ticket.queued` para todos os supervisores do departamento. **Fixed 5min, non-configurable** (FR-009). |
| `AppointmentReminderJob` (novo) | Cron por tenant (default `0 20 * * *`) | Para cada tenant com `reminder_enabled = true`, varre `appointments` do dia seguinte (00:00–23:59 tenant TZ). Para cada appointment com contato com telefone, template `appointment_reminder` aprovado, canal WA ativo: monta variáveis, enfileira em `{slug}:outgoing_messages` via `OutgoingMessagePublisher`. Idempotência: `SETNX` em `{slug}:reminder_sent:{appointment_id}:{yyyyMMdd}`. Falhas geram evento `reminder_failed` (ticket ou MongoDB). |
| `NotificationArchiverJob` (novo) | Cron `0 3 * * *` | Para cada tenant ativo, `UPDATE notifications SET archived_at = NOW() WHERE created_at < NOW() - INTERVAL '90 days' AND archived_at IS NULL`. **Soft delete** (FR-007). |

**Recurring jobs schedule per-tenant**: o `AppointmentReminderJob` é registrado com `RecurringJobId = "appointment-reminder:{tenant_slug}"` e cron derivado de `tenant_notification_settings.reminder_time`. Quando o admin altera `reminder_time`, o `PUT /api/notification-settings` chama `RecurringJob.AddOrUpdate` para re-agendar (ver research §R5).

**WebSocket**: ASP.NET Core nativo + Redis Pub/Sub (ADR-005). Reutiliza `/ws/crm`. 2 eventos novos publicados em `{slug}:ws:attendant:{attendant_id}` (canal individual, não departamento — notification é endereçada a um atendente):

- `notification.new` — `{ id, event_type, title, body, entity_type, entity_id, created_at }`. Emitido por handlers após persist.
- `notification.unread_count` — `{ count }`. Emitido após persist, mark-as-read e mark-all-as-read.

**Canal individual de atendente**: já existe (`RedisKeys.WsAttendant`). `CrmWebSocketEndpoint` assina o canal do atendente autenticado adicionalmente ao(s) canal(is) de departamento.

**Web Push (FR-013/014)**:

- Biblioteca **`WebPush`** (NuGet, MIT, mantida) — implementa Web Push Protocol (RFC 8030 + VAPID). Decisão em research §R1.
- VAPID keys (`Push:VapidPublicKey`, `Push:VapidPrivateKey`, `Push:Subject`) em user-secrets dev; env vars em prod. Public key servida via `GET /api/push/vapid-public-key` para o frontend usar no `pushManager.subscribe()`.
- `WebPushDispatcher.SendAsync(subscription, payload, ct)` — captura `WebPushException` com `StatusCode == 410 Gone` e deleta a subscription do banco; outros erros geram log warning.
- Payload AES128GCM-encrypted (gerenciado pela lib). Tamanho máximo prático: 4 KB.

**Service Worker (frontend)**:

- Arquivo `src/sw-notifications.js` registrado por `WebPushService.register()` no boot do CRM.
- Service Worker NÃO é Angular SW (cache). Único job: receber `push` events e exibir notificação via `self.registration.showNotification(title, options)`.
- Click handler do SW: `clients.matchAll()` → se aba existir, foca + navega via `postMessage`; senão `clients.openWindow(payload.data.url)`.

**Manual Template Send (FR-023/024/025)**:

- `POST /api/tickets/{id}/send-template` body `{ template_id, variables: { name: value, ... } }`.
- Validações: ticket existe + atendente é o responsável (ou tem perm `Tickets.AnyAttendantSendTemplate` — V1.1, fora do escopo); template `approved`; todas as variáveis preenchidas.
- Renderiza preview server-side com a mesma engine usada na lib WhatsApp templates (Spec 008) — fonte única de verdade.
- Enfileira via `OutgoingMessagePublisher` em `{slug}:outgoing_messages` com `sender_type = attendant`, `message_type = template`. Passa pelo `WaOutgoingGuard` (allows attendant+template).

**Preferências (FR-015)**:

- `attendant_notification_preferences.event_push_flags` é `jsonb` com formato `{ "ticket.queued": false, "ticket.sla_breached": true, ... }`. Chave ausente = default `true`.
- Aplicação:
  1. Handler cria a notification em `notifications` (in-app **sempre** persiste).
  2. Antes de enviar push: SELECT prefs do atendente; se `push_enabled = false` → skip. Se `event_push_flags[event_type] == false` → skip.

**Regra de silêncio (FR-010)**:

- Frontend: quando `TicketDetailComponent` monta para `ticket_id`, envia WS msg `attendant.viewing_ticket` com `ticket_id`. Backend grava `{slug}:attendant_active_ticket:{attendant_id} = ticket_id` (TTL 60s). Heartbeat a cada 30s renova.
- `TicketNewMessageHandler` e `TicketClientRepliedHandler`: antes do push, lê a chave; se o `ticket_id` bate, **skip push** (in-app ainda persiste, pois feed pode ser consultado depois).

**Testing**:

- Backend: xUnit + Testcontainers (Postgres + Redis + Mongo). Testes principais:
  - `NotificationsEndpointTests` — paginação, mark-as-read, unread-count.
  - `TicketAssignedHandlerTests` — emit + persist + WS publish.
  - `TicketQueueMonitorJobTests` — não notifica antes de 5min; notifica exatamente após 5min; idempotente.
  - `AppointmentReminderJobTests` — idempotência por dia, condições de envio (canal/phone/template/toggle), falha → `reminder_failed` event.
  - `WebPushDispatcherTests` — 410 remove subscription; outros erros não removem.
  - `NotificationArchiverJobTests` — > 90 dias.
  - `SendTemplateEndpointTests` — variáveis vazias, template não-aprovado, ticket não autorizado.
- Frontend: Karma + Jasmine `.spec.ts` co-localizados. Mock `WebSocket` e `pushManager` em testes do SW.
- Concorrência: dois eventos simultâneos para o mesmo atendente → duas notifications criadas (sem race).
- Push: teste integração com endpoint Mockoon que retorna 410 → subscription removida.

**Target Platform**: Linux ARM64 (API); Cloudflare Pages (CRM SW publicado junto com o build).

**Project Type**: Web service (API .NET 10) + 1 SPA Angular (CRM) + Service Worker. Sem novo projeto.

**Performance Goals**:

- p95 in-app delivery (event fired → WS frame in atendente): **< 2s** (SC-001).
- p95 push delivery: **< 5s** (SC-002).
- `AppointmentReminderJob` para 1000 appointments do dia: **< 5min** (SC-004).

**Constraints**:

- Pacote `WebPush` NuGet é a única dependência nova. Sem mudança no stack constitucional (Hangfire, Redis, EF Core).
- Service Worker do CRM precisa estar disponível em `/` (escopo raiz). Cloudflare `_headers` precisa permitir `service-worker.js` com `Service-Worker-Allowed: /`.
- VAPID public/private keys são secrets — não commitar.

**Scale/Scope**:

- ~5–10 atendentes por tenant em V1; até ~100 notifications/dia/atendente em pico operacional.
- 100–500 appointments/dia/tenant em pico → 100–500 reminders/dia/tenant.
- 1–3 push subscriptions ativas por atendente (browsers diferentes).

---

## Constitution Check

*Gate: passou pré-Fase 0; revalidado pós-Fase 1 (ver seção final).*

| Princípio | Compliance | Notas |
|---|---|---|
| **I. Multi-Tenant Isolation (NN)** | ✅ | Tabelas `notifications`, `push_subscriptions`, `attendant_notification_preferences` em `tenant_{slug}.*`. `tenant_notification_settings` em `public.*` é **dados sobre o tenant**, não de feature — segue padrão de `public.tenants`. Redis keys via `RedisKeys.NotificationQueueAlert` / `ReminderSent` / `AttendantActiveTicket` (extensões do helper existente). MongoDB collections reutilizam `{slug}_*`. |
| **II. AI-First, Human-Assisted** | ✅ | Notificações são canal complementar — não removem nem alteram handoff. AI continua proibida de enviar templates (FR não muda Spec 008). `AppointmentReminderJob` usa `sender_type=system` (fora do range proibido pelo `WaOutgoingGuard`). |
| **III. Channel Agnosticism** | ✅ | Push e in-app são canais de saída para atendente, não para cliente. Lembrete WhatsApp usa pipeline existente (`OutgoingMessageWorker` → `WhatsAppOutgoingAdapter`); sem branching de canal aqui. |
| **IV. Security / LGPD (NN)** | ✅ | VAPID keys via env/user-secrets. `notifications.body` pode conter snippet de mensagem do cliente — **mantemos em DB tenant-scoped no Brasil** (mesma garantia do resto). Push payload contém title + body + URL; body truncado em 80 chars (FR mantém spec). Subscriptions são associadas ao atendente autenticado; logout não deleta automaticamente (V1.1) mas pode ser revogado em Preferências. JWT continua ≤15min; sem mudança em tokens. |
| **V. Simplicity / YAGNI** | ✅ | 1 lib nova (`WebPush`). Sem framework de mensageria; sem SignalR (ADR-005). Sem cache de unread-count (FR-003 explícito: live). Sem priority/grouping de notifications em V1. |
| **VI. Observability / Auditability** | ✅ | `reminder_failed` materializa evento imutável em `{slug}_ticket_events` (tickets) ou `{slug}_agent_activity_logs` (avulsos). Logs Serilog para WebPush 4xx/5xx. Métricas: `notifications_delivered_total{event_type}`, `push_subscriptions_active{tenant}`, `reminders_sent_total`, `reminders_failed_total{reason}`. |
| **VII. Test Discipline** | ✅ | Backend tests com Testcontainers (Postgres + Redis + Mongo). Sem mock de DB. Constants centralizadas (`NotificationEventTypes`, `RedisKeys.*`). Frontend `.spec.ts` co-localizados, incluindo SW (em ambiente jsdom com mock de `self.registration`). |

**Veredicto pré-Fase 0**: ✅ APROVADO. Sem violações; nenhuma entrada na Complexity Tracking necessária.

---

## Project Structure

### Documentation (this feature)

```text
specs/010-notifications/
├── plan.md              # Este arquivo
├── research.md          # Fase 0 — decisões sobre WebPush lib, scheduling per-tenant, dep Spec 11
├── data-model.md        # Fase 1 — entidades + DDL
├── contracts/           # Fase 1 — REST + WS + SW
│   ├── notifications-api.md
│   ├── push-api.md
│   ├── preferences-api.md
│   ├── tenant-settings-api.md
│   ├── manual-template-api.md
│   ├── notifications-websocket-events.md
│   └── service-worker-contract.md
├── quickstart.md        # Fase 1 — passo a passo dev (VAPID gen, registrar SW, testar push)
├── checklists/
│   └── requirements.md  # gerado por /speckit-specify (já existe)
└── tasks.md             # Fase 2 — saída do /speckit-tasks (NÃO criado aqui)
```

### Source Code (repository root)

```text
src/omniDesk.Api/
├── Domain/
│   ├── Notifications/
│   │   ├── Notification.cs                       # NOVO — entidade
│   │   ├── NotificationEventType.cs              # NOVO — enum / static class
│   │   ├── PushSubscription.cs                   # NOVO
│   │   ├── AttendantNotificationPreferences.cs   # NOVO
│   │   └── TenantNotificationSettings.cs         # NOVO
│   └── Authorization/
│       └── Permissions.cs                        # EXTENDIDO — Notifications.* perms
├── Features/
│   ├── Notifications/
│   │   ├── INotificationService.cs               # EXPANDIDO (substitui stub Spec 009)
│   │   ├── NotificationService.cs                # NOVO — implementação real
│   │   ├── NotificationsEndpoints.cs             # NOVO — list, unread-count, read, read-all
│   │   ├── PushEndpoints.cs                      # NOVO — subscribe, unsubscribe, vapid-public-key
│   │   ├── PreferencesEndpoints.cs               # NOVO — GET/PUT
│   │   ├── TenantSettingsEndpoints.cs            # NOVO — GET/PUT (TenantAdmin)
│   │   ├── Queries/
│   │   │   ├── ListNotificationsQuery.cs         # NOVO
│   │   │   └── UnreadCountQuery.cs               # NOVO
│   │   ├── Commands/
│   │   │   ├── MarkAsReadCommand.cs              # NOVO
│   │   │   ├── MarkAllAsReadCommand.cs           # NOVO
│   │   │   ├── SubscribePushCommand.cs           # NOVO
│   │   │   ├── UnsubscribePushCommand.cs         # NOVO
│   │   │   ├── UpdatePreferencesCommand.cs       # NOVO
│   │   │   └── UpdateTenantSettingsCommand.cs    # NOVO
│   │   └── Handlers/
│   │       ├── TicketAssignedHandler.cs          # NOVO
│   │       ├── TicketTransferredHandler.cs       # NOVO
│   │       ├── TicketNewMessageHandler.cs        # NOVO
│   │       ├── TicketClientRepliedHandler.cs     # NOVO
│   │       ├── TicketSlaWarningHandler.cs        # NOVO
│   │       ├── TicketSlaBreachedHandler.cs       # NOVO
│   │       ├── TicketQueuedHandler.cs            # NOVO
│   │       └── ReminderFailedHandler.cs          # NOVO
│   ├── Tickets/
│   │   └── SendTemplateEndpoint.cs               # NOVO — POST /api/tickets/{id}/send-template
│   └── WhatsApp/
│       └── Jobs/
│           └── AppointmentReminderJob.cs         # NOVO
├── Infrastructure/
│   ├── Notifications/
│   │   ├── NotificationRepository.cs             # NOVO
│   │   ├── PushSubscriptionRepository.cs         # NOVO
│   │   ├── AttendantPreferencesRepository.cs     # NOVO
│   │   ├── TenantSettingsRepository.cs           # NOVO
│   │   └── NotificationsConfiguration.cs         # NOVO — EF Core fluent config
│   ├── Push/
│   │   ├── WebPushDispatcher.cs                  # NOVO — wraps WebPush lib
│   │   └── VapidKeyProvider.cs                   # NOVO — reads IConfiguration
│   ├── WebSockets/
│   │   └── NotificationEventPublisher.cs         # NOVO — publishes notification.new + unread_count
│   ├── Jobs/
│   │   ├── TicketQueueMonitorJob.cs              # NOVO
│   │   └── NotificationArchiverJob.cs            # NOVO
│   ├── Persistence/
│   │   └── Migrations/
│   │       └── Add_Notifications_Push_Preferences.sql      # NOVO — migration tenant-scoped
│   │       └── Add_TenantNotificationSettings.sql          # NOVO — migration public
│   ├── Authorization/
│   │   └── RedisKeys.cs                          # ESTENDIDO — Notification queue alert, reminder sent, attendant active ticket
│   └── Appointments/
│       └── IAppointmentReadRepository.cs         # NOVO — bridge para Spec 11 (ou stub)
├── Hubs/
│   ├── Events/
│   │   └── NotificationEvents.cs                 # NOVO — constants
│   └── CrmWebSocketEndpoint.cs                   # ESTENDIDO — subscribe ao canal do atendente
└── Program.cs                                    # ESTENDIDO — DI registrations, RecurringJob adds
└── tests/omniDesk.Api.Tests/
    ├── Features/Notifications/
    │   ├── NotificationsEndpointTests.cs
    │   ├── PushEndpointsTests.cs
    │   ├── PreferencesEndpointsTests.cs
    │   ├── TenantSettingsEndpointsTests.cs
    │   └── Handlers/
    │       ├── TicketAssignedHandlerTests.cs
    │       ├── TicketTransferredHandlerTests.cs
    │       ├── TicketNewMessageHandlerTests.cs
    │       ├── TicketSlaBreachedHandlerTests.cs
    │       ├── TicketQueuedHandlerTests.cs
    │       └── ReminderFailedHandlerTests.cs
    ├── Features/Tickets/
    │   └── SendTemplateEndpointTests.cs
    ├── Features/WhatsApp/Jobs/
    │   └── AppointmentReminderJobTests.cs
    ├── Infrastructure/Push/
    │   └── WebPushDispatcherTests.cs
    └── Infrastructure/Jobs/
        ├── TicketQueueMonitorJobTests.cs
        └── NotificationArchiverJobTests.cs

src/omniDesk.Crm/src/
├── sw-notifications.js                           # NOVO — Service Worker
├── app/
│   ├── core/
│   │   └── services/
│   │       ├── web-push.service.ts (+ .spec.ts)             # NOVO
│   │       └── notification-stream.service.ts (+ .spec.ts)  # NOVO
│   ├── features/
│   │   ├── notifications/
│   │   │   ├── notification-bell.component.{ts,html,scss,spec.ts}      # NOVO
│   │   │   ├── notification-list.component.{ts,html,scss,spec.ts}      # NOVO
│   │   │   ├── notification-item.component.{ts,html,scss,spec.ts}      # NOVO
│   │   │   ├── preferences-page.component.{ts,html,scss,spec.ts}       # NOVO
│   │   │   └── notifications.service.{ts,spec.ts}                       # NOVO
│   │   ├── notification-settings/
│   │   │   └── settings-page.component.{ts,html,scss,spec.ts}          # NOVO
│   │   ├── whatsapp-templates/
│   │   │   └── send-template-modal.component.{ts,html,scss,spec.ts}    # NOVO
│   │   └── ticket-detail/
│   │       └── ticket-detail.component.ts                              # ESTENDIDO — botão "Enviar template" + emit attendant.viewing_ticket
│   ├── layout/
│   │   └── header/
│   │       └── header.component.{ts,html}                              # ESTENDIDO — slot do bell
│   └── app.routes.ts                                                   # ESTENDIDO — /preferences, /settings/notifications
└── angular.json                                                        # ESTENDIDO — asset entry para sw-notifications.js + _headers Service-Worker-Allowed
```

**Structure Decision**: Reaproveita estrutura `Features/<Domain>/{Endpoints,Commands,Queries,Handlers}` consolidada por Specs 005–009. Sem novos projetos C# ou Angular. Service Worker é arquivo único na raiz do CRM, copiado como asset pelo `angular.json` (mesmo padrão de `_redirects` e `_headers`).

---

## Constitution Check — Post-Design Re-evaluation

Revalidado após data-model.md + contracts/ + quickstart.md. Nenhuma surpresa:

| Princípio | Status pós-Fase 1 | Notas |
|---|---|---|
| **I. Multi-Tenant Isolation (NN)** | ✅ | Tabelas `notifications`, `push_subscriptions`, `attendant_notification_preferences` confirmadas em `tenant_{slug}.*`. `tenant_notification_settings` em `public.*` justificado (cfg sobre o tenant). Novas chaves Redis (`queue_alert`, `reminder_sent`, `attendant_active_ticket`) prefixadas via `RedisKeys` helper. Confirmado pelo data-model §"Entity Overview". |
| **II. AI-First, Human-Assisted** | ✅ | Sem alteração no orquestrador. `AppointmentReminderJob` usa `sender_type=system`, fora do range proibido pelo `WaOutgoingGuard`. Manual template send é `sender_type=attendant`, também permitido. |
| **III. Channel Agnosticism** | ✅ | Reminder usa pipeline existente `OutgoingMessagePublisher` → `WhatsAppOutgoingAdapter`. Sem branch de canal. |
| **IV. Security / LGPD (NN)** | ✅ | VAPID keys via user-secrets (dev) e env vars (prod) — documentado em quickstart §1. `notifications.body` permanece no DB tenant-scoped no Brasil. Push payload limitado a 4 KB; truncado em 120 chars no servidor. Soft delete (`archived_at`) cumpre §IV. |
| **V. Simplicity / YAGNI** | ✅ | 1 nova lib (`WebPush`, justificada em research §R1). 0 abstrações novas além do que o caso de uso pede. Sem cache de unread-count (FR-003 explícito). |
| **VI. Observability / Auditability** | ✅ | `reminder_failed` materializa evento imutável em `{slug}_ticket_events` ou `{slug}_agent_activity_logs`. Métricas planejadas: `notifications_delivered_total{event_type}`, `push_subscriptions_active{tenant}`, `reminders_sent_total`, `reminders_failed_total{reason}`. |
| **VII. Test Discipline** | ✅ | Backend tests com Testcontainers; sem mock DB. Constants centralizadas (`NotificationEventTypes`, `RedisKeys.*`). Frontend `.spec.ts` co-localizados. Quickstart §10 documenta como rodar suite localmente. |

**Veredicto pós-Fase 1**: ✅ APROVADO. Nenhuma violação introduzida pelo design detalhado. Pronto para `/speckit-tasks`.

---

## Complexity Tracking

> Sem violações de constituição. Tabela não preenchida.
