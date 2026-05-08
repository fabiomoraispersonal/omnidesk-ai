---

description: "Task list for Departamentos e Atendentes implementation"
---

# Tasks: Departamentos e Atendentes

**Input**: Design documents from `/specs/005-departments-attendants/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: A constituição (princípio VII — Test Discipline) torna testes **obrigatórios** neste projeto. Todo backend tem teste de integração com Testcontainers (Postgres + Redis reais — sem mock); todo frontend tem `.spec.ts` co-localizado.

**Organization**: Tasks agrupadas por user story para entrega independente e validação incremental.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Pode rodar em paralelo (arquivo distinto, sem dependência pendente)
- **[Story]**: Mapeia para a user story (US1–US9) — ausente em Setup/Foundational/Polish
- Caminhos de arquivo absolutos do repositório (`src/...`, `src/omniDesk.Api/tests/...`)

## Path Conventions

- Backend: `src/omniDesk.Api/{Domain,Features,Infrastructure}/`
- Frontend CRM: `src/omniDesk.Crm/src/app/`
- Tests: `src/omniDesk.Api/tests/omniDesk.Api.Tests/{Domain,Features,Infrastructure}/` (espelha a topologia do código testado, conforme convenção pós-Spec 004)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuração de ambiente e estrutura de pastas para esta feature.

- [X] T001 Adicionar `MAX_SUGGESTION_CONTEXT_MESSAGES=20` em `src/omniDesk.Api/.env.example` e `src/omniDesk.Api/appsettings.Development.json` (chave `Ai:MaxSuggestionContextMessages`); documentar cap de 50 em README local
- [X] T002 Criar migration SQL `Add_Departments_Attendants.sql` em `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` com as 5 tabelas (`departments`, `attendants`, `attendant_departments`, `attendant_status`, `canned_responses`) conforme `data-model.md` §1; aplicar em template do schema `tenant_{slug}` (a Spec 003 já provisiona o schema)
- [X] T003 [P] Criar estrutura de pastas backend: `src/omniDesk.Api/Domain/{Departments,Attendants,CannedResponses}/`, `src/omniDesk.Api/Features/{Departments,Attendants,CannedResponses,AiSuggestions,Distribution}/`, `src/omniDesk.Api/Infrastructure/{Departments,Attendants,CannedResponses,Presence,Distribution,WebSockets}/`
- [X] T004 [P] Criar estrutura de pastas frontend: `src/omniDesk.Crm/src/app/core/presence/`, `src/omniDesk.Crm/src/app/features/{departments,attendants,canned-responses,ticket-queue,ai-suggestion}/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domínio compartilhado, infraestrutura de Redis/WebSocket/Hangfire e wire-up de DI. Bloqueia TODAS as user stories.

**⚠️ CRITICAL**: Nenhuma user story pode começar antes deste bloco completar.

### Backend — domínio compartilhado

- [X] T005 [P] Criar enum `AttendanceStatus` em `src/omniDesk.Api/Domain/Attendants/AttendanceStatus.cs` com valores `Online`, `Away`, `Offline` (data-model §1.4)
- [X] T006 [P] Criar `Department` em `src/omniDesk.Api/Domain/Departments/Department.cs` com propriedades de data-model §1.1; criar `DepartmentBusinessHours.cs` (Value Object com `Start`, `End`, `Days`)
- [X] T007 [P] Criar `Attendant` em `src/omniDesk.Api/Domain/Attendants/Attendant.cs`; `AttendantStatusEntry.cs` (snapshot de presença); interface `IAttendantRepository.cs`
- [X] T008 [P] Criar `CannedResponse` em `src/omniDesk.Api/Domain/CannedResponses/CannedResponse.cs`; `CannedResponseVariable.cs` com constantes (`ClientName`, `AttendantName`, `TicketNumber`, `DepartmentName`)
- [X] T009 [P] Criar `RedisKeys.cs` em `src/omniDesk.Api/Infrastructure/Authorization/RedisKeys.cs` (ou amplia existente) com helpers tipados: `AttendantStatus(slug, id)`, `RoundRobin(slug, deptId)`, `TicketLock(slug, ticketId)`, `WsTenant(slug)`, `WsDept(slug, id)`, `WsAttendant(slug, id)` — princípio I

### Backend — infraestrutura de presença e lock

- [X] T010 Criar `PresenceCache` em `src/omniDesk.Api/Infrastructure/Presence/PresenceCache.cs` — Redis-backed, TTL 5 min; métodos `GetAsync(slug, attendantId)`, `SetAsync(...)`, `RenewHeartbeatAsync(...)`, `InvalidateAsync(...)`; depende de T009
- [X] T011 Criar `PresenceLogger` em `src/omniDesk.Api/Infrastructure/Presence/PresenceLogger.cs` — Mongo-backed, coleção `{slug}_attendant_status_logs` (data-model §2.1); método `LogTransitionAsync(from, to, by, attendantId, attendantName)`
- [X] T012 Criar `TicketLock` em `src/omniDesk.Api/Infrastructure/Distribution/TicketLock.cs` — `SET NX EX 10` wrapper sobre `IConnectionMultiplexer`; método `TryAcquireAsync(slug, ticketId, holderId)` retorna `IAsyncDisposable` que libera o lock no `Dispose`; depende de T009 (research §R2)
- [X] T013 Criar `RoundRobinCursor` em `src/omniDesk.Api/Infrastructure/Distribution/RoundRobinCursorRedis.cs` — `INCR + EXPIRE 3600`; método `NextIndexAsync(slug, deptId, eligibleCount)`; depende de T009 (research §R1)

### Backend — EF Core configurations e migration

- [X] T014 Criar configurations EF Core em `src/omniDesk.Api/Infrastructure/Departments/DepartmentConfiguration.cs` (com mapeamento de `business_hours` como owned type ou colunas separadas), `Infrastructure/Attendants/{Attendant,AttendantStatus,AttendantDepartment}Configuration.cs`, `Infrastructure/CannedResponses/CannedResponseConfiguration.cs`; registrar em `AppDbContext` (use schema dinâmico `tenant_{slug}` já existente)
- [X] T015 Aplicar migration de T002 em ambiente de dev: rodar `dotnet ef database update --project src/omniDesk.Api` (operador valida; documenta em `quickstart-evidences.md` deste spec)

### Backend — WebSocket e DI

- [X] T016 Criar `DepartmentEventBus` em `src/omniDesk.Api/Infrastructure/WebSockets/DepartmentEventBus.cs` — publica em pub/sub Redis nos canais por escopo (research §R4); método `PublishAsync<T>(channel, eventType, payload)`
- [X] T017 Criar `AttendantHubHandler` em `src/omniDesk.Api/Infrastructure/WebSockets/AttendantHubHandler.cs` — handler WebSocket nativo que aceita `subscribe` por canal e valida claims contra os 3 níveis (`tenant`, `dept:{id}`, `attendant:self`); recusa `attendant:{other_id}` com 403
- [X] T018 Wire DI em `src/omniDesk.Api/Program.cs`: registrar `PresenceCache`, `PresenceLogger`, `TicketLock`, `RoundRobinCursor`, `DepartmentEventBus` (Singleton); mapear endpoint `/ws` para `AttendantHubHandler`; registrar `Add_Departments_Attendants` migration na pipeline de provisioning (Spec 003 já roda os SQLs do tenant — basta adicionar o arquivo)

### Frontend — base de presença e WebSocket

- [X] T019 [P] Criar `presence.signal.ts` em `src/omniDesk.Crm/src/app/core/presence/` — `signal<AttendanceStatus>` derivado dos eventos WebSocket
- [X] T020 [P] Criar `presence-websocket.service.ts` em `src/omniDesk.Crm/src/app/core/presence/` — abre conexão `/ws`, subscreve `tenant`/`dept:{id}`/`attendant:self`, despacha eventos para signals; `.spec.ts` co-localizado verificando subscribe/unsubscribe e dispatch

**Checkpoint**: Foundation pronta. User stories podem prosseguir em paralelo respeitando dependências entre US.

---

## Phase 3: User Story 1 — Tenant admin organiza times por departamentos (Priority: P1) 🎯 MVP

**Goal**: Cadastro completo de departamentos e atendentes com vínculos N:N — base do CRM.

**Independent Test**: Tenant admin cria 2 departamentos, cadastra 3 atendentes, vincula cada um a pelo menos 1 dept; listagem e edição funcionam fim-a-fim.

### Backend — Departments

- [X] T021 [US1] Implementar `DepartmentsEndpoints.Map` em `src/omniDesk.Api/Features/Departments/DepartmentsEndpoints.cs` com `GET /`, `GET /{id}`, `POST /`, `PUT /{id}`, `DELETE /{id}`, `GET /{id}/attendants`; cada um com policy correta da Spec 004 (`CanCreateDepartment`, `CanEditDepartment`, `CanListDepartments`)
- [X] T022 [US1] Criar commands em `src/omniDesk.Api/Features/Departments/Commands/{Create,Update,Deactivate}DepartmentCommand.cs` com handlers; depende de T021
- [X] T023 [US1] Criar `CreateDepartmentValidator` em `src/omniDesk.Api/Features/Departments/Validators/CreateDepartmentValidator.cs` com regras: `name 2-100`, `business_hours.start < end`, `business_hours` tudo nulo OU tudo preenchido, nome único por tenant (FluentValidation); idem `UpdateDepartmentValidator`
- [X] T024 [US1] Implementar guarda de exclusão em `DeactivateDepartmentCommand`: bloqueia se `attendant_departments.count > 0` ou se houver tickets ativos (consulta defensiva caso Spec 008 ainda não esteja implementada — try/catch + fallback de "verificação não suportada"); responder 422 `DEPARTMENT_HAS_ACTIVE_TICKETS` ou `DEPARTMENT_HAS_LINKED_ATTENDANTS`

### Backend — Attendants

- [X] T025 [US1] Implementar `AttendantsEndpoints.Map` em `src/omniDesk.Api/Features/Attendants/AttendantsEndpoints.cs` com `GET /`, `GET /{id}`, `POST /`, `PUT /{id}`, `DELETE /{id}`, `PUT /{id}/departments`, `POST /{id}/avatar`; policies da Spec 004 (`CanCreateAttendant`, `CanEditAttendant`, `CanDeactivateAttendant`)
- [X] T026 [US1] Criar commands `{Create,Update,Deactivate}AttendantCommand.cs`; criar handler de `UpdateAttendantDepartmentsCommand` com transação (zera `is_primary`, marca o escolhido); validação `primary_department_id ∈ department_ids`
- [X] T027 [US1] Criar `CreateAttendantValidator` em `src/omniDesk.Api/Features/Attendants/Validators/CreateAttendantValidator.cs`: `user_id` existe em `public.users`, ainda não é atendente; `max_simultaneous_chats` 1–100; `department_ids` ≥ 1 quando `primary_department_id` for fornecido
- [X] T028 [US1] Implementar upload de avatar em `Features/Attendants/AvatarUploadEndpoint.cs`: aceita multipart (≤ 2 MB, JPG/PNG/WebP), redimensiona para 256×256 (System.Drawing.Common), persiste em `tenant-{slug}/avatars/attendants/{id}/256x256.{ext}`, retorna URL assinada de 7 dias (research §R9)

### Frontend — Departments

- [X] T029 [P] [US1] Criar `department.service.ts` em `src/omniDesk.Crm/src/app/features/departments/services/` com métodos `list()`, `get(id)`, `create(...)`, `update(id, ...)`, `deactivate(id)`, `getAttendants(id)`; `.spec.ts` cobrindo cada método + erro 422
- [X] T030 [P] [US1] Criar `department-list.component` em `src/omniDesk.Crm/src/app/features/departments/department-list/` com tabela PrimeNG (nome, status, atendentes, tickets ativos), botão "Novo"; `.spec.ts` co-localizado
- [X] T031 [P] [US1] Criar `department-form.component` em `src/omniDesk.Crm/src/app/features/departments/department-form/` com Reactive Form (validação dos campos), seletor de dias com chips, time-picker início/fim, inputs SLA opcionais; `.spec.ts` co-localizado

### Frontend — Attendants

- [X] T032 [P] [US1] Criar `attendant.service.ts` em `src/omniDesk.Crm/src/app/features/attendants/services/` com CRUD + `updateDepartments(...)` + `uploadAvatar(file)`; `.spec.ts`
- [X] T033 [P] [US1] Criar `attendant-list.component` com tabela PrimeNG (avatar, nome, departamentos, status, tickets ativos); filtros por dept + status; `.spec.ts`
- [X] T034 [P] [US1] Criar `attendant-form.component` com seletor multi-departamento (chips), marcação de principal, slider de `max_simultaneous_chats`, file input para avatar com preview; `.spec.ts`

### Tests — backend

- [X] T035 [P] [US1] Criar `DepartmentsEndpointsTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Departments/` — Testcontainers Postgres real; CRUD completo, regra de soft delete bloqueado, validação `business_hours` mistura nulos
- [X] T036 [P] [US1] Criar `AttendantsEndpointsTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Attendants/` — CRUD, vínculos N:N atomicidade do `is_primary`, upload de avatar (mock MinIO), guarda de `user_id` único

**Checkpoint**: Estrutura mínima pronta — nada do CRM operacional funciona sem isto. **MVP entregável.**

---

## Phase 4: User Story 2 — Atendente recebe ticket distribuído automaticamente (Priority: P1)

**Goal**: Round-robin atômico distribui tickets entre elegíveis sem perder nenhum.

**Independent Test**: 3 atendentes online → 6 tickets em sequência → cada um recebe 2; nenhum excede o limite.

### Backend — Distribution core

- [ ] T037 [US2] Criar `BusinessHoursEvaluator` em `src/omniDesk.Api/Features/Distribution/BusinessHoursEvaluator.cs` com `IsAvailable(now, businessHours)` (24/7 quando null, conforme research §R5) e `ElapsedBusinessMinutes(start, now, hours)` (cálculo somando intervalos úteis)
- [ ] T038 [US2] Criar `TicketAssignmentService` em `src/omniDesk.Api/Features/Distribution/TicketAssignmentService.cs` implementando o pseudocódigo de `contracts/round-robin-distribution.md`: aquire lock → query elegíveis → escolhe via cursor → UPDATE ticket + increment counter → publish events → release lock
- [ ] T039 [US2] Criar query helper `EligibleAttendantsQuery` em `Features/Distribution/EligibleAttendantsQuery.cs`: junta `attendants × attendant_departments × attendant_status` (Postgres) cruzando com presença Redis; retorna `List<AttendantSnapshot>` com `id`, `active_ticket_count`, `max_simultaneous_chats` ordenado por `id`
- [ ] T040 [US2] Expor endpoint interno `POST /api/internal/tickets/{id}/assign` (consumido pelo Spec 008 — Tickets) que invoca `TicketAssignmentService.AssignAsync(...)`; retorna `AssignmentResult` com `outcome`, `assigned_attendant_id`, `queue_reason`

### Frontend — Queue board (supervisor view)

- [ ] T041 [P] [US2] Criar `ticket-queue.service.ts` em `src/omniDesk.Crm/src/app/features/ticket-queue/services/` consumindo eventos `ticket.assigned`/`ticket.queued` para manter estado da fila por departamento; `.spec.ts`
- [ ] T042 [P] [US2] Criar `queue-board.component` em `src/omniDesk.Crm/src/app/features/ticket-queue/queue-board/` — visão de supervisor com colunas por departamento, badges de fila e atendentes online; `.spec.ts`

### Tests — backend

- [ ] T043 [P] [US2] Criar `RoundRobinCursorTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Distribution/` — Testcontainers Redis real; 100 incrementos com 5 atendentes, valida diff máx ≤ 1 (SC-003); cobre cursor expirado (TTL) e reset
- [ ] T044 [P] [US2] Criar `TicketAssignmentServiceTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Distribution/`: distribui em rajada (FR-013/014), respeita capacity (FR-018), enfileira sem elegíveis (FR-015), classifica `QueueReason` corretamente
- [ ] T045 [P] [US2] Criar `BusinessHoursEvaluatorTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Distribution/`: cobre matriz 4×4 da Spec §3.3, departamento sem horário = 24/7 (FR-002), `ElapsedBusinessMinutes` com cruzamento de noite/fim de semana

**Checkpoint**: Distribuição automática funcional + verificação algorítmica.

---

## Phase 5: User Story 3 — Lock impede atribuição duplicada (Priority: P1)

**Goal**: Independente de carga, dois atendentes nunca pegam o mesmo ticket.

**Independent Test**: 50 pares concorrentes → 0 atribuições duplicadas (SC-002).

### Backend — Manual pickup endpoint

- [ ] T046 [US3] Implementar `POST /api/tickets/{id}/pickup` em `src/omniDesk.Api/Features/Distribution/PickupTicketEndpoint.cs`: usa `TicketLock`; se ticket já atribuído a outro, retorna `409 ALREADY_PICKED_UP`; se ticket atribuído ao próprio caller, idempotente; emite `ticket.assigned` ou `ticket.transferred` conforme caso

### Tests — backend

- [ ] T047 [P] [US3] Criar `TicketLockTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Infrastructure/Distribution/` — Testcontainers Redis; valida `SET NX EX`, expira em 10 s, libera no Dispose, lock órfão expira sozinho
- [ ] T048 [P] [US3] Criar `ConcurrentPickupTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Distribution/` — 50 pares de requests `POST /pickup` paralelos para o mesmo ticket; assert exatamente 1 sucesso por par (SC-002); mede latência p95 ≤ 200 ms

**Checkpoint**: Garantia de integridade em concorrência.

---

## Phase 6: User Story 4 — Transferência de tickets (Priority: P2)

**Goal**: Atendente transfere para colega, departamento ou outra fila com histórico íntegro e SLA recalculado.

**Independent Test**: Atendente A transfere ticket para departamento Y; histórico chega íntegro; SLA recalculado.

### Backend — Transfer

- [ ] T049 [US4] Criar `TransferTicketCommand` em `src/omniDesk.Api/Features/Distribution/Commands/TransferTicketCommand.cs` aceitando `to_attendant_id` OU `to_department_id` + motivo opcional; reseta `sla_started_at` quando muda de departamento (FR-026); registra `ticket_transfers` (assume tabela ou metadata em Spec 008)
- [ ] T050 [US4] Expor `POST /api/tickets/{id}/transfer` em `Features/Distribution/TransferTicketEndpoint.cs`; valida que o caller é o assignee atual ou supervisor; emite `ticket.transferred`

### Frontend — Transfer UI

- [ ] T051 [P] [US4] Criar `transfer-ticket-dialog.component` em `src/omniDesk.Crm/src/app/features/ticket-queue/transfer-dialog/` — modal PrimeNG com seletor de atendente/departamento, campo motivo opcional; `.spec.ts`

### Tests — backend

- [ ] T052 [P] [US4] Criar `TransferTicketTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Distribution/`: transferência mantém histórico (assert mensagens preservadas), recalcula SLA (FR-026, assert `sla_started_at` mudou), motivo registrado, evento WebSocket emitido nos canais corretos

**Checkpoint**: Operação ganha flexibilidade sem perder integridade do histórico.

---

## Phase 7: User Story 5 — Status de presença com timeout automático (Priority: P2)

**Goal**: Status reflete realidade — timeouts automáticos em 15/30 min; toda transição é auditada.

**Independent Test**: Atendente fica `online` sem interação; aos 15 min vai para `away`; aos 45 min para `offline`; ambas geram log Mongo e evento WebSocket.

### Backend — Status endpoints

- [ ] T053 [US5] Implementar `PATCH /api/attendants/{id}/status` em `src/omniDesk.Api/Features/Attendants/UpdateStatusEndpoint.cs` aceitando `{ status }`; restrição: atendente só muda o próprio (a menos que seja supervisor); side-effects: Redis + Postgres + Mongo + evento WebSocket (FR-007, FR-011, FR-012)
- [ ] T054 [US5] Implementar `PATCH /api/attendants/{id}/heartbeat` em `Features/Attendants/HeartbeatEndpoint.cs`: renova TTL Redis e atualiza `last_heartbeat_at`; sem body, sem evento; idempotente
- [ ] T055 [US5] Criar `PresenceTimeoutJob` em `src/omniDesk.Api/Features/Distribution/PresenceTimeoutJob.cs` (Hangfire recurring 1 min): varre `attendant_status` para `online` há 15 min sem heartbeat → `away` (changed_by=system); varre `away` há 30 min → `offline`; cada transição reusa o mesmo pipeline de side-effects do T053
- [ ] T056 [US5] Registrar `PresenceTimeoutJob` em `Program.cs` via `RecurringJob.AddOrUpdate(...)` com cron `*/1 * * * *`

### Frontend — Status toggle e supervisor view

- [ ] T057 [P] [US5] Criar `attendant-status-toggle.component` em `src/omniDesk.Crm/src/app/features/attendants/attendant-status-toggle/` — toggle 3-way `online`/`away`/`offline` com PrimeNG SelectButton; integra com `presence.service.ts`; dispara heartbeat HTTP a cada 60 s enquanto aba ativa + interação detectada (mouse/keyboard); `.spec.ts`
- [ ] T058 [P] [US5] Atualizar `presence.service.ts` para incluir `setStatus(status)`, `heartbeat()`; gerencia visibility events da aba (Document Visibility API); `.spec.ts`

### Tests — backend

- [ ] T059 [P] [US5] Criar `PresenceCacheTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Infrastructure/Presence/` — Testcontainers Redis; valida TTL 5 min, `RenewHeartbeatAsync` reseta TTL, `InvalidateAsync` apaga
- [ ] T060 [P] [US5] Criar `PresenceTimeoutJobTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Distribution/` — usa `IClock` mockável; cenários: 15 min sem heartbeat → away; 45 min → offline; com heartbeat recente não muda; eventos emitidos com `changed_by=system`
- [ ] T061 [P] [US5] Criar `UpdateStatusTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Attendants/`: side-effects em todos os 4 lugares (Redis, Postgres, Mongo, WebSocket); supervisor pode alterar de outros; atendente não pode alterar de outros (403)

**Checkpoint**: Presença em tempo real auditável e consistente.

---

## Phase 8: User Story 6 — Respostas pré-formadas (Priority: P2)

**Goal**: Atendente acelera atendimento com respostas reutilizáveis e variáveis substituídas no momento do uso.

**Independent Test**: Resposta com `{{client_name}}` etc. é renderizada com valores reais antes do envio; nenhum placeholder literal vaza.

### Backend — Canned responses

- [ ] T062 [US6] Implementar `CannedResponsesEndpoints.Map` em `src/omniDesk.Api/Features/CannedResponses/CannedResponsesEndpoints.cs` com `GET /`, `GET /{id}`, `POST /`, `PUT /{id}`, `DELETE /{id}`, `POST /render`
- [ ] T063 [US6] Criar `VariableSubstitution` em `src/omniDesk.Api/Features/CannedResponses/VariableSubstitution.cs`: regex puro `\{\{(\w+)\}\}` com tabela de fallbacks (research §R7, FR-034); variáveis desconhecidas preservadas + log Warning
- [ ] T064 [US6] Criar `CannedResponseValidator` em `Features/CannedResponses/Validators/CannedResponseValidator.cs`: `title 2-100`, `content 1-4000`, `department_id` existente e ativo; título único por escopo
- [ ] T065 [US6] Implementar `RenderCannedResponseEndpoint` (`POST /render`): aceita `template_id` + `conversation_id`; busca contexto (cliente, atendente, ticket, dept); aplica `VariableSubstitution`; retorna `rendered` + `missing_variables`
- [ ] T066 [US6] Implementar guarda de autor: PUT/DELETE só permite ao `created_by` ou ao tenant_admin (Spec 004); response 403 `FORBIDDEN_NOT_OWNER` quando outro atendente tenta

### Frontend — Picker e form

- [ ] T067 [P] [US6] Criar `canned-response.service.ts` em `src/omniDesk.Crm/src/app/features/canned-responses/services/` com CRUD + `render(templateId, conversationId)`; `.spec.ts`
- [ ] T068 [P] [US6] Criar `canned-response-picker.component` em `src/omniDesk.Crm/src/app/features/canned-responses/canned-response-picker/` — autocomplete acionado por `/`, busca por título e conteúdo, render preview + insert no campo de mensagem; `.spec.ts`
- [ ] T069 [P] [US6] Criar `canned-response-form.component` em `src/omniDesk.Crm/src/app/features/canned-responses/canned-response-form/` — Reactive Form com escopo (global/dept), preview de variáveis em tempo real; `.spec.ts`

### Tests — backend

- [ ] T070 [P] [US6] Criar `VariableSubstitutionTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/CannedResponses/`: substitui as 4 variáveis canônicas, fallbacks corretos (FR-034), preserva variáveis desconhecidas, performance < 1 ms para template de 4000 chars (SC-006)
- [ ] T071 [P] [US6] Criar `CannedResponsesCrudTests.cs`: CRUD completo, escopo por dept respeita filtro, autor pode editar, supervisor não-autor bloqueado, tenant_admin sempre pode

**Checkpoint**: Produtividade do atendente no chat com variáveis sem vazar placeholders.

---

## Phase 9: User Story 7 — Comportamento de transbordo (Priority: P2)

**Goal**: Combina horário comercial + presença para escolher entre transferir, enfileirar com mensagem "ocupados" ou enfileirar com horário do próximo turno.

**Independent Test**: Matriz 4×4 da Spec §3.3 testada via `BusinessHoursEvaluator` + `TicketAssignmentService`.

### Backend — Wire-up de transbordo

- [ ] T072 [US7] Estender `TicketAssignmentService.AssignAsync` em `src/omniDesk.Api/Features/Distribution/TicketAssignmentService.cs`: quando `Outcome.Queued`, popular `QueueReason` corretamente baseado em `BusinessHoursEvaluator.IsAvailable(now)` × `eligible.IsEmpty`; o evento `ticket.queued` carrega `next_business_window_start` calculado quando `OutsideBusinessHoursNoOneOnline`
- [ ] T073 [US7] Adicionar método `BusinessHoursEvaluator.NextBusinessWindowStart(now, hours)` que retorna `DateTimeOffset?` com início do próximo período comercial considerando timezone do tenant (Spec 003 — `tenants.timezone`)

### Tests — backend

- [ ] T074 [P] [US7] Criar `OverflowBehaviorTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Distribution/` — 4 cenários da matriz §3.3 (FR-027–030); valida `QueueReason` correto + payload do evento `ticket.queued`

**Checkpoint**: Sem mensagens incoerentes ao cliente quando ninguém pode atender agora.

---

## Phase 10: User Story 8 — Sugestão de resposta com IA (Priority: P3)

**Goal**: Atendente recebe sugestão contextual da IA, sempre revisa antes de enviar; nenhuma mensagem sai sem aprovação humana.

**Independent Test**: Atendente clica "Sugerir"; preview surge editável; edita e envia; cliente recebe versão editada; telemetria registra `human_action=edited`.

### Backend — Suggestion service

- [ ] T075 [US8] Criar `SuggestReplyService` em `src/omniDesk.Api/Features/AiSuggestions/SuggestReplyService.cs`: consome `IAgentRuntime` (Spec 002 — Agentes IA) para buscar prompt do sub-agente vinculado ao dept; monta prompt conforme research §R6 (system prompts + N últimas mensagens); chama OpenAI; trunca resposta a 1000 caracteres
- [ ] T076 [US8] Implementar `POST /api/conversations/{id}/suggest-reply` em `Features/AiSuggestions/SuggestReplyEndpoint.cs`: valida ownership da conversa OU `Policies.CanViewAllConversations`; rate limit 30/min/atendente (RateLimiter por chave de usuário); retorna `suggestion_id` + `text` + `model` + `elapsed_ms` + tokens
- [ ] T077 [US8] Criar `AiSuggestionLogger` em `Features/AiSuggestions/AiSuggestionLogger.cs`: persiste em `{slug}_ai_suggestion_logs` (data-model §2.2); método `LogGenerationAsync(...)` retorna `_id` usado como `suggestion_id`
- [ ] T078 [US8] Implementar `PATCH /api/conversations/{id}/suggestions/{suggestion_id}` em `Features/AiSuggestions/UpdateSuggestionActionEndpoint.cs`: aceita `{ human_action, final_message_text? }`; **não envia mensagem** — apenas grava ação no Mongo (FR-038, SC-007); valida que o caller é o atendente que originou a sugestão
- [ ] T079 [US8] Tratar erros de provedor em `SuggestReplyService`: timeout > 10 s → 504 `AI_PROVIDER_TIMEOUT`, 5xx → 502 `AI_PROVIDER_ERROR`, rate limit → 429 `AI_RATE_LIMIT`; mensagens PT-BR; log estruturado (FR-040)

### Frontend — Suggestion panel

- [ ] T080 [P] [US8] Criar `suggestion.service.ts` em `src/omniDesk.Crm/src/app/features/ai-suggestion/services/` com `request(conversationId)`, `recordAction(conversationId, suggestionId, action, finalText?)`; tratamento de erros com toast PT-BR; `.spec.ts`
- [ ] T081 [P] [US8] Criar `suggestion-panel.component` em `src/omniDesk.Crm/src/app/features/ai-suggestion/suggestion-panel/`: botão "Sugerir resposta com IA", área de preview editável (textarea), botões "Aprovar e enviar", "Editar e enviar", "Descartar"; integra com endpoint de mensagens da Spec 008 quando aprovar; **não envia mensagem se a resposta vier via API mock** (defense-in-depth); `.spec.ts`

### Tests — backend

- [ ] T082 [P] [US8] Criar `SuggestReplyServiceTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/AiSuggestions/`: monta prompt corretamente (sub-agente do dept incluso, N últimas mensagens), trunca em 1000 chars, mock OpenAI retorna texto, falha de provedor cai no fallback
- [ ] T083 [P] [US8] Criar `SuggestionActionTests.cs`: PATCH grava ação correta no Mongo, **não cria mensagem na conversa** (assert via repositório de mensagens); 403 quando outro atendente tenta atualizar; idempotente (duas chamadas com mesma action sobrescreve)
- [ ] T084 [P] [US8] Criar `SuggestionAuditTests.cs`: cobertura de SC-007 (zero mensagens enviadas via fluxo de sugestão sem PATCH human_action); inspeciona que entre POST /suggest-reply e o envio real só ocorre PATCH na timeline

**Checkpoint**: IA assistiva entregue com defense-in-depth; SC-007 verificado.

---

## Phase 11: User Story 9 — SLA visual (Priority: P3)

**Goal**: Atendente prioriza tickets em risco com badges amarelo/vermelho; horário fora do expediente não conta.

**Independent Test**: Ticket criado às 17h50 com SLA 60 min em dept que encerra 18h → contador pausa às 18h, retoma 8h do próximo dia útil; badge amarelo em 80 % e vermelho em 100 % de tempo útil.

### Backend — SLA calculator

- [ ] T085 [US9] Criar `SlaCalculator` em `src/omniDesk.Api/Features/Distribution/SlaCalculator.cs`: calcula `elapsed_business_minutes` reusando `BusinessHoursEvaluator`; expõe `ComputeSlaSnapshot(ticket, dept, now)` retornando `{ first_response_status, first_response_elapsed_minutes, resolution_status, resolution_elapsed_minutes }`; status ∈ `ok`/`warning`/`overdue`/`not_configured`
- [ ] T086 [US9] Atualizar `GET /api/attendants/{id}/tickets` para incluir `sla` no payload (data-model — contracts/attendants-api.md); idem para endpoints de listagem de tickets em fila

### Frontend — SLA badge

- [ ] T087 [P] [US9] Criar `sla-badge.component` em `src/omniDesk.Crm/src/app/shared/components/sla-badge/`: aceita `@Input() snapshot`; renderiza badge PrimeNG com cor (amarelo ≥ 80 %, vermelho ≥ 100 %, ok < 80 %); contador regressivo MM:SS atualizando a cada segundo; `.spec.ts`
- [ ] T088 [P] [US9] Integrar `sla-badge` no `ticket-queue/queue-board.component` e em qualquer card de ticket existente do CRM

### Tests — backend

- [ ] T089 [P] [US9] Criar `SlaCalculatorTests.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Distribution/`: cenários de pause/resume cruzando 18h → 08h dia útil seguinte (FR-043); badges nas thresholds 80/100 % com erro ≤ 30 s (SC-008); dept sem SLA retorna `not_configured`

**Checkpoint**: Visibilidade visual do SLA sem engine de notificação (V2).

---

## Phase 12: Polish & Cross-Cutting Concerns

**Purpose**: Documentação, observabilidade, perf check, integração com Spec 004 (`dept_ids`).

- [ ] T090 [P] Atualizar `ClaimsTransformer` (Spec 004) em `src/omniDesk.Api/Infrastructure/Authorization/ClaimsTransformer.cs` para popular `dept_ids` claim consultando `tenant_{slug}.attendant_departments` JOIN `tenant_{slug}.attendants ON user_id` (research §R8); remover o try/catch fallback de "tabela ausente"
- [ ] T091 [P] Adicionar `DistributionBenchmark` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Performance/DistributionBenchmark.cs`: mede p95 do `TicketAssignmentService.AssignAsync` em rajada de 1000 tickets; assert ≤ 150 ms (Performance Goal do plan)
- [ ] T092 [P] Adicionar `WebSocketLatencyBenchmark` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Performance/WebSocketLatencyBenchmark.cs`: mede latência de `attendant.status_changed` até cliente receber; assert ≤ 1 s (SC-004)
- [ ] T093 Atualizar `docs/ARCHITECTURE.md` adicionando seção "Departamentos e Atendentes" com fluxo de distribuição, regra de transbordo e cross-link com `contracts/round-robin-distribution.md`
- [ ] T094 [P] Auditar endpoints existentes em busca de uso correto das policies novas necessárias (`Policies.CanCreateAttendant`, etc.) — verificar que nenhuma checagem manual de role permanece em `SendInviteEndpoint` e endpoints similares
- [ ] T095 Executar `quickstart.md` §§ A–G manualmente em ambiente local; preencher `quickstart-evidences.md` (não comitar dados sensíveis)
- [ ] T096 [P] Adicionar logs estruturados de negação de policy específicos desta feature (department/attendant) — reusa `AuthorizationFailureLogger` da Spec 004

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: sem dependências.
- **Phase 2 (Foundational)**: depende de Phase 1. Bloqueia TODAS as user stories.
- **Phase 3 (US1 — MVP)**: depende de Phase 2 apenas.
- **Phase 4 (US2)**: depende de US1 (precisa de departamentos e atendentes para distribuir).
- **Phase 5 (US3)**: depende de US2 (lock está em uso pela distribuição; testes formalizam a garantia).
- **Phase 6 (US4)**: depende de US2 (transferência usa o mesmo `TicketAssignmentService`).
- **Phase 7 (US5)**: depende de Phase 2 (Hangfire + PresenceCache); **independente** de US1–US4 mas compartilha `attendants` cadastrados com US1.
- **Phase 8 (US6)**: depende de Phase 2 + US1 (canned responses referenciam departments e attendants).
- **Phase 9 (US7)**: depende de US2 (estende `TicketAssignmentService`).
- **Phase 10 (US8)**: depende de Spec 002 (Agentes IA) **estar implementada**; pode iniciar após Phase 2 mas é entregue após US1–US7.
- **Phase 11 (US9)**: depende de US2 (`BusinessHoursEvaluator`).
- **Phase 12 (Polish)**: depende das stories desejadas; T090 depende de US1 ter populado `attendant_departments`.

### Parallel Opportunities

- **Phase 1**: T003, T004 em paralelo.
- **Phase 2**: T005–T009 em paralelo (5 arquivos de domínio independentes); T010–T013 em paralelo (4 arquivos de infraestrutura sem dependência cruzada); T014–T015 sequenciais; T016–T017 paralelos; T019–T020 paralelos.
- **Phase 3 (US1)**: backend Departments (T021–T024) sequencial dentro de si mas paralelo a Attendants (T025–T028); frontend (T029–T034) todo paralelo entre si após backend; testes (T035–T036) paralelos.
- **Phase 4 (US2)**: T037–T040 sequenciais dentro de Distribution; frontend (T041–T042) e tests (T043–T045) paralelos após backend.
- **Phase 5 (US3)**: T046 sequencial; T047, T048 paralelos.
- **Phase 6 (US4)**: T049–T050 sequenciais; T051, T052 paralelos.
- **Phase 7 (US5)**: T053–T056 sequenciais (status pipeline); T057–T058 e T059–T061 paralelos.
- **Phase 8 (US6)**: T062–T066 sequenciais; T067–T069 e T070–T071 paralelos.
- **Phase 9 (US7)**: T072–T073 sequenciais; T074 paralelo.
- **Phase 10 (US8)**: T075–T079 sequenciais (mesma feature); T080–T081 e T082–T084 paralelos.
- **Phase 11 (US9)**: T085–T086 sequenciais; T087–T088 e T089 paralelos.
- **Equipes paralelas pós-Phase 2**:
  - **Dev A**: US1 → US4 → US7
  - **Dev B**: US2 → US3 → US9
  - **Dev C**: US5 → US6 → US8

---

## Parallel Example: User Story 1 (após Foundational completo)

```bash
# Backend Departments e Attendants em paralelo (módulos distintos):
Task: "Implement DepartmentsEndpoints + commands + validators"
Task: "Implement AttendantsEndpoints + commands + validators + avatar upload"

# Frontend services + components paralelos por feature:
Task: "department.service.ts + .spec.ts"
Task: "department-list + department-form + .spec.ts"
Task: "attendant.service.ts + .spec.ts"
Task: "attendant-list + attendant-form + .spec.ts"

# Testes finais paralelos:
Task: "DepartmentsEndpointsTests.cs"
Task: "AttendantsEndpointsTests.cs"
```

## Parallel Example: User Story 5 — Presença

```bash
# Backend (sequencial pelo pipeline de status):
Task: "PATCH /status endpoint"
Task: "PATCH /heartbeat endpoint"
Task: "PresenceTimeoutJob recurring"
Task: "Wire-up Program.cs"

# Frontend e tests paralelos:
Task: "attendant-status-toggle.component + .spec.ts"
Task: "presence.service.ts heartbeat dispatcher"
Task: "PresenceCacheTests.cs"
Task: "PresenceTimeoutJobTests.cs"
Task: "UpdateStatusTests.cs"
```

---

## Implementation Strategy

### MVP First (Phases 1 → 2 → 3)

1. **Phase 1: Setup** — env var + migration + folders.
2. **Phase 2: Foundational** — domínio + presença + lock + WebSocket dispatcher + DI.
3. **Phase 3: US1** — CRUD de departamentos e atendentes com vínculos N:N.
4. **STOP & VALIDATE**: Tenant admin consegue cadastrar times do zero. Demo possível.

### Incremental Delivery

5. **Phase 4 (US2)**: distribuição automática → habilita o coração da operação.
6. **Phase 5 (US3)**: testes de lock + endpoint manual pickup → garante integridade.
7. **Phase 6 (US4)**: transferência → flexibilidade operacional.
8. **Phase 7 (US5)**: presença com timeout → distribuição reflete realidade.
9. **Phase 8 (US6)**: canned responses → produtividade.
10. **Phase 9 (US7)**: transbordo refinado → mensagens coerentes ao cliente.
11. **Phase 10 (US8)**: sugestão IA (após Spec 002 estar pronta) → produtividade avançada.
12. **Phase 11 (US9)**: SLA visual → priorização visual.
13. **Phase 12 (Polish)**: docs, perf, integração final com Spec 004.

### Parallel Team Strategy

Após Phase 2:

- **Dev A (operação)**: US1 → US4 → US7
- **Dev B (infraestrutura)**: US2 → US3 → US9
- **Dev C (UX/IA)**: US5 → US6 → US8

Tudo converge na Phase 12.

---

## Notes

- [P] tasks = arquivos distintos, sem dependência ainda pendente.
- [Story] label rastreia task ↔ user story para entrega independente.
- Cada user story é independentemente completável e testável (em conformidade com a estratégia da spec).
- Constituição V (Simplicity): zero pacote NuGet/npm novo introduzido — apenas built-ins + dependências já existentes.
- Constituição VII (Test Discipline): Testcontainers + DB real obrigatório; magic strings proibidas (vide `RedisKeys.*`, `AttendanceStatus.*`, `CannedResponseVariable.*`).
- Commit após cada task ou grupo lógico (especialmente após cada Checkpoint).
- US10 (não numerada explicitamente): Spec 002 (Agentes IA) precisa expor `IAgentRuntime` para US8 funcionar. Caso esteja pendente, US8 fica em standby.
- Avoid: tarefas vagas, conflito no mesmo arquivo entre [P] tasks, dependências cruzadas que quebrem independência das stories.

---

## Resumo

| Fase | Tasks | Foco |
|---|---|---|
| 1. Setup | T001–T004 | Env, migration, pastas |
| 2. Foundational | T005–T020 | Domínio + presence + lock + WebSocket + DI (16 tasks) |
| 3. US1 (P1) — MVP | T021–T036 | CRUD de departamentos e atendentes (16 tasks) |
| 4. US2 (P1) | T037–T045 | Round-robin + lock + distribuição (9 tasks) |
| 5. US3 (P1) | T046–T048 | Manual pickup + testes de concorrência (3 tasks) |
| 6. US4 (P2) | T049–T052 | Transferência + recálculo de SLA (4 tasks) |
| 7. US5 (P2) | T053–T061 | Status + heartbeat + Hangfire timeout (9 tasks) |
| 8. US6 (P2) | T062–T071 | Canned responses + variáveis (10 tasks) |
| 9. US7 (P2) | T072–T074 | Transbordo refinado (3 tasks) |
| 10. US8 (P3) | T075–T084 | Sugestão IA com aprovação humana (10 tasks) |
| 11. US9 (P3) | T085–T089 | SLA visual + cálculo (5 tasks) |
| 12. Polish | T090–T096 | Integração Spec 004 + docs + perf + audit (7 tasks) |
| **Total** | **96 tasks** | |

**MVP** = Phases 1+2+3 (36 tasks até T036) — tenant operacional cadastra times e atendentes em produção.
