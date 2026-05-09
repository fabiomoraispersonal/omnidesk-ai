---

description: "Task list for Agentes de IA implementation"
---

# Tasks: Agentes de IA

**Input**: Design documents from `/specs/006-ai-agents/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, ADR-006-001, ADR-006-002, cross-spec-pendencies.md

**Tests**: A constituição (princípio VII — Test Discipline) torna testes **obrigatórios**. Backend: xUnit + Testcontainers (Postgres + Redis + Mongo reais). OpenAI usa `MockHttpMessageHandler` por exceção justificada em ADR-006-001 + smoke `[Trait("openai-live")]` fora do CI. Frontend: `.spec.ts` co-localizado.

**Organization**: Tarefas agrupadas por user story para entrega independente.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Pode rodar em paralelo (arquivo distinto, sem dependência pendente)
- **[Story]**: Mapeia para a user story (US1–US6) — ausente em Setup/Foundational/Polish
- Caminhos relativos do repo: `src/...`, `src/omniDesk.Api/tests/...`

## Path Conventions

- Backend: `src/omniDesk.Api/{Domain,Features,Infrastructure}/`
- Frontend CRM: `src/omniDesk.Crm/src/app/`
- Tests: `src/omniDesk.Api/tests/omniDesk.Api.Tests/{Domain,Features,Infrastructure,Smoke}/` (espelha topologia do código)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Variáveis de ambiente, migrations e estrutura de pastas para esta feature.

- [X] T001 Adicionar em `src/omniDesk.Api/.env.example` e `src/omniDesk.Api/appsettings.Development.json` as 4 chaves novas: `Ai:RunTimeoutSeconds=30`, `Ai:RunMaxRetries=1`, `Ai:RunRetryBackoffSeconds=3`, `Ai:PlaygroundTtlSeconds=1800`; documentar no README local
- [X] T002 Criar migration tenant-scoped `Add_AiAgents_AiSettings.sql` em `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` com tabelas `ai_agents`, `ai_settings`, `ai_threads` conforme `data-model.md` §7.1
- [X] T003 Criar migration public-scope `Add_DefaultDepartmentId_To_Tenants.sql` em `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` com `ALTER TABLE public.tenants ADD COLUMN default_department_id uuid` conforme `data-model.md` §7.2
- [X] T004 [P] Criar estrutura de pastas backend: `src/omniDesk.Api/Domain/{AiAgents,AiSettings,AiThreads}/`, `src/omniDesk.Api/Features/{AiAgents,AiSettings,AgentRuntime}/`, `src/omniDesk.Api/Infrastructure/{AiAgents,AiSettings,AiThreads,OpenAi,Queues,ActivityLogs}/`
- [X] T005 [P] Criar estrutura de pastas frontend: `src/omniDesk.Crm/src/app/features/ai-agents/{pages/{agents-list,agent-edit,ai-settings},shared,data-access}/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domínio compartilhado, infraestrutura OpenAI/Mongo/Queues e contratos internos. Bloqueia TODAS as user stories.

**⚠️ CRITICAL**: Nenhuma user story pode começar antes deste bloco completar.

### Domínio compartilhado

- [X] T006 [P] Criar `AgentType.cs` em `src/omniDesk.Api/Domain/AiAgents/AgentType.cs` com enum `Orchestrator`/`SubAgent` + helper `Parse(string)` (data-model §1.1)
- [X] T007 [P] Criar `ToolNames.cs` em `src/omniDesk.Api/Domain/AiAgents/ToolNames.cs` com constantes `HandoffToAgent`, `TransferToHuman`, `CheckAvailability`, `CreateAppointment` (princípio VII — sem magic strings; contracts/tool-calls.md)
- [X] T008 [P] Criar `AgentVariableNames.cs` em `src/omniDesk.Api/Domain/AiAgents/AgentVariableNames.cs` com constantes `CompanyName`, `DepartmentName`, `AttendantName`
- [X] T009 [P] Criar `AgentActivityAction.cs` em `src/omniDesk.Api/Domain/AiAgents/AgentActivityAction.cs` com enum `Respond`, `HandoffToAgent`, `TransferToHuman`, `ApiError`
- [X] T010 [P] Criar `HandoffKeywords.cs` em `src/omniDesk.Api/Domain/AiAgents/HandoffKeywords.cs` com lista PT-BR estática (`atendente`, `humano`, `gerente`, `responsável`, `quero falar com alguém`) e método `Detect(string text)` retornando bool — case-insensitive, normaliza acentos (FR-013)
- [X] T011 [P] Criar entidade `AiAgent.cs` em `src/omniDesk.Api/Domain/AiAgents/AiAgent.cs` (data-model §1.1) + `IAiAgentRepository.cs` interface
- [X] T012 [P] Criar entidade `AiSettings.cs` em `src/omniDesk.Api/Domain/AiSettings/AiSettings.cs` (data-model §1.2) + `IAiSettingsRepository.cs`
- [X] T013 [P] Criar entidade `AiThread.cs` em `src/omniDesk.Api/Domain/AiThreads/AiThread.cs` (data-model §1.3) + `IAiThreadRepository.cs`
- [X] T014 [P] Criar POCO `AgentActivityLog.cs` em `src/omniDesk.Api/Domain/AiAgents/AgentActivityLog.cs` (Mongo doc, data-model §3.1)
- [X] T015 Modificar `src/omniDesk.Api/Domain/Tenants/Tenant.cs` para adicionar propriedade `DefaultDepartmentId : Guid?` (com comment `// Spec 006 — FR-016`)

### Configurações EF Core

- [X] T016 [P] Criar `AiAgentConfiguration.cs` em `src/omniDesk.Api/Infrastructure/AiAgents/AiAgentConfiguration.cs` mapeando colunas + check constraint `chk_orchestrator_no_dept` + índices (data-model §1.1)
- [X] T017 [P] Criar `AiSettingsConfiguration.cs` em `src/omniDesk.Api/Infrastructure/AiSettings/AiSettingsConfiguration.cs`
- [X] T018 [P] Criar `AiThreadConfiguration.cs` em `src/omniDesk.Api/Infrastructure/AiThreads/AiThreadConfiguration.cs`
- [X] T019 Atualizar `src/omniDesk.Api/Infrastructure/Persistence/AppDbContext.cs` adicionando `DbSet<Tenant>` já existe — apenas validar; **e em** TenantDbContext registrar `DbSet<AiAgent>`, `DbSet<AiSettings>`, `DbSet<AiThread>` via configurations
- [X] T020 Atualizar `src/omniDesk.Api/Infrastructure/Persistence/Configurations/TenantConfiguration.cs` (ou criar) para mapear nova coluna `DefaultDepartmentId` em `public.tenants`

### Contratos internos (gateways stub)

- [X] T021 [P] Criar `IConversationGateway.cs` em `src/omniDesk.Api/Features/AgentRuntime/IConversationGateway.cs` com interface + records `AiThreadDto`, `ConversationMessage`, `OutgoingMessage` (contracts/conversation-gateway.md)
- [X] T022 [P] Criar `ITicketCreationGateway.cs` em `src/omniDesk.Api/Features/AgentRuntime/ITicketCreationGateway.cs` com interface + records `TicketHandoffRequest`, `TicketHandoffResult` (contracts/ticket-creation-gateway.md)

### OpenAI infrastructure

- [X] T023 Criar `IAssistantsApi.cs` em `src/omniDesk.Api/Infrastructure/OpenAi/IAssistantsApi.cs` com métodos `EnsureAssistantAsync`, `UpdateAssistantAsync`, `CreateThreadAsync`, `DeleteThreadAsync`, `AppendUserMessageAsync`, `CreateRunAsync`, `PollRunAsync`, `SubmitToolOutputsAsync`, `GetLatestAssistantMessageAsync`
- [X] T024 Criar `AssistantsApi.cs` em `src/omniDesk.Api/Infrastructure/OpenAi/AssistantsApi.cs` implementando wrapper sobre `OpenAI.Assistants.AssistantClient`; recebe `IHttpClientFactory` + `OpenAiKeyResolver` para resolver chave por execução (research §R7)
- [X] T025 Criar `OpenAiKeyResolver.cs` em `src/omniDesk.Api/Infrastructure/OpenAi/OpenAiKeyResolver.cs` com método `Task<OpenAiCredentials> ResolveAsync(Guid tenantId, CancellationToken)` (tenant `OpenAiApiKeyEnc` via `IDataProtectionProvider` → fallback global) — research §R7
- [X] T026 Criar `OpenAiToolDefinitions.cs` em `src/omniDesk.Api/Infrastructure/OpenAi/OpenAiToolDefinitions.cs` exportando os 4 JSON schemas de function calls (contracts/tool-calls.md §1-4)

### Filas e logging

- [X] T027 Criar `QueueNames.cs` em `src/omniDesk.Api/Infrastructure/Queues/QueueNames.cs` com helpers `Incoming(slug)` e `Outgoing(slug)` (princípio VII)
- [X] T028 [P] Criar `IncomingMessagePublisher.cs` em `src/omniDesk.Api/Infrastructure/Queues/IncomingMessagePublisher.cs` (Hangfire `IBackgroundJobClient` enqueue na fila `{slug}:incoming_messages`)
- [X] T029 [P] Criar `OutgoingMessagePublisher.cs` em `src/omniDesk.Api/Infrastructure/Queues/OutgoingMessagePublisher.cs` (análogo)
- [X] T030 Criar `AgentActivityLogger.cs` em `src/omniDesk.Api/Infrastructure/ActivityLogs/AgentActivityLogger.cs` — escreve documento em `{slug}_agent_activity_logs` Mongo; nunca registra conteúdo de mensagem do cliente (constituição IV; contracts/agent-runtime-events.md)
- [X] T031 Criar `RetryPolicy.cs` em `src/omniDesk.Api/Features/AgentRuntime/RetryPolicy.cs` com lógica: 1 retry após 3s para 5xx/timeout/429; sem retry para 401/403; sem retry para run terminal (research §R9, FR-018/019)

### Provisioning + DI

- [X] T032 Modificar `src/omniDesk.Api/Infrastructure/Provisioning/TenantProvisioningJob.cs` — renomear `CopyAgentTemplatesAsync` → `ProvisionAiAgentsAsync`, ajustar INSERT para `{schema}.ai_agents` com colunas corretas (data-model §1.1, cross-spec-pendencies §003-B); criar row em `{schema}.ai_settings` com defaults; idempotente
- [X] T033 Atualizar `src/omniDesk.Api/Program.cs` registrando: `AddScoped<IAssistantsApi, AssistantsApi>`, `AddSingleton<OpenAiKeyResolver>`, `AddScoped<AgentActivityLogger>`, `AddSingleton<RetryPolicy>`, `AddScoped<IConversationGateway, ChannelStubGateway>`, `AddScoped<ITicketCreationGateway, StubTicketCreationGateway>`; adicionar 2 filas Hangfire `incoming_messages` e `outgoing_messages`

### Testes Foundational

- [X] T034 [P] Criar `AgentTypeTests.cs` em `tests/omniDesk.Api.Tests/Domain/AiAgents/AgentTypeTests.cs` cobrindo enum + `Parse`
- [X] T035 [P] Criar `HandoffKeywordsTests.cs` em `tests/omniDesk.Api.Tests/Domain/AiAgents/HandoffKeywordsTests.cs` cobrindo PT-BR + acentos + case-insensitive (FR-013)
- [X] T036 [P] Criar `OpenAiKeyResolverTests.cs` em `tests/omniDesk.Api.Tests/Infrastructure/OpenAi/OpenAiKeyResolverTests.cs` (Testcontainers Postgres) cobrindo: tenant com chave → usa tenant; tenant sem chave → cai no global; descriptografia via `IDataProtectionProvider` (FR-025)
- [X] T037 [P] Criar `RetryPolicyTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/RetryPolicyTests.cs` cobrindo: 5xx faz 1 retry; 401 não retenta; rate-limit faz 1 retry; timeout faz 1 retry (FR-018/019, research §R9)
- [X] T038 Criar `AssistantsApiContractTests.cs` em `tests/omniDesk.Api.Tests/Infrastructure/OpenAi/AssistantsApiContractTests.cs` com `MockHttpMessageHandler` cobrindo create/update assistant, create/delete thread, append message, create run, poll run, submit tool outputs (ADR-006-001)
- [X] T039 [P] Criar `AgentActivityLoggerTests.cs` em `tests/omniDesk.Api.Tests/Infrastructure/ActivityLogs/AgentActivityLoggerTests.cs` (Testcontainers Mongo) verificando: cada `action` produz 1 doc; campos certos; **zero PII** — buscar conteúdo da mensagem retorna nada (constituição IV)

**Checkpoint**: Foundation pronta. Provisionamento de novo tenant cria `ai_agents` + `ai_settings`. User stories podem começar.

---

## Phase 3: User Story 1 — Cliente atendido pelo Orchestrator (Priority: P1) 🎯 MVP

**Goal**: Tenant recém-provisionado tem Orchestrator funcional; cliente envia mensagem e recebe resposta da IA dentro de 5s sem qualquer sub-agente cadastrado.

**Independent Test**: QS-1 + QS-2 do `quickstart.md` — provisionar tenant novo, verificar 1 row `type=orchestrator` em `ai_agents`; via canal stub, enviar mensagem e ver resposta + 1 doc em `agent_activity_logs`.

### Tests for User Story 1 ⚠️

- [ ] T040 [P] [US1] Criar `AiAgentProvisioningTests.cs` em `tests/omniDesk.Api.Tests/Infrastructure/Provisioning/AiAgentProvisioningTests.cs` (Testcontainers Postgres + Mongo) verificando que após `TenantProvisioningJob.RunAsync`: existe 1 row `ai_agents` com `type=orchestrator`, `is_active=true`, `template_id` preenchido; `ai_settings` row com defaults; idempotente em re-execução (US1 cenário 1, FR-001/031)
- [ ] T041 [P] [US1] Criar `ContextBuilderTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/ContextBuilderTests.cs` cobrindo: respeita `context_window_messages`; histórico vazio gera mensagem sintética "[Início da conversa]"; substitui variáveis (FR-022/023, research §R8)
- [X] T042 [P] [US1] Criar `PromptVariableSubstitutorTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/PromptVariableSubstitutorTests.cs` cobrindo: `{{company_name}}`, `{{department_name}}`, `{{attendant_name}}` substituídos; variáveis ausentes viram string vazia; variáveis desconhecidas permanecem literais e logam warning (FR-012)
- [ ] T043 [US1] Criar `IncomingMessageWorkerTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/IncomingMessageWorkerTests.cs` (Testcontainers Postgres+Redis+Mongo + MockHttpMessageHandler OpenAI) cobrindo: lock por conversa impede dupla execução; idempotência por `message_id`; criação de thread no primeiro contato; reuso do thread em mensagens subsequentes; produz 1 doc activity_log com `action=respond` (US1 cenário 2/5, FR-005/006, research §R3/R5/R6)
- [ ] T044 [US1] Criar `AgentOrchestratorTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/AgentOrchestratorTests.cs` cobrindo o fluxo linear de `ProcessAsync`: Orchestrator único responde; sem sub-agentes ativos não tenta handoff; respeita `current_agent_id` (research §R3)
- [ ] T045 [US1] Criar `AiAgentsEndpointsContractTests.cs` em `tests/omniDesk.Api.Tests/Features/AiAgents/AiAgentsEndpointsContractTests.cs` cobrindo: GET `/api/agents` retorna shape correto; GET `/api/agents/{id}` retorna prompt; PUT em Orchestrator aceita name/prompt/model; PUT rejeita `type` change (`CANNOT_CHANGE_TYPE`); DELETE em Orchestrator → 409 `CANNOT_DELETE_ORCHESTRATOR`; POST com `type=orchestrator` → 409 (contracts/agents-api.md)

### Backend implementation

- [X] T046 [P] [US1] Criar `IncomingMessage.cs` em `src/omniDesk.Api/Features/AgentRuntime/IncomingMessage.cs` (record com tenant, threadRef, content, messageId, sentAt)
- [X] T047 [P] [US1] Criar `OutgoingMessage.cs` em `src/omniDesk.Api/Features/AgentRuntime/OutgoingMessage.cs` (record já definido em IConversationGateway — confirmar reuso)
- [X] T048 [US1] Criar `PromptVariableSubstitutor.cs` em `src/omniDesk.Api/Features/AiAgents/Variables/PromptVariableSubstitutor.cs` com regex `\{\{(\w+)\}\}` + fallback (research §R8)
- [X] T049 [US1] Criar `ContextBuilder.cs` em `src/omniDesk.Api/Features/AgentRuntime/ContextBuilder.cs` — monta `instructions` com prompt resolvido + última mensagem do user (Assistants v2 envia histórico via thread; instructions é override por run)
- [X] T050 [US1] Criar `ChannelStubGateway.cs` em `src/omniDesk.Api/Features/AgentRuntime/ChannelStubGateway.cs` implementando `IConversationGateway` (Postgres `ai_threads` + Hangfire enqueue + Serilog) — contracts/conversation-gateway.md
- [X] T051 [US1] Criar `AgentResolver.cs` em `src/omniDesk.Api/Features/AgentRuntime/AgentResolver.cs` com método `ResolveCurrentAgentAsync(threadId)`: se `current_agent_id` null → Orchestrator; senão → sub-agente carregado
- [X] T052 [US1] Criar `AgentOrchestrator.cs` em `src/omniDesk.Api/Features/AgentRuntime/AgentOrchestrator.cs` implementando o fluxo linear de `ProcessAsync` (research §R3) — **sem** despacho de tool calls ainda (US2/US3 adicionam); responde direto via `runs.create` + poll + extrai assistant message + enfileira outgoing
- [X] T053 [US1] Criar `IncomingMessageWorker.cs` em `src/omniDesk.Api/Features/AgentRuntime/IncomingMessageWorker.cs` — Hangfire `[Queue("incoming_messages")]`; adquire lock Redis, checa idempotência, chama `AgentOrchestrator.ProcessAsync`; libera lock no finally
- [X] T054 [US1] Criar `OutgoingMessageWorker.cs` em `src/omniDesk.Api/Features/AgentRuntime/OutgoingMessageWorker.cs` — Hangfire `[Queue("outgoing_messages")]`; em V1 apenas Serilog log (Specs 007/008 conectam canais reais)
- [X] T055 [US1] Criar endpoint `GET /api/agents` + `GET /api/agents/{id}` + `PUT /api/agents/{id}` (apenas Orchestrator no escopo desta US) em `src/omniDesk.Api/Features/AiAgents/AiAgentsEndpoints.cs` — autoriza `tenant_admin` ou `supervisor` via policy `Policies.ManageAgents` (Spec 004 FR-016)
- [X] T056 [US1] Criar policy `Policies.ManageAgents` em `src/omniDesk.Api/Features/Authorization/Policies/AuthorizationPoliciesRegistration.cs` (admin OR supervisor); criar `Policies.ManageAiSettings` (admin only) — cross-spec §004-A
- [X] T057 [US1] Criar endpoint interno `POST /api/internal/test-incoming` em `src/omniDesk.Api/Features/AgentRuntime/InternalTestEndpoint.cs` (gated por `IHostEnvironment.IsDevelopment`) — atalho para QS-2 sem depender de Spec 007
- [ ] T058 [US1] Validar provisionamento: rodar smoke local provisionando tenant novo e verificar `ai_agents` + `ai_settings` criados (cross-spec §003-B)

### Frontend US1

- [X] T059 [P] [US1] Criar `ai-agents.types.ts` em `src/omniDesk.Crm/src/app/features/ai-agents/data-access/ai-agents.types.ts` com tipos `AiAgent`, `AiSettings`, `AiAgentSummary`
- [X] T060 [P] [US1] Criar `ai-agents.service.ts` em `src/omniDesk.Crm/src/app/features/ai-agents/data-access/ai-agents.service.ts` (HttpClient + Signals) — `list()`, `get(id)`, `update(id, dto)` por enquanto
- [X] T061 [US1] Criar componente `AgentsListPage` em `src/omniDesk.Crm/src/app/features/ai-agents/pages/agents-list/agents-list.page.ts` (+ `.html`, `.css`, `.spec.ts`) — exibe cards (PrimeNG Card + Tag) com badge de tipo; botão "Editar" no Orchestrator; sem ação "Excluir" no Orchestrator
- [X] T062 [US1] Criar `AgentEditPage` em `src/omniDesk.Crm/src/app/features/ai-agents/pages/agent-edit/agent-edit.page.ts` — form Reactive com campos `name`, `prompt` (PrimeNG Editor), `model` (Select); apenas para Orchestrator nesta US (sub-agente entra US3)
- [X] T063 [US1] Criar `ai-agents.routes.ts` em `src/omniDesk.Crm/src/app/features/ai-agents/ai-agents.routes.ts` com lazy loading; registrar em `app.routes.ts`
- [ ] T064 [US1] Adicionar item "Agentes de IA" no menu lateral do CRM em `src/omniDesk.Crm/src/app/layout/sidebar/sidebar.component.html` com guard de role `tenant_admin|supervisor`

**Checkpoint US1**: Orchestrator visível e editável no CRM; cliente envia mensagem via stub e recebe resposta — todos os asserts QS-1 e QS-2 passam.

---

## Phase 4: User Story 2 — Transbordo para humano (Priority: P1)

**Goal**: Cliente que pede explicitamente um humano, ou agente que decide via prompt, dispara `transfer_to_human` → cria ticket → IA não processa mais a conversa → próxima mensagem recebe auto-reply.

**Independent Test**: QS-3 — palavra-chave dispara transbordo, ticket criado, segunda mensagem recebe auto-reply.

### Tests for User Story 2 ⚠️

- [ ] T065 [P] [US2] Criar `HandoffKeywordDetectorTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/HandoffKeywordDetectorTests.cs` cobrindo: cada palavra-chave; case-insensitive; com/sem acento (FR-013, US2 cenário 1)
- [ ] T066 [P] [US2] Criar `ToolCallDispatcherTransferToHumanTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/ToolCallDispatcherTransferToHumanTests.cs` cobrindo: depto explícito; fallback para `default_department_id` quando agente é Orchestrator; fallback para `agent.department_id` quando sub-agente; nenhum disponível → erro de configuração (US2 cenário 3, FR-014/016)
- [ ] T067 [P] [US2] Criar `StubTicketCreationGatewayTests.cs` em `tests/omniDesk.Api.Tests/Infrastructure/AgentRuntime/StubTicketCreationGatewayTests.cs` (Testcontainers Postgres+Redis): cria ticket `status=queued`, `subject` truncado em 255, `sla_started_at` preenchido; insere snapshot em `ai_handoff_snapshots`; publica evento Redis `{slug}:ws:dept:{id}` (contracts/ticket-creation-gateway.md)
- [ ] T068 [US2] Criar `HandedOffAutoReplyTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/HandedOffAutoReplyTests.cs` verificando: thread com `handed_off_to_human_at != null` recebe nova mensagem → IncomingMessageWorker enfileira auto-reply do sistema sem chamar OpenAI; **zero novos docs** em `agent_activity_logs` (FR-015, US2 cenário 4)
- [ ] T069 [US2] Criar `AgentTransbordoMessageTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/AgentTransbordoMessageTests.cs` verificando que após `transfer_to_human`, mensagem do agente "Vou transferir você para nossa equipe de [Departamento]. Aguarde um momento." chega à fila outgoing (FR-033, US2 cenário 2a)

### Backend implementation US2

- [X] T070 [US2] Criar migration `Add_Ai_Handoff_Snapshots.sql` em `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` com tabela `ai_handoff_snapshots` (contracts/ticket-creation-gateway.md)
- [X] T071 [US2] Criar `HandoffKeywordDetector.cs` em `src/omniDesk.Api/Features/AgentRuntime/HandoffKeywordDetector.cs` (consome `HandoffKeywords`, normaliza acentos via `Text.NormalizationForm.FormD`)
- [X] T072 [US2] Criar `ToolCallDispatcher.cs` em `src/omniDesk.Api/Features/AgentRuntime/ToolCallDispatcher.cs` — apenas o caso `transfer_to_human` nesta US; resolve dept (param > default_department_id > agent.department_id); cria ticket via `ITicketCreationGateway`; marca `handed_off_to_human_at`; submit_tool_outputs com `instruction_for_agent` (contracts/tool-calls.md §2)
- [X] T073 [US2] Criar `StubTicketCreationGateway.cs` em `src/omniDesk.Api/Infrastructure/AgentRuntime/StubTicketCreationGateway.cs` implementando `ITicketCreationGateway` — INSERT `tickets` (Spec 005 scaffold) + INSERT `ai_handoff_snapshots` + publica Redis `{slug}:ws:dept:{department_id}`
- [X] T074 [US2] Estender `AgentOrchestrator.ProcessAsync` para: (a) detectar palavra-chave de transbordo via `HandoffKeywordDetector` antes do `runs.create` e injetar system message no thread (FR-013); (b) após poll do run, se `requires_action` com tool `transfer_to_human`, despachar via `ToolCallDispatcher`; (c) após despacho, finalizar run e parar
- [X] T075 [US2] Estender `IncomingMessageWorker` para: (a) chamar `IConversationGateway.IsHandedOffAsync(threadId)` no início; (b) se já transbordado, enfileirar auto-reply "Sua mensagem foi recebida. Um atendente responderá em breve." e retornar sem chamar OpenAI (FR-015)
- [X] T076 [US2] Estender `ChannelStubGateway` para implementar `IsHandedOffAsync` e `MarkHandedOffAsync` (UPDATE `ai_threads` + publica evento Redis `{slug}:ws:thread:{id}` payload `human_handoff` — contracts/agent-runtime-events.md)
- [X] T077 [US2] Adicionar fallback em `ToolCallDispatcher` para o caso de **departamento padrão também inativo** (cross-spec §005-E + cross-spec-pendencies §005-A fallback): registra `agent_activity_logs` com `action=api_error error.type=config_missing` e enfileira mensagem ao cliente "Estamos com uma instabilidade. Por favor, retorne mais tarde." (recurso de último escape — não cria ticket)

### Frontend US2

> Sem mudanças visuais — comportamento é todo back-end. Próxima mensagem do cliente já é exibida via lógica da Spec 005 quando o ticket aparecer na fila.

**Checkpoint US2**: QS-3 passa fim-a-fim. MVP entregue: Orchestrator atende e transborda — clínica pode operar com 1 humano + IA.

---

## Phase 5: User Story 3 — Sub-agentes especializados + handoff (Priority: P2)

**Goal**: Tenant cria sub-agentes vinculados a departamento; Orchestrator roteia via `handoff_to_agent`; sub-agente pode devolver ao Orchestrator ou rotear a outro sub-agente; desativação respeita conversa em andamento.

**Independent Test**: QS-4 — criar 2 sub-agentes, enviar mensagens em diferentes intenções e ver handoff funcionar; desativar sub-agente e ver que próxima mensagem cai no Orchestrator.

### Tests for User Story 3 ⚠️

- [ ] T078 [P] [US3] Criar `ToolCallDispatcherHandoffTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/ToolCallDispatcherHandoffTests.cs` cobrindo: handoff sucesso atualiza `current_agent_id` + grava log + publica evento Redis; handoff para sub-agente inativo retorna erro estruturado; handoff para `'orchestrator'` (atalho) resolve para o orchestrator do tenant; **loop detectado** após 3 handoffs ao mesmo agente retorna `HANDOFF_LOOP_DETECTED` (research §R4, contracts/tool-calls.md §1)
- [ ] T079 [P] [US3] Criar `AgentResolverActiveSubAgentsTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/AgentResolverActiveSubAgentsTests.cs` (Testcontainers Postgres) cobrindo: lista de sub-agentes ativos para o Orchestrator inclui apenas `is_active=true AND deleted_at IS NULL AND department.is_active=true`; ordenação alfabética por nome (cross-spec §005-E, FR-004)
- [X] T080 [P] [US3] Criar `CreateAiAgentValidatorTests.cs` em `tests/omniDesk.Api.Tests/Features/AiAgents/CreateAiAgentValidatorTests.cs` cobrindo: required fields; descritivo curto ≤300; prompt 10..50000; modelo na allowlist; depto ativo (data-model §6.1)
- [ ] T081 [P] [US3] Criar `SubAgentSoftDeleteTests.cs` em `tests/omniDesk.Api.Tests/Features/AiAgents/SubAgentSoftDeleteTests.cs` cobrindo: sub-agente sem histórico → DELETE físico; sub-agente com `agent_activity_logs` → soft delete (FR-010)
- [ ] T082 [US3] Criar `SubAgentDeactivatedDuringConversationTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/SubAgentDeactivatedDuringConversationTests.cs` cobrindo: thread com `current_agent_id=X`; X é desativado; nova mensagem → cai no Orchestrator (FR-032, US3 cenário 4a)
- [ ] T083 [P] [US3] Criar `AgentRuntimeRealImplTests.cs` em `tests/omniDesk.Api.Tests/Infrastructure/AiAgents/AgentRuntimeRealImplTests.cs` verificando que `AgentRuntime` (impl real) substitui `FallbackAgentRuntime` em DI e retorna sub-agente real do depto (cross-spec §005-A)

### Backend implementation US3

- [X] T084 [US3] Criar `CreateAiAgentValidator.cs` em `src/omniDesk.Api/Features/AiAgents/Validators/CreateAiAgentValidator.cs` (FluentValidation) — data-model §6.1
- [X] T085 [US3] Criar `UpdateAiAgentValidator.cs` em `src/omniDesk.Api/Features/AiAgents/Validators/UpdateAiAgentValidator.cs` — bloqueia mudança de `type`; bloqueia desativação do Orchestrator; restringe campos permitidos no Orchestrator (data-model §6.2, FR-007)
- [X] T086 [US3] Estender `AiAgentsEndpoints` com `POST /api/agents` — apenas sub-agente; cria registro + lazy assistant; valida depto ativo
- [X] T087 [US3] Estender `AiAgentsEndpoints` com `PUT /api/agents/{id}` (sub-agente — campos completos); agendar update do Assistant na OpenAI quando prompt/modelo mudar
- [X] T088 [US3] Estender `AiAgentsEndpoints` com `PATCH /api/agents/{id}/toggle` e `DELETE /api/agents/{id}` (decide soft vs físico via `agent_activity_logs` count + `ai_threads.current_agent_id` count)
- [X] T089 [US3] Estender `AgentResolver` com método `ListActiveSubAgentsAsync(tenantSlug)` retornando `[{id, name, short_description}]` filtrando por `is_active=true AND deleted_at IS NULL AND department.is_active=true`
- [X] T090 [US3] Estender `ContextBuilder` para anexar `system message` listando sub-agentes ativos disponíveis (nome + descritivo curto) — Orchestrator usa para decidir handoff (research §R3 step 3)
- [X] T091 [US3] Estender `ToolCallDispatcher` com caso `handoff_to_agent`: valida agente destino ativo; resolve `'orchestrator'` shortcut; detecta loop (3 handoffs ao mesmo destino); UPDATE `ai_threads.current_agent_id`; submit_tool_outputs; **abre novo run** com Assistant do destino (research §R4)
- [X] T092 [US3] Criar `AgentRuntime.cs` em `src/omniDesk.Api/Infrastructure/AiAgents/AgentRuntime.cs` implementando `IAgentRuntime` da Spec 005 com lógica real para `GetSubAgentForDepartmentAsync` (cross-spec §005-A); demais métodos retornam empty/null por enquanto (Spec 007 completa); registrar em DI substituindo `FallbackAgentRuntime`

### Frontend US3

- [X] T093 [P] [US3] Estender `ai-agents.types.ts` com tipos para Create/Update sub-agente
- [X] T094 [P] [US3] Estender `ai-agents.service.ts` com `create()`, `delete()`, `toggle()`
- [X] T095 [US3] Estender `AgentsListPage` para mostrar cards de sub-agentes; botão "Novo sub-agente"; toggle ativo/inativo via `PrimeNG InputSwitch`; ação "Excluir" (com confirmação) apenas em sub-agente — não no Orchestrator
- [X] T096 [US3] Estender `AgentEditPage` com modo "sub-agente": campos completos `name`, `short_description`, `prompt`, `model`, `department_id` (Select de departamentos ativos da Spec 005), `is_active`
- [X] T097 [US3] Criar `PromptVariablesHelperComponent` em `src/omniDesk.Crm/src/app/features/ai-agents/shared/prompt-variables-helper.component.ts` — exibe variáveis disponíveis e botão "Inserir" no editor

**Checkpoint US3**: tenant compõe operação multi-agente fim-a-fim; QS-4 passa.

---

## Phase 6: User Story 4 — Playground (Priority: P2)

**Goal**: Admin valida prompt no playground sem criar conversa real, ticket ou histórico persistente.

**Independent Test**: QS-5 — testar mensagem no playground, verificar zero registros em `ai_threads`/`agent_activity_logs`/`tickets`.

### Tests for User Story 4 ⚠️

- [ ] T098 [P] [US4] Criar `PlaygroundEndpointTests.cs` em `tests/omniDesk.Api.Tests/Features/AiAgents/PlaygroundEndpointTests.cs` (Testcontainers Postgres+Redis+Mongo + MockHttpMessageHandler) cobrindo: nova sessão cria thread temporário em Redis com TTL 1800s; sessão existente reusa thread; **zero rows** em `ai_threads`; **zero docs** em `agent_activity_logs`; tool calls retornam `{simulated: true}` (FR-026/027, SC-012, contracts/playground-api.md)
- [ ] T099 [P] [US4] Criar `PlaygroundCleanupJobTests.cs` em `tests/omniDesk.Api.Tests/Features/AiAgents/PlaygroundCleanupJobTests.cs` cobrindo: chaves Redis expiradas têm thread OpenAI deletado via `threads.delete`

### Backend implementation US4

- [X] T100 [US4] Criar `PlaygroundEndpoint.cs` em `src/omniDesk.Api/Features/AiAgents/Playground/PlaygroundEndpoint.cs` — `POST /api/agents/{id}/test` + `DELETE /api/agents/playground-sessions/{session_id}` (contracts/playground-api.md)
- [X] T101 [US4] Criar `PlaygroundSessionStore.cs` em `src/omniDesk.Api/Features/AiAgents/Playground/PlaygroundSessionStore.cs` (Redis hash `{slug}:playground:{session_id}` com TTL via `Ai:PlaygroundTtlSeconds`)
- [X] T102 [US4] Criar `PlaygroundCleanupJob.cs` em `src/omniDesk.Api/Features/AiAgents/Playground/PlaygroundCleanupJob.cs` (Hangfire recurring 1h) — varre Redis e deleta threads OpenAI órfãos (research §R12)
- [X] T103 [US4] Modificar `ToolCallDispatcher` para detectar contexto playground (`isPlayground` flag) e retornar `{simulated: true}` sem efeitos colaterais

### Frontend US4

- [X] T104 [US4] Criar `PlaygroundPaneComponent` em `src/omniDesk.Crm/src/app/features/ai-agents/shared/playground-pane.component.ts` (PrimeNG Card + InputText + Button + área de resposta com Signal); botão "Limpar conversa" (DELETE session)
- [X] T105 [US4] Integrar `PlaygroundPaneComponent` em `AgentEditPage` na aba "Testar"

**Checkpoint US4**: QS-5 passa — admin valida prompts sem poluir produção.

---

## Phase 7: User Story 5 — Configurações avançadas (Priority: P3)

**Goal**: Admin ajusta janela de contexto, allowlist de modelos e chave OpenAI própria; chave própria tem prioridade sobre global.

**Independent Test**: QS-6 — alterar `context_window_messages` e ver `input_tokens` cair; cadastrar chave própria e ver `openai_key_source: tenant`.

### Tests for User Story 5 ⚠️

- [ ] T106 [P] [US5] Criar `AiSettingsEndpointsContractTests.cs` em `tests/omniDesk.Api.Tests/Features/AiSettings/AiSettingsEndpointsContractTests.cs` cobrindo: GET retorna shape; PUT respeita range [5,100]; PUT com modelo fora da allowlist global retorna 400; auth `tenant_admin` only (contracts/ai-settings-api.md, FR-022/024)
- [ ] T107 [P] [US5] Criar `OpenAiCredentialsValidationTests.cs` em `tests/omniDesk.Api.Tests/Features/AiSettings/OpenAiCredentialsValidationTests.cs` — PUT `/openai-credentials` com chave válida → 200 + `key_preview`; chave inválida (mock 401) → 400 `OPENAI_KEY_INVALID`
- [ ] T108 [P] [US5] Criar `ContextWindowMessagesPropagationTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/ContextWindowMessagesPropagationTests.cs` verificando que `ContextBuilder` lê `ai_settings.context_window_messages` e respeita o limite (FR-023, SC-006)

### Backend implementation US5

- [X] T109 [US5] Criar `UpdateAiSettingsValidator.cs` em `src/omniDesk.Api/Features/AiSettings/Validators/UpdateAiSettingsValidator.cs` (data-model §6.3)
- [X] T110 [US5] Criar `AiSettingsEndpoints.cs` em `src/omniDesk.Api/Features/AiSettings/AiSettingsEndpoints.cs` com `GET /api/ai-settings`, `PUT /api/ai-settings`, `PUT /api/ai-settings/openai-credentials`, `DELETE /api/ai-settings/openai-credentials` — autoriza `Policies.ManageAiSettings` (admin only, FR-016 da Spec 004)
- [X] T111 [US5] Estender `OpenAiKeyResolver` com método `ValidateKeyAsync(key)` que faz `GET https://api.openai.com/v1/models` e retorna sucesso/falha (FR-025 + contracts/ai-settings-api.md)
- [X] T112 [US5] Garantir que `ContextBuilder` consome `IAiSettingsRepository.GetForTenantAsync(tenantId).ContextWindowMessages` em vez de hardcode

### Frontend US5

- [X] T113 [P] [US5] Criar `AiSettingsPage` em `src/omniDesk.Crm/src/app/features/ai-agents/pages/ai-settings/ai-settings.page.ts` com slider/InputNumber para `context_window_messages` (5-100), MultiSelect para `available_models`, seção "Credenciais OpenAI" com input mascarado para chave + botões "Cadastrar" e "Remover"
- [X] T114 [US5] Adicionar rota lazy `/configuracoes/agentes-de-ia/avancadas` em `ai-agents.routes.ts`; visível apenas para `tenant_admin`

**Checkpoint US5**: QS-6 passa.

---

## Phase 8: User Story 6 — Resiliência: falha OpenAI → transbordo automático (Priority: P3)

**Goal**: Erros 5xx/timeout disparam 1 retry após 3s; persistência → transbordo automático com mensagem de instabilidade. 401/403 transborda imediatamente.

**Independent Test**: QS-7 — injetar mock 503 e ver retry → transbordo em <10s; injetar 401 e ver transbordo imediato.

### Tests for User Story 6 ⚠️

- [ ] T115 [P] [US6] Criar `AutoTransbordoOnApiFailureTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/AutoTransbordoOnApiFailureTests.cs` (Testcontainers + MockHttpMessageHandler injectando falhas) cobrindo: 503 retry → 503 → transbordo automático com mensagem de instabilidade; tempo total <10s (SC-005); ticket criado em `tenants.default_department_id` quando agente é Orchestrator (FR-018/020/021, US6 cenário 1/2)
- [ ] T116 [P] [US6] Criar `NoRetryOnAuthFailureTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/NoRetryOnAuthFailureTests.cs` cobrindo: 401 → sem retry → transbordo imediato; 403 → idem; sem cair para chave global (FR-019, US6 cenário 3)
- [ ] T117 [P] [US6] Criar `SubAgentApiFailureRoutingTests.cs` em `tests/omniDesk.Api.Tests/Features/AgentRuntime/SubAgentApiFailureRoutingTests.cs` cobrindo: falha API durante run de sub-agente → ticket criado em `agent.department_id` (não em `default_department_id`) — US6 cenário 5

### Backend implementation US6

- [X] T118 [US6] Estender `AgentOrchestrator.ProcessAsync` com bloco try/catch envolvendo todo o fluxo OpenAI: ao capturar exceção, consulta `RetryPolicy.ShouldRetry(error)`; se sim → aguarda `Ai:RunRetryBackoffSeconds` e retenta uma vez; se não/após retry → loga `agent_activity_logs` com `action=api_error`, chama `ToolCallDispatcher.HandleApiFailure(threadId, currentAgentId)` que dispara transbordo automático com motivo "Falha técnica no agente de IA" + envia mensagem do sistema "Estamos com uma instabilidade técnica no momento. Vou transferir você para um de nossos atendentes." (FR-020, US6 cenário 1)
- [X] T119 [US6] Adicionar fault injection endpoint `POST /api/internal/fault-injector` em `src/omniDesk.Api/Features/AgentRuntime/InternalFaultInjector.cs` (gated por `IHostEnvironment.IsDevelopment` + DI flag `ASSISTANTS_API_FAULT_INJECTOR=true`) que faz `IAssistantsApi` retornar status code controlado nas próximas N chamadas — habilita QS-7
- [X] T120 [US6] Garantir que `ToolCallDispatcher.HandleApiFailure` resolve depto: sub-agente → `agent.department_id`; orchestrator → `tenants.default_department_id`; nenhum → fallback do T077

**Checkpoint US6**: QS-7 passa; SC-005 medido < 10s.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: ADRs, emenda constitucional, smoke live, documentação, varredura final.

- [X] T121 [P] Mover `ADR-006-001-openai-mock-strategy.md` e `ADR-006-002-frustration-detection-via-prompt.md` de `specs/006-ai-agents/` para `docs/adr/` conforme convenção do projeto (ARCHITECTURE.md §ADRs)
- [X] T122 Atualizar `docs/ARCHITECTURE.md` adicionando entradas dos 2 ADRs na seção "ADRs"
- [X] T123 Criar PR de emenda à constituição: editar `.specify/memory/constitution.md` Principle II (texto proposto em ADR-006-002); incrementar versão para `1.0.1` (PATCH); atualizar `LAST_AMENDED_DATE`; preencher Sync Impact Report
- [ ] T124 [P] Criar `OpenAiLiveSmoke.cs` em `tests/omniDesk.Api.Tests/Smoke/OpenAiLiveSmoke.cs` com `[Trait("openai-live", "true")]` cobrindo 1 cenário fim-a-fim contra OpenAI real (Assistants v2 com `gpt-4o`-mini para custo controlado) — ADR-006-001
- [ ] T125 [P] Atualizar `src/omniDesk.Api/tests/omniDesk.Api.Tests/.runsettings` (ou criar) excluindo `openai-live=true` no filter padrão do CI
- [ ] T126 Rodar `quickstart.md` integralmente (QS-1 a QS-8) em ambiente de dev local; capturar evidências em `quickstart-evidences.md` (mesmo padrão da Spec 005)
- [X] T127 [P] Atualizar `docs/DEPENDENCIES.md` marcando Spec 006 como pronta + descrevendo seus consumidores (Specs 005/007/008/010)
- [ ] T128 Verificar `cross-spec-pendencies.md` — todos os bloqueadores efetivos resolvidos (003-B, 003-C, 005-E); criar issues GitHub para os patches futuros não-bloqueantes (UI default_department_id; auto-distribuição de ticket de IA; badge "ticket de IA" na fila)
- [X] T129 [P] Atualizar `CLAUDE.md` SPECKIT block para `Status: implementado`
- [ ] T130 Code review interno + cleanup: remover endpoints `/api/internal/test-incoming` e `/api/internal/fault-injector` do build de produção (gated por `IHostEnvironment.IsDevelopment` — confirmar que não vazam)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências — pode começar imediatamente.
- **Foundational (Phase 2)**: depende de Phase 1 — bloqueia TODAS as user stories.
- **US1 (Phase 3)**: depende de Phase 2 — entrega MVP base.
- **US2 (Phase 4)**: depende de Phase 2 + reutiliza componentes da US1 (`AgentOrchestrator`, `IncomingMessageWorker`).
- **US3 (Phase 5)**: depende de Phase 2 + reutiliza componentes da US1 + estende `ToolCallDispatcher` da US2 (mas pode rodar em paralelo se a equipe coordenar bem o `ToolCallDispatcher`).
- **US4 (Phase 6)**: depende de Phase 2 — independente das outras (pode rodar em paralelo após Phase 2).
- **US5 (Phase 7)**: depende de Phase 2 — independente das outras.
- **US6 (Phase 8)**: depende de US1 + US2 (precisa do orchestrator funcional E do transbordo).
- **Polish (Phase 9)**: depende de todas as User Stories desejadas.

### User Story Dependencies (operacionais)

- US1 e US2 juntos formam o **MVP** — entregar primeiro e validar antes de iniciar US3+.
- US3 estende US2 via `ToolCallDispatcher.handle handoff_to_agent` — coordenar PR.
- US4 (playground) é independente — bom alvo para paralelo.
- US5 (configurações) é independente — bom alvo para paralelo.
- US6 (resiliência) requer US1+US2 estáveis — agendar último.

### Within Each User Story

- Testes (Testcontainers + MockHttpMessageHandler) escritos antes da implementação, esperam falhar (constituição §VII).
- Domain → Infrastructure → Features → Endpoints → Frontend.
- Cada US tem checkpoint independente — validar QS correspondente antes de avançar.

### Parallel Opportunities

- T004, T005 — pastas backend e frontend.
- T006-T014 — domínio puro, arquivos distintos (todos [P]).
- T016-T018 — EF configurations.
- T034-T037, T039 — testes foundational.
- T040-T042 — testes US1.
- T046-T047 — DTOs US1.
- T059-T060 — frontend types/service US1.
- T065-T067 — testes US2.
- T078-T081 — testes US3.
- T093-T094 — frontend types/service US3.
- T098-T099 — testes US4.
- T106-T108 — testes US5.
- T115-T117 — testes US6.
- T121, T124, T125, T127, T129 — polish.

---

## Parallel Example: User Story 1 — testes em paralelo

```bash
# Após Foundational completo:
Task: "T040 — AiAgentProvisioningTests"
Task: "T041 — ContextBuilderTests"
Task: "T042 — PromptVariableSubstitutorTests"

# Implementação modular paralela:
Task: "T046 — IncomingMessage.cs"
Task: "T047 — OutgoingMessage.cs"
Task: "T059 — ai-agents.types.ts"
Task: "T060 — ai-agents.service.ts"
```

---

## Implementation Strategy

### MVP (US1 + US2)

1. Phase 1 (Setup) → Phase 2 (Foundational).
2. Phase 3 (US1) — Orchestrator atende.
3. **VALIDATE**: QS-1 e QS-2 passam.
4. Phase 4 (US2) — transbordo funciona.
5. **VALIDATE**: QS-3 passa.
6. **MVP DEPLOY READY** — clínica/empresa pode operar com 1 atendente humano + IA.

### Incremental Delivery após MVP

- US3 (sub-agentes) → diferencial competitivo.
- US4 (playground) → qualidade operacional.
- US5 (configurações) → escala/custos.
- US6 (resiliência) → uptime + integrações com observabilidade.
- Polish → ADRs, emenda constitucional, smoke live.

### Parallel Team Strategy

Com 2-3 devs após Foundational:

- Dev A: US1 + US2 (sequencial — MVP path crítico).
- Dev B: US3 (pode iniciar quando T072 `ToolCallDispatcher` estiver merged).
- Dev C: US4 + US5 (independentes — pode atacar simultaneamente).
- US6 e Polish: time todo no fim.

---

## Notes

- [P] tarefas: arquivo distinto, sem dependência pendente.
- [Story] mapeia tarefa à user story para rastreabilidade.
- Cada user story deve ser completável e testável independentemente.
- Verificar testes falham antes de implementar (TDD obrigatório por constituição §VII).
- Commit após cada tarefa ou grupo lógico.
- ADRs movem-se para `docs/adr/` no Polish (Phase 9).
- Gateways stub (`ChannelStubGateway`, `StubTicketCreationGateway`) serão substituídos pelas Specs 007/008 — testes de comportamento devem sobreviver à substituição.

---

## Cobertura de FRs

| FR | Coberto por |
|---|---|
| FR-001, FR-031 | T002, T032, T040, T055 |
| FR-002 | T051, T052 |
| FR-003 | T072, T091 |
| FR-004 | T079, T089 |
| FR-005, FR-006 | T043, T052, T091 |
| FR-007 | T085, T087 |
| FR-008, FR-009 | T084, T086 |
| FR-010 | T081, T088 |
| FR-011 | (decisão de spec — sem código específico) |
| FR-012 | T042, T048 |
| FR-013 | T010, T035, T065, T071, T074 |
| FR-014 | T067, T072, T073 |
| FR-015 | T068, T075 |
| FR-016 | T015, T020, T072, T118-T120 |
| FR-017 | T118 (fallback Orchestrator) |
| FR-018, FR-019 | T031, T037, T115, T116, T118 |
| FR-020 | T118 (mensagem de instabilidade) |
| FR-021 | T030, T039 |
| FR-022, FR-023 | T108, T109, T110, T112 |
| FR-024 | T106, T109, T110 |
| FR-025 | T025, T036, T111 |
| FR-026, FR-027 | T098, T099, T100-T103 |
| FR-028, FR-029 | T023, T024, T038, T086, T087 |
| FR-030 | T030, T039 |
| FR-032 | T082, T091 |
| FR-033 | T069, T072 |

Cobertura completa.
