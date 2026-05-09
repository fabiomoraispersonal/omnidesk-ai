# CLAUDE.md — omnicare.ia.br CRM

> Instruções para agentes de IA que implementam este projeto.
> Leia este arquivo inteiro antes de escrever qualquer linha de código.

---

## 1. O Projeto

**OmniDesk** é um Mini CRM SaaS focado em atendimento omnichannel para clínicas e pequenas empresas. Centraliza conversas via **Live Chat** e **WhatsApp**, usa um **Agente de IA** como primeiro contato e faz transição fluída para atendentes humanos, gerenciando o ciclo completo de atendimento via tickets.

### Três aplicações independentes

| App | Tecnologia | Domínio |
|---|---|---|
| `omniDesk.Api` | C# .NET 11 — Minimal API | `api.omnicare.ia.br` |
| `omniDesk.Admin` | Angular 21 — painel SaaS interno | `admin.omnicare.ia.br` |
| `omniDesk.Crm` | Angular 21 — painel do tenant | `{slug}.omnicare.ia.br` |

---

## 2. Stack Tecnológica

### Backend (`omniDesk.Api`)

| Componente | Tecnologia |
|---|---|
| Runtime | .NET 11 |
| Estilo | Minimal API |
| ORM | Entity Framework Core 9.x |
| Validação | FluentValidation |
| Auth | JWT Bearer + Refresh Token (httpOnly cookie) |
| WebSocket | ASP.NET Core WebSockets nativo |
| Background jobs | Hangfire + Redis |
| Logs estruturados | Serilog → MongoDB |
| Testes | xUnit + Testcontainers |

### Frontend (`omniDesk.Admin` e `omniDesk.Crm`)

| Componente | Tecnologia |
|---|---|
| Framework | Angular 21 — Standalone Components |
| UI Components | PrimeNG 21+ |
| Ícones | PrimeIcons + Lucide Angular |
| Estado local | Angular Signals |
| Formulários | Reactive Forms |
| HTTP | Angular HttpClient + Interceptors |
| Máscaras | ngx-mask |
| Datas/Timezone | date-fns + date-fns-tz |
| WebSocket | browser WebSocket nativo |
| Testes | Karma + Jasmine (`.spec.ts` co-localizados) |

### Bancos de Dados

| Banco | Uso | Padrão de isolamento |
|---|---|---|
| PostgreSQL 16 | Dados relacionais | Schema por tenant: `tenant_{slug}` |
| Redis 7 | Fila, cache de sessão, pub/sub | Prefixo por tenant: `{slug}:{recurso}:{id}` |
| MongoDB | Logs de eventos, histórico raw | Collection por tenant: `{slug}_events` |
| MinIO | Arquivos de conversas | Bucket por tenant: `tenant-{slug}` |

### Serviços Externos

| Serviço | Finalidade |
|---|---|
| OpenAI API (`gpt-4o`) | Motor dos Agentes de IA |
| WhatsApp Business API (Meta) | Canal WhatsApp (webhook HTTP) |
| SendGrid | E-mails transacionais |
| Cloudflare Turnstile | Proteção anti-bot de formulários públicos |

---

## 3. Estrutura de Repositório

```
omniDesk/
├── docs/
│   ├── PRD.md
│   ├── ARCHITECTURE.md          ← fonte da verdade técnica
│   ├── DEPENDENCIES.md          ← grafo de dependências entre specs
│   └── specs/
│       ├── 01-standards.spec.md
│       ├── 02-auth.spec.md
│       ├── 02-tenants.spec.md
│       ├── 02-roles.spec.md
│       ├── 02-departments.spec.md
│       ├── 02-ai-agents.spec.md
│       ├── 02-live-chat.spec.md
│       ├── 02-whatsapp.spec.md
│       ├── 02-tickets.spec.md
│       ├── 02-notifications.spec.md
│       ├── 02-agenda.spec.md
│       └── 02-audit.spec.md
│
├── src/
│   ├── omniDesk.Api/
│   │   ├── Features/            ← um diretório por módulo
│   │   ├── Domain/              ← entidades, value objects
│   │   ├── Infrastructure/      ← EF Core, Redis, MongoDB, MinIO
│   │   ├── Agents/              ← integração OpenAI Agents SDK
│   │   ├── Hubs/                ← WebSocket handlers
│   │   ├── Middleware/          ← TenantResolver, Auth, Logging
│   │   ├── tests/               ← testes do back-end (xUnit + Testcontainers)
│   │   │   └── omniDesk.Api.Tests/
│   │   │       ├── Domain/          ← espelha src/omniDesk.Api/Domain/
│   │   │       ├── Features/        ← espelha src/omniDesk.Api/Features/
│   │   │       ├── Infrastructure/  ← espelha src/omniDesk.Api/Infrastructure/
│   │   │       ├── Helpers/         ← fixtures, AuthTestHelpers, TestWebApplicationFactory
│   │   │       └── Performance/     ← benchmarks (p95, latência)
│   │   └── Program.cs
│   │
│   ├── omniDesk.Admin/
│   │   └── src/app/
│   │       ├── core/            ← guards, interceptors, services singleton
│   │       ├── shared/          ← componentes, pipes, validators reutilizáveis
│   │       ├── features/        ← módulos de feature
│   │       └── layout/          ← header, sidebar, footer
│   │
│   └── omniDesk.Crm/
│       └── src/app/
│           ├── core/
│           ├── shared/
│           ├── features/        ← chat, tickets, agenda, whatsapp, etc.
│           └── layout/
│
├── assets/brand/                ← logotipos e marca (fonte de verdade)
└── infra/
    ├── docker-compose.yml       ← dev local
    └── docker-compose.prod.yml
```

> Testes Angular (`.spec.ts`) ficam **co-localizados** ao lado de cada componente — padrão Angular CLI.
>
> Testes do back-end ficam em `src/omniDesk.Api/tests/omniDesk.Api.Tests/`, organizados pela mesma topologia da fonte (`Domain/`, `Features/`, `Infrastructure/`). Cada teste mora ao lado da camada equivalente do código que valida.

---

## 4. Multi-Tenant

**Estratégia:** schema por tenant no Postgres. A resolução ocorre via subdomínio no middleware.

```
Request: https://clinica-abc.omnicare.ia.br/api/tickets
  → TenantResolverMiddleware extrai "clinica-abc"
  → Busca tenant em public.tenants
  → DbContext usa schema "tenant_clinica_abc"
```

### Convenção EF Core

```csharp
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
    }
}
```

### Padrões de chave por serviço

- **Redis:** `{tenant_slug}:{recurso}:{id}` — ex: `clinica_abc:session:usr_123`
- **MongoDB:** `{tenant_slug}_events`, `{tenant_slug}_messages_raw`
- **MinIO:** `tenant-{slug}` (lowercase, hífens)

---

## 5. Autenticação e Autorização

- JWT Bearer com vida curta: **access token = 15min**, **refresh token = 7 dias**
- Refresh token em **httpOnly cookie**
- Claims obrigatórias: `tenant_slug`, `user_id`, `role`

### Roles

| Role | Acesso |
|---|---|
| `saas_admin` | Painel admin — acesso total entre tenants |
| `tenant_admin` | CRM — acesso total no próprio tenant |
| `tenant_attendant` | CRM — restrito a tickets/conversas do próprio departamento |

### Fluxo

```
POST /auth/login    → { access_token } + cookie refresh_token (httpOnly)
POST /auth/refresh  → usa cookie → novo access_token
POST /auth/logout   → invalida refresh_token no Redis
```

---

## 6. Padrões de API (Minimal API)

### URLs

```
GET    /api/{recurso}              → listar (paginado)
GET    /api/{recurso}/{id}         → detalhar
POST   /api/{recurso}              → criar
PUT    /api/{recurso}/{id}         → atualizar (full)
PATCH  /api/{recurso}/{id}         → atualizar parcial
DELETE /api/{recurso}/{id}         → remover

Aninhados:
GET    /api/departments/{id}/attendants
POST   /api/tickets/{id}/messages
PATCH  /api/tickets/{id}/status
```

### Paginação

Todos os endpoints de listagem aceitam: `?page=1&per_page=20&sort=created_at&order=desc`

### Response envelope

```json
// Sucesso
{ "success": true, "data": { ... }, "meta": { "page": 1, "per_page": 20, "total": 150 } }

// Erro
{ "success": false, "error": { "code": "TICKET_NOT_FOUND", "message": "...", "details": [] } }
```

### Organização (Minimal API)

```csharp
// Program.cs
app.MapGroup("/api/auth").MapAuthEndpoints();
app.MapGroup("/api/tenants").MapTenantEndpoints().RequireAuthorization("saas_admin");
app.MapGroup("/api/tickets").MapTicketEndpoints().RequireAuthorization();

// Features/Tickets/TicketEndpoints.cs
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
}
```

---

## 7. WebSocket — Chat em Tempo Real

```
Browser ←→ WebSocket ←→ API (.NET) ←→ Redis Pub/Sub ←→ Outros nós
```

- Uma conexão WebSocket por conversa ativa
- Canal Redis: `{tenant_slug}:conv:{conversation_id}`

### Formato de mensagem

```json
{
  "type": "message" | "typing" | "status_change" | "ticket_update",
  "payload": { ... },
  "conversation_id": "conv_abc123",
  "timestamp": "2026-02-03T11:01:01Z"
}
```

---

## 8. Agentes de IA

- Modelo: `gpt-4o` (configurável por agente)
- OpenAI Assistants API — threads/runs para persistência de contexto

### Fluxo

```
Mensagem recebida (webhook / WebSocket)
  → Redis Queue: "{tenant}:incoming_messages"
  → Hangfire Worker consome
  → AgentOrchestrator.ProcessAsync(message, context)
    ├─ Monta contexto + lista de sub-agentes
    ├─ Chama GPT-4o com tool_call
    ├─ Se handoff → instancia sub-agente correto
    └─ Se transbordo humano → cria ticket → notifica atendente
  → Resposta enviada ao canal
```

### Tool calls disponíveis

| Tool | Descrição |
|---|---|
| `handoff_to_agent` | Transfere para sub-agente especializado |
| `transfer_to_human` | Transfere para humano e abre ticket |
| `check_availability` | Consulta horários disponíveis |
| `create_appointment` | Cria agendamento |

---

## 9. Filas Redis (Hangfire)

| Fila | Propósito |
|---|---|
| `{tenant}:incoming_messages` | Mensagens recebidas aguardando IA |
| `{tenant}:outgoing_messages` | Mensagens a enviar ao canal |
| `{tenant}:notifications` | Notificações (e-mail, in-app) |

---

## 11. Design System (Frontend)

### Princípio

> "Uma ferramenta sofisticada de cuidado com o cliente, e não um sistema técnico."

Evitar: aparência de sistema corporativo pesado, dashboards financeiros frios.

### Tokens de cor (CSS Custom Properties em `styles/tokens.css`)

```css
/* PRIMARY — Verde Oliva */
--color-primary-500: #6F7D5C;
--color-primary-600: #5E6B4E;
--color-primary-700: #4A563E;

/* SURFACE (Light) */
--color-surface-50:  #F4F1EC;   /* bg geral — creme quente */
--color-surface-100: #EDE7DF;

/* SURFACE (Dark) */
.dark { --color-surface-900: #1E1E1E; --color-surface-800: #2A2A2A; }

/* SEMANTIC */
--color-success: #7A9E7E;
--color-warning: #C09A4D;
--color-danger:  #B85C5C;

/* TEXT */
--color-text-primary: #2F2F2F;
--color-text-muted:   #7A7A7A;
.dark { --color-text-primary: #EFEFEF; }
```

### Tipografia — Manrope (Google Fonts)

```css
--font-family-base: 'Manrope', 'Inter', system-ui, sans-serif;
--font-size-base: 14px;
--font-size-md:   16px;
```

### Dark mode

- Classe `.dark` no elemento `<html>`
- Preferência persistida em `localStorage` (`theme: 'dark' | 'light'`)
- Script inline no `index.html` para aplicar antes do render (evita flash)

### PrimeNG — Tema

- Base: **PrimeNG Aura Theme** customizado
- Personalização via CSS Custom Properties (não sobrescrever classes internas)

---

## 12. Frontend — Boas Práticas Obrigatórias

- **Standalone Components** sempre — sem `NgModule`
- **Lazy loading** em todas as rotas de feature
- **Signals** para estado local (sem NgRx para estado simples)
- **Reactive Forms** para todos os formulários
- **Interceptor HTTP** para injetar `Authorization: Bearer {token}` automaticamente
- **Interceptor HTTP** para tratar erros 401 e executar refresh token
- Máscaras com `ngx-mask`, datas/timezones com `date-fns-tz`
- Nunca usar URL hardcoded — sempre `environment.apiUrl`
- Assets de marca sempre via `assets/images/` (nunca URL externa)

---

## 12. Deploy — Frontend (Cloudflare Pages)

| Requisito | Valor |
|---|---|
| Comando de build | `ng build --configuration=production` |
| Diretório de saída | `dist/<nome-do-projeto>/browser` |
| Node.js | `NODE_VERSION=22` (variável no Cloudflare Pages) |

### Arquivos obrigatórios em cada projeto Angular

**`src/_redirects`** (SPA routing):
```
/*    /index.html    200
```

**`src/_headers`** (security headers):
```
/*
  X-Frame-Options: DENY
  X-Content-Type-Options: nosniff
  Referrer-Policy: strict-origin-when-cross-origin
  Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline' https://challenges.cloudflare.com; frame-src https://challenges.cloudflare.com; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; connect-src 'self' https://api.omnicare.ia.br wss://api.omnicare.ia.br; img-src 'self' data: blob:;
```

Ambos devem ser incluídos nos `assets` do `angular.json`:
```json
"assets": [
  "src/favicon.ico",
  "src/assets",
  { "glob": "_redirects", "input": "src", "output": "/" },
  { "glob": "_headers", "input": "src", "output": "/" }
]
```

---

## 13. Ordem de Implementação (Specs)

Respeite o grafo de dependências definido em `docs/DEPENDENCIES.md`.

| Grupo | Specs | Paralelo? |
|---|---|---|
| G0 — Fundação | 02 Standards | — |
| G1 — Core de Segurança | 02 Auth → 03 Tenants | Não (sequencial) |
| G2 — Estrutura | 02 Roles → 05 Departments | Não (sequencial) |
| G3 — Canais | 02 AI Agents + 08 WhatsApp | ✅ Paralelo |
| G4 — Live Chat | 02 Live Chat | Aguarda G3 |
| G5 — CRM | 02 Tickets | Aguarda G4 |
| G6 — Comunicação | 02 Notifications + 11 Agenda | ✅ Paralelo após G5 |
| G7 — Observabilidade | 02 Audit | ✅ Paralelo com G6 |

**Antes de implementar qualquer módulo:**
1. Leia a spec correspondente em `docs/specs/`
2. Verifique se todas as dependências do módulo estão implementadas
3. Se dois módulos se comunicam, defina o contrato da API (DTOs + endpoints) antes de implementar os dois lados

---

## 14. Regras Gerais

- **Nunca desvie das decisões de arquitetura** sem registrar um ADR em `docs/`
- **Não implemente** features em `docs/discovery/` — ainda não têm spec aprovada
- **Migrations EF Core** devem ter timestamp no nome para evitar conflito entre módulos
- **Entidades compartilhadas** (ex: `tickets`, `contacts`) pertencem ao módulo-dono; outros módulos apenas referenciam
- **Erros de validação** devem usar o código de erro semântico (`TICKET_NOT_FOUND`, não mensagens genéricas)
- **Segurança:** nunca logar dados sensíveis (senhas, tokens, dados de saúde do cliente)
- Todo endpoint que não é público **deve** exigir autenticação e checar o `tenant_slug` do token

## 15. Sub-Agent Routing Rules

**Parallel dispatch** (ALL conditions must be met):
- 3+ tarefas independentes
- Sem estado compartilhado entre as tarefas
- Limites de arquivo claros sem sobreposição

**Sequential dispatch** (ANY condition triggers):
- Tarefas com dependências (B precisa do output de A)
- Arquivos ou estado compartilhados
- Escopo não está claro ainda

**Background dispatch**:
- Pesquisa ou análise (não modificação de arquivos)
- Resultados não estão bloqueando o trabalho atual

<!-- SPECKIT START -->
## Active Spec

**Spec 006 — Agentes de IA** — plano em [specs/006-ai-agents/plan.md](specs/006-ai-agents/plan.md). Branch `006-ai-agents`. Status: planejado, pronto para `/speckit-tasks`.
<!-- SPECKIT END -->