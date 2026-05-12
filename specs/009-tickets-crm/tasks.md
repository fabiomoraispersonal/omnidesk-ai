---
description: "Task list for Tickets / CRM (Pipeline Kanban) implementation"
---

# Tasks: Tickets / CRM (Pipeline Kanban)

**Input**: Design documents from `/specs/009-tickets-crm/`
**Prerequisites**: plan.md, spec.md, research.md (R1–R14), data-model.md, contracts/{tickets-api,contacts-api,pipelines-api,ticket-notes-events-api,ticket-creation-gateway,ticket-websocket-events,kanban-frontend-contract}.md, quickstart.md

**Tests**: Constituição §VII (Test Discipline) torna testes **obrigatórios**. Backend: xUnit + Testcontainers (Postgres + Redis + Mongo + MinIO reais — já configurados pelas Specs 007/008). CRM: Angular TestBed (`.spec.ts` co-localizado).

**Organization**: Tarefas agrupadas por user story para entrega independente. Esta spec **substitui** o scaffold de `tickets` da Spec 005 (status enum rewrite + 17 colunas novas) — por isso a Foundational é mais densa que de costume.

## Format: `[ID] [P?] [Story?] [Opus?] Description`

- **[P]**: Pode rodar em paralelo (arquivo distinto, sem dependência pendente)
- **[Story]**: Mapeia para user story (US1–US9) — ausente em Setup/Foundational/Polish
- **[Opus]**: Tarefa **complexa** — recomendado trocar para Claude Opus 4.7 durante a execução. São tasks com (a) concorrência crítica, (b) atomicidade multi-store (SQL + Mongo + Redis + WS), (c) migração de dados com risco de perda, ou (d) lógica de pausa/recálculo de SLA com side-effects encadeados. O resto pode (e deve, por custo/velocidade) rodar com **Sonnet 4.6**.
- Caminhos relativos do repo: `src/omniDesk.Api/...`, `src/omniDesk.Crm/...`

## Path Conventions

- Backend: `src/omniDesk.Api/{Domain,Features,Hubs,Infrastructure}/`
- Backend tests: `src/omniDesk.Api/tests/omniDesk.Api.Tests/{Domain,Features,Hubs,Infrastructure,Helpers}/`
- CRM Angular: `src/omniDesk.Crm/src/app/features/{tickets-kanban,ticket-detail,contacts,pipeline-config,live-chat-inbox}/`
- Migrations: `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` (single folder, padrão `Add_*.sql`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Chaves de configuração e estrutura de pastas.

- [x] T001 Adicionar em `src/omniDesk.Api/appsettings.json` e `src/omniDesk.Api/appsettings.Development.json` as chaves: `Tickets:SlaWarningThresholdPercent=80`, `Tickets:KanbanRefreshSeconds=30` (plan §Variáveis de configuração)
- [x] T002 [P] Criar estrutura backend: `src/omniDesk.Api/Domain/{Tickets,Contacts,Pipelines}/`, `src/omniDesk.Api/Features/{Tickets,Contacts,Pipelines}/{Commands,Queries,Validators,Notes}/`, `src/omniDesk.Api/Infrastructure/{Tickets,Jobs}/`. **NOTA**: `Domain/Tickets/` e `Infrastructure/Tickets/` já existem (scaffold Spec 005) — apenas expandir
- [x] T003 [P] Criar estrutura CRM: `src/omniDesk.Crm/src/app/features/{tickets-kanban,ticket-detail,contacts,pipeline-config}/{components,services}/`
- [x] T004 [P] Criar estrutura de teste: `src/omniDesk.Api/tests/omniDesk.Api.Tests/{Domain/{Tickets,Contacts,Pipelines},Features/{Tickets,Contacts,Pipelines,TicketCreationGateway,ConcurrentProtocolGeneration},Jobs}/`, `Helpers/{TicketTestHelpers.cs,FakeTicketEventStore.cs}` placeholders
- [x] T005 [P] Criar README curto em `src/omniDesk.Api/Features/Tickets/README.md` linkando ao plan, research e contratos

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Migrations, domain (rewrite do scaffold Spec 005), serviços compartilhados (protocolo, dedup, eventos), gateway de criação por IA, jobs base, RBAC, provisioning. Bloqueia TODAS as user stories.

**⚠️ CRITICAL**: Nenhuma user story pode começar antes desta fase completar. Em particular, o rename do enum `TicketStatus` quebra o código existente da Spec 005 — todas as referências têm de ser atualizadas neste bloco.

### Migrations SQL (ordem: Tickets → Contacts → TicketNotes → Pipelines → Visitors → Conversations)

- [x] T006 [Opus] Criar `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Tickets_FullModel.sql` conforme data-model.md §Migrations — ALTER `tickets` para renomear status (UPDATE map), adicionar 17 colunas novas, recriar CHECK constraints, renomear `assigned_attendant_id → attendant_id`, criar `search_vector` GENERATED + index GIN, unique parcial em `protocol`. Idempotente. **Por que Opus**: migração com data mapping (UPDATE de status) + reordenação atômica de DDL — erro aqui pode corromper dados de produção
- [x] T007 Criar `Add_Contacts.sql` — CREATE TABLE contacts + índices únicos parciais em `lower(email)` e `phone_normalized`, + FK `tickets.contact_id → contacts(id) ON DELETE SET NULL`
- [x] T008 Criar `Add_TicketNotes.sql` — CREATE TABLE ticket_notes + index `(ticket_id, created_at)`
- [x] T009 Criar `Add_Pipelines.sql` — CREATE TABLE pipelines + pipeline_columns + UNIQUE (pipeline_id, status_mapping); bootstrap INSERT 1 pipeline + 3 colunas por departamento existente
- [x] T010 [P] Criar `Add_ContactId_To_Visitors.sql` — ALTER visitors ADD COLUMN contact_id + FK + index
- [x] T011 [P] Criar `Add_TicketId_To_Conversations.sql` — ALTER conversations ADD COLUMN ticket_id + FK + index (substitui FK lógica comentada da Spec 007)
- [x] T012 [P] Coordenar com Spec 007 a adição de coluna `content_tsv tsvector GENERATED ... STORED` + GIN em `conversation_messages` (migration `Add_Messages_SearchVector.sql`). Se não estiver disponível ao deploy, busca degrada graciosamente (FR-038)

### Domain — Tickets (rewrite do scaffold)

- [x] T013 Reescrever `src/omniDesk.Api/Domain/Tickets/Ticket.cs` para o modelo V2 (todos os 23+ campos do data-model.md §1 Ticket). Substituir scaffold da Spec 005 in-place
- [x] T014 [P] Reescrever `src/omniDesk.Api/Domain/Tickets/TicketStatus.cs` — enum `New/InProgress/WaitingClient/Resolved/Cancelled` + `ToWireValue`/`IsTerminal`/`IsActive` extensions (data-model.md §Domain Model)
- [x] T015 [P] Criar `src/omniDesk.Api/Domain/Tickets/TicketPriority.cs` — enum `Low/Normal/High/Urgent` + extensions
- [x] T016 [P] Criar `src/omniDesk.Api/Domain/Tickets/TicketChannel.cs` — enum `LiveChat/WhatsApp/Manual` + extensions
- [x] T017 [P] Criar `src/omniDesk.Api/Domain/Tickets/TicketEventType.cs` — const set para Mongo: TicketCreated, AttendantAssigned, StatusChanged, Transferred, PriorityChanged, SubjectChanged, TagAdded, TagRemoved, NoteAdded, SlaBreached, TicketResolved, TicketCancelled (data-model.md §Domain Model)
- [x] T018 [P] Criar `src/omniDesk.Api/Domain/Tickets/TicketNote.cs` — entity append-only
- [x] T019 [P] Criar `src/omniDesk.Api/Domain/Tickets/TicketEvent.cs` — value object para Mongo writes (campos do data-model.md §8)
- [x] T020 [P] Criar `src/omniDesk.Api/Domain/Tickets/ITicketRepository.cs`, `ITicketNoteRepository.cs` (sem Update/Delete — apenas Add + ListByTicket — FR-042), `ITicketEventStore.cs`

### Domain — Contacts

- [x] T021 [P] Criar `src/omniDesk.Api/Domain/Contacts/Contact.cs` — entity (data-model.md §2)
- [x] T022 [P] Criar `src/omniDesk.Api/Domain/Contacts/ContactSourceChannel.cs` — enum LiveChat/WhatsApp/Manual
- [x] T023 [P] Criar `src/omniDesk.Api/Domain/Contacts/PhoneNormalizer.cs` — static, heurística BR (R5): remove não-dígitos, valida ≥8 dígitos, prefixa 55 se 10–11 dígitos
- [x] T024 [P] Criar `src/omniDesk.Api/Domain/Contacts/IContactRepository.cs`

### Domain — Pipelines

- [x] T025 [P] Criar `src/omniDesk.Api/Domain/Pipelines/Pipeline.cs` — entity (data-model.md §4)
- [x] T026 [P] Criar `src/omniDesk.Api/Domain/Pipelines/PipelineColumn.cs` — entity (data-model.md §5)
- [x] T027 [P] Criar `src/omniDesk.Api/Domain/Pipelines/PipelineDefaults.cs` — static com 3 colunas default ("Na Fila"/new/1, "Em Andamento"/in_progress/2, "Aguardando Cliente"/waiting_client/3) — sem magic strings
- [x] T028 [P] Criar `src/omniDesk.Api/Domain/Pipelines/PipelineStatusMapping.cs` — validador estático (rejeita duplicatas, exige exatamente 3 colunas, mapeamentos válidos)
- [x] T029 [P] Criar `src/omniDesk.Api/Domain/Pipelines/IPipelineRepository.cs`

### Domain — Modifications em entidades existentes

- [x] T030 Adicionar campo `Guid? ContactId` em `src/omniDesk.Api/Domain/LiveChat/Visitor.cs` (Spec 007) + propagar setter no construtor/atualização
- [x] T031 Adicionar campo `Guid? TicketId` em `src/omniDesk.Api/Domain/LiveChat/Conversation.cs` (Spec 007) + propagar

### Infrastructure — EF Configurations

- [x] T032 Reescrever `src/omniDesk.Api/Infrastructure/Tickets/TicketConfiguration.cs` para mapear todos os 23+ campos + `tags text[]`/`tsvector`/`deleted_at` + relacionamentos para `Contact` e `Conversation`
- [x] T033 [P] Criar `src/omniDesk.Api/Infrastructure/Tickets/TicketNoteConfiguration.cs` — append-only mapping
- [x] T034 [P] Criar `src/omniDesk.Api/Infrastructure/Contacts/ContactConfiguration.cs` — mapping + `source_channels text[]`
- [x] T035 [P] Criar `src/omniDesk.Api/Infrastructure/Pipelines/PipelineConfiguration.cs`
- [x] T036 [P] Criar `src/omniDesk.Api/Infrastructure/Pipelines/PipelineColumnConfiguration.cs` + escape de "order" (palavra reservada)
- [x] T037 [P] Atualizar `AppDbContext` (`Infrastructure/Persistence/AppDbContext.cs`) — adicionar `DbSet<Contact>`, `DbSet<TicketNote>`, `DbSet<Pipeline>`, `DbSet<PipelineColumn>`; ajustar `DbSet<Ticket>` (rewrite); aplicar configurations no `OnModelCreating`
- [x] T038 [P] Criar repositories: `TicketRepository.cs`, `ContactRepository.cs`, `PipelineRepository.cs` (apenas membros básicos — `GetById`, `Add`, `ListPaged`; queries específicas vêm nas user stories)

### Infrastructure — Serviços Core

- [x] T039 [Opus] Criar `src/omniDesk.Api/Infrastructure/Tickets/TicketProtocolService.cs` implementando algoritmo R1: data UTC → `nextval('tenant_{slug}.ticket_protocol_seq_YYYYMMDD')` com `CREATE SEQUENCE IF NOT EXISTS` em transação SERIALIZABLE no first-of-day. Formata `TK-{YYYYMMDD}-{nextval:D5}`. Inject `ITenantSlugAccessor`. **Por que Opus**: concorrência crítica com DDL on-demand + retry em deadlock SERIALIZABLE; SC-004 exige 0 colisões em 100 inserções paralelas
- [x] T040 Criar `src/omniDesk.Api/Infrastructure/Tickets/MongoTicketEventStore.cs` implementando `ITicketEventStore.AppendAsync(TicketEvent)` — writes em `{slug}_ticket_events` collection. Inject `IMongoClient` + `ITenantSlugAccessor`. Logs Serilog
- [x] T041 Criar `src/omniDesk.Api/Infrastructure/WebSockets/TicketEventPublisher.cs` — encapsula publish em `{slug}:crm:dept:{department_id}` + `{slug}:crm:supervisor` (R6). Métodos: `PublishCreatedAsync`, `PublishAssignedAsync`, `PublishStatusChangedAsync`, `PublishTransferredAsync`, `PublishSlaWarningAsync`, `PublishSlaBreachedAsync`. Inject `IConnectionMultiplexer`
- [x] T042 [P] Criar `src/omniDesk.Api/Hubs/Events/TicketCrmEvents.cs` — const set: `TicketCreated="ticket.created"`, `TicketAssigned`, `TicketStatusChanged`, `TicketTransferred`, `TicketSlaWarning`, `TicketSlaBreached` (sem magic strings — Princípio VII)
- [x] T043 Criar `src/omniDesk.Api/Features/Tickets/SlaPauseCalculator.cs` — static: dado `waiting_client_since`, `sla_paused_duration_minutes`, `sla_resolution_deadline`, calcula `EffectiveDeadline(now)`, `PercentConsumed(now)`. Cobertura: pausa em andamento + pausas acumuladas
- [x] T044 Criar `src/omniDesk.Api/Features/Tickets/TicketSubjectAutogen.cs` — gera subject das primeiras 100 chars da última mensagem; fallback "Atendimento via {canal}" para mídia sem texto (FR-040 + edge case do spec)
- [x] T045 [Opus] Criar `src/omniDesk.Api/Features/Contacts/ContactDeduplicationService.cs` implementando R9 — Redis lock `{slug}:contact:dedup:lock:{email_hash|phone_normalized}` TTL 3s; query por email lower OR phone_normalized; merge campos vazios; append source_channels; fallback se Redis indisponível (cria + retry no unique constraint). Inject `IConnectionMultiplexer`, `IContactRepository`. **Por que Opus**: race conditions sutis (lock + DB unique + fallback de duas camadas); FR-026/027 + SC-007 dependem disso

### Atualização do contrato de Handoff (Spec 006)

- [x] T046 Atualizar `src/omniDesk.Api/Features/AgentRuntime/ITicketCreationGateway.cs` adicionando `TicketChannel Channel`, `ContactHints? ContactHints`, `string? SubjectSuggestion` ao `TicketHandoffRequest`; estender `TicketHandoffResult` com `Protocol`, `AttendantId?`, `ContactId?` (contracts/ticket-creation-gateway.md)
- [x] T047 [Opus] Criar `src/omniDesk.Api/Features/Tickets/TicketCreationGateway.cs` (implementação real, substitui StubTicketCreationGateway): orquestra dedup contato → protocolo → SLA inicial → INSERT ticket → assignment (TicketAssignmentService) → snapshot history → update conversation.ticket_id → Mongo events `ticket_created` (+ `attendant_assigned` se atribuído) → WS events. Transação SQL atômica; side-effects pós-commit (R11). **Por que Opus**: 11 passos com 4 stores (PG + Mongo + Redis + WS) e falha em qualquer side-effect pós-commit deve logar sem reverter — design de atomicidade complexa
- [x] T048 Mover `src/omniDesk.Api/Infrastructure/AgentRuntime/StubTicketCreationGateway.cs` → `src/omniDesk.Api/Infrastructure/AgentRuntime/_Obsolete/StubTicketCreationGateway.cs` + comentário "/// OBSOLETE: Replaced by TicketCreationGateway in Spec 009. Kept for rollback in V1.0; remove in V1.1."
- [x] T049 Atualizar registro DI em `Program.cs` — `services.AddScoped<ITicketCreationGateway, TicketCreationGateway>();` (era StubTicketCreationGateway)
- [x] T050 Atualizar chamador em Spec 006 (`AgentOrchestrator.HandleHandoffAsync` ou equivalente) para preencher os novos campos do `TicketHandoffRequest`: `Channel` (vem da conversa), `ContactHints` (do visitor: email/phone/name), `SubjectSuggestion` (`TicketSubjectAutogen.Generate(history)`)

### Distribution — adaptação para os novos status

- [x] T051 Atualizar `src/omniDesk.Api/Features/Distribution/TicketAssignmentService.cs`: substituir `TicketStatus.Queued` por `New`, `Assigned` por `InProgress`. Ao atribuir: preencher `assigned_at`, `sla_first_response_deadline = assigned_at + dept.sla_first_response_minutes`, transição `new → in_progress`. Adicionar publish `ticket.assigned` via `TicketEventPublisher`
- [x] T052 Atualizar `src/omniDesk.Api/Features/Distribution/PickupTicketEndpoint.cs` — substituir nomes de status; usar novos códigos de erro (`TICKET_NOT_FOUND`, `TICKET_ALREADY_CLOSED` em vez de `Closed`)
- [x] T053 Atualizar `src/omniDesk.Api/Features/Distribution/TransferTicketEndpoint.cs` — delega para `Features/Tickets/Commands/TransferTicketCommand` (criado em US4); marcar com `[Obsolete]` se o endpoint Spec 005 vai migrar para `/api/tickets/{id}/transfer` (decidir junto com mantenedor de 005)
- [x] T054 Atualizar `src/omniDesk.Api/Features/Distribution/SlaCalculator.cs` — estender para considerar pausa em `waiting_client`: usa `SlaPauseCalculator` para computar prazo efetivo
- [x] T055 Atualizar `src/omniDesk.Api/Features/Distribution/AttendantAvailabilityHandler.cs` — prioriza tickets `new` (não `queued`) na fila do depto; respeita `max_simultaneous_chats`

### Provisioning — Pipelines automáticos

- [x] T056 Criar `src/omniDesk.Api/Features/Pipelines/PipelineProvisioningService.cs` — método `EnsurePipelineForDepartmentAsync(Guid departmentId)`: cria pipeline + 3 colunas default (`PipelineDefaults.DefaultColumns`) se ainda não existir. Idempotente
- [x] T057 Atualizar `src/omniDesk.Api/Infrastructure/Provisioning/TenantProvisioningJob.cs` (Spec 003) — após criar cada departamento, chama `PipelineProvisioningService.EnsurePipelineForDepartmentAsync(dept.Id)`
- [x] T058 Atualizar `src/omniDesk.Api/Features/Departments/Commands/CreateDepartmentCommand.cs` (Spec 005) — após inserir departamento, chama `PipelineProvisioningService.EnsurePipelineForDepartmentAsync(dept.Id)` — assim novos depts ganham pipeline automaticamente

### Backfill Jobs

- [x] T059 Criar `src/omniDesk.Api/Infrastructure/Jobs/BackfillTicketProtocolJob.cs` (one-shot Hangfire) — varre `tickets WHERE protocol IS NULL`, gera protocolo retroativo usando `created_at` (data UTC) via sequence per-tenant per-data, persiste. Idempotente. Loga progresso
- [x] T060 Criar `src/omniDesk.Api/Features/Contacts/ContactBackfillJob.cs` (one-shot Hangfire) — varre `visitors WHERE email IS NOT NULL OR phone IS NOT NULL AND contact_id IS NULL`, aplica `ContactDeduplicationService.FindOrCreateContact`, popula `visitor.contact_id`. Idempotente
- [x] T061 [P] Registrar ambos os jobs como manual triggers (sem cron) em `Program.cs` ou módulo Hangfire — operador roda via dashboard ou comando admin pós-deploy

### RBAC

- [x] T062 [P] Criar helper `src/omniDesk.Api/Features/Tickets/TicketAccessPolicy.cs` — `bool CanAccessTicket(Ticket t, ClaimsPrincipal user)`: `tenant_admin`/`supervisor` = true; `tenant_attendant` = true se `t.department_id ∈ attendant.department_ids`. Usado em endpoints e WS subscription

### Testes Foundational

- [x] T063 [P] Criar `tests/.../Domain/Tickets/TicketStatusTransitionsTests.cs` — cobertura de transições válidas e inválidas (data-model.md §Transições). xUnit Theory
- [x] T064 [P] Criar `tests/.../Domain/Tickets/SlaPauseCalculatorTests.cs` — pausa única, pausa multi-ciclo, pausa em andamento; usar `FakeTimeProvider`
- [x] T065 [P] Criar `tests/.../Domain/Contacts/PhoneNormalizerTests.cs` — BR formats: "(11) 99999-9999", "11999999999", "+55 11 99999-9999"; edge cases (vazio, muito curto, internacional sem 55)
- [x] T066 [P] Criar `tests/.../Domain/Pipelines/PipelineStatusMappingTests.cs` — rejeita duplicatas; rejeita ≠ 3 colunas; aceita reordenação
- [x] T067 [P] Criar `tests/.../Features/ConcurrentProtocolGeneration/ConcurrentProtocolGenerationTests.cs` — 100 inserções paralelas em mesmo tenant/dia → 100 protocolos únicos (Testcontainers Postgres)
- [x] T068 [P] Criar `tests/.../Infrastructure/Tickets/MongoTicketEventStoreTests.cs` — append + leitura via Mongo (Testcontainers)
- [x] T069 [P] Criar `tests/.../Infrastructure/Persistence/Migrations/Add_Tickets_FullModel_DataMigrationTests.cs` — fixture: 5 rows com status antigos → executa migration → assert: status mapeado (queued→new, etc.), `protocol IS NULL` antes do backfill
- [x] T070 [P] Criar `tests/.../Helpers/TicketTestHelpers.cs` — fixtures: `CreateTenantWithDeptsAndAttendants`, `CreateTicket(...)`, `CreateContact(...)`, `CreatePipeline(...)`
- [x] T071 [P] Criar `tests/.../Helpers/FakeTicketEventStore.cs` — `ITicketEventStore` em memória; capture-and-assert API para testes

**Checkpoint**: Foundational completa. Schema migrado, gateway substituído, domínio reescrito. User stories podem começar **em paralelo**.

---

## Phase 3: User Story 1 — Abertura automática de ticket por transbordo da IA (Priority: P1) 🎯 MVP

**Goal**: Quando a IA chama `transfer_to_human`, sistema cria ticket completo (protocolo, SLA, contact dedup, round-robin) e envia atendente para `ticket-detail` com histórico completo.

**Independent Test**: QS1 — disparar handoff em conversa Live Chat com visitor identificado por e-mail; verificar criação do ticket em ≤2s com protocolo no formato correto, atribuição round-robin a atendente online, histórico completo acessível.

### Tests for User Story 1

- [x] T072 [P] [US1] `tests/.../Features/TicketCreationGateway/TicketCreationGatewayTests.cs` — handoff cria ticket com `protocol`, `channel`, `subject` autogen, `contact_id` dedupado, atribuição round-robin, eventos Mongo e WS
- [x] T073 [P] [US1] `tests/.../Features/TicketCreationGateway/TicketCreationGateway_ContactDedupTests.cs` — visitor com email existente reaproveita contact_id; visitor sem hints → contact_id null
- [x] T074 [P] [US1] `tests/.../Features/TicketCreationGateway/TicketCreationGateway_NoAvailableAttendantTests.cs` — sem atendente online → ticket `new` com `attendant_id = null`, sem evento `ticket.assigned`
- [x] T075 [P] [US1] `tests/.../Features/Distribution/AttendantOnlineQueuePickupTests.cs` — atendente fica online → ticket mais antigo do depto é atribuído automaticamente (QS2)

### Implementação User Story 1

- [x] T076 [US1] [Opus] Implementar lógica completa em `TicketCreationGateway.cs` (já criado em T047) — fluxo dos 11 passos do contrato. Garantir transação SQL atômica e side-effects pós-commit (Mongo + Redis + WS) com try/catch que loga mas não reverte. **Por que Opus**: continuação de T047 — manter coerência de design durante a implementação completa
- [x] T077 [US1] Em `OutgoingMessageWorker` (Spec 006), adicionar lógica: ao processar mensagem `sender_type = attendant` em conversa com ticket vinculado sem `first_response_at`, preencher `tickets.first_response_at = message.sent_at` + emit WS event opcional
- [x] T078 [US1] Atualizar `AttendantAvailabilityHandler` (já estendido em T055) para emitir `ticket.assigned` via `TicketEventPublisher` ao atribuir ticket da fila
- [x] T079 [P] [US1] Criar hook de notificação (chamada para Spec 010 quando implementada) em `TicketCreationGateway` após atribuir: `INotificationService.NotifyTicketAssignedAsync(attendantId, ticketId)` — em V1 stub (no-op) com TODO

**Checkpoint US1**: Ticket nasce do handoff IA com todos os atributos. Falta a UI (US2) para tornar visível ao atendente.

---

## Phase 4: User Story 2 — Atendente atua nos tickets pelo Kanban (Priority: P1) 🎯 MVP

**Goal**: Atendente abre CRM em `/kanban`, vê tickets do seu depto distribuídos por status, clica em um card → ticket-detail com histórico e ações (responder, mudar status via drag-drop, encerrar).

**Independent Test**: QS3 — atendente arrasta card de "Em Andamento" para "Aguardando Cliente"; verifica status mudou no banco + evento WS recebido; cliente envia mensagem → card volta automaticamente.

### Backend — Endpoints e Queries

- [x] T080 [P] [US2] Criar `src/omniDesk.Api/Features/Tickets/Queries/ListTicketsQuery.cs` — filtros (department, attendant incluindo null, channel, priority, tags multi, period), paginação 20/pg, ordenação por `created_at desc`. Default exclui `resolved`/`cancelled` (`include_terminal=false`). Respeita `TicketAccessPolicy`
- [x] T081 [P] [US2] Criar `src/omniDesk.Api/Features/Tickets/Queries/GetTicketDetailQuery.cs` — retorna ticket + conversa + mensagens + notes + contato + SLA computado. Valida acesso via `TicketAccessPolicy`
- [x] T082 [P] [US2] Criar `src/omniDesk.Api/Features/Tickets/Commands/ChangeTicketStatusCommand.cs` — valida transição (ChangeStatusValidator), aplica side-effects (`waiting_client_since` set/unset, somar pausa), persiste, Mongo event `status_changed`, WS event `ticket.status_changed`
- [x] T083 [P] [US2] Criar `src/omniDesk.Api/Features/Tickets/Validators/ChangeStatusValidator.cs` (FluentValidation) — matriz de transições da data-model.md §Transições
- [x] T084 [P] [US2] Criar `src/omniDesk.Api/Features/Tickets/Commands/ResolveTicketCommand.cs` — preenche `resolved_at`, calcula pausa final, atualiza conversation em cascata (`conversation.status = resolved`), reseta `has_reminder_alert`. Mongo event `ticket_resolved`. Side-effect: encerra conversa via `EndConversationCommand` (Spec 007)
- [x] T085 [P] [US2] Criar `src/omniDesk.Api/Features/Tickets/Commands/CancelTicketCommand.cs` — preenche `cancelled_at`. **NÃO** atualiza conversa. Reseta `has_reminder_alert`. Mongo event `ticket_cancelled`
- [x] T086 [P] [US2] Criar `src/omniDesk.Api/Features/Tickets/Commands/UpdateTicketCommand.cs` — edita `subject`, `priority`, `tags`. Eventos Mongo correspondentes (`subject_changed`, `priority_changed`, `tag_added`/`tag_removed` por delta). Rejeita se `IsTerminal()`
- [x] T087 [US2] Criar `src/omniDesk.Api/Features/Tickets/TicketEndpoints.cs` — mapeia:
  - `GET /api/tickets` (ListTicketsQuery)
  - `GET /api/tickets/{id}` (GetTicketDetailQuery)
  - `PUT /api/tickets/{id}` (UpdateTicketCommand)
  - `PATCH /api/tickets/{id}/status` (ChangeTicketStatusCommand)
  - `POST /api/tickets/{id}/resolve` (ResolveTicketCommand)
  - `POST /api/tickets/{id}/cancel` (CancelTicketCommand)
  - Todos com `RequireAuthorization` + validação por `TicketAccessPolicy`. Envelopes per Spec 001
- [x] T088 [P] [US2] Criar `src/omniDesk.Api/Features/Tickets/Notes/TicketNotesEndpoints.cs` mapeando `POST /api/tickets/{id}/notes` e `GET /api/tickets/{id}/notes`. `POST` chama `AddTicketNoteCommand` (criar em T089)
- [x] T089 [P] [US2] Criar `src/omniDesk.Api/Features/Tickets/Notes/AddTicketNoteCommand.cs` — INSERT em ticket_notes + Mongo event `note_added` (apenas `note_id`). FluentValidator (content 1–10000 chars). **NÃO** emite WS event (notas são privadas)
- [x] T090 [US2] Registrar endpoints no `Program.cs`: `app.MapGroup("/api/tickets").MapTicketEndpoints().RequireAuthorization();`

### Frontend — Kanban

- [x] T091 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/services/tickets.service.ts` — signal store + HTTP (GET list, GET detail, PATCH status, POST resolve/cancel). Reactive cache atualizado por eventos WS
- [x] T092 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/services/kanban-websocket.service.ts` — extends `crm-websocket.service` (Spec 007), consome 6 eventos novos (R6 + contracts/ticket-websocket-events.md), aplica diff no signal store
- [x] T093 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/components/sla-badge.component.ts` — calcula cor (🟢🟡🔴) via signal computed a partir de `sla_resolution_deadline + paused_minutes` + tick 1s
- [x] T094 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/components/ticket-card.component.ts` — anatomia do contracts/kanban-frontend-contract.md §2: ícone canal + protocolo + badges + nome contato + subject truncado + tags (≤3 + contador) + atendente + tempo desde criação
- [x] T095 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/components/reminder-alert-badge.component.ts` — exibe ⚠️ se `has_reminder_alert=true`
- [x] T096 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/components/kanban-column.component.ts` — header com nome + contagem; lista de cards com `cdkDropList` (PrimeNG/CDK); empty state
- [x] T097 [US2] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/tickets-kanban.component.ts` (standalone, lazy) — orquestra: filtros + 3 colunas + service WS + `cdkDropListGroup`. `onDrop`: PATCH /status com optimistic update + rollback em erro
- [x] T098 [US2] Criar `tickets-kanban.component.html` + `.scss` — layout per contracts/kanban-frontend-contract.md §1. Skeleton loader durante GET inicial
- [x] T099 [US2] Atualizar `src/omniDesk.Crm/src/app/app.routes.ts` — adicionar rotas: `/` redirect para `/kanban`; `/kanban` → lazy `tickets-kanban`; `/tickets/:id` → lazy `ticket-detail` (criado em T103); `/conversations` mantém (Spec 007)

### Frontend — Ticket Detail

- [x] T100 [P] [US2] Extrair `conversation-timeline.component.ts` de `features/live-chat-inbox/components/` (Spec 007) para `src/omniDesk.Crm/src/app/shared/components/conversation-timeline/` — reutilizável por Live Chat Inbox e Ticket Detail. Suporta renderização agnóstica (mensagens IA + atendente + cliente + handoff divider per R11)
- [x] T101 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/ticket-detail/components/internal-notes-section.component.ts` — colapsável, cor de fundo distinta, header "🔒 Anotações internas — não visíveis ao cliente"; lista cronológica + textarea + botão "Adicionar"
- [x] T102 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/ticket-detail/components/ticket-side-panel.component.ts` — painel direito: status editável inline, prioridade, tags, SLA countdown, dados contato (link para `/contacts/{id}`), botões Transferir/Encerrar/Cancelar
- [x] T103 [US2] Criar `src/omniDesk.Crm/src/app/features/ticket-detail/ticket-detail.component.ts` (standalone, lazy) — layout 2 painéis. Painel esquerdo: timeline + notes + campo de resposta (reusa pattern Spec 007). Painel direito: side-panel. Inject `tickets.service` + `ticket-detail.service`
- [x] T104 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/ticket-detail/components/inline-status-editor.component.ts` — dropdown com transições válidas (calculado client-side); aciona PATCH /status
- [x] T105 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/ticket-detail/components/inline-priority-editor.component.ts` + `tags-editor.component.ts` — edição inline com debounce
- [x] T106 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/ticket-detail/services/ticket-detail.service.ts` — GET detail + signal stream
- [x] T107 [US2] Atualizar `live-chat-inbox.component.ts` (Spec 007) — ao clicar em conversa com `ticket_id` preenchido, navegar para `/tickets/{ticket_id}` (não abrir detail interno do LCI). Conversas sem ticket continuam com detalhe inline

### Tests for User Story 2

- [x] T108 [P] [US2] `tests/.../Features/Tickets/ListTicketsQueryTests.cs` — filtros isolados, RBAC por depto, paginação, ordenação
- [x] T109 [P] [US2] `tests/.../Features/Tickets/ChangeTicketStatusCommandTests.cs` — transições válidas e inválidas; side-effects (waiting_client_since, pausa SLA); evento Mongo + WS
- [x] T110 [P] [US2] `tests/.../Features/Tickets/ResolveTicketCommandTests.cs` — cascade para conversation; reset has_reminder_alert; pausa final calculada se em waiting_client
- [x] T111 [P] [US2] `tests/.../Features/Tickets/UpdateTicketCommandTests.cs` — eventos Mongo `subject_changed`/`priority_changed`/`tag_added`/`tag_removed`; rejeita se IsTerminal
- [x] T112 [P] [US2] `tests/.../Features/Tickets/Notes/AddTicketNoteCommandTests.cs` — content válido/inválido; Mongo event `note_added` apenas com `note_id`
- [x] T113 [P] [US2] `src/omniDesk.Crm/.../tickets-kanban.component.spec.ts` — drag-drop simulation; filtros aplicados; WS event recebido aciona update
- [x] T114 [P] [US2] `src/omniDesk.Crm/.../ticket-detail.component.spec.ts` — render timeline; toggle notes; inline editors

**Checkpoint US2**: Atendente atua nos tickets full-cycle. MVP funcional (US1+US2): cliente → IA → ticket → atendente trabalha → encerra.

---

## Phase 5: User Story 3 — SLA com pausa, badge visual e alertas (Priority: P2)

**Goal**: Sistema emite warning ao cruzar 80% do prazo e breach ao expirar (per tipo: first_response e resolution). Pausa em waiting_client. CRM mostra badge colorido e toasts.

**Independent Test**: QS4 — esperar 80% do prazo SLA; verificar badge amarelo + WS warning. Esperar expiração; badge vermelho + WS breach + evento Mongo.

### Backend

- [x] T115 [P] [US3] [Opus] Criar `src/omniDesk.Api/Infrastructure/Jobs/TicketSlaMonitorJob.cs` — cron `* * * * *` Hangfire. Varre tickets ativos por tenant (per-schema query), calcula consumo via `SlaPauseCalculator`, emite warning/breach idempotente via Redis flags `{slug}:ticket:{id}:sla_warned:{type}` (TTL 24h). Persiste `sla_breached` em Mongo. Inject `IConnectionMultiplexer`, `ITicketEventStore`, `ITicketEventPublisher`. Configurable threshold via `Tickets:SlaWarningThresholdPercent`. **Por que Opus**: idempotência multi-tenant + cálculo de pausa correto + per-schema loop; SC-010 exige rigor temporal
- [x] T116 [P] [US3] Criar `src/omniDesk.Api/Infrastructure/Jobs/WaitingClientResumerJob.cs` — sob demanda (enfileirado pelo `IncomingMessageWorker` quando mensagem chega em conversa com ticket `waiting_client`). Calcula pausa, soma a `sla_paused_duration_minutes`, zera `waiting_client_since`, transição `→ in_progress`, eventos Mongo + WS
- [x] T117 [US3] Modificar `IncomingMessageWorker` (Spec 006) para enfileirar `WaitingClientResumerJob` quando detecta mensagem do cliente em conversa cujo ticket está `waiting_client`
- [x] T118 [US3] Registrar `TicketSlaMonitorJob` no startup Hangfire (`Program.cs`): `RecurringJob.AddOrUpdate<TicketSlaMonitorJob>("sla-monitor", j => j.RunAsync(), Cron.Minutely())`. **Importante**: monitor deve iterar todos os tenants (similar ao `TenantMetricsCollectorJob`)

### Frontend

- [x] T119 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/ticket-detail/components/sla-countdown.component.ts` — contador regressivo "Restam: 1h 23min" via `date-fns`. Atualiza via signal + interval(1000)
- [x] T120 [US3] Adicionar handlers em `kanban-websocket.service.ts` (T092) para `ticket.sla_warning` e `ticket.sla_breached` — disparam toast e atualizam badge no card

### Tests for User Story 3

- [x] T121 [P] [US3] `tests/.../Jobs/TicketSlaMonitorJobTests.cs` — usar `FakeTimeProvider`: ticket criado → avança 80% → verifica WS warning emitido + Redis flag setada; avança até expirar → WS breach + Mongo event. Re-run não duplica
- [x] T122 [P] [US3] `tests/.../Jobs/WaitingClientResumerJobTests.cs` — ticket em waiting_client há 30min → cliente envia mensagem → job calcula pausa (30min), soma, zera waiting_client_since, transição para in_progress
- [x] T123 [P] [US3] `tests/.../Features/Tickets/Sla_PauseLifecycleTests.cs` — múltiplas pausas (waiting → in_progress → waiting → in_progress); `sla_paused_duration_minutes` acumula corretamente
- [x] T124 [P] [US3] `src/omniDesk.Crm/.../sla-countdown.component.spec.ts` — renderização correta + atualização em tempo real

**Checkpoint US3**: SLA tracking completo. Atendente vê alertas e badges; sistema persiste breach em Mongo para auditoria.

---

## Phase 6: User Story 4 — Transferência entre atendentes/departamentos (Priority: P2)

**Goal**: Atendente transfere ticket para outro atendente ou depto. Histórico preservado; SLA recalculado em transferência inter-departamento; nota automática se contexto preenchido.

**Independent Test**: QS5 — Maria transfere ticket Comercial para Financeiro; verifica ticket some do Kanban Comercial, aparece em Financeiro com SLA recalculado, pausa zerada, evento `transferred` em Mongo.

### Backend

- [x] T125 [P] [US4] [Opus] Criar `src/omniDesk.Api/Features/Tickets/Commands/TransferTicketCommand.cs` — validação (target_attendant_id XOR target_department_id; mesmo tenant; ticket não terminal); side-effects: muda `attendant_id`/`department_id`; se mudou depto: recalcula SLAs + zera `sla_paused_duration_minutes`; preserva `first_response_at`; cria `ticket_note` automática se `note` preenchida; Mongo event `transferred`; WS event. **Por que Opus**: ≥6 side-effects condicionados + recálculo de SLA com preservação seletiva; FR-016 + SC-012 dependem
- [x] T126 [P] [US4] Criar `src/omniDesk.Api/Features/Tickets/Validators/TransferTicketValidator.cs` — request shape conforme contracts/tickets-api.md §POST /transfer
- [x] T127 [US4] Adicionar `POST /api/tickets/{id}/transfer` em `TicketEndpoints.cs` (T087) — handler chama `TransferTicketCommand`
- [x] T128 [US4] Adicionar `PATCH /api/tickets/{id}/attendant` em `TicketEndpoints.cs` — handler simples que chama `TransferTicketCommand` com `target_type=attendant`. Use case: reatribuição rápida sem dialog

### Frontend

- [x] T129 [P] [US4] Criar `src/omniDesk.Crm/src/app/features/ticket-detail/components/transfer-dialog.component.ts` — dialog PrimeNG: dropdown depto + dropdown atendente (dinâmico baseado em depto) ou "Fila" + textarea de nota + botão confirmar
- [x] T130 [US4] Integrar dialog em `ticket-side-panel.component.ts` — botão "Transferir" abre dialog; ao confirmar, chama `tickets.service.transfer(id, payload)`
- [x] T131 [P] [US4] Adicionar handler em `kanban-websocket.service.ts` para `ticket.transferred` — remove card se ticket saiu do depto do atendente; adiciona se entrou

### Tests for User Story 4

- [x] T132 [P] [US4] `tests/.../Features/Tickets/TransferTicketCommandTests.cs` — cenários: transferência intra-depto (sem recalc SLA), inter-depto (recalc + zera pausa), para fila (attendant=null), com nota (cria ticket_note), com ticket terminal (409)
- [x] T133 [P] [US4] `src/omniDesk.Crm/.../transfer-dialog.component.spec.ts` — render + validação + submit

**Checkpoint US4**: Tickets podem fluir entre atendentes e departamentos sem perder histórico ou contexto.

---

## Phase 7: User Story 5 — Criação manual de ticket por atendente (Priority: P2)

**Goal**: Atendente abre dialog "+ Novo Ticket", busca/cria contato, preenche dados, opcionalmente atribui a si. Cria ticket `channel=manual` sem conversa.

**Independent Test**: QS6 — Maria cria ticket manual com contato novo "Júlia Pereira"; verifica ticket no Kanban + contato criado com phone_normalized. Repete com mesmo telefone → contato reutilizado (dedup).

### Backend

- [x] T134 [P] [US5] Criar `src/omniDesk.Api/Features/Tickets/Commands/CreateManualTicketCommand.cs` — orquestra: dedup contato (se hints fornecidos) → cria ticket com `channel=manual` → protocolo via TicketProtocolService → assign_to_me ou round-robin → eventos
- [x] T135 [P] [US5] Criar `src/omniDesk.Api/Features/Tickets/Validators/CreateManualTicketValidator.cs` — exige `department_id`, `subject`; valida contact aninhado (nome OU email OU phone)
- [x] T136 [US5] Adicionar `POST /api/tickets` em `TicketEndpoints.cs` (handler chama CreateManualTicketCommand)

### Frontend

- [x] T137 [P] [US5] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/components/new-ticket-dialog.component.ts` — dialog PrimeNG: autocomplete contato (busca em `/api/contacts?q=`) + form contato novo (collapsible) + form ticket (depto, subject, priority, tags, assign_to_me)
- [x] T138 [US5] Integrar dialog no `tickets-kanban.component.ts` — botão "+ Novo Ticket" abre dialog; ao confirmar, redireciona para `/tickets/{novo_id}`

### Tests for User Story 5

- [x] T139 [P] [US5] `tests/.../Features/Tickets/CreateManualTicketCommandTests.cs` — fluxos: com contact existente, com contact novo (dedup), com `assign_to_me=true`, com `assign_to_me=false` + round-robin, sem atendente disponível (fica `new`)
- [x] T140 [P] [US5] `src/omniDesk.Crm/.../new-ticket-dialog.component.spec.ts`

**Checkpoint US5**: Atendimentos offline (telefone, presencial) entram no funil formal.

---

## Phase 8: User Story 6 — Perfil de contato com dedup e histórico (Priority: P2)

**Goal**: Atendente clica no nome do contato no ticket-detail → abre `/contacts/{id}` com dados editáveis + abas paginadas (Tickets + Conversas).

**Independent Test**: QS7 — contato com 25 tickets antigos; atendente navega para perfil; verifica paginação 20/pg, ordem decrescente, links para tickets/conversas antigas funcionais.

### Backend — Endpoints

- [x] T141 [P] [US6] Criar `src/omniDesk.Api/Features/Contacts/Queries/ListContactsQuery.cs` — paginação, busca por nome/email/phone via ILIKE + agregados (tickets_count, last_interaction_at)
- [x] T142 [P] [US6] Criar `src/omniDesk.Api/Features/Contacts/Queries/GetContactQuery.cs` — detalhe + agregados (tickets_count, conversations_count, last_interaction_at)
- [x] T143 [P] [US6] Criar `src/omniDesk.Api/Features/Contacts/Queries/ListContactTicketsQuery.cs` — tickets paginados 20/pg do contato, ordem `created_at desc`, include_terminal=true por default
- [x] T144 [P] [US6] Criar `src/omniDesk.Api/Features/Contacts/Queries/ListContactConversationsQuery.cs` — conversas paginadas
- [x] T145 [P] [US6] Criar `src/omniDesk.Api/Features/Contacts/Commands/CreateContactCommand.cs` — chama `ContactDeduplicationService.FindOrCreateContact` (T045) e retorna resultado
- [x] T146 [P] [US6] Criar `src/omniDesk.Api/Features/Contacts/Commands/UpdateContactCommand.cs` — edita fields; recalcula `phone_normalized` se phone mudou; conflitos retornam 409 EMAIL_CONFLICT/PHONE_CONFLICT
- [x] T147 [US6] Criar `src/omniDesk.Api/Features/Contacts/ContactEndpoints.cs` mapeando GET list, GET {id}, POST, PUT, GET {id}/tickets, GET {id}/conversations. Registrar em `Program.cs`

### Frontend

- [x] T148 [P] [US6] Criar `src/omniDesk.Crm/src/app/features/contacts/services/contacts.service.ts` — HTTP wrapper para endpoints
- [x] T149 [P] [US6] Criar `src/omniDesk.Crm/src/app/features/contacts/components/contact-form.component.ts` — edição inline (nome, email, phone, notes)
- [x] T150 [P] [US6] Criar `src/omniDesk.Crm/src/app/features/contacts/components/contact-tickets-list.component.ts` — Paginator PrimeNG, 20/pg, links para `/tickets/{id}`
- [x] T151 [P] [US6] Criar `src/omniDesk.Crm/src/app/features/contacts/components/contact-conversations-list.component.ts` — idem para conversas
- [x] T152 [US6] Criar `src/omniDesk.Crm/src/app/features/contacts/contact-profile.component.ts` — Tabs (Tickets / Conversas) + contact-form
- [x] T153 [US6] Adicionar rota `/contacts/:id` em `app.routes.ts` (lazy)
- [x] T154 [US6] No `ticket-side-panel.component.ts` (T102), tornar nome do contato um link para `/contacts/{contact_id}`

### Tests for User Story 6

- [x] T155 [P] [US6] `tests/.../Features/Contacts/ContactDeduplicationServiceTests.cs` — race test com 3 inserções paralelas mesmo email → 1 contato; merge de campos vazios; append source_channels
- [x] T156 [P] [US6] `tests/.../Features/Contacts/ListContactTicketsQueryTests.cs` — paginação, ordem, include_terminal
- [x] T157 [P] [US6] `tests/.../Features/Contacts/UpdateContactCommandTests.cs` — phone_normalized recalc; conflitos
- [x] T158 [P] [US6] `src/omniDesk.Crm/.../contact-profile.component.spec.ts`

**Checkpoint US6**: Atendente tem visão 360° do cliente — histórico completo navegável.

---

## Phase 9: User Story 7 — Filtros e busca full-text (Priority: P3)

**Goal**: Painel de filtros + busca full-text em protocol/subject/contact.name/message content. Resultados em lista (não Kanban) ao buscar.

**Independent Test**: QS8 — busca por "sábado" retorna tickets cujo subject ou mensagens contenham; filtros refinam Kanban; protocolo exato bate.

### Backend

- [x] T159 [P] [US7] Criar `src/omniDesk.Api/Features/Tickets/Queries/SearchTicketsQuery.cs` — usa `websearch_to_tsquery('portuguese', :q)` em `tickets.search_vector` + `contacts.name` + `conversation_messages.content_tsv` (via LEFT JOIN com fallback se coluna não existir). Paginação 20/pg. Match exato de protocolo prioritário (verificação preliminar via `WHERE protocol = :q`). Respeita `TicketAccessPolicy`
- [x] T160 [US7] Adicionar lógica em `ListTicketsQuery` (T080): se `q` preenchido, delegar para `SearchTicketsQuery`; senão filtros normais. Endpoint mantém um único path `GET /api/tickets`

### Frontend

- [x] T161 [P] [US7] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/components/kanban-filters.component.ts` — Dropdown(depto), Dropdown(atendente, incluindo "Sem atendente"), Dropdown(canal), MultiSelect(prioridade), AutoComplete(tag), Calendar/atalhos (período). Emit filter state via signal
- [x] T162 [P] [US7] Criar `src/omniDesk.Crm/src/app/features/tickets-kanban/components/search-bar.component.ts` — InputText + debounce 300ms + botão limpar. Min 3 chars
- [x] T163 [US7] Integrar filters + search-bar no `tickets-kanban.component.ts`: quando search ativo, swap visual: oculta colunas Kanban e exibe lista (`<ticket-card>` em coluna única + Paginator)

### Tests for User Story 7

- [x] T164 [P] [US7] `tests/.../Features/Tickets/SearchTicketsQueryTests.cs` — Testcontainers Postgres: insere tickets + contatos + mensagens; busca por protocolo (match exato), por palavra do subject, por palavra de mensagem; ordem por relevância + data; RBAC
- [x] T165 [P] [US7] `tests/.../Features/Tickets/ListTicketsQuery_FiltersTests.cs` — cobertura de cada filtro isoladamente + composição

**Checkpoint US7**: Operação de alto volume é possível — filtros e busca tornam tickets encontráveis.

---

## Phase 10: User Story 8 — Anotações internas privativas (Priority: P3)

**Goal**: Garantir que anotações sejam visíveis no detail (já implementado em T101), append-only, e **nunca** vazem para IA/canais.

**Independent Test**: QS9 — adicionar nota; verificar que (a) aparece imediatamente no detail; (b) cliente não recebe nada no widget; (c) prompts da IA não incluem o texto.

### Implementação

- [x] T166 [P] [US8] Validar em `AgentOrchestrator` (Spec 006) que ao montar o contexto da IA, **apenas** `conversation_messages` é incluído — `ticket_notes` é explicitamente excluído (asserção em código + comentário). Adicionar teste explícito
- [x] T167 [P] [US8] Adicionar Serilog `Destructure.ByTransforming<TicketNote>(n => new { n.Id, n.TicketId, n.AttendantId, n.CreatedAt /* content omitido */ })` em `Program.cs` para garantir notas não logam conteúdo
- [x] T168 [P] [US8] Confirmar que `TicketCrmEvents` payloads **não** carregam `note.content` (apenas `note_id`) — revisar contracts/ticket-websocket-events.md + implementação de `TicketEventPublisher`

### Tests for User Story 8

- [x] T169 [P] [US8] `tests/.../Features/Tickets/Notes/InternalNotesIsolationTests.cs` — adiciona nota em ticket → simula cliente enviando mensagem → assert: prompt da IA construído **não** contém o conteúdo da nota; canal Live Chat (WebSocket) não recebe nenhuma mensagem com o conteúdo; canal WhatsApp idem (mock OutgoingMessageWorker)
- [x] T170 [P] [US8] `tests/.../Infrastructure/Logging/SerilogNoteDestructureTests.cs` — assert: Serilog captura de TicketNote masca o `content`

**Checkpoint US8**: Anotações 100% isoladas do cliente.

---

## Phase 11: User Story 9 — Configuração visual do pipeline (Priority: P3)

**Goal**: tenant_admin renomeia/reordena/colore as 3 colunas do pipeline por departamento.

**Independent Test**: QS10 — admin renomeia "Na Fila" → "Aguardando atribuição", reordena, define cor; abrir Kanban reflete imediatamente. Tentar duplicar status_mapping → 400.

### Backend

- [x] T171 [P] [US9] Criar `src/omniDesk.Api/Features/Pipelines/Queries/GetPipelineWithColumnsQuery.cs` + `ListPipelinesQuery`
- [x] T172 [P] [US9] Criar `src/omniDesk.Api/Features/Pipelines/Commands/UpdatePipelineColumnsCommand.cs` — recebe array de 3 colunas, valida via `PipelineStatusMapping.Validate(...)` (T028); update transacional
- [x] T173 [US9] Criar `src/omniDesk.Api/Features/Pipelines/PipelineEndpoints.cs` mapeando GET /api/pipelines, GET /api/pipelines/{id}, PUT /api/pipelines/{id}/columns. RBAC: PUT exige role `tenant_admin`

### Frontend

- [x] T174 [P] [US9] Criar `src/omniDesk.Crm/src/app/features/pipeline-config/services/pipeline-config.service.ts`
- [x] T175 [US9] Criar `src/omniDesk.Crm/src/app/features/pipeline-config/pipeline-config.component.ts` (standalone, lazy) — seletor de departamento + lista das 3 colunas com input nome + reorder via CDK drag-drop + color picker (PrimeNG) + botão salvar
- [x] T176 [US9] Adicionar rota `/settings/pipelines/:departmentId` em `app.routes.ts` (lazy) + guard de role `tenant_admin`

### Tests for User Story 9

- [x] T177 [P] [US9] `tests/.../Features/Pipelines/UpdatePipelineColumnsCommandTests.cs` — duplicate status → 400; ≠ 3 colunas → 400; rename → ok; reorder → ok; color hex válida/inválida
- [x] T178 [P] [US9] `tests/.../Features/Pipelines/PipelineProvisioningServiceTests.cs` — novo depto provisiona pipeline + 3 colunas default; idempotente
- [x] T179 [P] [US9] `src/omniDesk.Crm/.../pipeline-config.component.spec.ts`

**Checkpoint US9**: Personalização visual completa do Kanban.

---

## Phase 12: Polish & Cross-Cutting Concerns

**Purpose**: Refinamento, integração com Spec 010 (notificações), cleanup de Live Chat Inbox, performance, validação E2E (QS1–QS13).

### Integração com Spec 010 (Notificações — V1.1)

- [x] T180 [P] Criar interface `INotificationService` em `src/omniDesk.Api/Features/Notifications/` (V1: implementação no-op com TODO; Spec 010 entregará a real). Inject em `TicketCreationGateway` (T079) para notificar atendente designado

### Reset has_reminder_alert

- [x] T181 No `ResolveTicketCommand` (T084) e `CancelTicketCommand` (T085), garantir reset de `has_reminder_alert = false` (já mencionado nos commands — verificar implementação cobre)

### Live Chat Inbox Refactor

- [x] T182 Em `src/omniDesk.Crm/src/app/features/live-chat-inbox/`, ajustar comportamento: lista exibe conversas **sem ticket** preferencialmente; conversas com ticket têm um link "Abrir ticket TK-..." que navega para `/tickets/{id}` em vez de abrir detail interno. Manter detail interno para conversas sem ticket (raras)

### Documentação

- [x] T183 [P] Atualizar `docs/ARCHITECTURE.md` mencionando: substituição do scaffold Spec 005, pipelines provisionados por departamento, sequence per-day para protocolo, TicketSlaMonitorJob cron
- [x] T184 [P] Criar/atualizar `docs/specs/09-tickets.spec.md` se houver desvio entre o que foi implementado e o spec original (ex: novos códigos de erro descobertos)
- [x] T185 [P] Adicionar entrada em `docs/DEPENDENCIES.md` confirmando G5 (Tickets) implementado

### Performance

- [x] T186 [P] `tests/.../Performance/KanbanLoadPerformanceTests.cs` — benchmark: criar tenant com 100 tickets ativos; medir p95 do `ListTicketsQuery` (alvo < 1.5s)
- [x] T187 [P] `tests/.../Performance/SearchTicketsPerformanceTests.cs` — corpus 50k tickets + 500k mensagens; busca por palavra comum; alvo p95 < 1s

### Quickstart E2E

- [x] T188 Executar manualmente `specs/009-tickets-crm/quickstart.md` QS1–QS13 em ambiente dev. Documentar resultados em `specs/009-tickets-crm/quickstart-evidences.md` (padrão Spec 007). Marca SCs validados

### Cleanup

- [x] T189 [P] Em V1.1: remover `Infrastructure/AgentRuntime/_Obsolete/StubTicketCreationGateway.cs` (T048). **Não fazer em V1** — janela de rollback de 1 sprint
- [x] T190 [P] Atualizar `CLAUDE.md` (já feito durante plan) substituindo seção "Active Spec" para indicar Spec 009 completa quando todas as tasks marcadas

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** → começar imediatamente.
- **Foundational (Phase 2)** → depende de Setup. **Bloqueia todas as user stories** porque o rename do enum quebra código existente.
- **US1 (Phase 3)** + **US2 (Phase 4)** → ambas MVP, paralelizáveis após Foundational. US2 consome endpoints criados em paralelo; em sequência se equipe pequena.
- **US3, US4, US5, US6 (Phases 5–8)** → todas P2, paralelizáveis após Foundational. US4 (transfer) reusa `TicketEndpoints.cs` de US2 — coordenar arquivo.
- **US7, US8, US9 (Phases 9–11)** → todas P3, paralelizáveis. US7 depende de `T012` (search_vector em messages) se busca em mensagens for desejada.
- **Polish (Phase 12)** → depois das stories desejadas. T180 pode aguardar Spec 010.

### Within Each User Story

- Tests **DEVEM** ser escritos primeiro (Princípio VII).
- Domain entities → Repositories → Commands/Queries → Endpoints → Frontend services → Frontend components.
- Cada US tem um Checkpoint validável independentemente.

### Parallel Opportunities

- **Setup**: T002–T005 paralelos.
- **Foundational migrations**: T010–T012 paralelos com T006–T009 sequenciais (Tickets primeiro).
- **Foundational Domain**: T014–T029 (enums + entities + interfaces) paralelos com EF Configurations T032–T037.
- **Foundational Services**: T039 (Protocol), T040 (EventStore), T041 (Publisher), T045 (Dedup) paralelos.
- **US1+US2 simultâneos**: dois desenvolvedores podem fechar MVP em paralelo (compartilham apenas `TicketEndpoints.cs` em T087 — coordenar).
- **US3+US4+US5+US6**: 4 desenvolvedores em paralelo após MVP.
- **Tests [P]**: todos em arquivos distintos.

---

## Parallel Example: Foundational Domain

```bash
# Após migrations completarem (T006–T012), 8 streams paralelas:
Task T013 — Rewrite Ticket.cs
Task T014 — TicketStatus.cs
Task T015 — TicketPriority.cs
Task T016 — TicketChannel.cs
Task T017 — TicketEventType.cs
Task T021 — Contact.cs
Task T025 — Pipeline.cs
Task T027 — PipelineDefaults.cs
```

## Parallel Example: MVP (US1 + US2)

```bash
# Após Foundational (T006–T071) completar:
Stream A (US1): T072 → T073 → T074 → T075 → T076 → T077 → T078 → T079
Stream B (US2 backend): T080–T086 [P] → T087 (coord) → T088 [P] → T089 [P] → T090
Stream C (US2 frontend): T091–T096 [P] → T097 → T098 → T099 → T100–T106 [P] → T107
Stream D (US2 tests): T108–T114 [P]
```

---

## Implementation Strategy

### MVP First (US1 + US2 only — caminho feliz operacional)

1. Phase 1 (Setup) — 1 dia
2. Phase 2 (Foundational) — 4–5 dias (densa por causa do rewrite do scaffold Spec 005)
3. Phase 3 (US1) + Phase 4 (US2) — 5–7 dias paralelizados
4. **STOP + VALIDATE** com quickstart QS1+QS2+QS3+QS11
5. Deploy/demo MVP — cliente → IA → ticket → atendente trabalha → encerra

### Incremental Delivery

| Slice | User Stories | Valor entregue |
|---|---|---|
| 1 (MVP) | US1, US2 | Caminho feliz operacional do ticket |
| 2 | US3 | SLA tracking visível |
| 3 | US4 | Transferência fluida |
| 4 | US5 | Atendimentos offline entram no funil |
| 5 | US6 | Visão 360° do contato |
| 6 | US7 | Operação em alto volume |
| 7 | US8, US9 | Polimento de privacidade + customização |
| 8 (Polish) | — | Integração com Spec 010, performance, E2E |

### Parallel Team Strategy

Com 3 desenvolvedores:

- **Dev A (backend)**: lidera Foundational (T006–T071) → US1 backend (T076–T079) → US3 backend (T115–T118) → US4 backend (T125–T128)
- **Dev B (backend)**: ajuda Foundational → US2 backend (T080–T090) → US5 backend (T134–T136) → US6 backend (T141–T147)
- **Dev C (frontend)**: prepara estrutura CRM (T003) → aguarda Foundational endpoints → US2 frontend (T091–T107) → US3 frontend (T119–T120) → US4 frontend (T129–T131) → US5/US6/US7/US9 frontend

Tempo estimado total: 6–8 semanas para V1 completo (todas as 9 user stories + polish).

---

## Tasks recomendadas para Claude Opus 4.7

Esta spec é predominantemente executável com **Sonnet 4.6** (caminho padrão — mais rápido e econômico). As tasks abaixo, porém, têm risco técnico maior e se beneficiam de **Opus 4.7** durante a execução. Total: **7 de 190** tasks.

| Task | Fase | Risco | Por quê |
|---|---|---|---|
| T006 | Foundational | Migração de dados | UPDATE map de status + DDL reordenado; erro corrompe produção |
| T039 | Foundational | Concorrência | Sequence per-tenant per-day + retry SERIALIZABLE; SC-004 (0 colisões em 100×) |
| T045 | Foundational | Race condition | Lock Redis + fallback unique constraint; SC-007 (≤1% duplicatas) |
| T047 | Foundational | Atomicidade multi-store | 11 passos · 4 stores (PG + Mongo + Redis + WS) |
| T076 | US1 | Continuação T047 | Implementação completa do gateway |
| T115 | US3 | Idempotência + multi-tenant scan | Cron `* * * * *` com per-schema loop; SC-010 |
| T125 | US4 | Side-effects encadeados | Recálculo SLA + preservação seletiva + 6 side-effects |

**Workflow sugerido**: rodar todo o `/speckit-implement` em Sonnet 4.6; quando o orquestrador chegar nessas tasks, alternar via `/model claude-opus-4-7`, executar, voltar para `/model claude-sonnet-4-6`. Ou — se preferir simplicidade — fazer toda a Foundational em Opus (4 das 7 estão lá) e o resto em Sonnet.

---

## Notes

- **[P]**: arquivos distintos, sem dependência incompleta. **Não [P]**: mesmo arquivo (e.g., `TicketEndpoints.cs` adicionado em múltiplas tasks de US diferentes).
- **Migration ordering crítica**: T006 (tickets) → T007 (contacts c/ FK em tickets) → T008/T009/T010/T011 paralelos. Coordenar com Spec 007 em T012.
- **Rollback path**: `StubTicketCreationGateway` em `_Obsolete/` (T048) pode ser religado em DI se algo der errado no `TicketCreationGateway`. Em V1.1 remover.
- **Test discipline**: cada US tem testes [P] no início; rodar (vermelhos) antes de implementar.
- **Idempotência** crítica em: `TicketSlaMonitorJob` (Redis flags), `BackfillTicketProtocolJob`, `ContactBackfillJob`, `PipelineProvisioningService`.
- **Coordenação com outras specs**:
  - Spec 005 (Distribution): T051–T055 modificam código existente — coordenar.
  - Spec 006 (AI): T046, T050 alteram contrato `ITicketCreationGateway` — coordenar.
  - Spec 007 (Live Chat): T012, T030, T031, T100, T107 modificam — coordenar.
  - Spec 010 (Notifications): T079, T180 — stub V1, real em V1.1.
  - Spec 011 (Agenda): `has_reminder_alert` controlado por Agenda — apenas reset nesta spec.
- **Commits**: ao final de cada checkpoint (US completa). Convenção: `009: <descrição>` (alinhada ao histórico Spec 008).
