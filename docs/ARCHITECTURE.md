# ARCHITECTURE.md — OmniDesk CRM
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

> Este documento é a fonte da verdade técnica do projeto. Todo código gerado deve seguir as decisões aqui registradas. Não desvie sem registrar um ADR (Architecture Decision Record).

---

## 1. Visão Geral da Arquitetura

OmniDesk é composto por três aplicações independentes que se comunicam via API:

```
┌─────────────────────┐     ┌─────────────────────┐
│   admin-frontend    │     │    crm-frontend      │
│   (Angular 19)      │     │    (Angular 19)      │
│   Uso interno       │     │   Uso do tenant      │
└────────┬────────────┘     └──────────┬───────────┘
         │                             │
         └──────────────┬──────────────┘
                        │ HTTPS / REST + WebSocket
                        ▼
         ┌──────────────────────────────┐
         │         omniDesk-api         │
         │      (C# .NET 10 Minimal)    │
         │                              │
         │  ┌─────────┐ ┌────────────┐  │
         │  │  REST   │ │ WebSocket  │  │
         │  │Endpoints│ │  (Chat)    │  │
         │  └─────────┘ └────────────┘  │
         └──────┬───────────────────────┘
                │
    ┌───────────┼────────────────────────┐
    ▼           ▼            ▼           ▼
┌────────┐ ┌────────┐ ┌──────────┐ ┌────────┐
│Postgres│ │ Redis  │ │ MongoDB  │ │ MinIO  │
│(dados) │ │(fila/  │ │  (logs   │ │(arquiv │
│        │ │ cache) │ │  eventos)│ │   os)  │
└────────┘ └────────┘ └──────────┘ └────────┘
```

---

## 2. Stack Tecnológica

### 2.1 Backend — omniDesk-api

| Componente | Tecnologia | Versão |
|---|---|---|
| Runtime | .NET | 11 |
| Estilo de API | Minimal API | — |
| ORM | Entity Framework Core | 9.x |
| Validação | FluentValidation | latest |
| Autenticação | JWT Bearer + Refresh Token | — |
| WebSocket | ASP.NET Core WebSockets nativo | — |
| Fila/Background | Hangfire (jobs) + Redis (fila) | — |
| Logs estruturados | Serilog → MongoDB | — |
| Testes | xUnit + Testcontainers | — |

### 2.2 Frontend — admin-frontend e crm-frontend

| Componente | Tecnologia | Versão |
|---|---|---|
| Framework | Angular | 19 (Standalone Components) |
| Linguagem | TypeScript | 5.x |
| Componentes UI | PrimeNG | 17+ |
| Estilização | PrimeNG Theming + CSS Custom Properties | — |
| Estado local | Angular Signals | built-in |
| Formulários | Reactive Forms + FluentValidation (frontend) | built-in |
| HTTP Client | Angular HttpClient + Interceptors | built-in |
| Máscaras | ngx-mask | latest |
| Datas / Timezone | date-fns + date-fns-tz | latest |
| WebSocket client | native browser WebSocket | — |
| Testes | Karma + Jasmine (unit, co-localizados como `.spec.ts`) | built-in Angular CLI |

### 2.3 Bancos de Dados

| Banco | Uso | Estratégia |
|---|---|---|
| PostgreSQL 16 | Dados relacionais (todos os tenants) | Schema por tenant |
| Redis 7 | Fila de mensagens, cache de sessão, pub/sub WebSocket | Shared, prefix por tenant |
| MongoDB | Logs de eventos, histórico de conversas raw | Collection por tenant |
| MinIO | Arquivos enviados em conversas (imagens, docs) | Bucket por tenant |

### 2.4 Serviços Externos

| Serviço | Finalidade | SDK |
|---|---|---|
| OpenAI API | Motor dos Agentes de IA | openai-dotnet |
| WhatsApp Business API (Meta) | Canal WhatsApp | HTTP direto (webhook) |
| SendGrid | E-mails transacionais | SendGrid .NET SDK |

---

## 3. Estrutura de Repositório

```
omniDesk/
├── .antigravity/
│   └── rules.md                  ← Regras do projeto para o agente IA
│
├── docs/
│   ├── PRD.md
│   ├── ARCHITECTURE.md           ← este arquivo
│   ├── DEPENDENCIES.md           ← Grafo de dependências entre specs
│   └── specs/
│       ├── 01-standards.spec.md  ← Padrões técnicos globais (Angular design, i18n)
│       ├── 02-auth.spec.md       ← Autenticação (JWT, refresh token, 2FA)
│       ├── 03-tenants.spec.md    ← Provisionamento multi-tenant
│       ├── 04-roles.spec.md      ← Roles e permissões
│       ├── 05-departments.spec.md ← Departamentos e atendentes
│       ├── 06-ai-agents.spec.md  ← Agentes de IA e orquestração
│       ├── 07-live-chat.spec.md  ← Widget de live chat
│       ├── 08-whatsapp.spec.md   ← Integração WhatsApp Business
│       ├── 09-tickets.spec.md    ← Tickets / CRM / Kanban
│       ├── 10-notifications.spec.md ← Notificações in-app e push
│       ├── 11-agenda.spec.md     ← Agenda e catálogo de serviços
│       └── 12-audit.spec.md      ← Auditoria e observabilidade
│
├── src/
│   ├── omniDesk.Api/             ← Projeto principal .NET 11 (Minimal API)
│   │   ├── Features/             ← Um diretório por módulo (Auth, Tickets, etc.)
│   │   ├── Domain/               ← Entidades, value objects
│   │   ├── Infrastructure/       ← EF Core, Redis, MongoDB, MinIO
│   │   ├── Agents/               ← OpenAI Agents SDK integration
│   │   ├── Hubs/                 ← WebSocket handlers (SignalR)
│   │   ├── Middleware/           ← TenantResolver, Auth, Logging
│   │   ├── tests/                ← Testes do back-end (xUnit + Testcontainers)
│   │   │   └── omniDesk.Api.Tests/
│   │   │       ├── Domain/           ← espelha src/omniDesk.Api/Domain/
│   │   │       ├── Features/         ← espelha src/omniDesk.Api/Features/
│   │   │       ├── Infrastructure/   ← espelha src/omniDesk.Api/Infrastructure/
│   │   │       ├── Helpers/          ← TestWebApplicationFactory, AuthorizationFixture
│   │   │       └── Performance/      ← benchmarks (p95, latência)
│   │   └── Program.cs
│   │
│   ├── omniDesk.Admin/           ← Angular 19 — painel admin SaaS
│   │   └── src/
│   │       ├── app/
│   │       │   ├── core/         ← Guards, interceptors, services singleton
│   │       │   ├── shared/       ← Componentes, pipes, validators reutilizáveis
│   │       │   ├── features/     ← Módulos de feature (tenants, etc.)
│   │       │   └── layout/       ← Header, sidebar, footer
│   │       └── styles/           ← tokens.css, themes/
│   │
│   └── omniDesk.Crm/             ← Angular 19 — CRM do tenant
│       └── src/
│           ├── app/
│           │   ├── core/
│           │   ├── shared/
│           │   ├── features/     ← chat, tickets, agenda, whatsapp, etc.
│           │   └── layout/
│           └── styles/
│
├── assets/
│   └── brand/                    ← Logotipos e marca (fonte de verdade)
│       ├── README.md
│       ├── logo.svg
│       ├── logo-icon.svg
│       └── favicon.ico
│
└── infra/
    ├── docker-compose.yml        ← Dev local completo
    └── docker-compose.prod.yml   ← Produção

⚠️  **Testes ficam dentro do próprio projeto** — não há pasta `tests/` no root.
    - Back-end: `src/omniDesk.Api/tests/omniDesk.Api.Tests/`, organizado por
      camada (`Domain/`, `Features/`, `Infrastructure/`) espelhando a topologia
      do código testado.
    - Angular: `.spec.ts` co-localizados em cada projeto (`omniDesk.Admin`,
      `omniDesk.Crm`), ao lado de cada componente/serviço — padrão Angular CLI.
```


---

## 4. Multi-Tenant — Isolamento por Schema

### 4.1 Estratégia

Cada tenant tem um schema próprio no Postgres. A resolução do tenant é feita por **subdomínio** no middleware da API.

```
Request: https://clinica-abc.omnideskcm.com.br/api/tickets
  → TenantResolverMiddleware extrai "clinica-abc"
  → Busca tenant no schema "public.tenants"
  → Seta DbContext para usar schema "tenant_clinica_abc"
  → Request segue normalmente
```

### 4.2 Tabelas por Camada

**Schema `public` (sistema):**
- `tenants` — cadastro de clientes do SaaS
- `tenant_configs` — configurações por tenant (webhook URLs, chaves de API)

**Schema `tenant_{slug}` (por cliente):**
- Todas as demais tabelas: users, departments, agents, tickets, conversations, messages, appointments, pipelines, etc.

### 4.3 Convenções EF Core

```csharp
// TenantDbContext sempre usa o schema do tenant correto
public class TenantDbContext : DbContext
{
    private readonly string _schema;

    public TenantDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        _schema = $"tenant_{tenantContext.Slug}";
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(_schema);
        // ... entity configurations
    }
}
```

### 4.4 Redis — Prefixo por Tenant

Todas as chaves Redis devem usar o padrão: `{tenant_slug}:{recurso}:{id}`

Exemplos:
- `clinica_abc:session:usr_123`
- `clinica_abc:conversation:conv_456`
- `clinica_abc:queue:messages`

### 4.5 MongoDB — Collection por Tenant

Padrão de nome de collection: `{tenant_slug}_events`, `{tenant_slug}_messages_raw`

### 4.6 MinIO — Bucket por Tenant

Padrão de bucket: `tenant-{slug}` (lowercase, hífens)

---

## 5. Autenticação e Autorização

### 5.1 Estratégia

- **JWT Bearer Token** com refresh token
- Tokens têm vida curta: access token = 15min, refresh token = 7 dias
- Refresh token armazenado em httpOnly cookie
- Claim obrigatória em todo token: `tenant_slug`, `user_id`, `role`

### 5.2 Roles

| Role | Acesso |
|---|---|
| `saas_admin` | Painel admin — acesso total entre tenants |
| `tenant_admin` | CRM — acesso total no próprio tenant |
| `tenant_attendant` | CRM — acesso restrito a tickets/conversas do próprio atendente ou departamento |

### 5.3 Fluxo de Auth

```
1. POST /auth/login → retorna { access_token, expires_in }
                    → seta httpOnly cookie com refresh_token
2. Requests → Authorization: Bearer {access_token}
3. POST /auth/refresh → usa cookie refresh_token → retorna novo access_token
4. POST /auth/logout → invalida refresh_token no Redis
```

### 5.4 Framework de Autorização (Spec 004 — Roles e Permissões)

A matriz de permissões é a **fonte única de verdade**: vive na spec 004 (`specs/004-roles-permissions/spec.md` §§4.1–4.12) e é consumida por contrato por todas as demais specs. Sem regras de autorização locais.

| Camada | Responsabilidade |
|---|---|
| `Domain/Authorization/Roles` | constantes de role (`saas_admin`, `tenant_admin`, `supervisor`, `attendant`) |
| `Domain/Authorization/Permissions` (`Policies`) | nomes de policies (`PainelAdmin.Access`, `Tickets.ViewAll`, …) |
| `Features/Authorization/Policies/AuthorizationPoliciesRegistration` | `AddPolicy()` para cada entrada da matriz |
| `Features/Authorization/Policies/RoleRequirement[Handler]` | hierarquia + `Exact` + handoff para impersonation |
| `Features/Authorization/Policies/ForbidsDuringImpersonationRequirement` | bloqueia ações sensíveis com `impersonating: true` |
| `Infrastructure/Authorization/ClaimsTransformer` | injeta `role`, `is_active`, `dept_ids` por request (cache Redis 60 s) |
| `Infrastructure/Authorization/ClaimsCache` | chave `{tenant_slug}:user:{user_id}:claims` |
| `Infrastructure/Authorization/DepartmentScopeFilter` | primitiva `IQueryable<T>.ForCurrentUserScope()` para o escopo do attendant |
| `Features/Authorization/Impersonation/ImpersonationTokenIssuer` | JWT de 5 min (TTL via `IMPERSONATION_JWT_TTL_SECONDS`, máx 600 s) |
| `Features/Authorization/Impersonation/ImpersonationAuditEnricher` | enricher Serilog: `Impersonating`, `ImpersonatedBy`, `Jti` |
| `Features/Authorization/UserLifecycle/{Deactivate,Reactivate}UserCommand` | invalida claims cache + refresh tokens em ≤ 1 s |
| `Features/Authorization/UserLifecycle/LastTenantAdminGuard` | bloqueia desativação do último `tenant_admin` ativo |

Cross-link com [contracts/authorization-policies.md](../specs/004-roles-permissions/contracts/authorization-policies.md), [contracts/department-scoping.md](../specs/004-roles-permissions/contracts/department-scoping.md) e [contracts/impersonation-token.md](../specs/004-roles-permissions/contracts/impersonation-token.md).

**Regras de aplicação obrigatória**:

- Todo endpoint protegido **deve** referenciar uma constante `Policies.*` — nunca string literal.
- `RequireAuthorization(Policies.PainelAdminAccess)` é aplicado ao group `/api/admin`.
- Endpoints sem policy declarada caem no default `RequireAuthorization()` (autenticação obrigatória).
- Negação de autorização gera log `Warning` com `{user_id, role, policy, tenant_slug, endpoint}` e responde com payload PT-BR genérico em produção.

---

### 5.5 Departamentos e Atendentes (Spec 005)

Spec 005 entrega a estrutura humana do CRM: departamentos com horário comercial e SLA, atendentes vinculados N:N a departamentos, presença em tempo real, distribuição automática de tickets, transferência manual, respostas pré-formadas e sugestão de IA. Reusa toda a infraestrutura das Specs 002–004.

| Camada | Responsabilidade |
|---|---|
| `Domain/Departments`, `Domain/Attendants`, `Domain/CannedResponses`, `Domain/Tickets` (scaffold) | Entidades de domínio |
| `Features/Departments`, `Features/Attendants`, `Features/CannedResponses` | CRUD endpoints + validators |
| `Features/Distribution/TicketAssignmentService` | Round-robin atômico (Redis cursor) + lock (`SET NX EX`) + fan-out WebSocket |
| `Features/Distribution/BusinessHoursEvaluator` | Disponibilidade do departamento + SLA com pause/resume |
| `Features/Distribution/SlaCalculator` | Status `ok`/`warning`/`overdue`/`not_configured` por phase |
| `Features/Distribution/PresenceTimeoutJob` | Hangfire `*/1 * * * *` — `online→away` (15 min), `away→offline` (30 min) |
| `Features/Distribution/{Pickup,Transfer}TicketEndpoint` | Manual pickup + transferência entre attendant/dept |
| `Features/AiSuggestions/SuggestReplyService` | Consome `IAgentRuntime` (Spec 002) + OpenAI; nunca envia sem aprovação humana |
| `Features/AiSuggestions/AiSuggestionLogger` | Mongo `{slug}.ai_suggestion_logs` (FR-038, SC-007) |
| `Infrastructure/Presence/{PresenceCache,PresenceLogger}` | Redis (TTL 5 min) + Mongo `{slug}.attendant_status_logs` |
| `Infrastructure/Distribution/{TicketLock,RoundRobinCursorRedis}` | Primitivas atômicas |
| `Infrastructure/WebSockets/{DepartmentEventBus,AttendantHubHandler}` | Pub/sub Redis + handler nativo (ADR-005, sem SignalR) |

**Eventos WebSocket**: `attendant.status_changed`, `ticket.assigned`, `ticket.transferred`, `ticket.queued`. Canais: `{slug}:ws:tenant`, `{slug}:ws:dept:{id}`, `{slug}:ws:attendant:{id}`.

**Garantias críticas**:

- **SC-002**: 0 atribuições duplicadas em 50 pares concorrentes (`ConcurrentPickupTests`).
- **SC-003**: round-robin com diff máximo de 1 ticket em 100 atribuições (`RoundRobinCursorTests`).
- **SC-004**: status_changed entrega no painel em ≤ 1 s p95 (`WebSocketLatencyBenchmark`).
- **SC-005**: timeout de presença detectado em ≤ 30 s do limite (`PresenceTimeoutJobTests`).
- **SC-007**: 0 mensagens enviadas pelo fluxo de IA sem ação humana (`SuggestionAuditTests`).
- **Performance**: assignment p95 ≤ 150 ms (`DistributionBenchmark`).

Cross-link: [contracts/round-robin-distribution.md](../specs/005-departments-attendants/contracts/round-robin-distribution.md), [contracts/websocket-events.md](../specs/005-departments-attendants/contracts/websocket-events.md), [contracts/ai-suggestion-api.md](../specs/005-departments-attendants/contracts/ai-suggestion-api.md).

---

## 6. Endpoints — Padrões Minimal API

### 6.1 Convenções de URL

```
GET    /api/{recurso}              → listar (com paginação)
GET    /api/{recurso}/{id}         → detalhar
POST   /api/{recurso}              → criar
PUT    /api/{recurso}/{id}         → atualizar (full)
PATCH  /api/{recurso}/{id}         → atualizar parcial
DELETE /api/{recurso}/{id}         → remover

Recursos aninhados:
GET    /api/departments/{id}/attendants
POST   /api/tickets/{id}/messages
PATCH  /api/tickets/{id}/status
```

### 6.2 Padrão de Response

```json
// Sucesso
{
  "success": true,
  "data": { ... },
  "meta": { "page": 1, "per_page": 20, "total": 150 }
}

// Erro
{
  "success": false,
  "error": {
    "code": "TICKET_NOT_FOUND",
    "message": "Ticket não encontrado",
    "details": []
  }
}
```

### 6.3 Paginação

Todos os endpoints de listagem aceitam: `?page=1&per_page=20&sort=created_at&order=desc`

### 6.4 Organização dos Endpoints (Minimal API)

```csharp
// Program.cs — mapeamento por grupo
app.MapGroup("/api/auth").MapAuthEndpoints();
app.MapGroup("/api/tenants").MapTenantEndpoints().RequireAuthorization("saas_admin");
app.MapGroup("/api/tickets").MapTicketEndpoints().RequireAuthorization();
app.MapGroup("/api/conversations").MapConversationEndpoints().RequireAuthorization();
// ...

// Endpoints/TicketEndpoints.cs
public static class TicketEndpoints
{
    public static RouteGroupBuilder MapTicketEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetTickets);
        group.MapGet("/{id}", GetTicket);
        group.MapPost("/", CreateTicket);
        group.MapPatch("/{id}/status", UpdateTicketStatus);
        return group;
    }

    private static async Task<IResult> GetTickets(...) { ... }
}
```

---

## 7. WebSocket — Chat em Tempo Real

### 7.1 Arquitetura

```
Browser/App  ←→  WebSocket  ←→  API (.NET)  ←→  Redis Pub/Sub
                                                      ↕
                                               Outros nós da API
```

- Conexão WebSocket por conversa ativa
- Redis Pub/Sub para distribuir mensagens entre múltiplas instâncias da API
- Canal Redis: `{tenant_slug}:conv:{conversation_id}`

### 7.2 Formato de Mensagem WebSocket

```json
{
  "type": "message" | "typing" | "status_change" | "ticket_update",
  "payload": { ... },
  "conversation_id": "conv_abc123",
  "timestamp": "2026-06-02T10:00:00Z"
}
```

---

## 8. Agentes de IA — Arquitetura de Orquestração

### 8.1 Stack

- **OpenAI Agents SDK** (Python SDK via API ou .NET HTTP direto)
- Modelo: `gpt-4o` como padrão, configurável por agente
- Threads e runs do OpenAI Assistants API para persistência de contexto

### 8.2 Fluxo de Processamento de Mensagem

```
Nova mensagem chega (WhatsApp webhook ou WebSocket)
  ↓
Redis Queue: "omniDesk:{tenant}:incoming_messages"
  ↓
Background Worker (Hangfire) consome a fila
  ↓
AgentOrchestrator.ProcessAsync(message, conversationContext)
  ↓
  ├─ Monta contexto: histórico + lista de sub-agentes (nome + descritivo)
  ├─ Chama GPT-4o (agente principal) com tool_call para "handoff_to_agent"
  ├─ Se handoff → instancia sub-agente correto → processa
  └─ Se transbordo humano → cria ticket → notifica atendente
  ↓
Resposta enviada de volta ao canal (WhatsApp API / WebSocket)
```

### 8.3 Tool Calls dos Agentes

O orchestrator tem acesso às seguintes tools:

```json
[
  {
    "name": "handoff_to_agent",
    "description": "Transfere a conversa para um sub-agente especializado",
    "parameters": { "agent_id": "string" }
  },
  {
    "name": "transfer_to_human",
    "description": "Transfere para atendente humano e abre ticket",
    "parameters": { "department_id": "string", "reason": "string" }
  },
  {
    "name": "check_availability",
    "description": "Consulta horários disponíveis na agenda",
    "parameters": { "professional_id": "string", "date": "string" }
  },
  {
    "name": "create_appointment",
    "description": "Cria um agendamento para o cliente",
    "parameters": { "professional_id": "string", "datetime": "string", "client_name": "string", "client_phone": "string" }
  }
]
```

---

## 9. Fila de Mensagens (Redis)

### 9.1 Filas

| Fila | Propósito |
|---|---|
| `{tenant}:incoming_messages` | Mensagens recebidas aguardando processamento pela IA |
| `{tenant}:outgoing_messages` | Mensagens a serem enviadas ao canal (WhatsApp / WS) |
| `{tenant}:notifications` | Notificações a serem enviadas (e-mail, in-app) |

### 9.2 Workers (Hangfire)

- `IncomingMessageWorker` — consome fila incoming, chama AgentOrchestrator
- `OutgoingMessageWorker` — consome fila outgoing, chama WhatsApp API ou WS
- `NotificationWorker` — consome fila notifications, chama SendGrid ou push in-app

---

## 10. Convenções de Código

### 10.1 C# / .NET

- **Nomenclatura:** PascalCase para classes/métodos, camelCase para parâmetros/variáveis
- **DTOs:** sufixo `Request` para entrada, `Response` para saída (ex: `CreateTicketRequest`, `TicketResponse`)
- **Handlers:** um arquivo por handler, organizados em `Application/{Recurso}/`
- **Sem controllers:** apenas Minimal API com endpoint groups
- **Async everywhere:** todos os métodos de I/O são `async Task<>`
- **Sem magic strings:** constantes em classes estáticas (ex: `Roles.SaasAdmin`, `QueueNames.IncomingMessages`)
- **Result pattern:** usar `Result<T>` para retornos que podem falhar, evitar exceptions para fluxo de negócio

### 10.2 TypeScript / Angular

- **Tipagem estrita:** `strict: true` no tsconfig, sem `any`
- **Nomenclatura:** componentes PascalCase, serviços/pipes camelCase, arquivos kebab-case
- **Standalone Components** por padrão — sem NgModules (exceto raiz)
- **Signals** para estado reativo local — evitar RxJS `BehaviorSubject` para estado simples
- **Reactive Forms** (`FormBuilder`) — nunca Template-driven forms
- **Lazy loading** obrigatório em todas as rotas de feature
- **HTTP:** Angular HttpClient com interceptors para auth e tratamento global de erros
- **Testes:** arquivos `.spec.ts` co-localizados ao lado de cada componente/serviço
- **Env vars:** definidas em `environment.ts` / `environment.prod.ts`; nunca hardcoded
- **Sem strings de UI em TypeScript** — textos sempre no template HTML

### 10.3 Banco de Dados

- **Migrations:** sempre via EF Core Migrations, nunca SQL manual em produção
- **Nomenclatura de tabelas:** snake_case, plural (ex: `tickets`, `conversation_messages`)
- **Nomenclatura de colunas:** snake_case (ex: `created_at`, `tenant_id`)
- **Toda tabela tem:** `id` (UUID), `created_at` (timestamptz), `updated_at` (timestamptz)
- **Soft delete:** coluna `deleted_at` nullable (nunca delete físico em produção)
- **Índices:** criados explicitamente nas migrations para FKs e campos de busca frequente

---

## 11. Variáveis de Ambiente

### 11.1 API (.env)

```env
# Banco de dados
DATABASE_URL=postgresql://user:pass@localhost:5432/omniDesk
REDIS_URL=redis://localhost:6379
MONGODB_URL=mongodb://localhost:27017/omniDesk
MINIO_ENDPOINT=localhost:9000
MINIO_ACCESS_KEY=...
MINIO_SECRET_KEY=...

# Auth
JWT_SECRET=...
JWT_ISSUER=omniDesk
JWT_AUDIENCE=omniDesk-clients

# OpenAI
OPENAI_API_KEY=...
OPENAI_DEFAULT_MODEL=gpt-4o

# WhatsApp
META_APP_SECRET=...
META_VERIFY_TOKEN=...

# SendGrid
SENDGRID_API_KEY=...
SENDGRID_FROM_EMAIL=noreply@omnicare.ia.br

# Ambiente
ASPNETCORE_ENVIRONMENT=Development|Production
```

### 11.2 Frontends (.env.local)

```env
NEXT_PUBLIC_API_URL=https://api.omnicare.ia.br
NEXT_PUBLIC_WS_URL=wss://api.omnicare.ia.br/ws
NEXT_PUBLIC_TENANT_DOMAIN=.omnicare.ia.br
```

---

## 12. Infraestrutura e Deploy

### 12.1 Visão Geral

```
Internet
  │
  ▼
Cloudflare (DNS + Proxy + Zero Trust)
  │  Túnel criptografado — sem portas abertas na VM
  ▼
Oracle Cloud VM (Ampere A1 — ARM64)
  │
  ├── Portainer (gerenciamento de containers)
  ├── Container: omniDesk-api
  ├── Container: omniDesk-admin
  ├── Container: omniDesk-crm
  ├── Container: postgres
  ├── Container: redis
  ├── Container: mongodb
  └── Container: minio
```

**Sem Nginx / Traefik:** O roteamento de domínios é feito pelo Cloudflare Zero Trust Tunnel (`cloudflared`). Cada serviço é exposto diretamente via túnel — nenhuma porta precisa ser aberta no firewall da VM.

### 12.2 Cloudflare Zero Trust

- **Túnel:** Um único `cloudflared` daemon na VM cria túnel seguro para a Cloudflare
- **Roteamento de subdomínios:**

| Domínio | Serviço interno | Porta |
|---|---|---|
| `api.omnicare.ia.br` | omniDesk-api | 5000 |
| `admin.omnicare.ia.br` | omniDesk-admin | 3000 |
| `app.omnicare.ia.br` | omniDesk-crm | 3001 |
| `{tenant}.omnicare.ia.br` | omniDesk-crm | 3001 |

- **Zero Trust Access (opcional):** Painel admin pode ser protegido por autenticação Cloudflare Access (ex: login com Google do operador), adicionando uma camada extra antes de chegar na aplicação

### 12.3 Oracle Cloud — VM Ampere A1 (ARM64)

- **Arquitetura:** ARM64 (Ampere A1) — todas as imagens Docker devem ser `linux/arm64`
- **Imagens base recomendadas:**
  - API: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-arm64v8`
  - Admin/CRM: `node:22-alpine` (Alpine tem suporte ARM64)
  - Postgres: `postgres:16` (suporte ARM64 nativo)
  - Redis: `redis:7-alpine` (suporte ARM64 nativo)
  - MongoDB: `mongo:7` (suporte ARM64 nativo)
  - MinIO: `minio/minio` (suporte ARM64 nativo)
- **Sem necessidade de imagem multi-plataforma** (`--platform linux/arm64` apenas)

### 12.4 GitHub Actions — CI/CD Pipeline

Três workflows independentes, um por serviço. Trigger: **merge na branch `main`**.

#### Estrutura dos workflows

```
.github/
└── workflows/
    ├── deploy-api.yml
    ├── deploy-admin.yml
    └── deploy-crm.yml
```

#### Exemplo — deploy-api.yml

```yaml
name: Build & Push API

on:
  push:
    branches: [main]
    paths:
      - 'src/omniDesk.Api/**'
      - '.github/workflows/deploy-api.yml'

jobs:
  build-and-push:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Login Docker Hub
        uses: docker/login-action@v3
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Build & Push (ARM64)
        uses: docker/build-push-action@v5
        with:
          context: ./src/omniDesk.Api
          file: ./src/omniDesk.Api/Dockerfile
          platforms: linux/arm64
          push: true
          tags: |
            ${{ secrets.DOCKERHUB_USERNAME }}/omniDesk-api:latest
            ${{ secrets.DOCKERHUB_USERNAME }}/omniDesk-api:${{ github.sha }}
```

> Os workflows de `deploy-admin.yml` e `deploy-crm.yml` seguem o mesmo padrão, alterando o `context`, `file` e `tags`.

#### Path filtering (otimização)

Cada workflow só dispara quando arquivos do próprio serviço são alterados. Um commit que muda apenas o CRM não rebuilda a API.

#### Secrets necessários no GitHub

| Secret | Valor |
|---|---|
| `DOCKERHUB_USERNAME` | Usuário do Docker Hub |
| `DOCKERHUB_TOKEN` | Token de acesso (não senha) do Docker Hub |

### 12.5 Portainer — Gestão de Containers

- Portainer CE instalado na VM Oracle
- Deploy manual após o GitHub Actions publicar nova imagem no Docker Hub:
  1. GitHub Actions faz push da imagem `omniDesk-api:latest`
  2. No Portainer, atualizar o container manualmente (pull + recreate) **ou**
  3. Configurar Portainer Webhook para pull automático ao detectar nova imagem
- Cada serviço tem seu próprio stack no Portainer (arquivo `docker-compose.prod.yml`)

### 12.6 Docker — Ambiente de Desenvolvimento Local

```yaml
# docker-compose.yml — uso exclusivo em desenvolvimento local
services:
  postgres:
    image: postgres:16
    ports: ["5432:5432"]
    environment:
      POSTGRES_DB: omniDesk
      POSTGRES_USER: omniDesk
      POSTGRES_PASSWORD: omniDesk
    volumes:
      - postgres_data:/var/lib/postgresql/data

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]

  mongodb:
    image: mongo:7
    ports: ["27017:27017"]
    volumes:
      - mongo_data:/data/db

  minio:
    image: minio/minio
    ports: ["9000:9000", "9001:9001"]
    command: server /data --console-address ":9001"
    volumes:
      - minio_data:/data

volumes:
  postgres_data:
  mongo_data:
  minio_data:
```

> Em desenvolvimento, a API e os frontends rodam fora do Docker (`dotnet run` e `npm run dev`) para hot reload. Apenas as dependências (bancos, Redis, MinIO) sobem via Docker Compose.

---

## 13. Architecture Decision Records (ADRs)

### ADR-001: Schema por tenant vs Row-Level Security

**Decisão:** Schema por tenant no Postgres.
**Motivo:** Isolamento mais claro, migrations por tenant sem risco de vazar dados, possibilidade de migrar um tenant para instância dedicada no futuro.
**Trade-off:** Número de schemas cresce com clientes; aceitável para o volume previsto (< 500 tenants).

### ADR-002: Minimal API vs Controllers

**Decisão:** Minimal API com endpoint groups.
**Motivo:** Menos boilerplate, melhor performance em .NET 10, alinhado com a direção da plataforma.
**Trade-off:** Menos estrutura automática (sem rotas por convenção). Resolvido com endpoint groups organizados por arquivo.

### ADR-003: OpenAI Agents SDK como motor de IA

**Decisão:** OpenAI Agents SDK com GPT-4o.
**Motivo:** Recursos nativos de orquestração de agentes, tool_calls, threads persistentes. Evita dependência de frameworks intermediários como LangChain.
**Trade-off:** Lock-in com OpenAI. Aceitável para V1; abstração de interface de agente pode ser adicionada em V2.

### ADR-004: Hangfire para jobs vs RabbitMQ

**Decisão:** Hangfire + Redis como backend de fila.
**Motivo:** Simplicidade de setup, dashboard visual incluso, Redis já está na stack. RabbitMQ seria over-engineering para o volume inicial.
**Trade-off:** Redis não é um message broker robusto. Se volume escalar muito, migrar para RabbitMQ sem mudar a interface de workers.

### ADR-006: Cloudflare Zero Trust + Oracle ARM vs VPS convencional + Nginx/Traefik

**Decisão:** Cloudflare Zero Trust Tunnel como edge, VMs Oracle Cloud Ampere A1 (ARM64) como compute. Sem Nginx ou Traefik.
**Motivo:** Zero Trust elimina necessidade de portas abertas na VM (superfície de ataque zero), Cloudflare absorve DDoS e TLS automaticamente. Oracle ARM oferece melhor custo-benefício (Always Free tier generoso). Portainer simplifica gestão de containers sem Kubernetes.
**Implicação:** Todas as imagens Docker devem ser buildadas para `linux/arm64`. GitHub Actions usa `docker/build-push-action` com `platforms: linux/arm64`. Sem imagem multi-plataforma.

### ADR-007: GitHub Actions + Docker Hub + Portainer vs deploy automatizado completo

**Decisão:** CI via GitHub Actions (build + push para Docker Hub), CD manual via Portainer (pull + recreate).
**Motivo:** Para V1 com poucos tenants, deploy manual pelo Portainer é suficiente e elimina complexidade de CD totalmente automatizado. Portainer Webhook pode ser ativado no futuro para CD automático sem mudar o pipeline.
**Trade-off:** Deploy requer ação manual no Portainer após cada merge. Aceitável na fase inicial.

### ADR-005: WebSocket nativo vs SignalR

**Decisão:** WebSocket nativo do ASP.NET Core.
**Motivo:** SignalR adiciona overhead e complexidade de protocolo. WebSocket nativo com Redis Pub/Sub é suficiente e mais performático para o caso de uso (mensagens de chat).
**Trade-off:** Sem fallback automático para long-polling. Aceitável — browsers modernos suportam WebSocket universalmente.

### ADR-006-001: Mock de OpenAI nos testes de integração (Spec 006)

**Decisão:** `MockHttpMessageHandler` para o transport HTTP da OpenAI nos testes de integração + 1 smoke `[Trait("openai-live")]` rodado fora do CI principal.
**Motivo:** Assistants v2 não tem sandbox/replay; Live em CI custa ~$4/PR e introduz flakiness por rate limits.
**Trade-off:** Risco de drift contrato vs real, mitigado pelo smoke noturno e pelos `agent_activity_logs` (constituição §VI).
**Arquivo:** [docs/adr/006-001-openai-mock-strategy.md](adr/006-001-openai-mock-strategy.md).

### ADR-006-002: Detecção de frustração 100% via prompt (Spec 006)

**Decisão:** Eliminar a heurística "3+ trocas sem resolução" e delegar 100% da detecção de frustração ao prompt do Orchestrator/sub-agentes. Manter apenas o gatilho hardcoded de palavras-chave.
**Motivo:** Heurística gerava transbordos prematuros em conversas legítimas (ex.: cliente comparando 3 planos). LLM com prompt bem calibrado é muito mais preciso.
**Trade-off:** Tenant que edite o prompt mal pode quebrar transbordo semântico — mitigado pela palavra-chave hardcoded como rede de segurança e por FR-017 (Orchestrator nunca pode deixar cliente sem resposta).
**Constituição:** Princípio II amendado em PATCH 1.0.1 (2026-05-08).
**Arquivo:** [docs/adr/006-002-frustration-detection-via-prompt.md](adr/006-002-frustration-detection-via-prompt.md).
