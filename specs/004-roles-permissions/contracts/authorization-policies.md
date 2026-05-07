# Contract: Authorization Policies

Mapeamento canônico **policy ↔ roles permitidas ↔ FR ↔ spec consumidora**. Esta tabela é a **fonte única de verdade** para o registro em `AuthorizationPoliciesRegistration.cs` e para os testes paramétricos em `PolicyMatrixTests.cs`. Qualquer divergência entre código e tabela é um bug de PR.

Convenção:

- `saas_admin` aparece apenas em policies do contexto Painel Admin (4.1) — não acumula com as roles de CRM.
- Hierarquia CRM: `tenant_admin ⊇ supervisor ⊇ attendant`. Quando uma role aparece como permitida, todas acima dela na hierarquia também são permitidas, automaticamente, pelo `RoleRequirement` (sem repetir na tabela).
- "Escopo" indica quando além da role um filtro horizontal por departamento ou propriedade (`próprio`) se aplica — implementado pelo `DepartmentScopeRequirement` ou pela camada de domínio.

---

## 4.1 — Painel Admin (Spec 02)

| Policy | Roles | FR | Notas |
|---|---|---|---|
| `PainelAdmin.Access` | `saas_admin` | FR-008, FR-009 | Pré-requisito de qualquer endpoint montado em `admin.omnicare.ia.br` |

> Todas as ações da matriz 4.1 (listar tenants, provisionar, bloquear, impersonar, etc.) são endpoints da Spec 02; aqui esta spec contribui apenas a policy de gating de contexto.

---

## 4.2 — Departamentos e Atendentes (Spec 04)

| Policy | Roles | Escopo extra | FR |
|---|---|---|---|
| `Departments.Create` | `tenant_admin` | — | FR-010 |
| `Departments.Edit` | `tenant_admin` | — | FR-010 |
| `Departments.Deactivate` | `tenant_admin` | — | FR-010 |
| `Departments.List` | `tenant_admin`, `supervisor`, `attendant` | `attendant` recebe apenas departamentos vinculados (`DepartmentScope=Membership`) | FR-013 |
| `Attendants.Create` | `supervisor` | — | FR-011 |
| `Attendants.Edit` | `supervisor` | `attendant` permitido apenas para o próprio perfil (`Scope=Self`) | FR-011 |
| `Attendants.Deactivate` | `supervisor` | — | FR-011 |
| `Attendants.UpdateOwnStatus` | `attendant` | `Scope=Self` | FR-012 |
| `Attendants.ViewAnyTickets` | `supervisor` | `attendant` vê apenas os próprios — não recebe a policy | matriz 4.2 |
| `QuickReplies.Create` | `attendant` | — | matriz 4.2 |
| `QuickReplies.EditAny` | `supervisor` | `attendant` apenas as próprias (`Scope=Self`) | matriz 4.2 |
| `Tickets.TransferOrAssign` | `attendant` | — | matriz 4.2 |

Hierarquia automática: onde `supervisor` aparece, `tenant_admin` é igualmente permitido. Onde `attendant` aparece, todas as roles superiores também são.

---

## 4.3 — Live Chat: Widget (Spec 06)

| Policy | Roles | FR |
|---|---|---|
| `Widget.View` | `supervisor` | FR-014 |
| `Widget.EditAppearance` | `supervisor` | FR-014 |
| `Widget.EditPrivacyTerms` | `supervisor` | FR-014 |
| `Widget.EditDomains` | `tenant_admin` | FR-014 |
| `Widget.Toggle` | `tenant_admin` | FR-014 |
| `Widget.ViewInstallationCode` | `supervisor` | FR-014 |

---

## 4.4 — Live Chat: Conversas (Spec 06)

| Policy | Roles | Escopo | FR |
|---|---|---|---|
| `Conversations.ViewAll` | `supervisor` | — | FR-015 |
| `Conversations.ViewScoped` | `attendant` | `DepartmentScope=Membership` ou `AssignedToUser` | FR-015 |
| `Conversations.CloseManually` | `attendant` | apenas atribuídas a si (`Scope=Assigned`) | matriz 4.4 |
| `Conversations.ManageBrowserNotifications` | `attendant` | `Scope=Self` | matriz 4.4 |

---

## 4.5 — Agentes de IA (Spec 05)

| Policy | Roles | FR |
|---|---|---|
| `Agents.View` | `supervisor` | FR-016 |
| `Agents.EditOrchestrator` | `supervisor` | FR-016 |
| `Agents.ManageSubAgents` | `supervisor` | FR-016 |
| `Agents.UsePlayground` | `supervisor` | FR-016 |
| `Agents.EditAdvancedConfig` | `tenant_admin` | FR-016 |
| `Agents.ViewLogs` | `supervisor` | matriz 4.5 |

`attendant` não recebe nenhuma policy de Agentes (FR-017).

---

## 4.6 — Tickets (Spec 08)

| Policy | Roles | Escopo | FR |
|---|---|---|---|
| `Tickets.ViewAll` | `supervisor` | — | FR-018 |
| `Tickets.ViewScoped` | `attendant` | `DepartmentScope=Membership` | FR-018 |
| `Tickets.CreateManually` | `attendant` | — | matriz 4.6 |
| `Tickets.EditAny` | `supervisor` | `attendant` apenas próprios (`Scope=Owner`) | FR-018 |
| `Tickets.ChangeStatus` | `attendant` | apenas com acesso ao ticket | matriz 4.6 |
| `Tickets.Transfer` | `attendant` | — | matriz 4.6 |
| `Tickets.AddNotes` | `attendant` | apenas em tickets próprios (`Scope=Owner`) | matriz 4.6 |
| `Tickets.ViewEventHistory` | `attendant` | apenas em tickets próprios (`Scope=Owner`) | matriz 4.6 |
| `Tickets.ConfigurePipeline` | `supervisor` | — | FR-019 |

---

## 4.7 — Contatos (Spec 08)

| Policy | Roles | FR |
|---|---|---|
| `Contacts.Manage` | `attendant` | FR-020 |

(Listagem, busca, perfil, histórico, criação e edição agrupados nesta única policy — todas as roles de CRM, sem distinção por departamento.)

---

## 4.8 — WhatsApp (Spec 07)

| Policy | Roles | FR |
|---|---|---|
| `Whatsapp.ViewStatus` | `supervisor` | FR-021 |
| `Whatsapp.EditConfig` | `tenant_admin` | FR-021 |
| `Whatsapp.ViewAccessToken` | `tenant_admin` | FR-022 |
| `Whatsapp.Toggle` | `tenant_admin` | FR-021 |
| `Whatsapp.ViewTemplates` | `supervisor` | FR-021 |
| `Whatsapp.ManageTemplates` | `supervisor` | FR-021 |

`supervisor` enxerga apenas o estado "configurado/não configurado" do Access Token — implementado na resposta da Spec 07 (não é uma policy distinta, é serialização condicional pela role).

---

## 4.9 — Notificações (Spec 09)

| Policy | Roles | Escopo | FR |
|---|---|---|---|
| `Notifications.ViewOwn` | `attendant` | `Scope=Self` | matriz 4.9 |
| `Notifications.MarkAsRead` | `attendant` | `Scope=Self` | matriz 4.9 |
| `Notifications.ConfigurePushPreferences` | `attendant` | `Scope=Self` | matriz 4.9 |
| `Notifications.ConfigureForClients` | `supervisor` | — | FR-023 |

---

## 4.10 — Agenda (Spec 10)

| Policy | Roles | FR |
|---|---|---|
| `Agenda.View` | `attendant` | matriz 4.10 |
| `Agenda.ManageAppointment` | `attendant` | matriz 4.10 |
| `Agenda.ConfirmAppointment` | `attendant` | matriz 4.10 |
| `Agenda.CancelAppointment` | `attendant` | matriz 4.10 |
| `Agenda.MarkNoShow` | `attendant` | matriz 4.10 |
| `Agenda.ResendReminder` | `attendant` | matriz 4.10 |
| `Agenda.ManageProfessionals` | `supervisor` | FR-024 |
| `Agenda.ManageServices` | `supervisor` | FR-024 |
| `Agenda.ConfigureAvailability` | `supervisor` | FR-024 |
| `Agenda.ConfigureCancellationPolicy` | `tenant_admin` | FR-024 |

---

## 4.11 — Auditoria (Spec 11)

| Policy | Roles | FR |
|---|---|---|
| `Audit.ViewActivity` | `tenant_admin` | FR-025 |
| `Audit.ManageApiKeys` | `tenant_admin` | FR-025 |

---

## 4.12 — Autenticação (Spec 01)

| Policy | Roles | Escopo | FR |
|---|---|---|---|
| `Auth.InviteUser` | `supervisor` | `supervisor` só pode convidar `attendant`/`supervisor` | FR-033 |
| `Auth.InviteSupervisor` | `tenant_admin` | exclusivo (não acumula via hierarquia para fins de criar role par) | FR-033 |
| `Auth.DeactivateUser` | `tenant_admin` | bloqueado se alvo é último `tenant_admin` (R9) | FR-034, FR-038 |
| `Auth.ManageOwnSession` | `attendant` | `Scope=Self` | matriz 4.12 |
| `Auth.ManageOwn2FA` | `attendant` | `Scope=Self` | matriz 4.12 |

> `Auth.InviteSupervisor` é o único caso em que **não** queremos a herança implícita: o `supervisor` está abaixo na hierarquia mas a regra exige que somente `tenant_admin` crie outros `supervisor`. Implementado como `RequireExactRole(Roles.TenantAdmin)` no `RoleRequirement`.

---

## Forma de registro (referência para implementação)

```csharp
// AuthorizationPoliciesRegistration.cs (resumo)
services.AddAuthorization(options =>
{
    // Painel Admin
    options.AddPolicy(Policies.PainelAdminAccess,
        p => p.AddRequirements(new RoleRequirement(Roles.SaasAdmin, exact: true)));

    // Departamentos
    options.AddPolicy(Policies.CanCreateDepartment,
        p => p.AddRequirements(new RoleRequirement(Roles.TenantAdmin, exact: true)));
    options.AddPolicy(Policies.CanListDepartments,
        p => p.AddRequirements(
            new RoleRequirement(Roles.Attendant),                  // hierarchy: ≥ attendant
            new DepartmentScopeRequirement(Scope.Membership)));    // attendant filtrado

    // ... (~50 entradas no total — derivadas das tabelas acima)
});
```

A correspondência é 1:1 com as tabelas desta página. Adicionar/remover entradas em uma só dessas duas fontes é defeito.

---

## Como evoluir

1. **Nova ação em uma spec**: adicionar nova linha na tabela apropriada acima, depois registrar no `AuthorizationPoliciesRegistration.cs`, depois adicionar caso no `PolicyMatrixTests.cs`. Order matters — sem entrada na tabela, PR é rejeitado.
2. **Nova role**: violação da Constituição V (escopo deliberado V1 = 4 roles). Exige amendment + ADR.
3. **Renomear policy**: refactor coordenado (constante + tabela + testes + chamadas em endpoints). Considerar deprecation para releases anteriores.
