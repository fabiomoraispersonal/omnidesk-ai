---
description: "Task list for Notifications (in-app bell + browser push + WhatsApp reminders) implementation"
---

# Tasks: Notifications (Spec 010)

**Input**: Design documents from `/specs/010-notifications/`
**Prerequisites**: plan.md, spec.md, research.md (R1–R15), data-model.md, contracts/{notifications-api, push-api, preferences-api, tenant-settings-api, manual-template-api, notifications-websocket-events, service-worker-contract}.md, quickstart.md

**Tests**: Constituição §VII (Test Discipline) torna testes **obrigatórios**. Backend: xUnit + Testcontainers (Postgres + Redis + Mongo — já configurados pelas Specs 007/008/009). CRM: Angular TestBed (`.spec.ts` co-localizado).

**Organization**: Tarefas agrupadas por user story (US1–US6 do spec.md) para entrega independente. Esta spec **substitui** o `NoOpNotificationService` introduzido pela Spec 009 — a substituição é feita na Foundational para evitar regressão silenciosa de notificações que a Spec 009 já espera dispará-las.

## Format: `[ID] [P?] [Story?] [Opus?] Description`

- **[P]**: Pode rodar em paralelo (arquivo distinto, sem dependência pendente)
- **[Story]**: Mapeia para user story (US1–US6) — ausente em Setup/Foundational/Polish
- **[Opus]**: Tarefa **complexa** — recomendado trocar para Claude Opus 4.7 durante a execução. São tasks com (a) atomicidade multi-store (SQL + Redis + WS + push), (b) crypto / VAPID, (c) job per-tenant scheduling com timezone, ou (d) Service Worker / Web Push protocol. O resto pode (e deve, por custo/velocidade) rodar com **Sonnet 4.6**.
- Caminhos relativos do repo: `src/omniDesk.Api/...`, `src/omniDesk.Crm/...`

## Path Conventions

- Backend: `src/omniDesk.Api/{Domain,Features,Hubs,Infrastructure}/`
- Backend tests: `src/omniDesk.Api/tests/omniDesk.Api.Tests/{Domain,Features,Infrastructure,Helpers}/`
- CRM Angular: `src/omniDesk.Crm/src/app/{features,core,layout}/`
- Migrations: `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` (padrão `Add_*.sql`)
- Service Worker: `src/omniDesk.Crm/src/sw-notifications.js`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Chaves de configuração, pacote NuGet `WebPush`, estrutura de pastas, registros DI base.

- [ ] T001 Adicionar referência NuGet `WebPush` (>= 2.0.0) em `src/omniDesk.Api/omniDesk.Api.csproj` (research §R1)
- [ ] T002 [P] Adicionar chaves em `src/omniDesk.Api/appsettings.json` (defaults vazios) e `src/omniDesk.Api/appsettings.Development.json` (apenas placeholders comentados): `Push:VapidSubject`, `Push:VapidPublicKey`, `Push:VapidPrivateKey`, `Notifications:ArchiveRetentionDays=90`, `Notifications:QueueAlertThresholdMinutes=5`
- [ ] T003 [P] Criar estrutura backend: `src/omniDesk.Api/Domain/Notifications/`, `src/omniDesk.Api/Features/Notifications/{Commands,Queries,Handlers}/`, `src/omniDesk.Api/Infrastructure/{Notifications,Push,Appointments}/`
- [ ] T004 [P] Criar estrutura CRM: `src/omniDesk.Crm/src/app/features/{notifications,notification-settings,whatsapp-templates}/`, `src/omniDesk.Crm/src/app/core/services/` (já existe — apenas garantir)
- [ ] T005 [P] Criar estrutura de testes: `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Notifications/{Handlers,}/`, `Infrastructure/Push/`, `Infrastructure/Jobs/`
- [ ] T006 [P] Criar README curto em `src/omniDesk.Api/Features/Notifications/README.md` linkando plan.md, research.md e contracts/
- [ ] T007 [P] Atualizar `src/omniDesk.Crm/angular.json` para incluir asset entry para `sw-notifications.js` e atualizar `src/_headers` com `Service-Worker-Allowed: /` para `/sw-notifications.js` (contract service-worker §"Cloudflare deployment")

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Migrations, domain entities, repositórios, expansão do `INotificationService`, `RedisKeys`, event constants, registros DI. **Substitui `NoOpNotificationService`** pela implementação real (sem push ainda — push entra na US2).

**⚠️ CRITICAL**: Nenhuma user story pode começar antes desta fase completar. A expansão do `INotificationService` quebra a assinatura usada por Spec 009 (`TicketCreationGateway`, `TransferTicketCommand`) — call sites têm de ser atualizados aqui.

### Migrations

- [ ] T008 [Opus] Criar `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Notifications_Push_Preferences.sql` (tenant-scoped) conforme data-model.md §Notification + §PushSubscription + §AttendantNotificationPreferences. Inclui: `CREATE TABLE notifications`, `CREATE TABLE push_subscriptions`, `CREATE TABLE attendant_notification_preferences`, todos os índices (incluindo o parcial `WHERE archived_at IS NULL`). Idempotente (`IF NOT EXISTS`). **Por que Opus**: 3 tabelas + 4 índices + FK cascades; erro na ordem ou no schema scope corrompe schemas de tenants existentes
- [ ] T009 Criar `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_TenantNotificationSettings.sql` (schema `public`) — `CREATE TABLE public.tenant_notification_settings` conforme data-model.md §TenantNotificationSettings (FK → `public.tenants`, defaults `false/false/'20:00'`). Idempotente
- [ ] T010 Estender `src/omniDesk.Api/tests/omniDesk.Api.Tests/Helpers/TenantSchemaFixture.cs` para aplicar as 2 migrations novas em `ApplySchemaAsync` e adicionar `notifications`, `push_subscriptions`, `attendant_notification_preferences`, `public.tenant_notification_settings` à lista de truncate de `TruncateTenantTablesAsync`

### Domain entities + constants

- [ ] T011 [P] Criar `src/omniDesk.Api/Domain/Notifications/NotificationEventTypes.cs` — static class com 8 constantes + `IReadOnlySet<string> AllowedValues` (data-model §NotificationEventType)
- [ ] T012 [P] Criar `src/omniDesk.Api/Domain/Notifications/Notification.cs` — entidade EF Core com `Id, AttendantId, EventType, Title, Body, EntityType, EntityId, IsRead, CreatedAt, ArchivedAt`
- [ ] T013 [P] Criar `src/omniDesk.Api/Domain/Notifications/PushSubscription.cs` — entidade com `Id, AttendantId, Endpoint, P256dh, Auth, UserAgent, CreatedAt`
- [ ] T014 [P] Criar `src/omniDesk.Api/Domain/Notifications/AttendantNotificationPreferences.cs` — entidade com `AttendantId (PK), PushEnabled, EventPushFlags (Dictionary<string, bool> mapeado para jsonb), UpdatedAt`
- [ ] T015 [P] Criar `src/omniDesk.Api/Domain/Notifications/TenantNotificationSettings.cs` — entidade com `TenantId (PK), FollowUpEnabled, ReminderEnabled, ReminderTime (TimeOnly), UpdatedAt`
- [ ] T016 [P] Criar `src/omniDesk.Api/Hubs/Events/NotificationEvents.cs` — `public const string NotificationNew = "notification.new"; public const string NotificationUnreadCount = "notification.unread_count";`

### EF Core configurations + DbContext extensions

- [ ] T017 Criar `src/omniDesk.Api/Infrastructure/Notifications/NotificationsConfiguration.cs` com `NotificationConfiguration`, `PushSubscriptionConfiguration`, `AttendantNotificationPreferencesConfiguration` (data-model §"EF Core Configurations")
- [ ] T018 Estender `src/omniDesk.Api/Infrastructure/Persistence/AppDbContext.cs` adicionando `DbSet<Notification> Notifications`, `DbSet<PushSubscription> PushSubscriptions`, `DbSet<AttendantNotificationPreferences> AttendantNotificationPreferences` + `modelBuilder.ApplyConfiguration(new NotificationConfiguration())` etc.
- [ ] T019 Adicionar `DbSet<TenantNotificationSettings> TenantNotificationSettings` em `PublicDbContext` (criar configuration inline ou em `Infrastructure/Notifications/TenantNotificationSettingsConfiguration.cs`) com `ToTable("tenant_notification_settings", "public")`

### Redis keys + WS publisher

- [ ] T020 Estender `src/omniDesk.Api/Infrastructure/Authorization/RedisKeys.cs` adicionando: `NotificationQueueAlert(slug, ticketId)`, `ReminderSent(slug, appointmentId, dateYyyymmdd)`, `AttendantActiveTicket(slug, attendantId)`. Cada uma com docstring que liga ao FR correspondente
- [ ] T021 Criar `src/omniDesk.Api/Infrastructure/WebSockets/NotificationEventPublisher.cs` com `PublishNewAsync(slug, attendantId, payload)` e `PublishUnreadCountAsync(slug, attendantId, count)` — publica em `RedisKeys.WsAttendant(slug, attendantId)`. Envelope segue padrão de `TicketEventPublisher` (type/payload/timestamp/tenant_slug). Snake-case JSON

### Repositories

- [ ] T022 [P] Criar `src/omniDesk.Api/Infrastructure/Notifications/NotificationRepository.cs` — métodos `AddAsync`, `GetByIdForAttendantAsync`, `ListForAttendantAsync(attendantId, page, perPage, unreadOnly)`, `CountUnreadAsync(attendantId)`, `MarkAsReadAsync(notificationId, attendantId)`, `MarkAllAsReadAsync(attendantId)`, `ArchiveOlderThanAsync(cutoff)`
- [ ] T023 [P] Criar `src/omniDesk.Api/Infrastructure/Notifications/PushSubscriptionRepository.cs` — `UpsertAsync(attendantId, endpoint, p256dh, auth, userAgent)`, `DeleteByEndpointForAttendantAsync(attendantId, endpoint)`, `DeleteByEndpointAsync(endpoint)` (usada pelo dispatcher em 410), `GetByAttendantAsync(attendantId)`
- [ ] T024 [P] Criar `src/omniDesk.Api/Infrastructure/Notifications/AttendantPreferencesRepository.cs` — `GetAsync(attendantId)` (retorna defaults se ausente), `UpsertAsync(attendantId, pushEnabled, eventPushFlags)`
- [ ] T025 [P] Criar `src/omniDesk.Api/Infrastructure/Notifications/TenantSettingsRepository.cs` (no PublicDbContext) — `GetAsync(tenantId)` retorna defaults se ausente, `UpsertAsync(tenantId, followUpEnabled, reminderEnabled, reminderTime)`

### INotificationService (expansion — replaces Spec 009 NoOp)

- [ ] T026 [Opus] Reescrever `src/omniDesk.Api/Features/Notifications/INotificationService.cs` com as 8 assinaturas de research §R8. Renomear o `NoOpNotificationService` existente para `Tests/Helpers/NotificationServiceStub.cs` e mover para o assembly de testes (compat 3 call sites). **Por que Opus**: refactor de interface usada por 3+ call sites de produção (TicketCreationGateway, TransferTicketCommand) — quebrar compilação aqui sem cascade adequado bloqueia toda a build
- [ ] T027 Criar `src/omniDesk.Api/Features/Notifications/NotificationService.cs` implementando a interface. **Sem push ainda** (push é US2 / T064–T070). Cada método: (a) monta título/corpo PT-BR conforme spec.md tabela 2.2; (b) chama `NotificationRepository.AddAsync`; (c) chama `NotificationEventPublisher.PublishNewAsync` + `PublishUnreadCountAsync`. Para `NotifySlaBreachedAsync` e `NotifyTicketQueuedAsync`, expande recipients via `SupervisorLookupService` (T028)
- [ ] T028 Criar `src/omniDesk.Api/Features/Notifications/SupervisorLookupService.cs` com `GetDepartmentSupervisorsAsync(departmentId, ct)` retornando `IReadOnlyList<Guid>` (attendant ids) conforme research §R6: `TenantAdmin` global + `Supervisor` linkado via `attendant_departments`. Cache de 60s por departmentId em memória (`MemoryCache`) — invalidate é V1.1 (não bloqueia)
- [ ] T029 Atualizar `src/omniDesk.Api/Program.cs`: substituir `AddScoped<INotificationService, NoOpNotificationService>()` por `AddScoped<INotificationService, NotificationService>()`. Adicionar registros: `AddScoped<NotificationRepository>`, `AddScoped<PushSubscriptionRepository>`, `AddScoped<AttendantPreferencesRepository>`, `AddScoped<TenantSettingsRepository>`, `AddScoped<NotificationEventPublisher>`, `AddScoped<SupervisorLookupService>`, `AddSingleton<IMemoryCache>` (se ainda não tiver)
- [ ] T030 Atualizar call sites do `INotificationService` que mudaram de assinatura: `src/omniDesk.Api/Features/Tickets/TicketCreationGateway.cs` (passar `contactName` extraído de `request.ContactHints`), `src/omniDesk.Api/Features/Distribution/Commands/TransferTicketCommand.cs` (chamar `NotifyTicketTransferredAsync`), `src/omniDesk.Api/Features/Distribution/PickupTicketEndpoint.cs` (chamar `NotifyTicketAssignedAsync`). Atualizar os 3 testes em `tests/.../Features/TicketCreationGateway/` para usar `NotificationServiceStub` (do test helper)

### Foundational tests

- [ ] T031 [P] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Notifications/NotificationServiceTests.cs` cobrindo: `NotifyTicketAssignedAsync` persiste notification correta + publica WS; `NotifyTicketQueuedAsync` faz fan-out para múltiplos supervisores; `NotifySlaBreachedAsync` notifica atendente + supervisores; idempotência básica
- [ ] T032 [P] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Notifications/SupervisorLookupServiceTests.cs` — seed 1 TenantAdmin + 2 Supervisors (1 no depto, 1 fora), assert retorna 2 (admin + supervisor do depto)

**Checkpoint**: ✅ Build verde. Spec 009 continua passando (mas agora notifications são persistidas de verdade). Pronto para iniciar User Stories em paralelo.

---

## Phase 3: User Story 1 — In-App Notification Bell (Priority: P1) 🎯 MVP

**Goal**: Atendente vê badge de não lidas no header; clica → painel com lista paginada; clica em uma → marca como lida e navega para o ticket; "Marcar todas como lidas" funciona.

**Independent Test**: Atendente logado no CRM. Acionar uma atribuição via API (Spec 009). Em ≤ 2s o badge aparece com `1`. Painel mostra a notificação. Clicar → ticket abre + badge zera. Testável sem push e sem WhatsApp.

### Tests for User Story 1 ⚠️

- [ ] T033 [P] [US1] Criar `tests/.../Features/Notifications/NotificationsEndpointTests.cs` — GET feed paginado (20/pág), `unread_only=true` filtra, `unread-count` retorna live (não cache), `PATCH /{id}/read` flipa `is_read`, `POST /read-all` zera todas, erro 403 para id de outro atendente
- [ ] T034 [P] [US1] Criar `tests/.../Features/Notifications/Handlers/TicketAssignedHandlerTests.cs` — `NotifyTicketAssignedAsync` é chamado quando ticket é criado via `TicketCreationGateway`; notification persistida com `event_type='ticket.assigned'`, `entity_type='ticket'`, `entity_id=<ticket_id>`; WS event publicado em `{slug}:ws:attendant:{attendantId}`

### Implementation for User Story 1

- [ ] T035 [P] [US1] Criar `src/omniDesk.Api/Features/Notifications/Queries/ListNotificationsQuery.cs` com `ExecuteAsync(attendantId, page, perPage, unreadOnly, ct)` — usa `NotificationRepository.ListForAttendantAsync`. Retorna `(items, total)`. Filtra `archived_at IS NULL`
- [ ] T036 [P] [US1] Criar `src/omniDesk.Api/Features/Notifications/Queries/UnreadCountQuery.cs` com `ExecuteAsync(attendantId, ct)` retornando `min(count, 99)`
- [ ] T037 [P] [US1] Criar `src/omniDesk.Api/Features/Notifications/Commands/MarkAsReadCommand.cs` — `ExecuteAsync(notificationId, attendantId, ct)`; retorna `Result<NotificationReadDto>`. Erros: `NOT_FOUND`, `NOT_OWNER`. Pós-success: publica `notification.unread_count` com novo total
- [ ] T038 [P] [US1] Criar `src/omniDesk.Api/Features/Notifications/Commands/MarkAllAsReadCommand.cs` — `ExecuteAsync(attendantId, ct)`; retorna `int markedCount`. Pós-success: publica `notification.unread_count` com `count=0`
- [ ] T039 [US1] Criar `src/omniDesk.Api/Features/Notifications/NotificationsEndpoints.cs` com `MapNotificationsEndpoints` registrando: `GET /`, `GET /unread-count`, `PATCH /{id}/read`, `POST /read-all`. Cada handler chama o query/command correspondente. Resposta usa envelope padrão `{ success, data, meta }` (conforme padrão Spec 002). Validador FluentValidation para `per_page` ∈ [1,50]
- [ ] T040 [US1] Registrar em `Program.cs`: `app.MapGroup("/api/notifications").MapNotificationsEndpoints().RequireAuthorization()`. Registrar DI: `AddScoped<ListNotificationsQuery>`, `AddScoped<UnreadCountQuery>`, `AddScoped<MarkAsReadCommand>`, `AddScoped<MarkAllAsReadCommand>`

### Handlers (in-app only — push handlers vêm em US2)

- [ ] T041 [P] [US1] Criar `src/omniDesk.Api/Features/Notifications/Handlers/TicketAssignedHandler.cs` — assina (in-process) o ponto em que `TicketCreationGateway` e `TicketAssignmentService` emitem ticket.assigned. **Implementação**: em vez de event bus, esses sites já chamam `INotificationService.NotifyTicketAssignedAsync` (feito em T030). O handler é o próprio `NotificationService.NotifyTicketAssignedAsync` — esta task valida em integration test que o path está conectado e remove o `Handler` se não houver lógica adicional além do que `NotificationService` já faz. Documentar no README de Features/Notifications
- [ ] T042 [P] [US1] Criar `src/omniDesk.Api/Features/Notifications/Handlers/TicketTransferredHandler.cs` — análogo: `TransferTicketCommand` (Spec 009) chama `NotifyTicketTransferredAsync`; handler valida path E2E
- [ ] T043 [US1] Criar `src/omniDesk.Api/Features/Notifications/Handlers/TicketNewMessageHandler.cs` — chamado por `SendVisitorMessageCommand` (Spec 007) e `WhatsAppIncomingAdapter` (Spec 008) quando mensagem do cliente chega em ticket com `attendant_id` setado. Por enquanto **sem regra de silêncio** (a regra é em US2 quando há push). Em T043 só persiste in-app. Inserir hooks nos 2 call sites para chamar `INotificationService.NotifyNewMessageAsync`
- [ ] T044 [US1] Criar `src/omniDesk.Api/Features/Notifications/Handlers/TicketClientRepliedHandler.cs` — disparado pelo `WaitingClientResumerJob` (Spec 009) ao transição `waiting_client → in_progress`. Hook no job para chamar `INotificationService.NotifyClientRepliedAsync`

### Frontend — Bell + List + Service

- [ ] T045 [P] [US1] Criar `src/omniDesk.Crm/src/app/features/notifications/notifications.service.ts` (+ `.spec.ts`) — HttpClient para `GET /api/notifications`, `GET /api/notifications/unread-count`, `PATCH /{id}/read`, `POST /read-all`. Retorna Observables ou Signals (preferir Signals: `unreadCount = signal(0)`, `recent = signal<Notification[]>([])`)
- [ ] T046 [P] [US1] Criar `src/omniDesk.Crm/src/app/core/services/notification-stream.service.ts` (+ `.spec.ts`) — assina o WS `/ws/crm` (reutiliza `CrmWebSocketService` existente). Filtra mensagens `type: "notification.new"` e `type: "notification.unread_count"`, atualiza signals do `notifications.service`. No `ws.reconnected`, chama `unreadCount` REST para reconciliar (research §R15)
- [ ] T047 [P] [US1] Criar `src/omniDesk.Crm/src/app/features/notifications/notification-item.component.{ts,html,scss,spec.ts}` — recebe `@Input() notification`, renderiza ícone do `event_type`, título, body truncado em 80 chars, tempo relativo via `date-fns/formatDistanceToNow` (PT-BR locale). Click emite `(click)` que o pai consome
- [ ] T048 [US1] Criar `src/omniDesk.Crm/src/app/features/notifications/notification-list.component.{ts,html,scss,spec.ts}` — overlay/dropdown com scroll infinito (page param), botão "Marcar todas como lidas". Usa `notifications.service`. No click de uma item: `PATCH .../read` + `router.navigateByUrl(entity_type === 'ticket' ? '/tickets/'+entity_id : '/conversations/'+entity_id)`. Fallback: se 404 ao navegar, exibe toast "Item não encontrado" (edge case spec §)
- [ ] T049 [US1] Criar `src/omniDesk.Crm/src/app/features/notifications/notification-bell.component.{ts,html,scss,spec.ts}` — ícone PrimeIcons `bell`, badge PrimeNG com `unreadCount`. `unreadCount === 0` → badge oculto; `unreadCount > 99` → badge mostra "99+". Click toggle `NotificationListComponent` (PrimeNG `OverlayPanel` ou similar)
- [ ] T050 [US1] Estender `src/omniDesk.Crm/src/app/layout/header/header.component.{ts,html}` adicionando `<app-notification-bell>` no slot direito (antes do menu do user). Garantir que `NotificationStreamService.connect()` é chamado no `ngOnInit` do header (já autenticado neste ponto)

**Checkpoint**: ✅ User Story 1 completa. Atendente recebe notificações in-app em tempo real, lista funciona com paginação, marca como lida funciona, navegação funciona. Sem push, sem WhatsApp.

---

## Phase 4: User Story 2 — Browser Push Notifications (Priority: P2)

**Goal**: Permissão solicitada 1× no primeiro login; push entregue em ≤ 5s; multiple browsers do mesmo atendente recebem; 410 limpa subscription; silence rule (tab ativa) silencia push para o ticket aberto.

**Independent Test**: Logar como atendente em 2 browsers (Chrome + Firefox). Permitir notificações em ambos. Acionar `ticket.new_message` em outro ticket. Ambos browsers recebem push. Acionar para um ticket aberto na tab ativa de Chrome — só Firefox recebe push.

### Tests for User Story 2 ⚠️

- [ ] T051 [P] [US2] Criar `tests/.../Features/Notifications/PushEndpointsTests.cs` — `GET /api/push/vapid-public-key` retorna key configurada; `POST /api/push/subscribe` cria registro; chamar de novo com mesmo endpoint faz upsert (não duplica); `DELETE /api/push/unsubscribe` remove; 401 sem auth
- [ ] T052 [P] [US2] Criar `tests/.../Infrastructure/Push/WebPushDispatcherTests.cs` — usa stub `WebPushClient` (interface adaptada). Cenários: 200 OK não toca DB; `WebPushException(StatusCode=410)` chama `DeleteByEndpointAsync`; `WebPushException(StatusCode=404)` também chama delete; `WebPushException(StatusCode=429)` apenas loga warning
- [ ] T053 [P] [US2] Criar `tests/.../Features/Notifications/Handlers/TicketNewMessageHandlerTests.cs` cobrindo silence rule: com `{slug}:attendant_active_ticket:{att}` setado para `ticket_X`, push para `ticket_X` é skip; push para `ticket_Y` ocorre normalmente; in-app **sempre** persiste

### Backend — VAPID + Endpoints

- [ ] T054 [Opus] [US2] Criar `src/omniDesk.Api/Infrastructure/Push/VapidKeyProvider.cs` — lê `Push:VapidSubject/PublicKey/PrivateKey` de `IConfiguration`. Valida formato base64url da public key e que subject começa com `mailto:`. Lança `InvalidOperationException` no startup se faltar config em prod (mas em dev permite tudo vazio). **Por que Opus**: validação de crypto config — falha silenciosa aqui leva a 0 pushes entregues em prod sem erro óbvio
- [ ] T055 [Opus] [US2] Criar `src/omniDesk.Api/Infrastructure/Push/WebPushDispatcher.cs` com `Task SendAsync(PushSubscription sub, string payloadJson, CancellationToken ct)`. Usa `WebPushClient.SendNotification(...)` com `VapidDetails` de `VapidKeyProvider`. Try/catch `WebPushException`: `410 / 404` → `PushSubscriptionRepository.DeleteByEndpointAsync(sub.Endpoint)` + log info; outros → log warning sem retry (research §R1). Truncar `body` para 120 chars antes do send (research §R10). **Por que Opus**: Web Push protocol é AES128GCM + JWT VAPID; erros silenciosos travam o canal todo
- [ ] T056 [US2] Criar `src/omniDesk.Api/Features/Notifications/PushEndpoints.cs` com `MapPushEndpoints`: `GET /vapid-public-key`, `POST /subscribe`, `DELETE /unsubscribe`. `SubscribePushCommand` faz upsert por `endpoint`. Registrar `app.MapGroup("/api/push").MapPushEndpoints().RequireAuthorization()` em Program.cs
- [ ] T057 [US2] Estender `NotificationService` (T027) para — **após** persistir in-app e publicar WS — disparar push: SELECT subscriptions do attendant + check prefs (`push_enabled` + `event_push_flags[event_type] != false`) + para cada subscription, `await WebPushDispatcher.SendAsync(...)`. Fan-out paralelo com `Task.WhenAll` mas com timeout 3s por sub. Atualizar handlers de SLA e queue equivalentemente

### Backend — Silence rule (active ticket)

- [ ] T058 [Opus] [US2] Estender `src/omniDesk.Api/Hubs/CrmWebSocketEndpoint.cs` para tratar mensagens do cliente `{ type: "attendant.viewing_ticket", ticket_id }` — se ticket_id não-null: `SET {slug}:attendant_active_ticket:{attendant_id} = {ticket_id} EX 60`; senão: `DEL ...`. **Por que Opus**: parsing seguro de mensagens vindas do client + interação com Redis no path crítico do WS (não pode bloquear connect)
- [ ] T059 [US2] Atualizar `TicketNewMessageHandler` (T043) e `TicketClientRepliedHandler` (T044): antes de chamar `WebPushDispatcher`, checar `{slug}:attendant_active_ticket:{attendant_id}`; se for o mesmo `ticket_id` do evento → skip push (in-app já persistido). Adicionar teste do skip path em `TicketNewMessageHandlerTests` (T053 já cobre)

### Frontend — Service Worker + Push Service

- [ ] T060 [Opus] [US2] Criar `src/omniDesk.Crm/src/sw-notifications.js` exatamente conforme contracts/service-worker-contract.md — install/activate/push/notificationclick. Sem cache. **Por que Opus**: Service Worker é code-once + runtime-debugado-via-DevTools-instável; padrões sutis como `event.waitUntil` ou `clients.matchAll` errados causam push silenciosamente perdido
- [ ] T061 [P] [US2] Criar `src/omniDesk.Crm/src/app/core/services/web-push.service.ts` (+ `.spec.ts`) — `register()` registra SW, lê VAPID public key via `GET /api/push/vapid-public-key`, decide permission flow (research §R7 e contracts/service-worker §"Permission flow"): se `default` → `Notification.requestPermission()` 1× e marca `localStorage.permission_requested = true`; se `denied` → não re-solicita; se `granted` → `pushManager.subscribe(...)` + `POST /api/push/subscribe`. Listener `navigator.serviceWorker.onmessage` → `Router.navigateByUrl(e.data.url)`. Método `unregister()` para logout
- [ ] T062 [US2] Chamar `WebPushService.register()` no boot do CRM autenticado (em `src/omniDesk.Crm/src/app/app.component.ts` `effect()` após login). Adicionar toggle em logout para `unsubscribe`
- [ ] T063 [US2] Estender `src/omniDesk.Crm/src/app/features/ticket-detail/ticket-detail.component.ts` para enviar `attendant.viewing_ticket` ao WS no `ngOnInit` (com `ticket_id`) + setInterval 30s heartbeat + `ngOnDestroy` envia `{ ticket_id: null }` (contract notifications-websocket-events §"attendant.viewing_ticket")

**Checkpoint**: ✅ User Story 2 completa. Push entrega em < 5s, múltiplos browsers, 410 limpa, silence rule funciona. Confirmar via quickstart §5–6.

---

## Phase 5: User Story 3 — SLA Breach and Queue Alerts (Priority: P2)

**Goal**: Atendente + supervisores recebem notificação quando SLA é rompido; supervisores recebem `ticket.queued` exatamente após 5 min de fila sem atendente; valor fixo, não configurável.

**Independent Test**: Configurar ticket com SLA de 1 min, deixar passar — atendente atribuído + 2 supervisores do depto recebem notificação. Criar ticket sem atendente, esperar 5 min — supervisores recebem; idempotente (não recebem 2 vezes).

### Tests for User Story 3 ⚠️

- [ ] T064 [P] [US3] Criar `tests/.../Features/Notifications/Handlers/TicketSlaBreachedHandlerTests.cs` — quando `TicketSlaMonitorJob` (Spec 009) emite breach, `NotifySlaBreachedAsync` é chamado: atendente + N supervisores recebem; sem atendente, só supervisores; teste com 0 supervisores também passa (sem erro)
- [ ] T065 [P] [US3] Criar `tests/.../Infrastructure/Jobs/TicketQueueMonitorJobTests.cs` — ticket em `new` há 4 min → 0 notificações; há 5 min → 1 round de notifs para supervisores; chamar job de novo → 0 (idempotente via Redis NX); 2 supervisores → 2 notification rows; após ticket atribuído, próximo run não notifica novamente (status mudou)

### Implementation for User Story 3

- [ ] T066 [P] [US3] Criar `src/omniDesk.Api/Features/Notifications/Handlers/TicketSlaWarningHandler.cs` — assinatura: hook no `TicketSlaMonitorJob` (Spec 009) que após `EmitWarningAsync` chama `INotificationService.NotifySlaWarningAsync(attendantId, ...)`. Apenas atendente (não supervisor) recebe warning
- [ ] T067 [US3] Criar `src/omniDesk.Api/Features/Notifications/Handlers/TicketSlaBreachedHandler.cs` — hook no `TicketSlaMonitorJob` após `EmitBreachAsync` chama `INotificationService.NotifySlaBreachedAsync(ticketId, protocol, departmentId, attendantId?)`. Service fan-out conforme T027/T028
- [ ] T068 [US3] Estender `src/omniDesk.Api/Infrastructure/Jobs/TicketSlaMonitorJob.cs` (Spec 009) adicionando chamadas a `INotificationService.NotifySlaWarningAsync` e `NotifySlaBreachedAsync` logo após os emits WS existentes. Try/catch — falha de notificação não pode travar o job
- [ ] T069 [Opus] [US3] Criar `src/omniDesk.Api/Infrastructure/Jobs/TicketQueueMonitorJob.cs` — cron `* * * * *`. Para cada tenant ativo: query SQL bruto `SELECT id, department_id, protocol FROM tenant_{slug}.tickets WHERE status='new' AND attendant_id IS NULL AND deleted_at IS NULL AND created_at <= NOW() - INTERVAL '5 minutes'`. Para cada row: `SETNX {slug}:queue_alert:{ticket_id} 1 EX 3600`; se ganhou, `INotificationService.NotifyTicketQueuedAsync(ticketId, protocol, departmentId)`. **Por que Opus**: scan multi-tenant + idempotência via Redis NX + try/catch granular por tenant para isolar falhas
- [ ] T070 [US3] Registrar em Program.cs: `RecurringJob.AddOrUpdate<TicketQueueMonitorJob>("ticket-queue-monitor", j => j.RunAsync(default), "* * * * *", TimeZoneInfo.Utc)`
- [ ] T071 [US3] Estender `Handlers/TicketQueuedHandler.cs` análogo aos outros — apenas validação E2E (a lógica vive no service + job)

**Checkpoint**: ✅ User Story 3 completa. Alertas operacionais entregando para supervisores + atendentes.

---

## Phase 6: User Story 4 — WhatsApp Appointment Reminder Job (Priority: P3)

**Goal**: Job diário envia `appointment_reminder` 24h antes do agendamento; idempotente; condições (canal + phone + template + toggle); falha gera evento + badge + notification.

**Independent Test**: Toggle ativado, appointment para amanhã com contato com phone, template aprovado: trigger manual do job → 1 mensagem enviada via outgoing queue. Re-trigger → 0 mensagens. Deletar phone do contato → próximo trigger gera `reminder_failed` + badge + notif.

### Tests for User Story 4 ⚠️

- [ ] T072 [P] [US4] Criar `tests/.../Infrastructure/Jobs/AppointmentReminderJobTests.cs` (Testcontainers) — happy path: 1 appointment, 1 contato, 1 template approved, toggle on, channel on → 1 outgoing enqueued + 1 `wa_message_statuses` row + Redis flag setada; re-run no mesmo dia → 0 enqueued; toggle off → 0; sem template → 0; sem phone + appointment ligado a ticket → `reminder_failed` event + notification + `has_reminder_alert = true`
- [ ] T073 [P] [US4] Criar `tests/.../Features/Notifications/Handlers/ReminderFailedHandlerTests.cs` — appointment com ticket → evento em `{slug}_ticket_events` + notification ao atendente + flag `has_reminder_alert`; appointment sem ticket → log em `{slug}_agent_activity_logs` + notification aos supervisores do depto

### Implementation for User Story 4

- [ ] T074 [P] [US4] Criar `src/omniDesk.Api/Infrastructure/Appointments/IAppointmentReadRepository.cs` com `Task<IReadOnlyList<AppointmentReminderDto>> GetUpcomingAppointmentsAsync(string slug, DateOnly date, CancellationToken ct);`. DTO: `id, contact_id, scheduled_for, status, ticket_id?, department_id?`. (research §R3)
- [ ] T075 [US4] Criar `src/omniDesk.Api/Infrastructure/Appointments/AppointmentReadRepository.cs` — SQL bruto `SELECT id, contact_id, scheduled_for, status, ticket_id, department_id FROM tenant_{slug}.appointments WHERE date_trunc('day', scheduled_for) = @date AND status = 'confirmed'`. **Se a tabela `appointments` não existir (Spec 11 não mergeada)**: detectar `42P01 undefined_table` e retornar `[]` com log warning (research §R3)
- [ ] T076 [P] [US4] Criar `src/omniDesk.Api/Features/Notifications/Handlers/ReminderFailedHandler.cs` — `OnAppointmentReminderFailedAsync(slug, appointment, reason, ct)`: se `appointment.ticket_id != null`: append `TicketEvent { event_type=ReminderFailed, ... }` em `{slug}_ticket_events`; UPDATE `tickets SET has_reminder_alert = true`; `NotifyReminderFailedAsync(attendantId, ticketId, protocol, contactName, reason)`. Senão: insert em `{slug}_agent_activity_logs` com `action="reminder_failed"`; supervisor lookup via `SupervisorLookupService(department_id)` + notify cada um
- [ ] T077 [Opus] [US4] Criar `src/omniDesk.Api/Features/WhatsApp/Jobs/AppointmentReminderJob.cs` com `Task RunAsync(string tenantSlug, CancellationToken ct)`. Fluxo:
  1. Resolver tenant via `tenantSlug` (busca em `public.tenants`).
  2. Carregar `tenant_notification_settings`; se `reminder_enabled = false` → return.
  3. Calcular `targetDate = today (tenant tz) + 1` (research §R5).
  4. Buscar appointments via `IAppointmentReadRepository`.
  5. Para cada appointment: condições do FR-017 (canal `is_enabled=true`, contact com phone, template `appointment_reminder` `status=approved`, toggle on). Se falhar alguma → `ReminderFailedHandler.OnAppointmentReminderFailedAsync(...)`.
  6. Se passar: `SETNX {slug}:reminder_sent:{appointment_id}:{yyyyMMdd} 1 EX 172800`. Se NX falhou → skip (já enviado hoje). Se NX ganhou: montar variáveis (paciente, hora, profissional) + enqueue via `OutgoingMessagePublisher` com `sender_type=system`, `message_type=template`, `template_id` da `appointment_reminder`. Catch de exceções por appointment — falha individual não para o lote. **Por que Opus**: orquestração multi-store (Postgres + Redis + WhatsApp outbound) com idempotência + tratamento de erro granular + integração com adapter da Spec 11 ausente
- [ ] T078 [US4] Criar `src/omniDesk.Api/Features/Notifications/Schedulers/AppointmentReminderScheduler.cs` com `ApplyAsync(Guid tenantId, TenantNotificationSettings settings, CancellationToken ct)`. Lê `tenants.timezone`, computa cron a partir de `reminder_time`, chama `RecurringJob.AddOrUpdate<AppointmentReminderJob>("appointment-reminder:{slug}", j => j.RunAsync(slug, default), cron, new RecurringJobOptions { TimeZone = tz })`. Se `reminder_enabled=false` → `RecurringJob.RemoveIfExists("appointment-reminder:{slug}")` (research §R2)
- [ ] T079 [US4] Estender `Program.cs` adicionando one-shot `BackfillReminderJobsJob` registrado pós-startup que itera `tenant_notification_settings` e chama `AppointmentReminderScheduler.ApplyAsync` para cada tenant (idempotente)

**Checkpoint**: ✅ User Story 4 completa. Job de lembrete operacional por tenant, idempotente, falhas tratadas. Confirmar via quickstart §7.

---

## Phase 7: User Story 5 — Manual WhatsApp Template Send (Priority: P3)

**Goal**: Atendente envia template aprovado para reativar conversa fora da janela de 24h. Modal lista templates → seleciona → preenche variáveis → preview → confirma.

**Independent Test**: Ticket com sessão WA expirada. Atendente clica "Enviar template", escolhe `follow_up`, preenche, vê preview, confirma. Mensagem aparece no thread; `wa_message_statuses` registra; ticket continua no mesmo status.

### Tests for User Story 5 ⚠️

- [ ] T080 [P] [US5] Criar `tests/.../Features/Tickets/SendTemplateEndpointTests.cs` — happy path enqueue; 403 quando atendente não é o assigned (e não é TenantAdmin); 422 com `TEMPLATE_NOT_APPROVED` / `TEMPLATE_VARIABLES_MISSING` / `CONTACT_HAS_NO_PHONE`; quando template é `appointment_reminder` e `has_reminder_alert=true`, flag reseta para false após enqueue

### Implementation for User Story 5

- [ ] T081 [P] [US5] Criar `src/omniDesk.Api/Features/Tickets/Commands/SendManualTemplateCommand.cs` com `ExecuteAsync(ticketId, attendantId, templateId, variables, ct)`. Validações conforme contracts/manual-template-api.md (semantic error codes). Render usando engine do Spec 008 (`WhatsAppTemplateRenderer` se existe — senão criar wrapper que substitui `{{var}}` simples). Enqueue via `OutgoingMessagePublisher` com `sender_type=attendant`, `message_type=template`. Side effect: se `template.name == "appointment_reminder"` e `ticket.has_reminder_alert == true` → UPDATE para `false` na mesma transação
- [ ] T082 [US5] Criar `src/omniDesk.Api/Features/Tickets/SendTemplateEndpoint.cs` com `POST /api/tickets/{id}/send-template`. Registrar no group de tickets em Program.cs (já existe). Validador FluentValidation para variables não vazias
- [ ] T083 [P] [US5] Criar `src/omniDesk.Crm/src/app/features/whatsapp-templates/send-template-modal.component.{ts,html,scss,spec.ts}` — modal PrimeNG `Dialog` recebendo `@Input() ticketId`. `ngOnInit`: GET templates approved da Spec 008. Form Reactive com FormArray para variáveis baseado no template selecionado. Preview ao vivo (substituição client-side de `{{var}}` por valor — research §R12). Botão "Enviar" → `POST /api/tickets/{id}/send-template`. Toast de sucesso/erro
- [ ] T084 [US5] Estender `src/omniDesk.Crm/src/app/features/ticket-detail/ticket-detail.component.{ts,html}` — botão "Enviar template" no painel direito. Abre `SendTemplateModalComponent` com `ticketId`. Após sucesso, recarrega thread de mensagens

**Checkpoint**: ✅ User Story 5 completa. Template manual funcional.

---

## Phase 8: User Story 6 — Attendant Notification Preferences (Priority: P3)

**Goal**: Atendente configura push em Perfil → Preferências: toggle global + checkboxes por evento.

**Independent Test**: Desabilitar `ticket.queued` push em prefs. Acionar evento queued → in-app aparece, push não dispara. Desabilitar toggle global → nenhum push dispara para nenhum evento.

### Tests for User Story 6 ⚠️

- [ ] T085 [P] [US6] Criar `tests/.../Features/Notifications/PreferencesEndpointsTests.cs` — GET sem row existente retorna defaults completos; PUT cria upsert; PUT com chave inválida em `event_push_flags` retorna 422 `INVALID_EVENT_TYPE`; após PUT, próximo push do tipo desabilitado é skipped (integração com `WebPushDispatcher` mockado)

### Implementation for User Story 6

- [ ] T086 [P] [US6] Criar `src/omniDesk.Api/Features/Notifications/Commands/UpdatePreferencesCommand.cs` com `ExecuteAsync(attendantId, pushEnabled, eventPushFlags, ct)`. Valida chaves em `NotificationEventTypes.AllowedValues`. Upsert via `AttendantPreferencesRepository`
- [ ] T087 [US6] Criar `src/omniDesk.Api/Features/Notifications/PreferencesEndpoints.cs` com `GET /api/notifications/preferences` (retorna shape completo conforme contracts/preferences-api.md, preenchendo `true` para chaves ausentes) e `PUT /api/notifications/preferences`. Registrar em Program.cs
- [ ] T088 [US6] Atualizar `NotificationService.SendPushAsync` (interno, T057) para consultar prefs **antes de cada push**: se `pushEnabled=false` → skip; se `eventPushFlags[event_type] == false` → skip. Garantir que in-app **sempre** persiste (a checagem é só sobre push)
- [ ] T089 [P] [US6] Criar `src/omniDesk.Crm/src/app/features/notifications/preferences-page.component.{ts,html,scss,spec.ts}` — formulário Reactive com toggle `push_enabled` + 8 checkboxes (um por event_type, label PT-BR). Carrega `GET /api/notifications/preferences` em `ngOnInit`. Save → `PUT`. Toast de sucesso. Rota `/preferences` em `app.routes.ts` lazy-load
- [ ] T090 [US6] Adicionar link "Preferências" no menu de usuário (`src/omniDesk.Crm/src/app/layout/header/header.component.html`) navegando para `/preferences`

**Checkpoint**: ✅ User Story 6 completa. Atendente controla push por evento.

---

## Phase 9: Tenant Notification Settings (Cross-cutting, supports US4 + follow-up)

**Purpose**: Tela de admin para `follow_up_enabled`, `reminder_enabled`, `reminder_time`. Disparada por completar US4; o follow-up opt-in (FR-026) é parcialmente independente mas vive na mesma tela.

- [ ] T091 [P] Criar `tests/.../Features/Notifications/TenantSettingsEndpointsTests.cs` — GET retorna defaults se row ausente; PUT como TenantAdmin upserta + chama scheduler; PUT como Attendant retorna 403; PUT com `reminder_time` inválido retorna 422
- [ ] T092 [P] Criar `src/omniDesk.Api/Features/Notifications/Commands/UpdateTenantSettingsCommand.cs` — upsert via `TenantSettingsRepository` + chama `AppointmentReminderScheduler.ApplyAsync(tenantId, settings)`
- [ ] T093 Criar `src/omniDesk.Api/Features/Notifications/TenantSettingsEndpoints.cs` com `GET /api/notification-settings` e `PUT /api/notification-settings`. Policy `TenantAdmin` (existente em `Infrastructure/Authentication/`). Registrar em Program.cs
- [ ] T094 Atualizar `src/omniDesk.Api/Features/Tickets/Commands/ResolveTicketCommand.cs` (ou equivalente que encerra ticket, Spec 009) para — se `tenant_notification_settings.follow_up_enabled = true` e ticket tem WA conversation e template `follow_up` aprovado e contato com phone — enqueue follow-up via `OutgoingMessagePublisher` com `sender_type=system`, `template=follow_up`. Reusa `WhatsAppOutgoingAdapter`. Falha gera log warning (não trava resolve)
- [ ] T095 Atualizar `src/omniDesk.Api/Features/Tickets/Commands/ResolveTicketCommand.cs` (ou equivalente) para também resetar `tickets.has_reminder_alert = false` no fechamento (FR-021)
- [ ] T096 [P] Criar `src/omniDesk.Crm/src/app/features/notification-settings/settings-page.component.{ts,html,scss,spec.ts}` — 3 controles (2 toggles + 1 time picker PrimeNG `Calendar timeOnly`). GET/PUT contra `/api/notification-settings`. Guard `TenantAdminGuard` na rota `/settings/notifications`
- [ ] T097 Atualizar `src/omniDesk.Crm/src/app/app.routes.ts` adicionando rotas lazy-loaded `/preferences` (US6) e `/settings/notifications` (US-cross)

**Checkpoint**: ✅ Configurações por tenant funcionais; follow-up automático opt-in funcional.

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T098 [P] Criar `src/omniDesk.Api/Infrastructure/Jobs/NotificationArchiverJob.cs` — cron `0 3 * * *`. Para cada tenant ativo: `UPDATE tenant_{slug}.notifications SET archived_at = NOW() WHERE created_at < NOW() - INTERVAL @days DAY AND archived_at IS NULL` (usar `Notifications:ArchiveRetentionDays`). Registrar via `RecurringJob.AddOrUpdate("notifications-archiver", ..., "0 3 * * *", Utc)`
- [ ] T099 [P] Criar `tests/.../Infrastructure/Jobs/NotificationArchiverJobTests.cs` — 100 rows, 60 > 90 dias → exatamente 60 archived; runs idempotentes (segundo run não altera nada)
- [ ] T100 [P] Adicionar métricas em `Infrastructure/Metrics/` (se existe — senão `NotificationMetrics.cs`): `notifications_delivered_total{event_type}`, `notifications_push_failed_total{reason}`, `push_subscriptions_active`, `reminders_sent_total{tenant_slug}`, `reminders_failed_total{reason}`. Incrementar nos pontos relevantes
- [ ] T101 [P] Adicionar testes de concorrência: `tests/.../Features/Notifications/ConcurrentNotificationTests.cs` — disparar 50 `NotifyTicketAssignedAsync` em paralelo para 5 attendants; assert 50 rows totais, 50 WS publishes, sem deadlock
- [ ] T102 [P] Auditoria de segurança: confirmar que `NotificationsEndpoints` SEMPRE filtra por `attendant_id = current_user`; nenhum endpoint retorna notification de outro atendente; tests no `NotificationsEndpointTests` (T033) já cobrem — adicionar caso adicional de cross-tenant attempt
- [ ] T103 [P] Documentar em `docs/ARCHITECTURE.md` na seção de Notifications: tabelas, fluxo de push, idempotência do reminder, decisão R6 sobre supervisor, link para ADRs se aplicável
- [ ] T104 [P] Documentar em `docs/DEPENDENCIES.md` que Spec 010 depende de Spec 009 (✅ ok) e Spec 11 Agenda (paralelizada via adapter — `NullAppointmentReadRepository` é o stub default)
- [ ] T105 Atualizar `src/omniDesk.Api/Features/Notifications/README.md` (criado em T006) com setup VAPID resumido + link para quickstart.md
- [ ] T106 Validar manual via quickstart.md §4–9 (in-app, push, silence rule, reminder job, queue monitor, manual template) em dev local. Ticket items conforme spec.md §"Critérios de Aceite". Documentar resultados em `specs/010-notifications/quickstart-run-{date}.md`
- [ ] T107 Cleanup: remover `NoOpNotificationService` do código de produção definitivamente (após confirmar via grep que só vive em `tests/Helpers/`)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Sem dependências — pode começar imediatamente. T001 deve preceder qualquer task que use `WebPush` (T055).
- **Foundational (Phase 2)**: Bloqueia TODAS as user stories. T008–T010 (migrations + fixture) precedem T011–T032. T026 (expansão da interface) precede T029 (DI) e T030 (call sites Spec 009). T031–T032 podem ser escritos em paralelo aos handlers (que ficam em US1+).
- **US1 (Phase 3)**: Depende apenas de Foundational. Endpoints + bell são fechados em si.
- **US2 (Phase 4)**: Depende de Foundational + US1 (precisa do path WS funcional para silence rule e do `NotificationService` para invocar push após persist).
- **US3 (Phase 5)**: Depende de Foundational + US1 (precisa de `NotificationService` para fan-out). **Não depende de US2** — push é orthogonal; alertas SLA também valem in-app sem push.
- **US4 (Phase 6)**: Depende de Foundational + US1 (handlers + service). Indepedente de US2/US3.
- **US5 (Phase 7)**: Depende de Foundational (rota `/tickets/{id}/send-template` precisa do escopo de tickets). Indepedente de US1–US4.
- **US6 (Phase 8)**: Depende de Foundational + US2 (preferências são significativas apenas se push existe — em V1 a tela ainda é útil mesmo sem push porque registra a intenção; mas o test T088 valida o gate de push).
- **Phase 9 (Tenant Settings)**: Depende de US4 (scheduler).
- **Phase 10 (Polish)**: Depende de tudo.

### Within Each User Story

- Testes (Constituição §VII) escritos antes da implementação dentro da story
- Domain entities → repositories → service → command/query → endpoint → frontend
- Diferentes user stories podem ser trabalhadas em paralelo por desenvolvedores diferentes

### Parallel Opportunities

- **Phase 1**: T001 sequencial (depend ordering em `csproj`); T002–T007 em paralelo
- **Phase 2**: T008 + T009 sequenciais (mesma migration folder, possível conflito); T011–T016 em paralelo (entidades distintas); T022–T025 em paralelo (repos distintos); T031–T032 em paralelo
- **Phase 3 (US1)**: T035–T038 em paralelo (queries/commands distintas); T041–T044 sequenciais (mexem em handlers + chamadas em call sites de Spec 007/008/009); T045–T047 em paralelo (frontend services distintos); T048–T050 sequenciais (componentes filho → pai)
- **Phase 4 (US2)**: T054–T055 sequenciais (provider → dispatcher); T056–T059 sequenciais (cada um adiciona ao endpoint + handler); T060 sequencial (SW); T061–T063 em paralelo
- **Phase 5 (US3)**: T066–T068 sequenciais (handler → hook no job); T069 isolado
- **Phase 6 (US4)**: T074–T076 em paralelo; T077 sequencial (depende de tudo acima); T078–T079 sequenciais
- **Phase 7 (US5)**: T081–T082 sequenciais; T083–T084 sequenciais
- **Phase 8 (US6)**: T086–T087 sequenciais; T088 sequencial (toca o service); T089–T090 sequenciais
- **Phase 9**: T092–T093 sequenciais; T094–T095 sequenciais (no mesmo command); T096–T097 sequenciais
- **Phase 10**: T098–T105 majoritariamente em paralelo; T106–T107 finais

---

## Parallel Example: User Story 1

```bash
# Launch tests in parallel (TDD-first):
Task T033: "NotificationsEndpointTests in tests/.../Features/Notifications/NotificationsEndpointTests.cs"
Task T034: "TicketAssignedHandlerTests in tests/.../Features/Notifications/Handlers/TicketAssignedHandlerTests.cs"

# Launch queries/commands in parallel:
Task T035: "ListNotificationsQuery in src/omniDesk.Api/Features/Notifications/Queries/ListNotificationsQuery.cs"
Task T036: "UnreadCountQuery in src/omniDesk.Api/Features/Notifications/Queries/UnreadCountQuery.cs"
Task T037: "MarkAsReadCommand in src/omniDesk.Api/Features/Notifications/Commands/MarkAsReadCommand.cs"
Task T038: "MarkAllAsReadCommand in src/omniDesk.Api/Features/Notifications/Commands/MarkAllAsReadCommand.cs"

# Frontend services in parallel:
Task T045: "notifications.service in src/omniDesk.Crm/src/app/features/notifications/notifications.service.ts"
Task T046: "notification-stream.service in src/omniDesk.Crm/src/app/core/services/notification-stream.service.ts"
Task T047: "notification-item.component in src/omniDesk.Crm/src/app/features/notifications/notification-item.component.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 (Setup) + Phase 2 (Foundational): 32 tasks. Build verde, Spec 009 não regride.
2. Phase 3 (US1): 18 tasks. Bell + lista + mark-as-read + WS in-app.
3. **STOP and VALIDATE**: Testar quickstart §4 (in-app notifications E2E). Demo possível.
4. Deploy: notificações in-app sem push e sem WhatsApp.

### Incremental Delivery

1. **Sprint 1**: Setup + Foundational → MVP ready
2. **Sprint 2**: US1 → Deploy/Demo (notificações in-app)
3. **Sprint 3**: US2 + US3 → Deploy/Demo (push + alertas SLA/queue)
4. **Sprint 4**: US4 + Phase 9 → Deploy/Demo (reminders + tenant settings)
5. **Sprint 5**: US5 + US6 + Phase 10 → Final

### Parallel Team Strategy

Após Foundational (Phase 2) concluída:

- **Dev A**: US1 (Bell + List) — owner de feature CRM
- **Dev B**: US2 (Push + SW) — owner de Web Push
- **Dev C**: US4 (Reminder Job) — owner de Hangfire/WhatsApp integration

US3 + US5 + US6 + Phase 9 + Phase 10 acopla-se sequencialmente quando os 3 acima ramificam.

---

## Notes

- **[P]** tasks = arquivos distintos, sem dependência pendente
- **[Story]** mapeia para US1–US6 do spec.md
- **[Opus]** flagged tasks: trocar modelo durante execução (cost-aware, custo-benefício de capacidade vs. preço)
- Testes Testcontainers requerem Docker — rodar localmente ou em CI com runner Docker-enabled
- Cada user story é independentemente testável (e demonstrável). MVP é US1 sozinho.
- Commit por task ou grupo lógico (não bundle gigante). Mensagens: `010 T0XX: <descrição curta>`
- Após cada checkpoint, rodar quickstart.md correspondente para validar manual
- Spec 11 Agenda paralelizada via adapter stub — não bloqueia US4 (mas US4 só envia mensagens reais quando Spec 11 mergea sua tabela `appointments`)
