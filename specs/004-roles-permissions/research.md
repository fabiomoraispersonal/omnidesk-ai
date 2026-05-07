# Phase 0 — Research: Roles e Permissões

Decisões técnicas tomadas para implementar a spec sem violar a Constituição (v1.0.0). Cada decisão é registrada com rationale + alternativas avaliadas e descartadas.

---

## R1 — Modelo de autorização do ASP.NET Core: Policies vs. Requirements vs. Claims simples

**Decision**: Usar **Authorization Policies** com `RoleRequirement` customizado + `DepartmentScopeRequirement` separado para o filtro horizontal do `attendant`.

**Rationale**:

- Policies nomeadas (`Policies.CanCreateDepartment`, `Policies.CanEditAccessToken`, ...) tornam a matriz da spec **rastreável no código**: cada célula da seção 4.x da spec ↔ uma constante em `Policies.cs` ↔ um `[Authorize(Policy = ...)]` no endpoint. Em revisão de PR, basta procurar a constante para localizar todos os pontos protegidos.
- O modelo embutido de `[Authorize(Roles = "...")]` foi descartado: depende de strings, não suporta combinação com escopo de departamento e mistura mal com hierarquia (tenant_admin ⊇ supervisor ⊇ attendant). Policies + Requirement explícito implementam herança de uma vez só (`role >= required` em código).
- `DepartmentScopeRequirement` separado (em vez de embutido no `RoleRequirement`) porque escopo de departamento é uma dimensão **horizontal** (filtragem de IQueryable) — não vertical (gating de endpoint). Misturar geraria policies "Can list tickets *if* in department" — confuso e não-reutilizável.

**Alternativas avaliadas**:

- **`[Authorize(Roles = ...)]` puro**: descartado — magic strings, sem suporte a hierarquia, sem composição com escopo.
- **Cancan/CASL-style permissions library**: descartado — built-in do .NET resolve sem dependência externa (princípio V — Simplicity).
- **PolicyProvider dinâmico** (gera policy on-the-fly): descartado — todas as policies são conhecidas em compile-time; registro estático é mais simples e auditável.

---

## R2 — Onde aplicar o filtro por departamento do `attendant`

**Decision**: Implementar como **extension method `IQueryable<T>.ForCurrentUserScope()`** em `Infrastructure/Authorization/DepartmentScopeFilter.cs`. Cada query de Tickets/Conversas que precise filtrar por departamento o invoca explicitamente. **Não usar EF Global Query Filters.**

**Rationale**:

- Global Query Filter parece elegante mas é **opaco**: o desenvolvedor que lê a query não vê o filtro aplicado, e qualquer `IgnoreQueryFilters()` (necessário para casos legítimos como `tenant_admin` listando todos) compromete a regra silenciosamente.
- Extension method explícito força o autor da query a tomar a decisão consciente: "filtrar por escopo do usuário aqui" ou "esta query é admin-only, sem escopo".
- O método encapsula o `if (currentUser.Role == Roles.Attendant)` + join com `user_departments` em um lugar — atualizações futuras na regra acontecem em um único arquivo.

**Alternativas avaliadas**:

- **EF Global Query Filter**: descartado pelos motivos acima.
- **Specification Pattern**: descartado — overhead de classe por query sem ganho real para um filtro dimensional único.
- **Filtro no service layer (puxar tudo, filtrar em memória)**: descartado — performance e quebra de paginação SQL.

---

## R3 — Propagação do estado do usuário (role + departamentos) para a request

**Decision**: Usar **`IClaimsTransformation`** registrado no DI. No início de cada requisição autenticada, o transformer lê o `sub` do JWT, busca em cache (Redis com TTL 60s) a role atual e a lista de departamentos do usuário, e injeta como claims. Se o usuário foi desativado, o cache é purgado pelo `DeactivateUserCommand` — a próxima requisição lê do banco, encontra `is_active=false` e a `IClaimsTransformation` retorna falha que se traduz em 401.

**Rationale**:

- Cache curto (60s) cobre 99% das requisições com latência sub-ms; purga explícita na desativação garante SC-005 (≤ 1 s).
- Manter a role/departamentos **fora** do JWT (apenas `sub` + `tenant_slug` ficam no token) elimina a necessidade de revogar tokens em vigor para promoções/rebaixamentos de role — o próximo request já lê os novos valores. Decisão alinhada à Spec 002, que já mantém JWT minimalista.
- `IClaimsTransformation` é o ponto de extensão padrão do ASP.NET Core para enriquecer claims após a autenticação — não introduz padrão novo.

**Alternativas avaliadas**:

- **Embutir role e dept_ids no JWT**: descartado — qualquer mudança exigiria revogar todos os tokens em vigor (complexo de orquestrar) ou aceitar até 15 min de janela com claim stale.
- **Custom middleware**: descartado — `IClaimsTransformation` é mais idiomático e roda na ordem correta (após autenticação, antes de autorização).
- **Recarregar do banco a cada request sem cache**: descartado — pico de QPS multiplica tráfego no Postgres sem necessidade.

---

## R4 — Token de impersonation: separar do access token regular

**Decision**: Token de impersonation é um **JWT distinto** (mesmo formato/algoritmo do access token regular — RS256, gerado pelo `ImpersonationTokenIssuer`) com:

- Claims diferenciadoras: `role: "saas_admin"`, `impersonating: true`, `tenant_slug: <alvo>`, `impersonated_by: "saas_admin"`.
- TTL de 5 min (configurável via env `IMPERSONATION_JWT_TTL_SECONDS`, sempre ≤ 600).
- **Sem refresh token associado** — expira sem renovação possível (FR-029).
- Mesmo middleware de validação JWT do access token regular (`AddJwtBearer`) — distingue por presença da claim `impersonating: true`.

**Rationale**:

- Reutiliza toda a stack de validação JWT existente, sem nova configuração de scheme.
- A claim `impersonating: true` é o gatilho único para: (a) o `ImpersonationContextHandler` adicionar `impersonated_by` em todo audit log; (b) o frontend CRM exibir a barra de aviso permanente.
- Sem refresh = sem necessidade de blacklist; expiração natural fecha a janela.

**Alternativas avaliadas**:

- **Reutilizar access token com claim adicional**: descartado — confunde os dois fluxos e exigiria garantir que o token nunca seja emitido por refresh.
- **Token opaco em Redis**: descartado — todos os outros tokens de auth do projeto são JWT; introduzir formato novo violaria simplicidade.

---

## R5 — Frontend: como expor role/permissões para os componentes Angular

**Decision**: Um `signal<Role | null>` em `core/authorization/role.signal.ts` é populado a partir do parsing das claims do access token (já gerenciado pelo `AuthService` da Spec 002). Componentes consomem via:

- **Guards** (`role.guard.ts`, `permission.guard.ts`): roteamento (`canActivate`/`canMatch`) — esconde rotas inteiras.
- **Diretiva estrutural `*omniHasRole`**: esconde controles individuais (botões, menus). Aceita uma role mínima ou um array de roles permitidas.
- **`computed` derivados**: para casos complexos no template (ex.: `isAtLeastSupervisor = computed(() => role() === Roles.TenantAdmin || role() === Roles.Supervisor)`).

**Rationale**:

- Signals são built-in do Angular 21 — aderente à constituição (sem libs extras).
- Diretiva estrutural elimina condicionais manuais inline — a UI fica enxuta e o gating é declarativo.
- A role é apenas guidance da UX — a verdade absoluta vem do backend (defense-in-depth).

**Alternativas avaliadas**:

- **Service injetável com método `hasRole()`**: descartado — força uso de `*ngIf` com chamadas de método em template (anti-pattern de change detection).
- **Pipe (`role | hasRole:'supervisor'`)**: descartado — pior legibilidade que diretiva estrutural.
- **Loja externa (NgRx)**: descartado — overkill para um único signal de role.

---

## R6 — Testar a matriz: estratégia para cobrir as ~50 células sem proliferar testes manuais

**Decision**: **Teste paramétrico único** (`PolicyMatrixTests.cs`) usando `[Theory]` + `[MemberData]`. A fonte de dados é uma estrutura `(Policy, Role, ExpectedResult, EndpointPath, HttpMethod)` que **espelha 1:1 a matriz da spec**. O teste sobe um host com Testcontainers (Postgres + Redis), autentica como cada role, dispara a requisição e verifica 200/403.

**Rationale**:

- Garante cobertura **completa** sem multiplicar arquivos: ~50 linhas de dados, 1 método de teste.
- A própria estrutura de dados serve de **executable spec** — divergência entre matriz documental e código é detectada na CI (test fails) ou no review (a tabela do teste é literalmente comparável à da spec).
- Conforme a constituição (princípio VII), Testcontainers + DB real são obrigatórios.

**Alternativas avaliadas**:

- **Um arquivo de teste por feature**: descartado — duplica boilerplate e dilui cobertura cross-feature.
- **Apenas unit tests dos handlers** sem hit no endpoint real: descartado — não pega bugs de configuração de policy/registration.

---

## R7 — Mensagens de erro de autorização (PT-BR) sem vazar informação

**Decision**: Negações 403 retornam um payload uniforme: `{ "error": "forbidden", "message": "Você não tem permissão para executar esta ação." }`. **Não revelar** qual policy faltou nem qual role exigida — apenas log estruturado em backend (Serilog → Mongo) com contexto completo (`user_id`, `role`, `policy`, `tenant_slug`, `endpoint`).

**Rationale**:

- LGPD / boas práticas de segurança: mensagens detalhadas em produção dão pistas para enumeração de privilégios.
- Em ambiente de desenvolvimento (`ASPNETCORE_ENVIRONMENT=Development`), o payload pode incluir o nome da policy para acelerar debug — mas nunca em produção.

**Alternativas avaliadas**:

- **Mensagem detalhada em produção**: descartada por motivos de segurança.
- **Não logar a negação**: descartada — perda de observabilidade (princípio VI da constituição).

---

## R8 — Como invalidar sessões na desativação em ≤ 1 s

**Decision**: O `DeactivateUserCommand` executa atomicamente:

1. `UPDATE public.users SET is_active = false, deactivated_at = NOW() WHERE id = @id`.
2. `redis.DEL("{tenant_slug}:user:{id}:claims")` — purga o cache do `IClaimsTransformation` (R3).
3. `redis.DEL("{tenant_slug}:refresh:{id}:*")` — invalida todos os refresh tokens em qualquer dispositivo.
4. Emite evento `UserDeactivated` no log (Serilog → Mongo) com timestamp.

A próxima requisição autenticada do usuário desativado:

- Busca pelo claim cache (Redis) → MISS.
- `IClaimsTransformation` cai no Postgres, encontra `is_active=false`, retorna falha → 401.
- Refresh token, se tentado, falha imediatamente (chave já apagada do Redis).

**Rationale**:

- Operação Redis é sub-ms; a janela de "usuário ainda autenticado" fica limitada à latência da próxima requisição dele (típico < 1 s).
- Reusa o cache do R3 — sem mecanismo paralelo de invalidação.

**Alternativas avaliadas**:

- **Token blacklist em Redis**: descartado — para JWT stateless é caro (todo request consulta Redis); o cache de claims do R3 já cumpre o papel.
- **Confiar apenas no TTL natural do JWT (15 min)**: descartado — viola FR-036 (imediato).

---

## R9 — Bloqueio do último `tenant_admin` (FR-038)

**Decision**: Validação aplicada em **dois pontos** com mensagem clara:

1. **`LastTenantAdminGuard`** invocado pelo `DeactivateUserCommand` antes de mutar — query: `SELECT COUNT(*) FROM public.users WHERE tenant_id = @id AND role = 'tenant_admin' AND is_active = true`. Se `count <= 1` e o alvo é o último, lança `LastTenantAdminException`.
2. **Constraint defensivo no banco** via `CHECK` ou trigger? Descartado — a regra envolve estado dinâmico (`is_active=true`), não vale o custo de complexidade SQL. Validação na aplicação é suficiente, dado que toda escrita passa pelo command.

**Rationale**:

- Guard único na camada de aplicação evita lockout sem complicar o schema.
- Mensagem retornada ao frontend: "Não é possível desativar o último Administrador ativo do tenant. Promova outro usuário a Administrador antes." — orienta ação corretiva.

**Alternativas avaliadas**:

- **Trigger Postgres**: descartado — adiciona complexidade no schema sem ganho prático (o caminho de escrita é único e controlado).
- **Apenas validação no frontend**: descartada — defense-in-depth exige backend authoritative.

---

## Resumo das decisões

| ID | Tema | Escolha |
|---|---|---|
| R1 | Modelo de authz .NET | Authorization Policies + Role/DepartmentScope Requirements |
| R2 | Filtro por departamento | Extension method explícito em `IQueryable<T>` |
| R3 | Estado do usuário no request | `IClaimsTransformation` + cache Redis 60s |
| R4 | Token de impersonation | JWT separado, claim `impersonating: true`, TTL 5 min |
| R5 | Authz no frontend | Signal + Guards + diretiva `*omniHasRole` |
| R6 | Testes da matriz | Teste paramétrico único (Theory + MemberData) |
| R7 | Mensagens de 403 | Genérica em produção, detalhada apenas em dev |
| R8 | Invalidação na desativação | Purge de cache de claims + refresh tokens em Redis |
| R9 | Último tenant_admin | Guard na camada de aplicação, sem trigger |

Todas as decisões respeitam a Constituição v1.0.0; nenhum ADR adicional é necessário (sem padrão arquitetural novo introduzido).
