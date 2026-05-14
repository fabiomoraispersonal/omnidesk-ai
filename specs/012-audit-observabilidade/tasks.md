# Tasks: Auditoria e Observabilidade

**Input**: Design documents from `specs/012-audit-observabilidade/`
**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅ | quickstart.md ✅

**Tests**: Incluídos na fase de polish (Phase 7). Seguem padrão Testcontainers do projeto.

**Organization**: Tarefas agrupadas por user story para enable implementação e teste independente.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Pode rodar em paralelo (arquivos diferentes, sem dependências entre si)
- **[Story]**: User story correspondente (US1–US4)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Criar os tipos e constantes base que todos os outros componentes precisam.

- [x] T001 Create `AuditLog` MongoDB document model with nested `AuditActor` and `AuditTarget` classes in `src/omniDesk.Api/Domain/Audit/AuditLog.cs`
- [x] T002 [P] Create `ApiKey` EF Core entity in `src/omniDesk.Api/Domain/Audit/ApiKey.cs`
- [x] T003 [P] Create `AuditEventNames` static class with all 29 event constants (grouped: Auth, Users, Tickets, Appointments, TenantConfig) in `src/omniDesk.Api/Domain/Audit/AuditEventNames.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Infrastructure de dados e serviços que TODOS os user stories precisam.

**⚠️ CRITICAL**: Nenhum user story pode começar até esta fase estar completa.

- [x] T004 Create `IAuditService` interface with `LogAsync(AuditEvent event)` and helper factory methods (`AuditActor.FromClaims`, `AuditActor.System`, `AuditTarget.From`) in `src/omniDesk.Api/Infrastructure/Audit/IAuditService.cs`
- [x] T005 Create `AuditMongoRepository` with `InsertAsync` (append-only) and `QueryAsync` (filter by event/actor/date range, paginated) in `src/omniDesk.Api/Infrastructure/Audit/AuditMongoRepository.cs` — collection name `{tenant_slug}_audit_logs`
- [x] T006 [P] Create `ApiKeyRepository` with `CreateAsync`, `ListActiveAsync`, `FindByHashAsync`, `RevokeAsync`, `CountActiveAsync` in `src/omniDesk.Api/Infrastructure/Audit/ApiKeyRepository.cs`
- [x] T007 Create `AuditService` implementing `IAuditService` with fire-and-forget `Task.Run` + error logging on failure (never throws) in `src/omniDesk.Api/Infrastructure/Audit/AuditService.cs`
- [x] T008 Create `AuditMongoIndexInitializer` that creates the 3 compound indexes on app startup in `src/omniDesk.Api/Infrastructure/Audit/AuditMongoIndexInitializer.cs` (indexes: tenant+timestamp, tenant+event+timestamp, tenant+actor.user_id+timestamp)
- [x] T009 Run EF Core migration: `dotnet ef migrations add AddApiKeys --context TenantDbContext` in `src/omniDesk.Api/` and verify migration file in `Infrastructure/Persistence/Migrations/`
- [x] T010 Register `IAuditService`/`AuditService`, `AuditMongoRepository`, `ApiKeyRepository`, and call `AuditMongoIndexInitializer` in `src/omniDesk.Api/Program.cs`

**Checkpoint**: Foundation ready — user story implementation pode começar.

---

## Phase 3: User Story 1 — Registro Automático de Eventos Críticos (Priority: P1) 🎯 MVP

**Goal**: Todos os 29 eventos geram documentos no MongoDB com estrutura correta.

**Independent Test**: Executar uma ação (ex: login, mudar status de ticket) e verificar documento no MongoDB com `tenant_slug`, `event`, `actor`, `timestamp` preenchidos. Ver QS-1 e QS-2 em `quickstart.md`.

- [x] T011 [US1] Inject `IAuditService` into `LoginEndpoint` and log `auth.login_success` (after successful auth) and `auth.login_failed` (including attempts with non-existent email — store attempted email in `metadata.attempted_email`) in `src/omniDesk.Api/Features/Auth/Login/LoginEndpoint.cs`
- [x] T012 [P] [US1] Add `auth.logout` to `src/omniDesk.Api/Features/Auth/Logout/LogoutEndpoint.cs`; add `auth.password_reset` to `src/omniDesk.Api/Features/Auth/ResetPassword/ResetPasswordEndpoint.cs`; add `auth.password_changed` to the change-password handler (locate in `Features/Auth/` or `Features/Me/`)
- [x] T013 [P] [US1] Add `auth.totp_enabled` to `src/omniDesk.Api/Features/Auth/Totp/TotpConfirmEndpoint.cs` and `auth.totp_disabled` to `src/omniDesk.Api/Features/Auth/Totp/TotpDisableEndpoint.cs`
- [x] T014 [P] [US1] Add `auth.impersonation_started` (with `actor.impersonated_by = "saas_admin"`) to `src/omniDesk.Api/Features/Admin/Impersonate/ImpersonateEndpoint.cs`; add `auth.impersonation_ended` to the impersonation token expiry path
- [x] T015 [P] [US1] Add `user.invited` to `src/omniDesk.Api/Features/Auth/Invite/SendInviteEndpoint.cs`; add `user.invite_accepted` to `src/omniDesk.Api/Features/Auth/Invite/AcceptInviteEndpoint.cs`; add `user.deactivated`, `user.reactivated`, `user.role_changed` to `src/omniDesk.Api/Features/Attendants/AttendantsEndpoints.cs`
- [x] T016 [P] [US1] Add ticket audit events: `ticket.created` to `CreateManualTicketCommand.cs`, `ticket.assigned` to the assignment command, `ticket.transferred` to `TransferTicketCommand.cs`, `ticket.status_changed` to `ChangeTicketStatusCommand.cs` and `ResolveTicketCommand.cs`, `ticket.cancelled` to `CancelTicketCommand.cs` — all in `src/omniDesk.Api/Features/Tickets/Commands/`
- [x] T017 [P] [US1] Add appointment audit events: `appointment.created` to `CreateAppointmentCommand.cs`, `appointment.confirmed` to `ConfirmAppointmentCommand.cs`, `appointment.cancelled` to `CancelAppointmentCommand.cs` (include `cancelled_by` in metadata), `appointment.no_show` to `MarkNoShowCommand.cs` — all in `src/omniDesk.Api/Features/Agenda/Appointments/Commands/`
- [x] T018 [P] [US1] Add tenant config audit events: `tenant.whatsapp_configured` to `src/omniDesk.Api/Features/WhatsApp/Config/WhatsAppConfigEndpoints.cs`; `tenant.openai_key_changed` to `src/omniDesk.Api/Features/AiSettings/AiSettingsEndpoints.cs`; `ai_agent.created`, `ai_agent.updated`, `ai_agent.deleted` to `src/omniDesk.Api/Features/AiAgents/AiAgentsEndpoints.cs`
- [x] T019 [US1] Create `AuditRetentionJob` (Hangfire monthly recurring, deletes documents with `timestamp < now() - 12 months`) in `src/omniDesk.Api/Infrastructure/Jobs/AuditRetentionJob.cs` and register `RecurringJob.AddOrUpdate` in `Program.cs`

**Checkpoint**: US1 completo — todos os 29 eventos devem ser verificáveis via MongoDB com QS-1 e QS-2.

---

## Phase 4: User Story 2 — Consulta de Atividade Recente no CRM (Priority: P2)

**Goal**: `tenant_admin` acessa lista paginada de eventos no CRM com filtros funcionais.

**Independent Test**: Acessar CRM → Configurações → Atividade Recente como `tenant_admin` e verificar listagem com eventos reais do Phase 3. Ver QS-1 em `quickstart.md`.

### Backend

- [x] T020 [US2] Create `AuditLogFilters` (record with `event?`, `actorId?`, `from?`, `to?`, `page`, `perPage`), `AuditLogDto` (response DTO without `user_agent`), and `GetAuditLogsHandler` in `src/omniDesk.Api/Features/Audit/GetAuditLogsHandler.cs`
- [x] T021 [US2] Create `AuditEndpoints.cs` with `GET /api/audit-logs` (JWT Bearer, `RequireAuthorization("tenant_admin")`) and register `app.MapGroup("/api/audit-logs").MapAuditEndpoints()` in `src/omniDesk.Api/Program.cs`

### Frontend CRM

- [x] T022 [P] [US2] Create `AuditService` with `getAuditLogs(filters: AuditLogFilters): Observable<PagedResult<AuditLogDto>>` in `src/omniDesk.Crm/src/app/features/audit/services/audit.service.ts`
- [x] T023 [P] [US2] Create `audit.routes.ts` with lazy-loaded route for `AuditActivityComponent` and add to app routes in `src/omniDesk.Crm/src/app/features/audit/audit.routes.ts`
- [x] T024 [US2] Create `AuditActivityComponent` with PrimeNG Table (paginated, 20/page), filter bar (event p-dropdown, actor p-select, date range p-calendar), and event description rendering (icon + human-readable label + relative timestamp) in `src/omniDesk.Crm/src/app/features/audit/audit-activity/audit-activity.component.ts/.html/.scss`
- [x] T025 [US2] Add "Atividade Recente" nav link to CRM settings navigation, visible only when current user has `tenant_admin` role, pointing to `/settings/audit` in the settings layout/nav component

**Checkpoint**: US2 completo — CRM exibe atividade com filtros funcionais. US1 ainda funciona.

---

## Phase 5: User Story 3 — Consulta via API para Ferramentas Externas (Priority: P2)

**Goal**: Ferramentas externas (Metabase) autenticam via `X-Api-Key` e acessam `GET /api/audit-logs`.

**Independent Test**: Criar uma API Key, usar `X-Api-Key` header para acessar `GET /api/audit-logs` e receber logs paginados. Ver QS-3 em `quickstart.md`.

- [x] T026 [US3] Create `ApiKeyAuthenticationHandler` (implements `IAuthenticationHandler`) that resolves tenant from API Key hash lookup, validates `revoked = false` and `expires_at`, populates `TenantId`/`Role` claims on success in `src/omniDesk.Api/Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs`
- [x] T027 [US3] Register `ApiKey` authentication scheme in `Program.cs` and update `GET /api/audit-logs` authorization policy in `AuditEndpoints.cs` to accept both JWT Bearer AND `X-Api-Key` schemes (`.AddAuthenticationSchemes("Bearer", "ApiKey")`)
- [x] T028 [US3] Implement fire-and-forget `last_used_at` update (via `_ = Task.Run(...)`) in `ApiKeyAuthenticationHandler` after successful authentication, using `ApiKeyRepository.UpdateLastUsedAtAsync`

**Checkpoint**: US3 completo — Metabase pode conectar ao endpoint. US1 e US2 ainda funcionam.

---

## Phase 6: User Story 4 — Gestão de API Keys pelo Tenant Admin (Priority: P3)

**Goal**: `tenant_admin` cria, lista e revoga API Keys no CRM. Chave bruta exibida apenas na criação.

**Independent Test**: Criar API Key no CRM, copiar chave, confirmar que ela não aparece mais em listagem. Revogar e confirmar que autenticação falha. Ver QS-3 a QS-5 em `quickstart.md`.

### Backend

- [x] T029 [US4] Create `ApiKeyDtos.cs` with `CreateApiKeyRequest` (name), `ApiKeyResponse` (id, name, scopes, lastUsedAt, expiresAt, revoked, createdAt — sem hash), and `CreatedApiKeyResponse` (extends ApiKeyResponse + `key` raw string) in `src/omniDesk.Api/Features/ApiKeys/ApiKeyDtos.cs`
- [x] T030 [US4] Create `CreateApiKeyHandler`: generate 32-byte random key → Base64Url → prefix `omni_`, compute SHA-256 hex hash, enforce 5-key limit (return `API_KEY_LIMIT_REACHED` 422 if `CountActive >= 5`), persist via `ApiKeyRepository.CreateAsync`, return `CreatedApiKeyResponse` with raw key in `src/omniDesk.Api/Features/ApiKeys/CreateApiKeyHandler.cs`
- [x] T031 [P] [US4] Create `ListApiKeysHandler` returning paginated `ApiKeyResponse` list (no `key_hash`) in `src/omniDesk.Api/Features/ApiKeys/ListApiKeysHandler.cs`
- [x] T032 [P] [US4] Create `RevokeApiKeyHandler` that sets `revoked = true`; returns 404 if key not found or already revoked in `src/omniDesk.Api/Features/ApiKeys/RevokeApiKeyHandler.cs`
- [x] T033 [US4] Create `ApiKeyEndpoints.cs` with `GET /api/api-keys`, `POST /api/api-keys`, `DELETE /api/api-keys/{id}` (all JWT, `RequireAuthorization("tenant_admin")`) and register endpoint group in `src/omniDesk.Api/Program.cs`

### Frontend CRM

- [x] T034 [P] [US4] Create `ApiKeysService` with `listApiKeys()`, `createApiKey(name)`, `revokeApiKey(id)` in `src/omniDesk.Crm/src/app/features/settings/api-keys/api-keys.service.ts`
- [x] T035 [P] [US4] Create `api-keys.routes.ts` with lazy-loaded route and add to settings routes in `src/omniDesk.Crm/src/app/features/settings/api-keys/api-keys.routes.ts`
- [x] T036 [US4] Create `ApiKeysComponent` with PrimeNG Table listing keys (name, created, last used, status) and revoke button (p-confirmDialog) in `src/omniDesk.Crm/src/app/features/settings/api-keys/api-keys.component.ts/.html/.scss`
- [x] T037 [US4] Create `CreateApiKeyDialogComponent` with name input form and one-time key display (copy-to-clipboard button, warning message that key will not be shown again) in `src/omniDesk.Crm/src/app/features/settings/api-keys/create-api-key-dialog/create-api-key-dialog.component.ts/.html`

**Checkpoint**: US4 completo — gestão de API Keys funcional. Todos os 4 user stories funcionam.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T038 [P] Write integration tests for `AuditMongoRepository` (insert, query with all filter combinations, tenant isolation — tenant A cannot see tenant B logs) using Testcontainers MongoDB in `src/omniDesk.Api/tests/omniDesk.Api.Tests/Infrastructure/Audit/AuditMongoRepositoryTests.cs`
- [x] T039 [P] Write integration tests for `GET /api/audit-logs` endpoint: JWT auth (tenant_admin succeeds, attendant 403), API Key auth (valid key succeeds, revoked key 401), filter params (event, actor_id, from/to), tenant isolation in `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Audit/AuditLogsEndpointTests.cs`
- [x] T040 [P] Write integration tests for `/api/api-keys` endpoints: create (success, 5-key limit), list (no key_hash exposed), revoke (success, 404 on already-revoked) in `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/ApiKeys/ApiKeysEndpointTests.cs`
- [ ] T041 Run all 7 quickstart validation scenarios from `specs/012-audit-observabilidade/quickstart.md` (QS-1 through QS-7) and record results
- [x] T042 [P] Update CLAUDE.md Active Spec section to mark Spec 012 as implemented and add to specs implementadas list

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Sem dependências — pode começar imediatamente
- **Foundational (Phase 2)**: Depende de Phase 1 — BLOQUEIA todos os user stories
- **US1 (Phase 3)**: Depende de Phase 2 — pode começar assim que Foundation estiver pronta
- **US2 (Phase 4)**: Depende de Phase 2 — pode rodar em paralelo com US1
- **US3 (Phase 5)**: Depende de US4 parcialmente (precisa de ApiKeyRepository do Phase 2) e de US2 (endpoint deve existir); na prática: sequencial após Phase 4
- **US4 (Phase 6)**: Depende de Phase 2 (ApiKeyRepository)
- **Polish (Phase 7)**: Depende de todos os user stories completos

### User Story Dependencies

- **US1 (P1)**: Pode começar após Phase 2 — sem dependências de outros stories
- **US2 (P2)**: Pode começar após Phase 2 — sem dependências de US1 para o backend; precisa de dados de US1 para validação end-to-end
- **US3 (P2)**: Depende de US2 (endpoint GET /api/audit-logs deve existir) e de Phase 2 (ApiKeyRepository)
- **US4 (P3)**: Pode começar após Phase 2 — backend independente; frontend independente de outros stories

### Within Each Phase

- Phase 2: T004 → T005 → T007 (AuditService depende de IAuditService e Repository); T006, T008, T009 paralelos
- Phase 3: T011–T018 podem rodar em paralelo (arquivos diferentes); T019 após T010
- Phase 4: T020 → T021 (endpoint depende de handler); T022–T025 após T021
- Phase 5: T026 → T027 → T028 (sequencial)
- Phase 6: T029 → T030–T032 (handlers dependem de DTOs); T033 após handlers; T034–T037 para frontend

### Parallel Opportunities

```bash
# Phase 1 — tudo em paralelo:
T001 | T002 | T003

# Phase 2 — paralelismo parcial:
T004, T005, T006, T007, T008, T009 em paralelo após T001-T003
T010 após todos acima

# Phase 3 — eventos diferentes, arquivos diferentes:
T012 | T013 | T014 | T015 | T016 | T017 | T018  (após T011 definir o padrão)

# Phase 4 — backend antes de frontend:
T020 → T021
T022 | T023 (em paralelo após T021)
T024 após T022
T025 após T023

# Phase 6 — DTOs antes de handlers:
T029 → T030 | T031 | T032  (em paralelo)
T033 após T030-T032
T034 | T035 (em paralelo)
T036 após T034
T037 após T036
```

---

## Implementation Strategy

### MVP First (US1 Only — Registro de Eventos)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T010)
3. Complete Phase 3: US1 (T011–T019)
4. **STOP e VALIDAR**: Verificar QS-1 e QS-2 — eventos chegando no MongoDB
5. Deploy/demo de observabilidade básica

### Incremental Delivery

1. Setup + Foundational → Infrastructure pronta
2. US1 → Todos os 29 eventos registrando → Demo auditoria
3. US2 → CRM mostra atividade → Tenant admin consegue monitorar
4. US3 → Metabase conecta → Análise avançada habilitada
5. US4 → API Keys gerenciáveis → Self-service pelo tenant admin

---

## Notes

- `[P]` = arquivos diferentes, sem dependências
- `[Story]` mapeia a tarefa ao user story para rastreabilidade
- Logs são append-only — nunca implementar UPDATE/DELETE em `AuditMongoRepository`
- `auth.login_failed` para e-mails inexistentes: `actor.user_id = null`, `metadata.attempted_email = email`
- `auth.impersonation_*`: `actor.impersonated_by = "saas_admin"` é obrigatório — verificar em todos os logs gerados durante sessão de impersonation
- Fire-and-forget no `AuditService`: falha no log NÃO deve propagar para o caller (swallow + log de erro)
- API Key raw nunca armazenada — apenas o SHA-256 hex — validar no code review
- Commitar após cada task ou grupo lógico
- Parar em qualquer checkpoint para validar story independentemente
