# Implementation Plan: Agentes de IA

**Branch**: `006-ai-agents` | **Data**: 2026-05-08 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/006-ai-agents/spec.md`

## Summary

Spec **AI-core** que entrega a camada de inteligência artificial do OmniDesk. Cada tenant ganha exatamente **um Orchestrator** (clonado do template global do SaaS Admin no provisionamento) e pode criar **N sub-agentes** especializados, cada um vinculado a um departamento. O Orchestrator recebe toda mensagem inicial, consulta a lista de sub-agentes ativos via descritivo curto e roteia via tool call `handoff_to_agent`; sub-agentes podem voltar ao Orchestrator ou rotear entre si. O transbordo para humano ocorre via tool call `transfer_to_human`, que cria ticket no departamento informado (ou no `default_department_id` do tenant quando o agente é o Orchestrator). Após o transbordo a IA não processa mais a conversa.

A integração usa **OpenAI Assistants API** via `openai-dotnet`: cada agente corresponde a um Assistant na OpenAI; cada conversa carrega um único `openai_thread_id` reutilizado em todos os handoffs. O backend monta contexto somente das últimas N mensagens (`ai_settings.context_window_messages`, default 20) para a primeira mensagem; subsequentes são append no thread. Falha 5xx/timeout retorna 1 retry com 3s; falha 401/403 não retenta. Toda execução grava em MongoDB `{slug}_agent_activity_logs`.

A spec **completa** trabalho em curso da Spec 003 — a entidade `Tenant` já carrega `OpenAiApiKeyEnc/Organization/Project` e o template global `agent_templates` já existe no Admin (`/api/admin/agent-templates`). Falta a tabela tenant-scoped `ai_agents` (a migration ainda não foi criada — o `TenantProvisioningJob.CopyAgentTemplatesAsync` já assume `{schema}.agents`, é uma **dívida da Spec 006**) e os dois novos campos em `Tenant` (`default_department_id`, `ai_settings_*`).

Expõe `/api/agents` (CRUD), `/api/agents/{id}/test` (playground), `/api/ai-settings` (avançado), e os pipelines internos `IncomingMessageWorker → AgentOrchestrator → OutgoingMessageWorker` que rodam na fila Hangfire `{slug}:incoming_messages` (já presente no compose). Por **estar à frente das Specs 007 (Live Chat) e 008 (Tickets/WhatsApp)**, esta spec entrega o pipeline com **stubs explícitos** para `IConversationGateway` e `ITicketCreationGateway`, deixando a integração completa para as specs subsequentes — mas com testes de contrato verificando o comportamento esperado por essas specs.

## Technical Context

**Backend**: C# .NET 10 — Minimal API + Endpoint Groups (continuação)
**Frontend**: TypeScript — Angular 21 Standalone Components + Signals (CRM em `src/omniDesk.Crm/`)
**ORM**: Entity Framework Core 9 + Migrations SQL tenant-scoped (padrão do projeto, ver `Add_Tickets_Scaffold.sql`)
**Storage**:

- PostgreSQL `tenant_{slug}.ai_agents`, `ai_settings`, `ai_threads` (aliás de conversa→thread enquanto Spec 007 não chega)
- PostgreSQL `public.tenants` ganha `default_department_id` (FK lógica, sem REFERENCES cross-schema) e `ai_global_max_context_messages`
- Redis `{slug}:incoming_messages` (Hangfire), `{slug}:outgoing_messages`, `{slug}:agent_run:{conversation_id}` (lock de execução), `{slug}:playground:{session_id}` (thread temporário)
- MongoDB `{slug}_agent_activity_logs` (1 doc por run — tokens, latência, ação, erro)
- OpenAI Assistants API (estado externo): 1 Assistant por agente + 1 Thread por conversa

**Background jobs**: Hangfire — 2 workers críticos.

| Worker | Fila | Trigger | Responsabilidade |
|---|---|---|---|
| `IncomingMessageWorker` | `{slug}:incoming_messages` | Adapter de canal enfileira | Resolve agente atual, monta contexto, executa run, processa tool calls, enfileira saída |
| `OutgoingMessageWorker` | `{slug}:outgoing_messages` | `IncomingMessageWorker` enfileira | Adapter de canal entrega ao cliente (Live Chat / WhatsApp) |

**WebSocket**: ASP.NET Core nativo + Redis Pub/Sub (ADR-005) — eventos `agent_message`, `agent_handoff`, `human_handoff` propagados ao CRM e ao widget. Detalhamento por Spec 007 (Live Chat); aqui ficam apenas as **publicações**, o roteamento aos canais é stub.

**AI**: OpenAI **Assistants v2** via `openai-dotnet` (`OpenAI` 2.x). Constituição obriga `gpt-4o` como default e veda LangChain. Variáveis de prompt resolvidas via regex no backend antes de cada `runs.create`.

**Testing**: xUnit + Testcontainers (Postgres + Redis + Mongo reais — sem mock); MockHttpMessageHandler para a OpenAI (única exceção, justificada na Complexity Tracking — não há sandbox oficial). Angular TestBed (`.spec.ts` co-localizado).
**Target Platform**: Linux ARM64 (Oracle Cloud, Docker `linux/arm64`); Cloudflare Pages (CRM).
**Project Type**: Web service (API .NET 10) + 1 SPA (CRM).

**Dependências backend** (todas já em uso desde Specs anteriores — **zero pacote novo**):

| Pacote | Já em uso desde | Propósito nesta spec |
|---|---|---|
| `Microsoft.EntityFrameworkCore` 9.x | Spec 002 | Domínio + migrations |
| `StackExchange.Redis` | Spec 002 | Filas, lock de run, playground |
| `Hangfire` | Spec 003 | Workers de mensagem |
| `MongoDB.Driver` | Spec 003 | `agent_activity_logs` |
| `FluentValidation.AspNetCore` | Constituição | Payloads de CRUD de agentes |
| `OpenAI` (openai-dotnet) | Spec 003 (config), Spec 005 (sugestão) | Assistants API + Threads + Runs |
| `Microsoft.AspNetCore.WebSockets` | .NET base | Publicação de eventos via Redis pub/sub |

**Dependências frontend** (built-ins + libs em uso):

- PrimeNG 21+ (Card, Tabela, Tag, Toast, Dialog, Editor, InputSwitch, Select)
- Reactive Forms para todos os formulários
- Signals para estado local da tela de configuração
- WebSocket nativo do browser para refletir status do playground

**Variáveis de ambiente** (todas existentes — uma adicionada):

| Variável | Origem | Uso nesta spec |
|---|---|---|
| `ConnectionStrings__Default` | Spec 002 | Postgres |
| `REDIS_URL` | Spec 003 | Filas, locks, playground |
| `MONGODB_URL` | Spec 003 | `agent_activity_logs` |
| `OPENAI_API_KEY` | Spec 003 | Fallback global quando tenant não tem chave própria |
| `OPENAI_DEFAULT_MODEL` | Spec 003 (já no `appsettings`) | Default `gpt-4o` |
| `AI_RUN_TIMEOUT_SECONDS` | **NOVA** (default `30`) | Timeout para `runs.poll` antes de aplicar política de retry |
| `AI_RUN_MAX_RETRIES` | **NOVA** (default `1`) | Política da Spec 006 (FR-018) |
| `AI_RUN_RETRY_BACKOFF_SECONDS` | **NOVA** (default `3`) | Política da Spec 006 (FR-018) |
| `AI_PLAYGROUND_TTL_SECONDS` | **NOVA** (default `1800`) | Vida útil do thread temporário do playground |

**Performance Goals**:

- Primeira resposta da IA (mensagem inicial) p95 ≤ **5 s** (constitui SC-002 + SC-002 da Constituição §VI).
- Continuação no mesmo thread p95 ≤ **3 s** (sem reenvio de histórico).
- Decisão de transbordo automático após falha técnica ≤ **10 s** (SC-005).
- Playground p95 ≤ **5 s** (mesmo SLA da resposta real).

**Constraints**:

- **Lock de run por conversa** obrigatório — duas mensagens em paralelo na mesma conversa não podem disparar dois runs simultâneos no mesmo thread (a OpenAI rejeita). Lock Redis `SET NX EX 60` em `{slug}:agent_run:{conversation_id}`.
- **Idempotência do worker**: re-entrega da fila não deve gerar dois runs para a mesma `(conversation_id, message_id)` — chave de idempotência derivada do `message_id` em Redis com TTL 24 h.
- **`current_agent_id = null` significa**: sob controle do Orchestrator OU sob controle humano (após transbordo). Diferença é discriminada via `conversation.handed_off_to_human_at`.
- **Soft delete sempre**: sub-agentes com qualquer registro em `agent_activity_logs` ou `conversations.current_agent_id` histórico **nunca** são removidos fisicamente.
- **Substituição de variáveis**: regex puro `\{\{(\w+)\}\}`, fallback para string vazia se variável ausente — sem `eval`.
- **Chave OpenAI do tenant**: armazenada em `Tenant.OpenAiApiKeyEnc` (já existe — campo `Enc`/criptografado por convenção). Resolução por execução: tenta tenant; se vazia, cai para `OPENAI_API_KEY`.
- **Threads OpenAI órfãos**: ao fechar conversa (Spec 007), o thread permanece na OpenAI até janela de retenção configurada — **fora de escopo desta spec** (Spec futura de cost management).
- **Timeout do `runs.poll`**: máximo 30 s; após esse limite é tratado como falha 5xx para fins da política de retry.

**Scale/Scope**:

- ~50 sub-agentes por tenant (cap operacional, não tecnicamente bloqueado).
- ~500 mensagens/hora por tenant (carga V1).
- ~1k execuções de tool call/dia por tenant.
- Playground: ≤ 5 sessões ativas/tenant (servidor descarta a mais antiga em LRU).

## Constitution Check

*GATE: deve passar antes de Phase 0 e ser reavaliado após Phase 1.*

| Princípio | Status | Observação |
|---|---|---|
| I. Multi-Tenant Isolation (NN) | ✅ PASS | Todas as tabelas vivem em `tenant_{slug}.*`. Filas Redis sempre prefixadas (`{slug}:incoming_messages`, `{slug}:outgoing_messages`, `{slug}:agent_run:*`, `{slug}:playground:*`). Mongo sempre `{slug}_agent_activity_logs`. **Public schema só recebe os 2 campos novos em `tenants`** — `default_department_id` e `ai_global_max_context_messages` (FK lógica; cross-schema é resolvido em runtime). `TenantResolverMiddleware` permanece como o primeiro middleware. |
| II. AI-First, Human-Assisted | ⚠️ DESVIO REGISTRADO | A constituição diz: _"detect handoff triggers: explicit keywords AND frustration signals (3+ unresolved exchanges)"_. A Spec 006 §11 P3 decidiu **derrubar a heurística de 3-trocas-frustradas** e delegar 100% da detecção de frustração ao prompt — mantendo apenas o gatilho hardcoded de palavras-chave. Justificativa em **Complexity Tracking**. Item será propagado a uma emenda **PATCH** da constituição (Principle II) ao final do plano. Demais itens do princípio (handoff completo de contexto, sem dead-ends, atendente vê histórico completo) são preservados — FR-014/015/017. |
| III. Channel Agnosticism | ✅ PASS | O Orchestrator opera sobre `IncomingMessage` / `OutgoingMessage` agnósticos. Adapters (Live Chat, WhatsApp) ficam fora desta spec — entram nas Specs 007/008. Aqui apenas declaro as **interfaces** (`IConversationGateway`, `ITicketCreationGateway`) com stubs. |
| IV. Security e LGPD (NN) | ✅ PASS | `Tenant.OpenAiApiKeyEnc` permanece criptografado em coluna dedicada (já existe). Logs nunca registram conteúdo de mensagem do cliente — apenas tokens, latência e ação. Dados em ARM64 BR. `agent_activity_logs` sem PII. Soft delete em sub-agentes. Variáveis de ambiente para timeouts e modelo default — sem hardcode. |
| V. Simplicity | ✅ PASS | Zero pacote NuGet/npm novo. Reusa OpenAI SDK já configurado. Reusa Hangfire/Redis/Mongo já provisionados. Nenhuma camada de orquestração extra (sem LangChain, sem Semantic Kernel — vedados pela constituição). Política de retry trivial (1 retry, 3 s). |
| VI. Observability e Auditability | ✅ PASS | Cada run grava `agent_activity_logs` (tenant, conversation, agent, action, tokens, latência, erro). Toda transição AI↔AI e AI↔Humano é evento discreto timestamped no Mongo. Conforme constituição §VI, expõe primary metric "median time to first AI response" via `latency_ms` agregado. Serilog estruturado em todos os endpoints e workers. |
| VII. Test Discipline | ⚠️ DESVIO JUSTIFICADO | Testcontainers para Postgres/Redis/Mongo (real) — alinhado. **Exceção**: chamadas à OpenAI usam `MockHttpMessageHandler` ou um `OpenAiClientStub` interno. Justificativa em Complexity Tracking — OpenAI não oferece sandbox/replay de Assistants. Cobrimos contrato vs. resposta real via 1 teste de smoke `[Trait("openai-live", "true")]` rodado fora do CI principal. Frontend `.spec.ts` co-localizado, sem magic strings (constantes em `Domain/AiAgents/AgentType.cs`, `Domain/AiAgents/ToolNames.cs`, `Infrastructure/Queues/QueueNames.cs`). |

**Resultado**: Constitution Check **APROVADO com 2 desvios documentados** abaixo. Reavaliação pós-Phase 1 — sem mudanças adicionais.

## Project Structure

### Documentation (this feature)

```text
specs/006-ai-agents/
├── plan.md                                       # Este arquivo
├── research.md                                   # Phase 0 — decisões técnicas (R1–R12)
├── data-model.md                                 # Phase 1 — entidades + relações + migrations
├── quickstart.md                                 # Phase 1 — fluxos de validação manual
├── contracts/
│   ├── agents-api.md                             # CRUD de agentes (CRM)
│   ├── ai-settings-api.md                        # Configurações avançadas
│   ├── playground-api.md                         # POST /agents/{id}/test
│   ├── tool-calls.md                             # handoff_to_agent / transfer_to_human / check_availability / create_appointment
│   ├── conversation-gateway.md                   # IConversationGateway (stub para Specs 007/008)
│   ├── ticket-creation-gateway.md                # ITicketCreationGateway (stub para Spec 008)
│   └── agent-runtime-events.md                   # Eventos Mongo + WebSocket pub/sub
├── checklists/
│   └── requirements.md                           # validado no /speckit-specify
├── ADR-006-001-openai-mock-strategy.md           # Justifica HttpMessageHandler mock
├── ADR-006-002-frustration-detection-via-prompt.md  # Emenda à constituição §II
├── cross-spec-pendencies.md                      # Varredura final (specs 001-005)
└── tasks.md                                      # Phase 2 — gerado por /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── omniDesk.Api/
│   ├── Domain/
│   │   ├── AiAgents/                                       # NOVO
│   │   │   ├── AiAgent.cs
│   │   │   ├── AgentType.cs                                # enum: Orchestrator, SubAgent
│   │   │   ├── ToolNames.cs                                # const string handoff_to_agent etc.
│   │   │   ├── AgentVariableNames.cs                       # const string company_name, department_name, attendant_name
│   │   │   ├── AgentActivityAction.cs                      # enum: Respond, HandoffToAgent, TransferToHuman, ApiError
│   │   │   ├── AgentActivityLog.cs                         # POCO Mongo
│   │   │   └── IAiAgentRepository.cs
│   │   ├── AiSettings/                                     # NOVO
│   │   │   ├── AiSettings.cs                               # 1:1 tenant
│   │   │   └── IAiSettingsRepository.cs
│   │   ├── AiThreads/                                      # NOVO — substituirá entidade `conversations` da Spec 007
│   │   │   ├── AiThread.cs                                 # transitional: id, tenant_id, openai_thread_id, current_agent_id, handed_off_to_human_at
│   │   │   └── IAiThreadRepository.cs
│   │   └── Tenants/Tenant.cs                                # MODIFICADO: + default_department_id (Guid?), + ai_settings (1:1)
│   │
│   ├── Features/
│   │   ├── AiAgents/                                       # NOVO
│   │   │   ├── AiAgentsEndpoints.cs                        # /api/agents — CRUD + toggle
│   │   │   ├── Commands/{Create,Update,Toggle,SoftDelete}AiAgentCommand.cs
│   │   │   ├── Queries/{ListAiAgents,GetAiAgent}.cs
│   │   │   ├── Validators/CreateAiAgentValidator.cs
│   │   │   ├── Playground/PlaygroundEndpoint.cs            # POST /agents/{id}/test
│   │   │   └── Variables/PromptVariableSubstitutor.cs      # regex + fallback
│   │   ├── AiSettings/                                     # NOVO
│   │   │   ├── AiSettingsEndpoints.cs                      # GET/PUT /api/ai-settings
│   │   │   └── Validators/UpdateAiSettingsValidator.cs
│   │   └── AgentRuntime/                                   # NOVO — coração desta spec
│   │       ├── AgentOrchestrator.cs                        # ProcessAsync(IncomingMessage, ct)
│   │       ├── AgentResolver.cs                            # decide qual agente recebe a mensagem
│   │       ├── ContextBuilder.cs                           # monta contexto (N últimas mensagens + variáveis)
│   │       ├── HandoffKeywordDetector.cs                   # PT-BR — FR-013
│   │       ├── ToolCallDispatcher.cs                       # roteia handoff_to_agent / transfer_to_human
│   │       ├── RetryPolicy.cs                              # 1 retry @ 3s, sem retry para 401/403
│   │       ├── IncomingMessageWorker.cs                    # Hangfire worker fila incoming
│   │       ├── OutgoingMessageWorker.cs                    # Hangfire worker fila outgoing
│   │       ├── IncomingMessage.cs                          # DTO interno agnóstico
│   │       ├── OutgoingMessage.cs                          # DTO interno agnóstico
│   │       ├── IConversationGateway.cs                     # STUB para Spec 007
│   │       ├── ITicketCreationGateway.cs                   # STUB para Spec 008
│   │       └── ChannelStubGateway.cs                       # impl temporária — loga + persiste em ai_threads
│   │
│   ├── Infrastructure/
│   │   ├── AiAgents/AiAgentConfiguration.cs                # EF Core
│   │   ├── AiSettings/AiSettingsConfiguration.cs
│   │   ├── AiThreads/AiThreadConfiguration.cs
│   │   ├── OpenAi/                                         # NOVO — client wrappers
│   │   │   ├── IAssistantsApi.cs                           # camada thin sobre openai-dotnet
│   │   │   ├── AssistantsApi.cs                            # impl real (Assistant CRUD, Thread, Run)
│   │   │   ├── OpenAiKeyResolver.cs                        # tenant > global
│   │   │   └── OpenAiToolDefinitions.cs                    # JSON Schema das 4 tools
│   │   ├── Queues/                                         # NOVO ou amplia
│   │   │   ├── QueueNames.cs                               # constantes
│   │   │   ├── IncomingMessagePublisher.cs
│   │   │   └── OutgoingMessagePublisher.cs
│   │   ├── ActivityLogs/                                   # NOVO
│   │   │   └── AgentActivityLogger.cs                      # Mongo writer
│   │   ├── Persistence/Migrations/Add_AiAgents_AiSettings.sql   # NOVO (cria ai_agents, ai_settings, ai_threads)
│   │   ├── Persistence/Migrations/Add_DefaultDepartmentId_To_Tenants.sql  # NOVO (public schema)
│   │   └── Provisioning/TenantProvisioningJob.cs           # MODIFICADO — INSERT em ai_agents + cria ai_settings
│   │
│   ├── tests/omniDesk.Api.Tests/                           # estrutura espelhada
│   │   ├── Domain/AiAgents/{AgentTypeTests, ToolNamesTests}.cs
│   │   ├── Features/
│   │   │   ├── AiAgents/{AiAgentsEndpointsTests, ValidatorsTests, PlaygroundEndpointTests}.cs
│   │   │   ├── AiSettings/AiSettingsEndpointsTests.cs
│   │   │   └── AgentRuntime/
│   │   │       ├── AgentOrchestratorTests.cs               # cobre US1, US3, FR-001..006
│   │   │       ├── HandoffKeywordDetectorTests.cs          # cobre FR-013
│   │   │       ├── ToolCallDispatcherTests.cs              # cobre US2, US3, FR-014..016
│   │   │       ├── RetryPolicyTests.cs                     # cobre US6, FR-018..020
│   │   │       ├── ContextBuilderTests.cs                  # cobre FR-022..023
│   │   │       ├── PromptVariableSubstitutorTests.cs       # cobre FR-012
│   │   │       ├── IncomingMessageWorkerTests.cs           # idempotência + lock
│   │   │       └── OpenAiKeyResolverTests.cs               # cobre FR-025
│   │   ├── Infrastructure/
│   │   │   ├── ActivityLogs/AgentActivityLoggerTests.cs    # cobre FR-021, FR-030
│   │   │   ├── OpenAi/AssistantsApiContractTests.cs        # MockHttpMessageHandler
│   │   │   └── Provisioning/AiAgentProvisioningTests.cs    # cobre US1 cenário 1, FR-031
│   │   └── Smoke/OpenAiLiveSmoke.cs                        # [Trait("openai-live")] — fora do CI principal
│   │
│   └── Program.cs                                          # wire-up dos endpoints + workers Hangfire
│
└── omniDesk.Crm/
    └── src/app/
        └── features/
            └── ai-agents/                                  # NOVO
                ├── ai-agents.routes.ts                     # lazy
                ├── pages/
                │   ├── agents-list/                        # cards
                │   ├── agent-edit/                         # form + playground
                │   └── ai-settings/                        # configurações avançadas
                ├── shared/
                │   ├── agent-card.component.ts
                │   ├── playground-pane.component.ts        # input + resposta + clear
                │   └── prompt-variables-helper.component.ts
                └── data-access/
                    ├── ai-agents.service.ts                # HttpClient + Signals
                    └── ai-agents.types.ts
```

**Structure Decision**: Mantida a arquitetura monorepo. Backend recebe três novas features no API (`AiAgents/`, `AiSettings/`, `AgentRuntime/`) e dois novos serviços de infraestrutura (`OpenAi/`, `ActivityLogs/`). Frontend ganha uma feature lazy-loaded `ai-agents/` no CRM. **Nenhum projeto `.csproj` novo**, **nenhum pacote NuGet/npm novo**. A entidade transitional `AiThread` existe **apenas** enquanto a Spec 007 (Live Chat) não introduz `Conversation` definitiva — quando isso ocorrer, `AiThread` é absorvida via migration de migração de dados.

Pendências cruzadas (specs 001–005) consolidadas no arquivo separado [`cross-spec-pendencies.md`](cross-spec-pendencies.md), gerado pela varredura final deste plan.

## Complexity Tracking

> **2 violações constitucionais documentadas e justificadas** — ADRs vinculadas.

| Violação | Princípio | Por que é necessário | Alternativa simples rejeitada porque… | ADR |
|---|---|---|---|---|
| **Detecção de frustração 100% via prompt — sem heurística "3+ trocas" hardcoded** | II. AI-First (não-NN) | A heurística "3 trocas sem resolução" é arbitrária e gera transbordos prematuros em conversas legítimas (ex.: cliente comparando 3 planos antes de escolher). O prompt do Orchestrator/sub-agente tem contexto semântico para identificar frustração de forma muito mais precisa. Decisão registrada em Spec 006 §11 P3. | Manter a heurística geraria transbordos falso-positivos e degradaria SC-AI-resolution-rate (>40% sem handoff). Híbrido (heurística + prompt) duplica responsabilidade e cria comportamento difícil de explicar a tenants. | [ADR-006-002](ADR-006-002-frustration-detection-via-prompt.md) — emenda **PATCH** ao Principle II da constituição |
| **Mock de OpenAI nos testes de integração** | VII. Test Discipline | A OpenAI não oferece sandbox/replay para Assistants v2 (apenas para Chat Completions clássico). Rodar Assistants real em CI custa ≈ $0.02/run × 200 testes = $4/PR e introduz flakiness por rate-limits e variação de resposta. | Testes só com OpenAI live (sem mock) tornariam o CI lento e caro. Pular testes de integração no runtime quebraria a cobertura dos workers. Solução híbrida: `MockHttpMessageHandler` para fluxo + 1 smoke `[Trait("openai-live")]` rodado fora do CI principal. | [ADR-006-001](ADR-006-001-openai-mock-strategy.md) |

### Padrões introduzidos sem violação (sem ADR exigida)

- **Lock de run por conversa**: `SET key value NX EX 60` em `{slug}:agent_run:{conversation_id}` — pattern já validado em Spec 005 (`TicketLock`).
- **Idempotência por message_id**: chave Redis `{slug}:msg_idempo:{message_id}` com `SET NX EX 86400`. Padrão de fila comum.
- **Política de retry ad-hoc**: classe `RetryPolicy` simples (1 retry com 3s) — Polly não foi adicionado por sobrecarga não justificada (V).
- **Resolução de chave OpenAI tenant > global**: implementado em `OpenAiKeyResolver` com fallback explícito.
- **`AiThread` transitional**: registrado em research.md R10 — vida útil até Spec 007 substituir.
