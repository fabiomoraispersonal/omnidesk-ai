# Phase 1 — Data Model: Roles e Permissões

Esta spec **não introduz novas tabelas no banco**. Ela formaliza tipos de domínio (enums e constantes) e descreve o ciclo de vida de entidades já existentes (Spec 002 — `users`, `refresh_tokens`; Spec 003 — `tenants`, `tenant_admin` inicial).

---

## 1. Tipos de domínio (código, não persistidos)

### 1.1 `Role` (enum)

```csharp
namespace OmniDesk.Domain.Authorization;

public enum Role
{
    SaasAdmin,    // Apenas no Painel Admin (admin.omnicare.ia.br)
    TenantAdmin,  // Super admin do tenant
    Supervisor,   // Operacional
    Attendant     // Atendimento
}
```

Persistência: armazenado como `varchar(20)` em `public.users.role` (lower_snake: `saas_admin`, `tenant_admin`, `supervisor`, `attendant`). Conversão via `EnumToStringConverter`.

### 1.2 `Roles` (constantes — nomes em string)

```csharp
public static class Roles
{
    public const string SaasAdmin   = "saas_admin";
    public const string TenantAdmin = "tenant_admin";
    public const string Supervisor  = "supervisor";
    public const string Attendant   = "attendant";

    public static readonly IReadOnlyList<string> AllCrmRoles =
        [TenantAdmin, Supervisor, Attendant];
}
```

Uso: claim `role` do JWT, comparações em `RoleRequirement`, testes paramétricos.

### 1.3 `Policies` (constantes — nomes das policies)

```csharp
public static class Policies
{
    // 4.1 Painel Admin
    public const string PainelAdminAccess           = "PainelAdmin.Access";

    // 4.2 Departamentos
    public const string CanCreateDepartment         = "Departments.Create";
    public const string CanEditDepartment           = "Departments.Edit";
    public const string CanListDepartments          = "Departments.List";
    public const string CanCreateAttendant          = "Attendants.Create";
    public const string CanEditAttendant            = "Attendants.Edit";
    public const string CanDeactivateAttendant      = "Attendants.Deactivate";
    public const string CanViewAnyAttendantTickets  = "Attendants.ViewAnyTickets";

    // 4.3 Live Chat — Widget
    public const string CanViewWidgetConfig         = "Widget.View";
    public const string CanEditWidgetAppearance     = "Widget.EditAppearance";
    public const string CanEditAuthorizedDomains    = "Widget.EditDomains";
    public const string CanToggleWidget             = "Widget.Toggle";

    // 4.4 Live Chat — Conversas
    public const string CanViewAllConversations     = "Conversations.ViewAll";

    // 4.5 Agentes de IA
    public const string CanViewAgents               = "Agents.View";
    public const string CanEditOrchestrator         = "Agents.EditOrchestrator";
    public const string CanManageSubAgents          = "Agents.ManageSubAgents";
    public const string CanUseAgentPlayground       = "Agents.UsePlayground";
    public const string CanEditAgentAdvancedConfig  = "Agents.EditAdvancedConfig";

    // 4.6 Tickets
    public const string CanViewAllTickets           = "Tickets.ViewAll";
    public const string CanConfigurePipelineColumns = "Tickets.ConfigurePipeline";

    // 4.7 Contatos
    public const string CanManageContacts           = "Contacts.Manage";

    // 4.8 WhatsApp
    public const string CanViewChannelStatus        = "Whatsapp.ViewStatus";
    public const string CanEditChannelConfig        = "Whatsapp.EditConfig";
    public const string CanViewAccessToken          = "Whatsapp.ViewAccessToken";
    public const string CanToggleChannel            = "Whatsapp.Toggle";
    public const string CanManageTemplates          = "Whatsapp.ManageTemplates";

    // 4.9 Notificações
    public const string CanConfigureClientNotifications = "Notifications.ConfigureForClients";

    // 4.10 Agenda
    public const string CanManageProfessionals      = "Agenda.ManageProfessionals";
    public const string CanManageServiceCatalog     = "Agenda.ManageServices";
    public const string CanConfigureAvailability    = "Agenda.ConfigureAvailability";
    public const string CanConfigureCancellationPolicy = "Agenda.ConfigureCancellationPolicy";

    // 4.11 Auditoria
    public const string CanViewAuditActivity        = "Audit.ViewActivity";
    public const string CanManageAuditApiKeys       = "Audit.ManageApiKeys";

    // 4.12 Auth
    public const string CanInviteUser               = "Auth.InviteUser";
    public const string CanInviteSupervisor         = "Auth.InviteSupervisor";  // só tenant_admin
    public const string CanDeactivateUser           = "Auth.DeactivateUser";
}
```

A correspondência entre policy e roles permitidas vive em [contracts/authorization-policies.md](contracts/authorization-policies.md) e é registrada em `AuthorizationPoliciesRegistration.cs`.

### 1.4 `RoleHierarchy` (auxiliar para herança)

```csharp
public static class RoleHierarchy
{
    // tenant_admin (3) > supervisor (2) > attendant (1)
    private static readonly Dictionary<string, int> Rank = new()
    {
        [Roles.TenantAdmin] = 3,
        [Roles.Supervisor]  = 2,
        [Roles.Attendant]   = 1,
    };

    public static bool IsAtLeast(string actual, string minimum) =>
        Rank.TryGetValue(actual, out var a) &&
        Rank.TryGetValue(minimum, out var m) &&
        a >= m;
}
```

Usado pelo `RoleRequirement` para implementar FR-004 (cumulatividade silenciosa). `saas_admin` é tratado fora desta hierarquia (vive em outro contexto).

---

## 2. Entidades persistidas afetadas

### 2.1 `public.users` (já existe — Spec 002)

Colunas relevantes para esta spec:

| Coluna | Tipo | Origem | Notas |
|---|---|---|---|
| `id` | `uuid` PK | Spec 002 | — |
| `tenant_id` | `uuid` NULL | Spec 002 | NULL apenas para `saas_admin` |
| `email` | `varchar` UNIQUE | Spec 002 | — |
| `role` | `varchar(20)` NOT NULL | Spec 002 | Valores em `Roles.*` |
| `is_active` | `bool` NOT NULL DEFAULT true | Spec 002 | Esta spec adiciona invalidação Redis (R8) |
| `deactivated_at` | `timestamptz` NULL | **NOVO nesta spec** | Marca quando a desativação ocorreu (auditoria) |
| `created_at` | `timestamptz` | Spec 002 | — |
| `updated_at` | `timestamptz` | Spec 002 | — |

**Migration**: adicionar coluna `deactivated_at` em uma migration EF Core nesta spec. Sem alteração em demais colunas.

**Constraint reforçado pela aplicação (R9 — não no schema)**:

> Para qualquer `tenant_id`, `COUNT(*) WHERE role = 'tenant_admin' AND is_active = true >= 1`.

### 2.2 `public.user_departments` (já existe — Spec 002 ou 04)

Tabela associativa N:N entre `users` e `departments`. Esta spec apenas **lê** via `DepartmentScopeFilter` para aplicar o escopo do `attendant`.

| Coluna | Tipo |
|---|---|
| `user_id` | `uuid` FK → `users.id` |
| `department_id` | `uuid` FK → `departments.id` (Spec 04) |

(Schema final fica formalizado pela Spec 04 — Departamentos. Esta spec não cria a tabela; apenas formaliza seu uso.)

### 2.3 `public.refresh_tokens` (já existe — Spec 002)

Esta spec não altera a estrutura. Define que **todos** os refresh tokens de um usuário são invalidados em duas situações:

- Desativação (FR-036).
- Mudança de role do usuário (edge case "Promoção/rebaixamento de role" na spec).

Mecanismo: `redis.DEL("{tenant_slug}:refresh:{user_id}:*")` (Spec 002 já mantém o índice em Redis).

---

## 3. Entidades efêmeras (não persistidas no banco)

### 3.1 Sessão de impersonation

Representada por um JWT de 5 minutos com claims:

```json
{
  "sub": "saas_admin",
  "role": "saas_admin",
  "tenant_slug": "{slug-alvo}",
  "impersonating": true,
  "impersonated_by": "saas_admin",
  "iss": "omnidesk-saas",
  "aud": "omnidesk-crm",
  "iat": 1746555000,
  "exp": 1746555300
}
```

- **Não** há refresh token associado.
- **Não** há registro persistido — observabilidade vem do log estruturado (Serilog → Mongo) que captura cada ação durante a sessão (FR-031).
- Detalhes completos em [contracts/impersonation-token.md](contracts/impersonation-token.md).

### 3.2 Cache de claims (Redis)

Chave: `{tenant_slug}:user:{user_id}:claims`
TTL: 60 segundos (R3)
Valor (JSON):

```json
{
  "role": "supervisor",
  "is_active": true,
  "department_ids": ["uuid-a", "uuid-b"]
}
```

**Não é persistido fora do Redis.** Recriado pela `IClaimsTransformation` no primeiro request após miss/expiração. Purgado explicitamente pelo `DeactivateUserCommand` (R8).

---

## 4. Estados e transições

### 4.1 Ciclo de vida do usuário

```
[Convidado] ──aceita convite──▶ [Ativo]
                                   │
                       deactivate (tenant_admin)
                                   ▼
                                [Inativo] ──reactivate──▶ [Ativo*]
                                                            *exige novo login (FR-037)
```

Restrição (FR-038): a transição `Ativo → Inativo` é **bloqueada** quando o usuário é `tenant_admin` e é o último ativo do tenant.

### 4.2 Modo do request

```
[Anônimo] ──login──▶ [Autenticado normal]    role ∈ {tenant_admin, supervisor, attendant}
[Anônimo] ──login admin──▶ [Autenticado SaaS] role = saas_admin
[Autenticado SaaS] ──gerar token de impersonation──▶ [Impersonating]   exp ≤ 5 min
[Impersonating] ──exp atinge limite──▶ (sem refresh; volta ao painel admin)
```

A transição `[Autenticado SaaS] → [Impersonating]` é a única forma de um `saas_admin` operar no contexto CRM (FR-003).

---

## 5. Validações e regras de domínio

| Regra | Implementação | FR/SC |
|---|---|---|
| Role deve estar no enum fechado | `EnumToStringConverter` + check de FluentValidation em criação/promoção | FR-001 |
| Tenant_id NULL ⇔ role = saas_admin | Constraint na aplicação (validator do `CreateUserCommand`) | FR-002 |
| saas_admin não pode ser criado via CRM | Validator rejeita `Roles.SaasAdmin` em comandos do contexto tenant | FR-002 |
| Hierarquia tenant_admin ⊇ supervisor ⊇ attendant | `RoleHierarchy.IsAtLeast()` no `RoleRequirement` | FR-004 |
| Negar por padrão | Endpoints sem `[Authorize(Policy=...)]` retornam 401 (autenticação obrigatória); ações sem policy listada na matriz exigem update da spec antes de implementar | FR-006 |
| Tenant_id do request deve casar com tenant_slug do JWT | `TenantResolverMiddleware` (Spec 003) — esta spec apenas confia | FR-007, FR-026 |
| Token de impersonation: TTL ≤ 5 min, sem refresh | `ImpersonationTokenIssuer` lê env `IMPERSONATION_JWT_TTL_SECONDS` (default 300, máximo enforced 600) | FR-029 |
| Banner de impersonation sempre visível | Componente Angular lê claim `impersonating` do token e renderiza barra fixa | FR-030 |
| Ação durante impersonation marca `impersonated_by` no log | `ImpersonationAuditEnricher` (Serilog) | FR-031 |
| Desativação invalida sessões em ≤ 1 s | `DeactivateUserCommand` purga Redis (R8) | FR-036, SC-005 |
| Reativação exige novo login | Não restaura refresh tokens (apenas `is_active = true`) | FR-037 |
| Último tenant_admin não pode ser desativado | `LastTenantAdminGuard` antes da mutação (R9) | FR-038 |

---

## 6. Diagrama relacional simplificado

```
public.tenants (Spec 003)
    │ id (uuid, PK)
    └────┐
         │
public.users (Spec 002 + esta spec)
    │ id (uuid, PK)
    │ tenant_id (uuid, FK → tenants.id; NULL para saas_admin)
    │ role (varchar — Roles.*)
    │ is_active (bool)
    │ deactivated_at (timestamptz, NULL) ◀── NOVO nesta spec
    └────┐
         │
public.user_departments (Spec 04)
    │ user_id (uuid, FK → users.id)
    │ department_id (uuid, FK → departments.id)
```

Sem novas FKs, sem novas tabelas — apenas a coluna `deactivated_at` adicionada via migration.

---

## 7. Considerações sobre dados sensíveis

- **Nenhum dado sensível adicional é armazenado** por esta spec.
- O JWT de impersonation, sendo curto (5 min), não exige armazenamento — vive apenas no header `Authorization` do navegador do `saas_admin` durante a sessão.
- Logs de auditoria (Mongo) **não** registram tokens completos — apenas claims relevantes (`user_id`, `role`, `tenant_slug`, `impersonated_by`, `action`, `timestamp`).
