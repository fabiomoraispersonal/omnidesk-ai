# Implementation Plan: Departamentos e Atendentes

**Branch**: `005-departments-attendants` | **Data**: 2026-05-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/005-departments-attendants/spec.md`

## Summary

Spec **operacional** que entrega a estrutura de times humanos do CRM: cadastro de departamentos com horário comercial e SLA opcional, atendentes vinculados N:N a departamentos, presença em tempo real (`online`/`away`/`offline` com timeout automático), distribuição automática de tickets via round-robin com lock atômico no Redis, transferência manual entre atendentes/departamentos, respostas pré-formadas com substituição de variáveis e sugestão de resposta com IA (sempre com aprovação humana). SLA é puramente visual no MVP — apenas badges amarelo/vermelho no card. Reusa integralmente a infraestrutura multi-tenant das Specs 002/003/004 (schema `tenant_{slug}`, claims JWT, matriz de policies, claims cache, department scoping). Expõe `/api/departments`, `/api/attendants`, `/api/canned-responses`, `/api/conversations/{id}/suggest-reply` e quatro eventos WebSocket consumidos pelo CRM.

## Technical Context

**Backend**: C# .NET 10 — Minimal API + Endpoint Groups (continuação)
**Frontend**: TypeScript — Angular 21 Standalone Components + Signals (CRM em `src/omniDesk.Crm/`)
**ORM**: Entity Framework Core 9 + Migrations (PostgreSQL)
**Storage**:

- PostgreSQL `tenant_{slug}.departments`, `attendants`, `attendant_departments`, `attendant_status`, `canned_responses`
- Redis (`{slug}:attendant_status:{id}`, `{slug}:rr:{department_id}`, `{slug}:ticket_lock:{ticket_id}`, `{slug}:ws:dept:{id}` pub/sub channel)
- MongoDB `{slug}_attendant_status_logs`, `{slug}_ai_suggestion_logs`
- MinIO `tenant-{slug}` (avatares dos atendentes — bucket existente da Spec 003)

**WebSocket**: ASP.NET Core nativo + Redis Pub/Sub (ADR-005). Conexões agrupadas por tenant + escopo (department / attendant).
**Background jobs**: Hangfire — recurring de **inatividade** (varre `online → away` aos 15 min e `away → offline` aos 30 min); fallback para falhas de heartbeat.
**AI**: OpenAI `gpt-4o` via `openai-dotnet` (Spec 002 — Agentes de IA). Prompt do sub-agente vinculado ao departamento entra no contexto da sugestão.
**Testing**: xUnit + Testcontainers (Postgres + Redis reais — sem mock); Angular TestBed (`.spec.ts` co-localizado).
**Target Platform**: Linux ARM64 (Oracle Cloud, Docker `linux/arm64`); Cloudflare Pages (CRM).
**Project Type**: Web service (API .NET 10) + 1 SPA (CRM). Painel Admin **não consome** este módulo.

**Dependências backend** (todas já no stack — nada novo):

| Pacote | Já em uso desde | Propósito |
|---|---|---|
| `Microsoft.EntityFrameworkCore` 9.x | Spec 002 | Domínio + migrations |
| `StackExchange.Redis` | Spec 002 | Cache de presença, lock de ticket, pub/sub |
| `Hangfire` | Spec 003 | Recurring de timeout de presença |
| `MongoDB.Driver` | Spec 003 | Logs de status + telemetria de sugestão IA |
| `Minio` | Spec 003 | Avatares (já provisionado) |
| `FluentValidation.AspNetCore` | Constituição | Validação de payloads |
| `Microsoft.AspNetCore.WebSockets` | .NET base | WebSocket nativo |
| `OpenAI` (openai-dotnet) | Spec 002 — Agentes | Sugestão de resposta |

**Dependências frontend** (built-ins + libs já em uso):

- `@angular/router` Guards + Resolvers para rotas de feature
- `@angular/common/http` para REST + interceptor existente
- Signals (`signal`, `computed`) para estado de presença e fila local
- WebSocket nativo do browser para os 4 eventos
- PrimeNG 21+ (Tabela, Card, Badge, Toast, Dialog, AutoComplete)

**Variáveis de ambiente** (todas existentes, exceto uma):

| Variável | Origem | Uso nesta spec |
|---|---|---|
| `ConnectionStrings__Default` | Spec 002 | Postgres |
| `REDIS_URL` | Spec 003 | Presença, lock, pub/sub |
| `MONGODB_URL` | Spec 003 | Logs |
| `OPENAI_API_KEY` | Spec 002 | Sugestão IA |
| `MAX_SUGGESTION_CONTEXT_MESSAGES` | **NOVA** (default `20`) | Quantas mensagens recentes alimentam a sugestão IA |

**Performance Goals**:

- Round-robin + lock + atribuição: p95 ≤ **150 ms** sob carga típica.
- Mudança de status no toggle até reflexo no painel do supervisor: p95 ≤ **1 s** (SC-004).
- Atribuição duplicada sob 50 pares concorrentes: **0** casos (SC-002).

**Constraints**:

- Round-robin deve ser **memoryless** entre reinícios da API (R3 — usa cursor em Redis com TTL curto).
- Lock de atribuição **deve** ter timeout protetivo (10 s) para liberar caso o handler caia.
- Substituição de variáveis em canned responses **deve** ser pura — sem eval de expressões.
- Sugestão IA **nunca** envia mensagem ao cliente sem ação humana — defense-in-depth (FR-038, SC-007).
- Logs de status no Mongo **não** carregam mensagens de conversa — apenas metadata.

**Scale/Scope**:

- ~10 departamentos por tenant (média), ~100 atendentes ativos por tenant (teto V1).
- Eventos WebSocket simultâneos por tenant: ~150 (atendentes online + supervisor + tenant admin).
- Sugestões IA esperadas: ~50/h por tenant (FR-039 e SC-010 baseiam-se neste número).

## Constitution Check

*GATE: deve passar antes de Phase 0 e ser reavaliado após Phase 1.*

| Princípio | Status | Observação |
|---|---|---|
| I. Multi-Tenant Isolation (NN) | ✅ PASS | Todas as tabelas vivem em `tenant_{slug}.*`. Redis sempre prefixado (`{slug}:attendant_status:*`, `{slug}:ticket_lock:*`, `{slug}:rr:*`, `{slug}:ws:*`). Mongo sempre `{slug}_*_logs`. MinIO avatares no bucket `tenant-{slug}` (já existente). `TenantResolverMiddleware` permanece como o primeiro middleware. |
| II. AI-First, Human-Assisted | ✅ PASS | Sugestão de resposta consome o sub-agente do departamento (Spec 002) e **nunca** envia sem aprovação humana (FR-038, SC-007). Fallback explícito quando o provedor falha (FR-040). |
| III. Channel Agnosticism | ✅ N/A | Sem código de canal — distribuição opera sobre `Ticket` agnóstico (Spec 008). |
| IV. Security e LGPD (NN) | ✅ PASS | Nenhuma PII nova é introduzida. Avatares reutilizam o bucket existente. Logs de status não carregam conteúdo de conversas. Sugestão IA passa por `OPENAI_API_KEY` em variável de ambiente — nunca em código. Soft delete (`is_active=false`) em departamentos e atendentes. |
| V. Simplicity | ✅ PASS | Zero pacote NuGet/npm novo. Round-robin é cursor incremental no Redis (sem libs). Canned responses substituem variáveis com regex puro `\{\{(\w+)\}\}`. SLA é cálculo na hora — sem agendamento. WebSocket reusa pub/sub do projeto. |
| VI. Observability e Auditability | ✅ PASS | Toda transição de status grava documento no Mongo (FR-011). Toda sugestão IA grava telemetria (FR-039). Eventos WebSocket carregam timestamps. Logs Serilog estruturados. |
| VII. Test Discipline | ✅ PASS | Testcontainers (Postgres + Redis reais) para round-robin, lock atômico, timeout de presença, substituição de variáveis. `.spec.ts` co-localizado em cada componente/serviço Angular. Magic strings substituídas por constantes em `Domain/Departments/RedisKeys.cs` e `Domain/Attendants/AttendanceStatus.cs`. |

**Resultado**: Constitution Check **APROVADO** sem ressalvas. Reavaliação pós-Phase 1 — sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/005-departments-attendants/
├── plan.md                                 # Este arquivo
├── research.md                             # Phase 0 — decisões técnicas (R1–R9)
├── data-model.md                           # Phase 1 — entidades + relações
├── quickstart.md                           # Phase 1 — fluxos de validação manual
├── contracts/
│   ├── departments-api.md                  # CRUD de departamentos
│   ├── attendants-api.md                   # CRUD + status de atendentes
│   ├── canned-responses-api.md             # CRUD de respostas pré-formadas + variáveis
│   ├── ai-suggestion-api.md                # Sugestão IA (POST /conversations/{id}/suggest-reply)
│   ├── websocket-events.md                 # 4 eventos
│   └── round-robin-distribution.md         # Algoritmo + lock atômico
├── checklists/
│   └── requirements.md                     # validado no /speckit-specify
└── tasks.md                                # Phase 2 — gerado por /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── omniDesk.Api/
│   ├── Domain/
│   │   ├── Departments/                                # NOVO
│   │   │   ├── Department.cs
│   │   │   ├── DepartmentBusinessHours.cs              # Value Object (start, end, days[])
│   │   │   └── IDepartmentRepository.cs
│   │   ├── Attendants/                                 # NOVO
│   │   │   ├── Attendant.cs
│   │   │   ├── AttendanceStatus.cs                     # enum: Online, Away, Offline
│   │   │   ├── AttendantStatusEntry.cs                 # status atual no Postgres
│   │   │   └── IAttendantRepository.cs
│   │   └── CannedResponses/                            # NOVO
│   │       ├── CannedResponse.cs
│   │       └── CannedResponseVariable.cs               # constantes
│   │
│   ├── Features/
│   │   ├── Departments/                                # NOVO
│   │   │   ├── DepartmentsEndpoints.cs                 # Map(GET, POST, PUT, DELETE)
│   │   │   ├── Commands/{Create,Update,Deactivate}DepartmentCommand.cs
│   │   │   ├── Queries/ListDepartmentsQuery.cs
│   │   │   └── Validators/CreateDepartmentValidator.cs
│   │   ├── Attendants/                                 # NOVO
│   │   │   ├── AttendantsEndpoints.cs
│   │   │   ├── Commands/{Create,Update,Deactivate,UpdateStatus}AttendantCommand.cs
│   │   │   ├── Queries/{ListAttendants,GetAttendantTickets}.cs
│   │   │   └── Validators/CreateAttendantValidator.cs
│   │   ├── CannedResponses/                            # NOVO
│   │   │   ├── CannedResponsesEndpoints.cs
│   │   │   ├── Commands/{Create,Update,Delete}CannedResponseCommand.cs
│   │   │   ├── VariableSubstitution.cs                 # regex + fallback
│   │   │   └── Validators/CannedResponseValidator.cs
│   │   ├── AiSuggestions/                              # NOVO
│   │   │   ├── SuggestReplyEndpoint.cs                 # POST /conversations/{id}/suggest-reply
│   │   │   └── SuggestReplyService.cs                  # chama OpenAI + telemetria Mongo
│   │   └── Distribution/                               # NOVO — domínio de atribuição
│   │       ├── TicketAssignmentService.cs              # round-robin + lock
│   │       ├── RoundRobinCursor.cs                     # Redis-backed cursor
│   │       ├── BusinessHoursEvaluator.cs               # decide transbordo (FR-027–030)
│   │       └── PresenceTimeoutJob.cs                   # Hangfire recurring 1 min
│   │
│   ├── Infrastructure/
│   │   ├── Departments/DepartmentConfiguration.cs      # EF Core
│   │   ├── Attendants/{Attendant,AttendantStatus,AttendantDepartment}Configuration.cs
│   │   ├── CannedResponses/CannedResponseConfiguration.cs
│   │   ├── Presence/                                   # NOVO
│   │   │   ├── PresenceCache.cs                        # Redis: status atual + heartbeat
│   │   │   └── PresenceLogger.cs                       # Mongo: status_logs
│   │   ├── Distribution/
│   │   │   ├── TicketLock.cs                           # Redis SET NX EX wrapper
│   │   │   └── RoundRobinCursorRedis.cs
│   │   ├── WebSockets/                                 # NOVO ou amplia existente
│   │   │   ├── DepartmentEventBus.cs                   # publica em Redis pub/sub
│   │   │   └── AttendantHubHandler.cs                  # WebSocket handler
│   │   └── Persistence/Migrations/Add_Departments_Attendants.sql
│   │
│   ├── tests/omniDesk.Api.Tests/                       # estrutura espelhada (post-spec-004)
│   │   ├── Domain/Departments/{DepartmentBusinessHoursTests, AttendanceStatusTests}.cs
│   │   ├── Features/
│   │   │   ├── Departments/{DepartmentsEndpointsTests, CreateDepartmentValidatorTests}.cs
│   │   │   ├── Attendants/{AttendantsEndpointsTests, UpdateStatusTests}.cs
│   │   │   ├── CannedResponses/{VariableSubstitutionTests, CrudTests}.cs
│   │   │   ├── AiSuggestions/SuggestReplyServiceTests.cs
│   │   │   └── Distribution/
│   │   │       ├── RoundRobinCursorTests.cs
│   │   │       ├── TicketAssignmentServiceTests.cs    # cobre FR-013–018 + SC-002
│   │   │       └── BusinessHoursEvaluatorTests.cs     # cobre FR-027–030
│   │   └── Infrastructure/
│   │       ├── Presence/PresenceCacheTests.cs
│   │       └── Distribution/TicketLockTests.cs        # FR-016 — lock atômico
│   │
│   └── Program.cs                                       # wire-up dos novos endpoints + Hangfire job
│
└── omniDesk.Crm/
    └── src/app/
        ├── core/
        │   └── presence/                                # NOVO
        │       ├── presence.signal.ts
        │       ├── presence.service.ts
        │       └── presence-websocket.service.ts
        └── features/
            ├── departments/                             # NOVO
            ├── attendants/                              # NOVO
            ├── canned-responses/                        # NOVO
            ├── ticket-queue/                            # NOVO
            └── ai-suggestion/                           # NOVO
```

**Structure Decision**: Mantida a arquitetura monorepo. Backend recebe quatro novas features (`Departments/`, `Attendants/`, `CannedResponses/`, `AiSuggestions/`) e um domínio transversal de **distribuição** (`Features/Distribution/`) que consolida round-robin, lock e regra de transbordo. Frontend ganha cinco features no CRM. Nenhum projeto `.csproj` novo, nenhum pacote NuGet/npm novo. Avatares reutilizam o bucket MinIO já provisionado pela Spec 003 (`tenant-{slug}`).

## Complexity Tracking

> **Sem violações da constituição** — esta tabela permanece vazia.

Padrões introduzidos:

- **Round-robin com cursor Redis** (Spec 005-R1): incremento atômico via `INCR` em chave `{slug}:rr:{department_id}` com TTL curto; ao reiniciar a API, recomeça de 0 — comportamento aceito pela A10. Não exige ADR.
- **Lock de atribuição** (Spec 005-R2): pattern padrão `SET key value NX EX 10`. Usado em pequena escala dentro do `TicketAssignmentService`. Não exige ADR.
- **Pause/resume de SLA por horário comercial** (Spec 005-R5): cálculo puro em memória ao renderizar — não há job. Aceitável dado o volume.
