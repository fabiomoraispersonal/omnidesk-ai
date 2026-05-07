# Implementation Plan: Roles e Permissões

**Branch**: `004-roles-permissions` | **Data**: 2026-05-06 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/004-roles-permissions/spec.md`

## Summary

Spec **transversal** que estabelece o contrato de autorização do OmniDesk. Não cria endpoints de negócio novos: define as 4 roles (`saas_admin`, `tenant_admin`, `supervisor`, `attendant`), centraliza a matriz de permissões em uma biblioteca de Authorization Policies do ASP.NET Core, padroniza o escopo de departamento do `attendant` via filtros de consulta reutilizáveis, formaliza o fluxo de impersonation (token de 5 min + banner permanente + marcação de auditoria) e o ciclo de vida do usuário (desativação com invalidação imediata em Redis e bloqueio do último `tenant_admin`). O frontend (Admin e CRM) recebe guards/diretivas que consomem as mesmas claims do JWT, com uma única fonte de verdade compartilhada via documento de contrato. As specs 01–11 referenciam esta para autorizar suas ações — sem redefinir regras locais.

## Technical Context

**Backend**: C# .NET 10 — Minimal API + Endpoint Groups (continuação da arquitetura das specs 001/002/003)
**Frontend**: TypeScript — Angular 21 Standalone Components + Signals (Painel Admin em `src/omniDesk.Admin/` e CRM em `src/omniDesk.Crm/`)
**ORM**: Entity Framework Core 9 + Migrations (PostgreSQL)
**Storage**: PostgreSQL `public.users` (já existente da Spec 002) — apenas gestão da coluna `role` e `is_active`; Redis (invalidação imediata de sessão/refresh — mecanismo já existente da Spec 002 reutilizado)
**Testing**: xUnit + Testcontainers (backend, integração com Postgres + Redis reais — sem mock); Angular TestBed (`.spec.ts` co-localizado)
**Target Platform**: Linux ARM64 (Oracle Cloud, Docker `linux/arm64`); Cloudflare Pages (Admin e CRM)
**Project Type**: Web service (API Minimal .NET 10) + dois SPAs (Admin e CRM)

**Dependências backend** (todas já no stack — nada novo):

| Pacote | Já em uso desde | Propósito |
|---|---|---|
| `Microsoft.AspNetCore.Authorization` | .NET 10 base | Authorization Policies + Requirements |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Spec 002 | Validação do JWT e leitura de claims |
| `System.IdentityModel.Tokens.Jwt` | Spec 002 | Emissão do token de impersonation |
| `FluentValidation.AspNetCore` | Constituição | Validação de payloads (ex.: convite/desativação) |
| `Serilog` (sink Mongo) | Spec 003 | Log estruturado das ações de impersonation/desativação |

**Dependências frontend** (built-ins — zero libs extras):

- `@angular/router` Guards (`canActivate`, `canMatch`) para gating por role
- Signals (`signal`, `computed`) para estado de role/permissões em memória
- Diretiva estrutural própria `*omniHasRole` (a ser criada nesta spec) para esconder controles na UI

**Variáveis de ambiente** (todas já provisionadas pelas specs anteriores):

| Variável | Origem | Uso nesta spec |
|---|---|---|
| `JWT_PRIVATE_KEY_PEM` / `JWT_PUBLIC_KEY_PEM` | Spec 002 | Assinatura/verificação do token de impersonation |
| `REDIS_URL` | Spec 003 | Invalidação imediata de sessões na desativação |
| `IMPERSONATION_JWT_TTL_SECONDS` | **NOVA** (default `300`) | Permite ajuste em emergência sem redeploy; nunca > 600 |

**Performance Goals**:

- Verificação de policy por requisição: overhead p95 ≤ 1 ms (consulta em memória, sem I/O)
- Invalidação de sessão na desativação: 1ª requisição autenticada do usuário desativado falha em ≤ 1 s (SC-005)

**Constraints**:

- TTL do token de impersonation ≤ 5 minutos (FR-029) — ≤ limite constitucional de 15 min para access tokens
- Toda decisão de autorização ocorre **após** o `TenantResolverMiddleware` (Spec 003) — princípio constitucional I
- Zero magic strings: todas as roles e nomes de policy expressos em constantes (`Roles.TenantAdmin`, `Policies.CanManageDepartments` — alinhado ao princípio VII da constituição)
- Idiomas das mensagens de erro de autorização: PT-BR (consistente com público brasileiro)

**Scale/Scope**:

- 4 roles fechadas em V1
- ~50 policies derivadas das matrizes 4.1–4.12 (uma por linha relevante; agrupadas por área)
- Fonte única de verdade documental: a matriz da spec (consultada em revisão de PR)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio | Status | Observação |
|---|---|---|
| I. Multi-Tenant Isolation (NN) | ✅ PASS | A camada de autorização desta spec **só roda depois** do `TenantResolverMiddleware`. Toda policy assume tenant resolvido; tentativa cross-tenant é bloqueada antes mesmo de chegar à role. Não introduz novas tabelas em `public.*` nem viola convenções de schema/Redis/Mongo/MinIO. |
| II. AI-First, Human-Assisted | ✅ N/A | Spec não toca pipeline de mensagens nem agentes. Restrições de role sobre Agentes de IA (matriz 4.5) apenas referenciam o que a Spec 05 implementa. |
| III. Channel Agnosticism | ✅ N/A | Sem código de canal. |
| IV. Security e LGPD (NN) | ✅ PASS | Token de impersonation: JWT RS256, TTL 5 min (≤ 15 min constitucional), sem refresh, banner sempre visível, auditado. Desativação invalida sessões em Redis imediatamente — alinhado a "soft delete + sem reuso de sessão". Sem PII nova no JWT (só `sub`, `role`, `tenant_slug`, marcadores de impersonation). |
| V. Simplicity | ✅ PASS | Reuso integral do mecanismo `Microsoft.AspNetCore.Authorization` (built-in .NET). Zero pacote novo. Diretiva Angular `*omniHasRole` é trivial (~30 linhas) e cobre todas as necessidades de gating na UI — alternativa via NgIf+condicionais inline foi rejeitada por gerar duplicação. ADR não exigido (apenas reuso de built-ins). |
| VI. Observability e Auditability | ✅ PASS | FR-031 obriga marcar `impersonated_by` em toda ação durante impersonation; persistido via Serilog→Mongo. Desativação gera evento auditado. Negações de policy logadas em nível `Warning` com contexto (user, role, action, tenant). |
| VII. Test Discipline | ✅ PASS | Backend: testes paramétricos cobrem **todas** as células das matrizes 4.1–4.12 com Testcontainers (Postgres + Redis reais); zero mock de banco. Frontend: cada guard/diretiva tem `.spec.ts` co-localizado. Constantes (`Roles.*`, `Policies.*`) eliminam magic strings. |

**Resultado**: Constitution Check **APROVADO** sem ressalvas.

## Project Structure

### Documentation (this feature)

```text
specs/004-roles-permissions/
├── plan.md              # Este arquivo
├── research.md          # Phase 0 — decisões técnicas
├── data-model.md        # Phase 1 — Roles, Policies, ciclo de vida do usuário
├── quickstart.md        # Phase 1 — como adicionar nova policy / verificar comportamento
├── contracts/
│   ├── authorization-policies.md   # Phase 1 — nomes de policy ↔ roles ↔ FR/spec
│   ├── department-scoping.md       # Phase 1 — primitiva de filtro por departamento
│   └── impersonation-token.md      # Phase 1 — claims, TTL, regras de uso
├── checklists/
│   └── requirements.md             # validado no /speckit-specify
└── tasks.md             # Phase 2 — gerado por /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── omniDesk.Api/
│   ├── Domain/
│   │   ├── Authorization/
│   │   │   ├── Role.cs                          # NOVO — enum/constantes (Roles.SaasAdmin, .TenantAdmin, .Supervisor, .Attendant)
│   │   │   └── Permissions.cs                   # NOVO — string constants para nomes de policy
│   │   └── Users/                               # já existe (Spec 002)
│   ├── Features/
│   │   ├── Authorization/                       # NOVO — feature transversal
│   │   │   ├── Policies/
│   │   │   │   ├── AuthorizationPoliciesRegistration.cs   # AddAuthorization(...) com todas as ~50 policies
│   │   │   │   ├── RoleRequirement.cs
│   │   │   │   ├── DepartmentScopeRequirement.cs
│   │   │   │   ├── DepartmentScopeHandler.cs
│   │   │   │   └── ImpersonationContextHandler.cs         # marca request com impersonated_by
│   │   │   ├── Impersonation/
│   │   │   │   ├── ImpersonationTokenIssuer.cs            # gera JWT 5min com claims
│   │   │   │   └── ImpersonationAuditEnricher.cs          # Serilog enricher
│   │   │   └── UserLifecycle/
│   │   │       ├── DeactivateUserCommand.cs               # invalidação Redis imediata
│   │   │       ├── ReactivateUserCommand.cs
│   │   │       └── LastTenantAdminGuard.cs                # FR-038
│   │   ├── Admin/                               # já existe (Spec 003)
│   │   ├── Auth/                                # já existe (Spec 002) — recebe `IsActive` + invalidate hook
│   │   └── Me/                                  # já existe
│   └── Infrastructure/
│       └── Authorization/
│           ├── ClaimsTransformer.cs             # NOVO — popula claims `role`, `tenant_slug`, `dept_ids`
│           └── DepartmentScopeFilter.cs         # NOVO — IQueryable<T> extension
│
├── omniDesk.Admin/                              # SPA Painel Admin
│   └── src/app/
│       ├── core/
│       │   ├── auth/                            # já existe — login do saas_admin
│       │   └── authorization/                   # NOVO
│       │       ├── role.signal.ts               # signal<Role | null> derivado do claims
│       │       ├── role.guard.ts                # CanActivate só permite saas_admin
│       │       └── has-role.directive.ts        # *omniHasRole (compartilhada via libs/ se necessário)
│       └── features/
│           └── tenants/                         # já existe — botão "Impersonar" usa endpoint
│
└── omniDesk.Crm/                                # SPA CRM
    └── src/app/
        ├── core/
        │   ├── auth/                            # já existe
        │   ├── authorization/                   # NOVO (estrutura espelhada do Admin)
        │   │   ├── role.signal.ts
        │   │   ├── role.guard.ts                # CanActivate por role
        │   │   ├── permission.guard.ts          # CanActivate por policy nomeada
        │   │   └── has-role.directive.ts        # *omniHasRole
        │   └── impersonation/                   # NOVO
        │       └── impersonation-banner.component.ts   # barra fixa quando claim `impersonating: true`
        └── features/
            └── ...                              # consumidores das specs 04–11

src/omniDesk.Api/tests/                          # testes mora dentro do próprio projeto
└── omniDesk.Api.Tests/
    ├── Domain/Authorization/
    │   └── RoleHierarchyTests.cs                # NOVO — hierarquia + edge cases
    ├── Features/Authorization/Policies/
    │   ├── PolicyMatrixTests.cs                 # NOVO — paramétrico cobrindo TODAS as células
    │   ├── RoleRequirementTests.cs              # NOVO — handler + impersonation
    │   ├── PainelAdminBoundaryTests.cs          # NOVO — gate /admin
    │   ├── SupervisorBoundaryTests.cs           # NOVO — system-only denials
    │   └── TenantAdminFullAccessTests.cs        # NOVO — cobertura herdada
    ├── Features/Authorization/Impersonation/
    │   ├── ImpersonationTokenTests.cs           # NOVO — emissão, claims, expiração
    │   ├── ImpersonationContextTests.cs         # NOVO — enricher Serilog
    │   └── ImpersonationForbiddenActionsTests.cs # NOVO — bloqueios sensíveis
    ├── Features/Authorization/UserLifecycle/
    │   ├── DeactivationFlowTests.cs             # NOVO — invalidação Redis < 1s
    │   ├── ReactivationFlowTests.cs             # NOVO — sem ressuscitar sessões
    │   └── LastTenantAdminGuardTests.cs         # NOVO — FR-038
    ├── Infrastructure/Authorization/
    │   └── DepartmentScopeFilterTests.cs        # NOVO — escopo do attendant
    ├── Helpers/AuthorizationFixture.cs          # NOVO — Postgres + Redis Testcontainers
    └── Performance/ClaimsTransformerBenchmark.cs # NOVO — p95 ≤ 1ms
```

> **Convenção de testes**: cada teste mora dentro da subpasta que espelha a camada do código testado (`Domain/`, `Features/`, `Infrastructure/`). Não há mais `tests/` no root do repositório.

**Structure Decision**: Mantida a arquitetura monorepo já consolidada (uma API + dois SPAs Angular), com nova feature transversal `Features/Authorization/` na API e pastas `core/authorization/` em cada SPA. Nenhum novo projeto/csproj é introduzido — apenas pastas e arquivos dentro dos projetos existentes (`omniDesk.Api`, `omniDesk.Admin`, `omniDesk.Crm`).

## Complexity Tracking

> **Sem violações da constituição** — esta tabela permanece vazia.

Nenhum padrão não-óbvio é introduzido: usamos exclusivamente `Microsoft.AspNetCore.Authorization` (built-in), `Signals` do Angular 21 (built-in), e Serilog + Redis já em produção. A diretiva `*omniHasRole` é uma diretiva estrutural padrão de ~30 linhas — não conta como padrão arquitetural distinto.
