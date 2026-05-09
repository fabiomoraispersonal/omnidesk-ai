# Cross-Spec Pendencies — Spec 006 (Agentes de IA)

> Varredura de specs **001 a 005** identificando pontos que **dependem** da implementação da Spec 006 ou que **expõem contratos** que a Spec 006 deve honrar/completar.
>
> Cada item é classificado por:
> - **Direção**: 🠔 _spec X aguarda 006_ · 🠖 _006 aguarda spec X_ · ⇋ _bidirecional_
> - **Status atual**: 🟢 já implementado · 🟡 parcial · 🔴 ausente
> - **Bloqueia 006?**: Sim/Não
>
> Itens 🔴/🟡 que bloqueiam 006 viram tasks no `/speckit-tasks` desta spec.

---

## 001 — Global Technical Standards

### 001-A — Padrão de tabelas tenant-scoped + migrations SQL com `{TENANT_SCHEMA}` placeholder

- **Direção**: 🠖 (006 deve seguir)
- **Status**: 🟢 já implementado (ver `Add_Tickets_Scaffold.sql`)
- **Bloqueia 006?**: Não — apenas convenção a respeitar.
- **Ação na Spec 006**: migrations `Add_AiAgents_AiSettings.sql` e `Add_DefaultDepartmentId_To_Tenants.sql` seguem o padrão. Adicionar runner caso ainda não exista (verificar — não encontrei runner explícito; provavelmente provisionamento usa `dotnet ef migrations` ou um custom runner que processa os `.sql`. Pendência menor — investigar em task T-INFRA).

### 001-B — Constituição §II "frustration signals (3+ unresolved exchanges)"

- **Direção**: 🠔 (constituição precisa ser amendada para refletir decisão da 006)
- **Status**: 🟡 conflito documentado em [ADR-006-002](ADR-006-002-frustration-detection-via-prompt.md) com proposta de **PATCH** ao Principle II.
- **Bloqueia 006?**: Não — a Spec 006 já registrou o desvio em `plan.md > Constitution Check`.
- **Ação**: criar PR de emenda à constituição como parte do entregável de 006 (task `T-CONST-AMEND`).

---

## 002 — Auth JWT

### 002-A — Claims `tenant_slug`, `user_id`, `role` no JWT

- **Direção**: 🠖 (006 consome)
- **Status**: 🟢 já implementado.
- **Bloqueia 006?**: Não.
- **Ação**: usar `IUserContext`/`ITenantContext` da Spec 002 nos endpoints de Spec 006 — sem novidade.

### 002-B — Endpoint `/api/me/tenant` (mascaramento de chave OpenAI)

- **Direção**: ⇋
- **Status**: 🟡 — a Spec 003 menciona `has_openai_key: true/false` no contrato de tenant. O endpoint `/api/ai-settings` desta spec (006) **expande** com `key_preview: "sk-...x4Q2"` para a UX de Configurações Avançadas.
- **Bloqueia 006?**: Não.
- **Ação**: confirmar que a Spec 003 não bloqueia exposição do preview via novo endpoint dedicado da Spec 006 — sem mudança em 003.

---

## 003 — Tenant Provisioning

### 003-A — `tenants.openai_api_key_enc` (criptografada)

- **Direção**: 🠖 (006 consome)
- **Status**: 🟢 — coluna existe (`Tenant.OpenAiApiKeyEnc`, `OpenAiOrganization`, `OpenAiProject`).
- **Bloqueia 006?**: Não.
- **Ação**: `OpenAiKeyResolver` (Spec 006) lê esses campos.

### 003-B — `agent_templates` global + `TenantProvisioningJob.CopyAgentTemplatesAsync`

- **Direção**: ⇋
- **Status**: 🟡 — `AgentTemplate` entity, endpoints `/api/admin/agent-templates`, e o método `CopyAgentTemplatesAsync` **já existem** no código. **Porém** o INSERT do método aponta para `{schema}.agents` (tabela inexistente — a migration que cria `ai_agents` é entregue pela Spec 006). **Bug latente**: novos provisionamentos hoje falham silenciosamente nesta etapa (ou em produção lançam SQL error). Verificar em logs Mongo se já houve incidente.
- **Bloqueia 006?**: **Sim** — Spec 006 deve criar a tabela e ajustar o INSERT.
- **Ação**: na Spec 006:
  1. Criar migration `Add_AiAgents_AiSettings.sql` (já planejado).
  2. Renomear `CopyAgentTemplatesAsync` → `ProvisionAiAgentsAsync` e ajustar SQL para `ai_agents` com colunas corretas (já no plano — `Provisioning/TenantProvisioningJob.cs MODIFICADO`).
  3. Criar row em `ai_settings` no provisionamento (nova responsabilidade).
  4. Garantir provisionamento idempotente — se reentrante, não duplica orchestrator (`ux_ai_agents_orchestrator` partial unique já protege).

### 003-C — `tenants.default_department_id` (FR-016 da Spec 006)

- **Direção**: 🠔 (003 expõe; 006 consome)
- **Status**: 🔴 — campo não existe em `Tenant`.
- **Bloqueia 006?**: **Sim**.
- **Ação**: Migration `Add_DefaultDepartmentId_To_Tenants.sql` (Spec 006). Endpoint `PATCH /api/me/tenant/default-department` será exposto pelo módulo de tenant settings — **a Spec 003 deve ganhar este endpoint posteriormente** (atualização menor da 003 documentada aqui — não bloqueia 006: o atalho de auto-fill no primeiro depto ativo cobre 95% dos casos sem UI dedicada).
- **Side effect na Spec 003**: adicionar item de tarefa para criar a UI de "Departamento padrão" em Painel Admin / Configurações (não-bloqueante para 006).

### 003-D — Template global do Orchestrator (FR-031 / Spec 006 §6.2)

- **Direção**: 🠖
- **Status**: 🟢 — `AgentTemplate` com `Type=orchestrator` é semeado via `DatabaseSeeder.cs` (Spec 003).
- **Bloqueia 006?**: Não. Verificar em smoke test que o seed roda em fresh DB e cria pelo menos 1 template `orchestrator` ativo.

---

## 004 — Roles & Permissions

### 004-A — FR-016: `tenant_admin` E `supervisor` podem gerenciar agentes; `context_window_messages` é exclusivo de `tenant_admin`

- **Direção**: 🠖 (006 deve seguir)
- **Status**: 🟢 — Spec 004 já fixou a regra. Spec 006 contracts foram **corrigidos** (commit pendente) para alinhar:
  - `agents-api.md`: aceita `tenant_admin` + `supervisor`.
  - `playground-api.md`: idem.
  - `ai-settings-api.md`: continua `tenant_admin` apenas.
- **Bloqueia 006?**: Não — alinhamento documental concluído.
- **Ação**: ao implementar, declarar policies `Policies.ManageAgents` (admin OR supervisor) e `Policies.ManageAiSettings` (admin only), reusar `RoleRequirement` da Spec 004.

### 004-B — Department scope para `attendant` (FR-013 da Spec 004)

- **Direção**: ⇋
- **Status**: 🟢 — não afeta diretamente Spec 006 porque atendentes **não** acessam endpoints de gestão de agentes; eles consomem `agent_activity_logs` apenas via tickets (Spec 008).
- **Bloqueia 006?**: Não.

---

## 005 — Departments & Attendants

### 005-A — `IAgentRuntime` + `FallbackAgentRuntime` (US7/US8 da 005, FR-037)

- **Direção**: 🠔 (005 já criou interface estub; 006 implementa de verdade)
- **Status**: 🟡 — a interface `Features/AiSuggestions/IAgentRuntime.cs` existe com `FallbackAgentRuntime` (no-op) registrada no DI da Spec 005.
- **Bloqueia 006?**: Não, mas **Spec 006 entrega** a impl real:
  - `Infrastructure/AiAgents/AgentRuntime.cs` (nova, vinculada à Spec 005 via DI override).
  - Métodos:
    - `GetSubAgentForDepartmentAsync(departmentId)` → consulta `ai_agents` ativos onde `department_id = X` (limit 1, mais recente). Se múltiplos sub-agentes vinculados ao mesmo depto (cenário válido), retornar o mais recentemente atualizado.
    - `GetRecentMessagesAsync(conversationId, maxCount)` → na Spec 006 retornará lista vazia (Spec 007 entrega o histórico real).
    - `GetClientNameAsync(conversationId)` → Spec 008 (tickets) ou Spec 007 (conversa); na 006 stub retorna null.
- **Ação 006**: implementar `AgentRuntime : IAgentRuntime` e registrá-lo via `services.AddScoped<IAgentRuntime, AgentRuntime>()` substituindo o `FallbackAgentRuntime`. Testes: validar `SuggestReplyService` (Spec 005 T075) com a impl real produz prompt com sub-agente correto.

### 005-B — Eventos de criação de ticket por IA precisam ser consumidos pelo round-robin

- **Direção**: ⇋
- **Status**: 🔴 — Spec 005 entrega round-robin para tickets criados via API (Spec 008 futura), mas não escuta o canal Redis `{slug}:ws:dept:{department_id}` payload `ticket_created_from_ai`.
- **Bloqueia 006?**: Não — ticket fica `queued` e atendente humano pode pegar manualmente. Auto-distribuição pode entrar em V1.5.
- **Ação 005 (post-006)**: adicionar consumidor do canal pub/sub que dispara `TicketAssignmentService.AttemptAssignAsync(ticket_id)` quando o ticket vier de IA. Deixa-se documentado aqui — será um patch curto na Spec 005.
- **Workaround V1**: tickets criados por transbordo aparecem com status `queued`; a UI da Spec 005 já tem ação "Pegar próximo da fila" que cobre o caso até a integração ser feita.

### 005-C — Distinção visual: ticket criado por IA vs. ticket criado direto

- **Direção**: 🠔 (005 UI deve refletir)
- **Status**: 🔴 — ticket criado por IA não tem flag visual hoje na UI da 005.
- **Bloqueia 006?**: Não.
- **Ação 005 (post-006)**: ticket recebe coluna `created_by_source` ENUM (`'ai'`, `'human'`, `'webhook'`) — será adicionada na Spec 008 (Tickets V2). Spec 006 não toca em `tickets`.

### 005-D — Histórico anexado ao ticket via `ai_handoff_snapshots`

- **Direção**: 🠔 (006 entrega; 005 não consome direto, mas Spec 008 / 005-attendant sim)
- **Status**: 🟢 entrega da 006 (ver `contracts/ticket-creation-gateway.md`).
- **Bloqueia 006?**: Não.

### 005-E — Sub-agente vinculado a depto que vira inativo

- **Direção**: ⇋
- **Status**: 🔴 — Spec 005 permite desativar departamento (FR de soft delete). Spec 006 deve detectar e **filtrar** sub-agentes cujo depto está inativo da lista enviada ao Orchestrator.
- **Bloqueia 006?**: Sim — invariante operacional.
- **Ação 006**: `AgentResolver` filtra sub-agentes ativos cujo `department.is_active = true`. Documentar em research como edge case já tratado (Spec 006 §Edge Cases já cobre).

---

## Resumo executivo

| Item | Bloqueia 006? | Ação |
|---|---|---|
| 001-A migrations runner | Não | Investigar runner em T-INFRA |
| 001-B emenda constitucional | Não | PR de PATCH ao Principle II ao final |
| 003-B INSERT em tabela inexistente | **Sim** | Criar migration + renomear método em provisioning job |
| 003-C `default_department_id` | **Sim** | Migration na Spec 006 |
| 003-D template global ativo | Não | Smoke test |
| 004-A policies de role | Não | Implementar `Policies.ManageAgents` |
| 005-A `IAgentRuntime` real | Não | Substituir `FallbackAgentRuntime` |
| 005-B auto-distribuição de ticket de IA | Não | Workaround manual; patch futuro em 005 |
| 005-E sub-agente com depto inativo | **Sim** | Filtro em `AgentResolver` |

**Bloqueadores efetivos**: 003-B, 003-C, 005-E — todos endereçados pelo plano atual da Spec 006.

**Patches futuros em outras specs (não bloqueantes)**: 003 (UI default_department_id), 005 (auto-distribuição de ticket de IA), 005 (badge visual de ticket de IA na UI), constituição §II.

---

## CRM Shell pendency (post-Spec 006)

A Spec 006 entrega 3 páginas Angular (`agents-list`, `agent-edit`, `ai-settings`) e a rota lazy `/configuracoes/agentes-de-ia/*`. Porém o `omniDesk.Crm` **ainda não tem layout autenticado** — `app.routes.ts` só tem rotas de auth (`/login`, `/forgot-password`, etc.). As páginas funcionam quando navegadas diretamente pela URL, mas **não há sidebar para acessá-las** (T064).

**Workaround**: navegar manualmente para `/configuracoes/agentes-de-ia` enquanto o shell autenticado não chega.
**Issue de follow-up**: [`follow-up-issues.md`](follow-up-issues.md) ISSUE-1.

---

## Pós-implementação — checklist

Após `/speckit-implement` da Spec 006, verificar:

- [ ] Provisionamento de novo tenant **completa sem erro** e cria `ai_agents` (orchestrator) + `ai_settings`.
- [ ] `IAgentRuntime` real está registrado no DI (não mais `FallbackAgentRuntime`).
- [ ] `SuggestReplyService` (Spec 005) consome o sub-agente real do depto.
- [ ] `tenants.default_department_id` é setado automaticamente no primeiro depto ativo criado.
- [ ] Sub-agentes com depto inativo não aparecem na lista do Orchestrator.
- [ ] Mensagens criadas por IA produzem documento em `agent_activity_logs`.
- [ ] Tickets criados por transbordo têm snapshot em `ai_handoff_snapshots` e disparam evento Redis.
