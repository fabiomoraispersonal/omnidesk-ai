---

description: "Task list for Roles e Permissões implementation"
---

# Tasks: Roles e Permissões

**Input**: Design documents from `/specs/004-roles-permissions/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: A constituição (princípio VII — Test Discipline) torna testes **obrigatórios** neste projeto. Toda task de implementação backend tem teste de integração com Testcontainers (Postgres + Redis reais — sem mock). Toda task de frontend tem `.spec.ts` co-localizado.

**Organization**: Tasks agrupadas por user story para entrega independente e validação incremental.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Pode rodar em paralelo (arquivo distinto, sem dependência pendente)
- **[Story]**: Mapeia para a user story (US1–US7) — ausente em Setup/Foundational/Polish
- Caminhos de arquivo absolutos do repositório (`src/...`, `src/omniDesk.Api/tests/...`)

## Path Conventions

- Backend: `src/omniDesk.Api/{Domain,Features,Infrastructure}/`
- Frontend Admin: `src/omniDesk.Admin/src/app/`
- Frontend CRM: `src/omniDesk.Crm/src/app/`
- Tests: `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/` (backend); `.spec.ts` co-localizado (frontend)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuração de ambiente e estrutura de pastas para esta feature.

- [X] T001 Adicionar `IMPERSONATION_JWT_TTL_SECONDS=300` em `src/omniDesk.Api/.env.example` e em `src/omniDesk.Api/appsettings.Development.json` (chave `Authorization:ImpersonationJwtTtlSeconds`); documentar máximo 600 no README local
- [X] T002 Criar EF Core migration `Add_DeactivatedAt_To_Users` em `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` adicionando coluna `deactivated_at timestamptz NULL` a `public.users`
- [X] T003 [P] Criar estrutura de pastas backend: `src/omniDesk.Api/Domain/Authorization/`, `src/omniDesk.Api/Features/Authorization/Policies/`, `src/omniDesk.Api/Features/Authorization/Impersonation/`, `src/omniDesk.Api/Features/Authorization/UserLifecycle/`, `src/omniDesk.Api/Infrastructure/Authorization/`
- [X] T004 [P] Criar estrutura de pastas frontend: `src/omniDesk.Admin/src/app/core/authorization/`, `src/omniDesk.Crm/src/app/core/authorization/`, `src/omniDesk.Crm/src/app/core/impersonation/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Núcleo do framework de autorização. Bloqueia TODAS as user stories.

**⚠️ CRITICAL**: Nenhuma user story pode começar antes deste bloco completar.

### Backend — domínio e contratos

- [X] T005 [P] Criar enum `Role` em `src/omniDesk.Api/Domain/Authorization/Role.cs` com valores `SaasAdmin`, `TenantAdmin`, `Supervisor`, `Attendant` (conforme data-model.md §1.1)
- [X] T006 [P] Criar classe estática `Roles` (constantes string) em `src/omniDesk.Api/Domain/Authorization/Roles.cs` com `SaasAdmin = "saas_admin"`, etc., e `IReadOnlyList<string> AllCrmRoles` (data-model.md §1.2)
- [X] T007 [P] Criar classe estática `Policies` em `src/omniDesk.Api/Domain/Authorization/Permissions.cs` com **todas** as constantes listadas em data-model.md §1.3 (~50 entradas — `PainelAdminAccess`, `CanCreateDepartment`, … `CanDeactivateUser`)
- [X] T008 [P] Criar `RoleHierarchy` em `src/omniDesk.Api/Domain/Authorization/RoleHierarchy.cs` com método `IsAtLeast(actual, minimum)` implementando ranks tenant_admin=3, supervisor=2, attendant=1 (data-model.md §1.4)

### Backend — requirements e handlers

- [X] T009 Criar `RoleRequirement` em `src/omniDesk.Api/Features/Authorization/Policies/RoleRequirement.cs` (propriedades `MinimumRole`, `Exact` bool); depende de T006, T008
- [X] T010 Criar `RoleRequirementHandler : AuthorizationHandler<RoleRequirement>` em `src/omniDesk.Api/Features/Authorization/Policies/RoleRequirementHandler.cs` aplicando hierarquia (research.md R1) ou `Exact`; trata também o caso de impersonation (`role=saas_admin + impersonating=true ⇒ tratar como tenant_admin`); depende de T009

### Backend — claims, cache e current user

- [X] T011 [P] Estender `ICurrentUser` em `src/omniDesk.Api/Infrastructure/Authentication/ICurrentUser.cs` adicionando `string Role { get; }`, `IReadOnlyList<Guid> DepartmentIds { get; }`, `bool IsImpersonating { get; }`, `string TenantSlug { get; }`
- [X] T012 Criar `ClaimsCache` em `src/omniDesk.Api/Infrastructure/Authorization/ClaimsCache.cs` — Redis backed, chave `{tenant_slug}:user:{user_id}:claims`, TTL 60s; métodos `GetAsync`, `SetAsync`, `InvalidateAsync` (research.md R3)
- [X] T013 Criar `ClaimsTransformer : IClaimsTransformation` em `src/omniDesk.Api/Infrastructure/Authorization/ClaimsTransformer.cs` — lê `sub`+`tenant_slug` do JWT, busca em `ClaimsCache` (miss ⇒ Postgres), injeta claims `role`, `is_active`, `dept_ids`; falha quando `is_active=false` (R3, R8); depende de T011, T012

### Backend — DI wire-up e migration

- [X] T014 Aplicar migration de T002: rodar `dotnet ef database update --project src/omniDesk.Api`
- [X] T015 Wire DI em `src/omniDesk.Api/Program.cs`: registrar `IClaimsTransformation` (Scoped → `ClaimsTransformer`), `IAuthorizationHandler` (Singleton → `RoleRequirementHandler`), `ClaimsCache` (Singleton, conexão Redis); depende de T010, T013

### Frontend — Painel Admin

- [X] T016 [P] Criar `src/omniDesk.Admin/src/app/core/authorization/role.signal.ts` — `signal<Role | null>` derivado das claims do `AuthService` existente (Spec 002)
- [X] T017 [P] Criar `src/omniDesk.Admin/src/app/core/authorization/role.guard.ts` — `CanActivateFn` permitindo apenas `Roles.SaasAdmin` (research.md R5)
- [X] T018 [P] Criar `src/omniDesk.Admin/src/app/core/authorization/has-role.directive.ts` — diretiva estrutural `*omniHasRole` aceitando array de roles
- [X] T019 [P] Criar `.spec.ts` co-localizados para T016, T017, T018 (3 arquivos)

### Frontend — CRM

- [X] T020 [P] Criar `src/omniDesk.Crm/src/app/core/authorization/role.signal.ts` — `signal<Role | null>` + `computed` `isAtLeastSupervisor`, `isImpersonating`
- [X] T021 [P] Criar `src/omniDesk.Crm/src/app/core/authorization/role.guard.ts` — `CanActivateFn` por role mínima usando hierarquia
- [X] T022 [P] Criar `src/omniDesk.Crm/src/app/core/authorization/permission.guard.ts` — `CanActivateFn` por nome de policy (consulta endpoint `GET /me/permissions`)
- [X] T023 [P] Criar `src/omniDesk.Crm/src/app/core/authorization/has-role.directive.ts` — espelho da diretiva do Admin
- [X] T024 [P] Criar `.spec.ts` co-localizados para T020, T021, T022, T023 (4 arquivos)

**Checkpoint**: Foundation pronta. User stories podem prosseguir em paralelo.

---

## Phase 3: User Story 1 — Modelo de roles consistente e matriz registrada (Priority: P1) 🎯 MVP

**Goal**: Todas as ~50 policies da matriz (seções 4.1–4.12) registradas e cobertas por teste paramétrico único; framework rastreável pela constante `Policies.*` em PR.

**Independent Test**: Rodar `dotnet test --filter PolicyMatrixTests` — 100% das células da matriz passam (cada combinação `(role, policy)` retorna 200/403 conforme spec).

### Implementation for User Story 1

- [X] T025 [US1] Implementar `AuthorizationPoliciesRegistration` em `src/omniDesk.Api/Features/Authorization/Policies/AuthorizationPoliciesRegistration.cs` registrando **todas** as policies de `contracts/authorization-policies.md` (~50 entradas, seções 4.1–4.12); seguir o exemplo de registro no final do contract
- [X] T026 [US1] Invocar `AuthorizationPoliciesRegistration.Register(builder.Services)` em `src/omniDesk.Api/Program.cs` (após `AddAuthentication`); depende de T025

### Tests for User Story 1

- [X] T027 [P] [US1] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/Policies/PolicyMatrixTests.cs` — `[Theory]` + `[MemberData]` cobrindo TODAS as células da matriz; cada caso: autentica como role X, dispara request HTTP em endpoint sentinela, verifica 200/403 conforme tabela; usa Testcontainers Postgres + Redis (research.md R6)
- [X] T028 [P] [US1] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Domain/Authorization/RoleHierarchyTests.cs` — `IsAtLeast` para todos os pares possíveis incluindo edge cases (role nula, role desconhecida)
- [X] T029 [P] [US1] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/Policies/RoleRequirementTests.cs` — handler trata: hierarquia, exact, impersonation (saas_admin+impersonating=true ⇒ tenant_admin)

**Checkpoint**: Matriz da spec ↔ código ↔ testes em sincronia. Esta é a base mínima viável (MVP).

---

## Phase 4: User Story 2 — Operador SaaS gerencia a plataforma (Priority: P1)

**Goal**: Painel Admin gateado por `Policies.PainelAdminAccess`; usuários CRM bloqueados; criação de `saas_admin` via CRM rejeitada.

**Independent Test**: Token CRM tentando acessar `/admin/*` retorna 401/403; tentativa de criar usuário com `role=saas_admin` via endpoint do CRM retorna 422.

### Implementation for User Story 2

- [X] T030 [US2] Aplicar `RequireAuthorization(Policies.PainelAdminAccess)` ao route group `/admin/*` em `src/omniDesk.Api/Program.cs` (ou onde os endpoints do Painel Admin são montados — Spec 03 já definiu o group)
- [X] T031 [US2] Adicionar regra em `src/omniDesk.Api/Features/Auth/Commands/Validators/CreateUserCommandValidator.cs` rejeitando `Roles.SaasAdmin` em comandos no contexto CRM (FluentValidation: `RuleFor(x => x.Role).NotEqual(Roles.SaasAdmin)`)
- [X] T032 [US2] Aplicar `roleGuard` (T017) às rotas de `src/omniDesk.Admin/src/app/app.routes.ts`

### Tests for User Story 2

- [X] T033 [P] [US2] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/Policies/PainelAdminBoundaryTests.cs` — token de cada role CRM em endpoint admin → 401/403; saas_admin → 200
- [X] T034 [P] [US2] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Auth/SaasAdminCreationGuardTests.cs` — POST `/users` com `role=saas_admin` retorna 422 com mensagem clara

**Checkpoint**: Painel Admin protegido; criação ilegítima de saas_admin bloqueada.

---

## Phase 5: User Story 3 — Tenant Admin assume o CRM no provisionamento (Priority: P1)

**Goal**: O usuário criado pela Spec 03 no provisionamento recebe `role=tenant_admin` e tem acesso herdado a tudo (≥ supervisor ≥ attendant).

**Independent Test**: Após `POST /admin/tenants` (Spec 03), primeiro login do destinatário no CRM permite executar uma policy de cada nível hierárquico.

### Implementation for User Story 3

- [X] T035 [US3] Verificar/ajustar `src/omniDesk.Api/Features/Admin/Tenants/TenantProvisioningJob.cs` (Spec 03) — confirmar que cria usuário inicial com `Role = Roles.TenantAdmin` (string) e `IsActive = true`; ajustar se ainda usa identificador antigo

### Tests for User Story 3

- [X] T036 [P] [US3] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/Policies/TenantAdminFullAccessTests.cs` — Testcontainers: provisiona tenant, login como super admin recém-criado, executa um endpoint protegido por `Policies.CanCreateDepartment` (tenant_admin only), `Policies.CanCreateAttendant` (≥ supervisor) e `Policies.CanManageContacts` (qualquer role CRM); todos retornam 200

**Checkpoint**: Spec 03 + esta spec integradas; tenant_admin opera em pleno desde o primeiro login.

✅ **Fim do MVP** — Specs 002 (auth), 003 (provisioning) e 004 (P1) entregam o caminho completo: provisionar tenant → tenant_admin loga → opera CRM com matriz aplicada.

---

## Phase 6: User Story 4 — Supervisor opera departamentos sem mexer no sistema (Priority: P2)

**Goal**: Supervisor recebe ✅ na coluna correspondente da matriz e ❌ explícito em itens system-only (Widget.EditDomains, Widget.Toggle, Whatsapp.EditConfig, Whatsapp.ViewAccessToken, Audit.ViewActivity, Agenda.ConfigureCancellationPolicy).

**Independent Test**: Token de `supervisor` retorna 403 nas 6 ações system-only listadas; retorna 200 nas operacionais.

### Tests for User Story 4

- [X] T037 [P] [US4] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/Policies/SupervisorBoundaryTests.cs` — supervisor explicitamente NÃO acessa as 6 policies system-only; verifica também serialização condicional do Access Token do WhatsApp (apenas "configured/not_configured" — coordenar com Spec 07 quando esta for implementada; aqui criar shape de teste com endpoint mockado/sentinela)

**Checkpoint**: Boundary supervisor ↔ tenant_admin auditada por testes.

---

## Phase 7: User Story 5 — Atendente atua apenas no seu escopo (Priority: P2)

**Goal**: Primitiva `IQueryable<T>.ForCurrentUserScope()` disponível para todas as queries de tickets/conversas; `attendant` recebe apenas itens de seus departamentos (ou atribuídos a si).

**Independent Test**: `attendant` vinculado ao Dept A não enxerga tickets do Dept B; com 0 depts vinculados, vê 0 tickets; com 2 depts vê os dois.

### Implementation for User Story 5

- [X] T038 [US5] Criar extension method `ForCurrentUserScope<T>` em `src/omniDesk.Api/Infrastructure/Authorization/DepartmentScopeFilter.cs` conforme `contracts/department-scoping.md` (assinatura completa, no-op para roles ≠ attendant)
- [X] T039 [US5] Criar overload `ForCurrentUserScopeOrAssignment` específico para `Ticket` e `Conversation` (filtro por dept OR assigned_to_user_id) — `contracts/department-scoping.md` §3
- [X] T040 [US5] Criar `DepartmentScopeRequirement` + handler em `src/omniDesk.Api/Features/Authorization/Policies/DepartmentScopeRequirement.cs` (apenas para policies que carregam `Scope=Membership`); registrar handler no DI

### Tests for User Story 5

- [X] T041 [P] [US5] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Infrastructure/Authorization/DepartmentScopeFilterTests.cs` — Testcontainers, casos: attendant com 1 dept, com 2 depts, com 0 depts, supervisor bypass, tenant_admin bypass, variant `OrAssignment` expõe ticket atribuído fora do escopo

**Checkpoint**: Specs 06 e 08 podem invocar `ForCurrentUserScope` diretamente quando implementadas.

---

## Phase 8: User Story 6 — Operador SaaS impersona tenant para suporte (Priority: P3)

**Goal**: `saas_admin` gera token JWT de 5 min via Painel Admin, abre CRM com banner permanente, todas as ações auditadas com `impersonated_by`, refresh rejeitado, ações sensíveis bloqueadas.

**Independent Test**: Fluxo manual em `quickstart.md §B` completa em 5 minutos com banner visível, expiração natural, log Mongo com `Impersonating: true`.

### Implementation for User Story 6

- [X] T042 [US6] Criar `ImpersonationTokenIssuer` em `src/omniDesk.Api/Features/Authorization/Impersonation/ImpersonationTokenIssuer.cs` — emite JWT RS256 com claims conforme `contracts/impersonation-token.md` §"Formato do JWT"; lê TTL de `IMPERSONATION_JWT_TTL_SECONDS` (default 300, máximo 600 — falha startup se exceder)
- [X] T043 [US6] Implementar/atualizar endpoint `POST /admin/tenants/{slug}/impersonation` em `src/omniDesk.Api/Features/Admin/Tenants/ImpersonationEndpoint.cs` (Spec 03 já tem `ImpersonateTenantCommand` — alinhar TTL e claims a esta spec); resposta conforme `contracts/impersonation-token.md` §"Endpoint emissor"
- [X] T044 [US6] Criar `ImpersonationAuditEnricher : ILogEventEnricher` em `src/omniDesk.Api/Features/Authorization/Impersonation/ImpersonationAuditEnricher.cs` — adiciona `Impersonating`, `ImpersonatedBy`, `Jti` aos eventos de log quando claim presente
- [X] T045 [US6] Wire enricher na config Serilog em `src/omniDesk.Api/Program.cs` (`.Enrich.With<ImpersonationAuditEnricher>()`)
- [X] T046 [US6] Atualizar `src/omniDesk.Api/Features/Auth/RefreshTokenEndpoint.cs` para rejeitar tokens com claim `impersonating: true` (retorna 400 com mensagem PT-BR clara)
- [X] T047 [US6] Criar `ForbidsDuringImpersonationRequirement` + handler em `src/omniDesk.Api/Features/Authorization/Policies/ForbidsDuringImpersonationRequirement.cs`; aplicar a `Auth.InviteUser`, `Auth.InviteSupervisor`, `Auth.DeactivateUser`, `Whatsapp.ViewAccessToken`, `Whatsapp.EditConfig` em `AuthorizationPoliciesRegistration` (atualizar T025)
- [X] T048 [US6] Frontend CRM: criar `src/omniDesk.Crm/src/app/core/impersonation/impersonation-banner.component.ts` (componente standalone PrimeNG `<p-message>` ou similar, cor de alerta, contador `MM:SS`, botão "Encerrar agora" → logout)
- [X] T049 [US6] Frontend CRM: integrar `<omni-impersonation-banner>` no shell layout em `src/omniDesk.Crm/src/app/layout/main-layout.component.html` (renderiza condicionalmente quando `roleSignal.isImpersonating()`)

### Tests for User Story 6

- [X] T050 [P] [US6] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/Impersonation/ImpersonationTokenTests.cs` — emissão com claims corretos, TTL respeitado, `jti` único, max 600s enforcement em startup
- [X] T051 [P] [US6] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/Impersonation/ImpersonationContextTests.cs` — token expirado → 401; subdomínio errado → 401; refresh tentativa → 400; ação genérica gera log com `ImpersonatedBy`
- [X] T052 [P] [US6] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/Impersonation/ImpersonationForbiddenActionsTests.cs` — 5 policies bloqueadas durante impersonation retornam 403 com mensagem PT-BR
- [X] T053 [P] [US6] Criar `impersonation-banner.component.spec.ts` co-localizado — renderiza quando `isImpersonating()=true`, esconde quando `false`, contador atualiza, botão Encerrar dispara logout

**Checkpoint**: Suporte via impersonation auditável e seguro.

---

## Phase 9: User Story 7 — Desativação corta acesso instantaneamente (Priority: P3)

**Goal**: `DeactivateUserCommand` invalida sessão em ≤ 1s; bloqueia desativação do último `tenant_admin`; reativação exige novo login.

**Independent Test**: Após desativação, próxima requisição autenticada do usuário falha em < 1s (medido em CI); tentar desativar último `tenant_admin` retorna 422.

### Implementation for User Story 7

- [X] T054 [US7] Criar `LastTenantAdminGuard` em `src/omniDesk.Api/Features/Authorization/UserLifecycle/LastTenantAdminGuard.cs` — query `COUNT(*) FROM users WHERE tenant_id=@id AND role='tenant_admin' AND is_active=true` (research.md R9); lança `LastTenantAdminException` quando count<=1 e alvo é o último
- [X] T055 [US7] Criar `DeactivateUserCommand` + handler em `src/omniDesk.Api/Features/Authorization/UserLifecycle/DeactivateUserCommand.cs` — invoca `LastTenantAdminGuard`, atualiza `is_active=false`, `deactivated_at=NOW()`, purga `ClaimsCache` e `redis.DEL("{tenant_slug}:refresh:{user_id}:*")`, emite log `UserDeactivated` (research.md R8)
- [X] T056 [US7] Criar `ReactivateUserCommand` + handler em `src/omniDesk.Api/Features/Authorization/UserLifecycle/ReactivateUserCommand.cs` — `is_active=true`, `deactivated_at=NULL`; **não** restaura sessões; emite log `UserReactivated`
- [X] T057 [US7] Adicionar endpoints em `src/omniDesk.Api/Features/Authorization/UserLifecycle/UserLifecycleEndpoints.cs`: `POST /users/{id}/deactivate` e `POST /users/{id}/reactivate` ambos com `RequireAuthorization(Policies.CanDeactivateUser)`
- [X] T058 [US7] Mensagens PT-BR: na exception do guard ("Não é possível desativar o último Administrador ativo do tenant. Promova outro usuário a Administrador antes.") e na resposta 422

### Tests for User Story 7

- [X] T059 [P] [US7] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/UserLifecycle/DeactivationFlowTests.cs` — Testcontainers, fluxo completo: cria usuário, faz login, desativa, mede latência da próxima requisição falhar (assertion `< 1000ms` para SC-005), verifica refresh tokens removidos do Redis
- [X] T060 [P] [US7] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/UserLifecycle/LastTenantAdminGuardTests.cs` — único tenant_admin: 422; com 2+: aceita; promover outro a tenant_admin antes desbloqueia
- [X] T061 [P] [US7] Criar `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/UserLifecycle/ReactivationFlowTests.cs` — reativar restaura `is_active`, mas usuário precisa logar de novo; sessões antigas continuam inválidas

**Checkpoint**: Ciclo de vida do usuário completo e auditado.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Documentação, observabilidade fina, validação E2E e auditoria de aderência.

- [X] T062 [P] Adicionar logging estruturado de negações de autorização em `src/omniDesk.Api/Features/Authorization/Policies/AuthorizationFailureLogger.cs` (handler de evento `AuthorizationFailureContext`) — nível `Warning` com campos `user_id`, `role`, `policy`, `tenant_slug`, `endpoint` (research.md R7)
- [X] T063 [P] Em ambiente `Production`, payload 403 retorna `{"error":"forbidden","message":"Você não tem permissão para executar esta ação."}` sem detalhes (research.md R7); em `Development`, incluir nome da policy
- [X] T064 Atualizar `docs/ARCHITECTURE.md` adicionando seção "Autorização" referenciando esta spec como fonte de verdade da matriz; cross-link com `contracts/authorization-policies.md`
- [X] T065 [P] Performance check: medir overhead p95 do `IClaimsTransformation` — criar benchmark simples em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Performance/ClaimsTransformerBenchmark.cs`; assert ≤ 1ms (Performance Goal do plan)
- [X] T066 Auditoria de aderência (SC-001): listar endpoints existentes nas Specs 02–03 e confirmar que cada um cita uma `Policies.*` correta; corrigir endpoints sem policy ou com policy errada
- [X] T067 Executar `quickstart.md` §§ B (impersonation), C (desativação), D (último tenant_admin), E (escopo attendant) manualmente em ambiente local; documentar evidências em `specs/004-roles-permissions/quickstart-evidences.md` (não comitar se contiver dados sensíveis)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: sem dependências, começa imediatamente.
- **Phase 2 (Foundational)**: depende de Phase 1. Bloqueia TODAS as user stories.
- **Phase 3 (US1)**: única dependência de US1 é Phase 2. Sem dependência cruzada com outras stories.
- **Phase 4 (US2)** e **Phase 5 (US3)**: dependem de Phase 2; podem rodar em paralelo a US1 (mas o teste paramétrico de US1 é o checkpoint canônico — recomenda-se concluir US1 antes para validar a base).
- **Phase 6 (US4)**: depende de Phase 2 e US1 (PolicyMatrixTests precisa estar verde).
- **Phase 7 (US5)**: depende de Phase 2; `DepartmentScopeFilter` é independente das policies.
- **Phase 8 (US6)**: depende de Phase 2; T047 atualiza arquivo de T025 → ordem importa entre essas duas tasks.
- **Phase 9 (US7)**: depende de Phase 2 e do `ClaimsCache` (T012) que já estará pronto.
- **Phase 10 (Polish)**: depende das stories desejadas estarem completas; T066 depende de T025/T026 (PolicyRegistration).

### User Story Dependencies

- **US1** entrega o framework + matriz; é a base para todas as outras (não-bloqueante mas recomendada como primeira).
- **US2, US3** consomem US1 implicitamente (via PolicyMatrixTests cobrir o caso) mas não dependem dela em código.
- **US4, US5, US6, US7** são independentes entre si — equipes podem dividir.

### Within Each User Story

- Tests (Testcontainers ou frontend `.spec.ts`) são obrigatórios e **devem falhar antes** da implementação respectiva (Test Discipline da constituição).
- Em backend: domínio → infraestrutura → endpoint → teste.
- Em frontend: signal → guard/diretiva → componente que consome.

### Parallel Opportunities

- **Phase 1**: T003, T004 em paralelo.
- **Phase 2**: T005–T008 em paralelo (4 arquivos de domínio independentes); T011 paralelo aos demais; T016–T019 (frontend Admin) em paralelo a T020–T024 (frontend CRM); T012 e T013 sequenciais.
- **Phase 3 (US1)**: T027, T028, T029 em paralelo (3 arquivos de teste distintos).
- **Phase 4 (US2)**: T033, T034 em paralelo.
- **Phase 7 (US5)**: testes de T041 paralelos à validação de outras stories.
- **Phase 8 (US6)**: T050, T051, T052, T053 em paralelo.
- **Phase 9 (US7)**: T059, T060, T061 em paralelo.
- **Equipes paralelas**: após Phase 2, US1 (1 dev), US4+US5 (1 dev), US6 (1 dev), US7 (1 dev).

---

## Parallel Example: User Story 1 (after Foundational complete)

```bash
# Implementação principal — sequencial (T025 → T026)
Task: "Implement AuthorizationPoliciesRegistration with all ~50 policies"
Task: "Wire registration in Program.cs"

# Testes em paralelo (3 arquivos distintos):
Task: "PolicyMatrixTests.cs (Theory + MemberData)"
Task: "RoleHierarchyTests.cs"
Task: "RoleRequirementTests.cs"
```

## Parallel Example: User Story 6 — Impersonation tests after backend implementation

```bash
# Testes 100% paralelos (4 arquivos distintos):
Task: "ImpersonationTokenTests.cs"
Task: "ImpersonationContextTests.cs"
Task: "ImpersonationForbiddenActionsTests.cs"
Task: "impersonation-banner.component.spec.ts"
```

---

## Implementation Strategy

### MVP First (Phases 1 → 2 → 3 → 4 → 5)

1. **Phase 1: Setup** — env var + migration + folders.
2. **Phase 2: Foundational** — domínio, requirements, claims transformer + cache, DI wire-up, frontends base.
3. **Phase 3: US1** — registrar todas as policies, teste paramétrico verde.
4. **Phase 4: US2** — gating do Painel Admin + guard de criação de saas_admin.
5. **Phase 5: US3** — verificação que tenant_admin do provisionamento herda tudo.
6. **STOP & VALIDATE**: Specs 02 e 03 continuam funcionando; matriz coberta; deploy/demo.

### Incremental Delivery

7. **Phase 6 (US4)**: testes de boundary do supervisor → deploy/demo.
8. **Phase 7 (US5)**: `DepartmentScopeFilter` → habilita Spec 06/08 → deploy/demo.
9. **Phase 8 (US6)**: impersonation completa → habilita suporte rastreável → deploy/demo.
10. **Phase 9 (US7)**: ciclo de vida do usuário (desativação/reativação) → deploy/demo.
11. **Phase 10 (Polish)**: documentação, performance check, auditoria SC-001.

### Parallel Team Strategy

Após Phase 2 (Foundational):

- **Dev A**: US1 (T025–T029) → US2 (T030–T034) → US3 (T035–T036)
- **Dev B**: US5 (T038–T041) → US6 (T042–T053)
- **Dev C**: US7 (T054–T061) → Polish (T062–T067)

Tudo converge na Phase 10.

---

## Notes

- [P] tasks = arquivos distintos, sem dependência ainda pendente.
- [Story] label rastreia task ↔ user story para entrega independente.
- Cada user story é independentemente completável e testável (em conformidade com a estratégia da spec).
- Constituição V (Simplicity): zero pacote NuGet/npm novo introduzido — apenas built-ins.
- Constituição VII (Test Discipline): Testcontainers + DB real obrigatório; magic strings proibidas (vide `Roles.*`, `Policies.*`).
- Commit após cada task ou grupo lógico (especialmente após cada Checkpoint).
- Avoid: tarefas vagas, conflito no mesmo arquivo entre [P] tasks, dependências cruzadas que quebrem independência das stories.

---

## Resumo

| Fase | Tasks | Foco |
|---|---|---|
| 1. Setup | T001–T004 | Env, migration, pastas |
| 2. Foundational | T005–T024 | Framework + claims + cache + frontends base (20 tasks) |
| 3. US1 (P1) | T025–T029 | Matriz registrada + teste paramétrico (5 tasks) |
| 4. US2 (P1) | T030–T034 | Painel Admin gateado (5 tasks) |
| 5. US3 (P1) | T035–T036 | Tenant admin do provisionamento (2 tasks) |
| 6. US4 (P2) | T037 | Boundary supervisor (1 task) |
| 7. US5 (P2) | T038–T041 | Department scope filter (4 tasks) |
| 8. US6 (P3) | T042–T053 | Impersonation (12 tasks) |
| 9. US7 (P3) | T054–T061 | Desativação/reativação (8 tasks) |
| 10. Polish | T062–T067 | Logging, docs, performance, auditoria (6 tasks) |
| **Total** | **67 tasks** | |

**MVP** = Phases 1+2+3+4+5 (36 tasks até T036) — tenant operacional com matriz completa em produção.
