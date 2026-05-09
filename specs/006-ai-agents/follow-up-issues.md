# Follow-up GitHub Issues — Spec 006

Issues a serem criadas via `gh issue create` quando houver credenciais. Conteúdo final dos itens **não-bloqueantes** levantados em [`cross-spec-pendencies.md`](cross-spec-pendencies.md) e por gaps de plataforma.

> Conferir antes de abrir — alguns podem já existir em outras specs ou no backlog.

---

## ISSUE-1 — Crm: shell autenticado + sidebar com "Agentes de IA"

**Severity**: medium
**Spec dona**: future Crm shell (provavelmente Spec 007 ou dedicada)
**Trigger**: Spec 006 T064 — não pôde adicionar item de menu lateral porque o `omniDesk.Crm` não tem layout autenticado/sidebar; só rotas de auth (`/login`, `/forgot-password`, etc.).

**Tarefa**:
- Criar layout autenticado (`AuthShellComponent` com header + sidebar + outlet).
- Adicionar item "Agentes de IA" na sidebar com guard `tenant_admin|supervisor` (ver `Policies.CanViewAgents`).
- Adicionar item "Configurações Avançadas" na sidebar com guard `tenant_admin` (`Policies.CanEditAgentAdvancedConfig`).
- Aninhar as rotas `configuracoes/agentes-de-ia/*` dentro do shell autenticado.

**Files**: `src/omniDesk.Crm/src/app/{layout,app.routes.ts}`

---

## ISSUE-2 — Spec 005: round-robin consome ticket criado por IA

**Severity**: medium
**Spec dona**: 005 (Departamentos & Atendentes)
**Trigger**: Spec 006 cross-spec §005-B.

**Tarefa**:
- Criar consumer Redis para canal `{slug}:ws:dept:{department_id}`.
- Ao receber payload `{type: "ticket_created_from_ai"}`, invocar `TicketAssignmentService.AttemptAssignAsync(ticket_id)`.

**Workaround atual**: ticket fica `queued`; atendente humano usa "Pegar próximo da fila" no CRM.

**Files**: `src/omniDesk.Api/Features/Distribution/AiTicketAssignmentSubscriber.cs` (novo), Program.cs (HostedService).

---

## ISSUE-3 — Spec 005/008: badge visual "ticket de IA" na fila do CRM

**Severity**: low
**Spec dona**: 005 (CRM frontend) / 008 (Tickets V2)
**Trigger**: Spec 006 cross-spec §005-C.

**Tarefa**:
- Adicionar coluna `created_by_source` ENUM (`ai|human|webhook`) em `tickets` (Spec 008).
- StubTicketCreationGateway (Spec 006) preenche `'ai'` quando criar via `transfer_to_human`.
- Frontend Spec 005 desenha badge no card.

**Files**: `Add_TicketSource.sql` (Spec 008), `StubTicketCreationGateway.cs`, `ticket-queue.component.html`.

---

## ISSUE-4 — Plataforma: SqlMigrationRunner para arquivos `Add_*.sql`

**Severity**: HIGH (latente)
**Spec dona**: nenhuma específica — é trabalho de plataforma
**Trigger**: Spec 006 cross-spec §001-A.

**Problema**: Os arquivos `Infrastructure/Persistence/Migrations/Add_*.sql` (gerados pelas Specs 005, 006) **nunca rodam automaticamente**. `TenantSchemaProvisioner.ProvisionSchemaAsync` chama `ctx.Database.MigrateAsync()` que processa migrations C# do EF Core — não os `.sql`. Em produção, os SQLs são aplicados manualmente ou por script externo não-rastreado.

**Tarefa**:
- Criar `SqlMigrationRunner` que lê `Migrations/*.sql`, substitui `{TENANT_SCHEMA}` quando aplicável, e aplica via `Database.ExecuteSqlRawAsync`.
- Manter tabela de versão (ex.: `__sql_migrations_history` separada da do EF Core) para evitar reaplicação.
- Integrar no `TenantSchemaProvisioner` (tenant-scoped) e em um startup hook (public-scoped).

**Affected migrations**:
- `Add_Departments_Attendants.sql` (Spec 005)
- `Add_Tickets_Scaffold.sql` (Spec 005)
- `Add_AiAgents_AiSettings.sql` (Spec 006)
- `Add_DefaultDepartmentId_To_Tenants.sql` (Spec 006)
- `Add_Ai_Handoff_Snapshots.sql` (Spec 006)

---

## ISSUE-5 — Spec 003: UI para `default_department_id` em Tenant Settings

**Severity**: low
**Spec dona**: 003 (Tenant Provisioning) ou Admin SaaS
**Trigger**: Spec 006 cross-spec §003-C.

**Tarefa**:
- Adicionar endpoint `PATCH /api/me/tenant/default-department` (CRM) e/ou `PATCH /api/admin/tenants/{id}/default-department` (Admin SaaS).
- UI: seletor de departamento default em "Configurações do Tenant".

**Workaround atual**: auto-fill no primeiro depto ativo criado (já implementado).

---

## ISSUE-6 — Auditoria: thread temporário de playground órfão na OpenAI

**Severity**: low
**Spec dona**: 006 — patch futuro
**Trigger**: Spec 006 research §R12.

**Problema**: `PlaygroundCleanupJob` é stub V1; threads OpenAI órfãos podem acumular se um restart matar o TTL do Redis antes do cleanup.

**Tarefa**:
- Implementar varredura: armazenar `openai_thread_id` + `playground_session_id` em coleção Mongo de curto prazo (TTL 7d) ao criar playground session.
- Job recurring 1h compara com Redis e deleta órfãos via `threads.delete`.

---

## Resumo

| # | Severity | Bloqueia algo? |
|---|---|---|
| 1 | medium | Sidebar do Crm (nada usa as 3 páginas via UI ainda) |
| 2 | medium | Auto-distribuição (humano pega manual) |
| 3 | low | Cosmético |
| 4 | **HIGH** | Migrations não rodam em prod sem manual fix |
| 5 | low | Auto-fill resolve 95% |
| 6 | low | Custo OpenAI eventual |

**Recomendação**: Issue 4 deve ser endereçada antes de qualquer release. Demais ficam no backlog.
