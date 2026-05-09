# Data Model — Spec 006 (Agentes de IA)

**Phase 1 output** — entidades, relações, migrations e regras de validação derivadas da spec + research.

Convenção: tabelas tenant-scoped vivem em `tenant_{slug}.<table>`; tabelas globais em `public.<table>`. Coleções Mongo seguem `{slug}_<collection>`.

---

## 1. Entidades Postgres (tenant schema)

### 1.1 `tenant_{slug}.ai_agents`

Representa um agente — Orchestrator único ou sub-agente — do tenant.

| Coluna | Tipo | Constraint | Notas |
|---|---|---|---|
| `id` | uuid | PK, default `gen_random_uuid()` | — |
| `template_id` | uuid | nullable | FK lógica para `public.agent_templates.id` (cross-schema, sem REFERENCES) — quando o agente foi clonado de um template global |
| `type` | varchar(16) | NOT NULL, CHECK `IN ('orchestrator','sub_agent')` | enum |
| `name` | varchar(100) | NOT NULL | exibido ao cliente |
| `short_description` | varchar(300) | NOT NULL DEFAULT '' | usado pelo Orchestrator p/ rotear |
| `prompt` | text | NOT NULL | instruções completas |
| `model` | varchar(50) | NOT NULL DEFAULT `'gpt-4o'` | precisa estar em `ai_settings.available_models` ∩ allowlist global |
| `department_id` | uuid | nullable, REFERENCES `tenant_{slug}.departments(id) ON DELETE RESTRICT` | obrigatório para sub_agent (CHECK abaixo); null para orchestrator |
| `openai_assistant_id` | varchar(100) | nullable | criado lazy ao primeiro run |
| `is_active` | boolean | NOT NULL DEFAULT true | inativo não aparece na lista do Orchestrator |
| `created_by` | uuid | NOT NULL | FK lógica para `public.users.id` |
| `created_at` | timestamptz | NOT NULL DEFAULT `now()` | — |
| `updated_at` | timestamptz | NOT NULL DEFAULT `now()` | atualizado por trigger ou app |
| `deleted_at` | timestamptz | nullable | soft delete |

**Constraints adicionais**:
- `CHECK ((type = 'orchestrator' AND department_id IS NULL) OR (type = 'sub_agent' AND department_id IS NOT NULL))` — impede orchestrator com depto, exige depto em sub_agent.
- **Unique partial index** `CREATE UNIQUE INDEX ux_ai_agents_orchestrator ON ai_agents (type) WHERE type = 'orchestrator' AND deleted_at IS NULL` — garante FR-001 (1 orchestrator por tenant ativo).

**Índices**:
- `CREATE INDEX idx_ai_agents_type_active ON ai_agents (type, is_active) WHERE deleted_at IS NULL` — listagem do Orchestrator (sub-agentes ativos).
- `CREATE INDEX idx_ai_agents_department_id ON ai_agents (department_id) WHERE deleted_at IS NULL` — busca pela vinculação.

**Estados**:

```
[draft inactive] --activate--> [active]
[active] --deactivate--> [inactive]
[inactive] --reactivate--> [active]
[any] --soft-delete (only if no FK refs in agent_activity_logs OR ai_threads)--> [deleted]
```

Notas: Soft delete é controlado pelo serviço (FR-010). Reativação é livre desde que `deleted_at IS NULL`.

---

### 1.2 `tenant_{slug}.ai_settings`

Configurações de IA do tenant — 1:1 com tenant (linha única no schema do tenant).

| Coluna | Tipo | Constraint | Notas |
|---|---|---|---|
| `id` | uuid | PK | gerado |
| `tenant_id` | uuid | NOT NULL, UNIQUE | FK lógica para `public.tenants.id` |
| `context_window_messages` | int | NOT NULL DEFAULT 20, CHECK `BETWEEN 5 AND 100` | FR-022 |
| `available_models` | text[] | NOT NULL DEFAULT `ARRAY[]::text[]` | vazio = usa allowlist global |
| `updated_at` | timestamptz | NOT NULL DEFAULT `now()` | — |

**Provisionamento**: row criada por `TenantProvisioningJob` (modificação) com defaults.

---

### 1.3 `tenant_{slug}.ai_threads` (transitional — ver R10)

Bridge mínima até a Spec 007 introduzir `conversations` definitiva.

| Coluna | Tipo | Constraint | Notas |
|---|---|---|---|
| `id` | uuid | PK | — |
| `external_conversation_ref` | varchar(100) | NOT NULL | id de canal: `livechat:<sid>` ou `whatsapp:<wa_id>` |
| `openai_thread_id` | varchar(100) | NOT NULL UNIQUE | retornado por `threads.create` |
| `current_agent_id` | uuid | nullable, REFERENCES `ai_agents(id) ON DELETE SET NULL` | null = orchestrator OU humano (ver `handed_off_to_human_at`) |
| `handed_off_to_human_at` | timestamptz | nullable | marca transbordo; após preenchimento, IA não processa mais |
| `created_at`, `updated_at` | timestamptz | — | — |

**Índices**:
- `CREATE UNIQUE INDEX ux_ai_threads_external_ref ON ai_threads (external_conversation_ref)` — uma thread por conversa externa.
- `CREATE INDEX idx_ai_threads_current_agent ON ai_threads (current_agent_id) WHERE handed_off_to_human_at IS NULL`.

**Migração futura**: Ao implementar Spec 007, os campos `openai_thread_id`, `current_agent_id`, `handed_off_to_human_at` migram para `conversations` e `ai_threads` é removida. Migração documentada em `cross-spec-pendencies.md`.

---

## 2. Modificações em entidades existentes (Postgres `public`)

### 2.1 `public.tenants` — adições

Migration: `Add_DefaultDepartmentId_To_Tenants.sql`.

| Coluna | Tipo | Default | Notas |
|---|---|---|---|
| `default_department_id` | uuid | nullable | FK lógica cross-schema para `tenant_{slug}.departments(id)` — sem REFERENCES (postgres não suporta FK cross-schema dinâmico). Validado na app (FluentValidation) e via trigger opcional. |

**Resolução em runtime**: ao acionar `transfer_to_human` a partir do Orchestrator, o serviço:
1. Lê `tenant.default_department_id`.
2. Confirma que o depto existe e está ativo (consulta no schema do tenant).
3. Se inválido (depto deletado/inativo), retorna erro de configuração — atendente humano notificado via fallback (depto admin do operador SaaS) — ver `cross-spec-pendencies.md` item 005-A.

**Atribuição**: campo é **opcional na criação** do tenant (Spec 003 não exigia). Tenant admin define via `PATCH /api/me/tenant/default-department` (a ser exposto pela Spec 003 — pendência) ou via auto-fill: ao criar o **primeiro departamento ativo**, se `default_department_id IS NULL`, o backend o seta automaticamente.

---

## 3. Coleções MongoDB

### 3.1 `{slug}_agent_activity_logs`

Documento por execução de agente. Sem PII — apenas metadata e metrics.

```json
{
  "_id": "ObjectId",
  "tenant_slug": "clinica-abc",
  "conversation_id": "uuid (ai_threads.id)",
  "agent_id": "uuid (ai_agents.id)",
  "agent_name": "Agente Comercial",
  "agent_type": "sub_agent",                 // ou "orchestrator"
  "action": "respond",                       // respond | handoff_to_agent | transfer_to_human | api_error
  "input_tokens": 450,
  "output_tokens": 120,
  "model": "gpt-4o",
  "latency_ms": 1240,
  "openai_run_id": "run_...",
  "openai_thread_id": "thread_...",
  "handoff_target_agent_id": null,           // preenchido em action=handoff_to_agent
  "handoff_target_department_id": null,      // preenchido em action=transfer_to_human
  "error": null,                             // {type, status, message} em action=api_error
  "timestamp": "2026-06-02T14:30:00Z"
}
```

**Índices**:
- `{tenant_slug: 1, conversation_id: 1, timestamp: -1}` — timeline por conversa.
- `{tenant_slug: 1, agent_id: 1, timestamp: -1}` — análise por agente.
- `{tenant_slug: 1, action: 1, timestamp: -1}` — métricas globais (errors rate).
- TTL **NÃO aplicado** — auditoria permanente (constituição §VI).

---

## 4. Estado Redis

| Chave | Tipo | TTL | Propósito |
|---|---|---|---|
| `{slug}:incoming_messages` | Hangfire queue | — | Fila de mensagens entrando |
| `{slug}:outgoing_messages` | Hangfire queue | — | Fila de mensagens saindo |
| `{slug}:agent_run:{conversation_id}` | string (lock) | 60 s | Lock de execução por conversa |
| `{slug}:msg_idempo:{message_id}` | string | 86400 s | Idempotência por mensagem |
| `{slug}:playground:{session_id}` | hash `{thread_id, agent_id, last_used}` | 1800 s | Sessão de teste |

---

## 5. Fluxo de transição de estado (state machine implícita)

```
                                       ┌────────────────────────────┐
[conversation NEW] --first message --> │ run no Orchestrator        │
                                       │ current_agent_id = null    │
                                       └─────┬──────────────────────┘
                                             │
              ┌──────────────────────────────┼──────────────────────────┐
              │ tool: handoff_to_agent       │ tool: transfer_to_human  │ assistant message → outgoing
              ▼                              ▼                          ▼
   current_agent_id = X            handed_off_to_human_at = now    [continua no Orchestrator]
   (sub-agente X)                  current_agent_id = null
   thread continua                 IA não processa mais mensagens
```

Após `handed_off_to_human_at != null`:
- Mensagens entrantes recebem auto-reply do sistema "Sua mensagem foi recebida…" (FR-015) — não chamam OpenAI.
- A conversa permanece visível para o atendente humano (via Spec 007/008/005).

---

## 6. Validações (FluentValidation)

### 6.1 `CreateAiAgentValidator` (sub_agent only — orchestrator não aceita create)

- `name`: required, length 1..100.
- `short_description`: required, length 1..300.
- `prompt`: required, length ≥ 10, ≤ 50_000 caracteres (limite prático para Assistant instructions).
- `model`: required, deve estar em `(ai_settings.available_models ∪ global_allowlist)`.
- `department_id`: required, deve referenciar departamento ativo do tenant.
- `is_active`: optional, default true.

### 6.2 `UpdateAiAgentValidator`

- Mesmos do create, mas todos opcionais. **Bloqueia** mudança de `type` (FR-007).
- Para Orchestrator: aceita apenas `name`, `prompt`, `model`. Rejeita `department_id`, `short_description` (irrelevantes).

### 6.3 `UpdateAiSettingsValidator`

- `context_window_messages`: required, integer, between 5 and 100 (FR-022).
- `available_models`: optional array; cada item deve estar em `global_allowlist` (não permite tenant adicionar modelo não-aprovado pelo SaaS).

---

## 7. Migrations

### 7.1 `Add_AiAgents_AiSettings.sql` (tenant scope)

```sql
-- Migration: Add_AiAgents_AiSettings
-- Generated: 2026-05-08
-- Spec: 006-ai-agents
-- Cria tabelas tenant-scoped para agentes de IA, configurações e bridge transitional para conversas.

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ai_agents (
    id                   uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    template_id          uuid,                                             -- FK lógica public.agent_templates
    type                 varchar(16)  NOT NULL CHECK (type IN ('orchestrator','sub_agent')),
    name                 varchar(100) NOT NULL,
    short_description    varchar(300) NOT NULL DEFAULT '',
    prompt               text         NOT NULL,
    model                varchar(50)  NOT NULL DEFAULT 'gpt-4o',
    department_id        uuid         REFERENCES {TENANT_SCHEMA}.departments(id) ON DELETE RESTRICT,
    openai_assistant_id  varchar(100),
    is_active            boolean      NOT NULL DEFAULT true,
    created_by           uuid         NOT NULL,
    created_at           timestamptz  NOT NULL DEFAULT now(),
    updated_at           timestamptz  NOT NULL DEFAULT now(),
    deleted_at           timestamptz,
    CONSTRAINT chk_orchestrator_no_dept CHECK (
        (type = 'orchestrator' AND department_id IS NULL)
        OR (type = 'sub_agent' AND department_id IS NOT NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_agents_orchestrator
    ON {TENANT_SCHEMA}.ai_agents (type) WHERE type = 'orchestrator' AND deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_ai_agents_type_active
    ON {TENANT_SCHEMA}.ai_agents (type, is_active) WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_ai_agents_department_id
    ON {TENANT_SCHEMA}.ai_agents (department_id) WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ai_settings (
    id                       uuid        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    tenant_id                uuid        NOT NULL UNIQUE,
    context_window_messages  int         NOT NULL DEFAULT 20
        CHECK (context_window_messages BETWEEN 5 AND 100),
    available_models         text[]      NOT NULL DEFAULT ARRAY[]::text[],
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ai_threads (
    id                          uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    external_conversation_ref   varchar(100) NOT NULL,
    openai_thread_id            varchar(100) NOT NULL UNIQUE,
    current_agent_id            uuid REFERENCES {TENANT_SCHEMA}.ai_agents(id) ON DELETE SET NULL,
    handed_off_to_human_at      timestamptz,
    created_at                  timestamptz  NOT NULL DEFAULT now(),
    updated_at                  timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_threads_external_ref
    ON {TENANT_SCHEMA}.ai_threads (external_conversation_ref);

CREATE INDEX IF NOT EXISTS idx_ai_threads_current_agent
    ON {TENANT_SCHEMA}.ai_threads (current_agent_id) WHERE handed_off_to_human_at IS NULL;
```

### 7.2 `Add_DefaultDepartmentId_To_Tenants.sql` (public scope)

```sql
-- Migration: Add_DefaultDepartmentId_To_Tenants
-- Generated: 2026-05-08
-- Spec: 006-ai-agents (extensão da Spec 003)
ALTER TABLE public.tenants
    ADD COLUMN IF NOT EXISTS default_department_id uuid;

COMMENT ON COLUMN public.tenants.default_department_id IS
    'FK lógica para tenant_{slug}.departments(id). Validação cross-schema feita na aplicação. ' ||
    'Usado em transbordo automático de Orchestrator (Spec 006).';
```

### 7.3 Modificação em `TenantProvisioningJob.cs`

O método `CopyAgentTemplatesAsync` é renomeado para `ProvisionAiAgentsAsync` e atualizado para:
1. Inserir em `{schema}.ai_agents` (não mais `agents`).
2. Mapear colunas do template para o novo schema:
   - `template.Name → ai_agents.name`
   - `template.Type ('orchestrator'|'sub_agent') → ai_agents.type`
   - `template.Description → ai_agents.short_description`
   - `template.Prompt → ai_agents.prompt`
   - `null → ai_agents.department_id` (sub-agentes do template não vinculam depto automaticamente — tenant configura depois)
3. Criar row em `{schema}.ai_settings` com defaults (`context_window_messages=20`, `available_models=[]`).
4. Idempotente: `ON CONFLICT (id) DO NOTHING` + checa que orchestrator único existe via partial unique index.

---

## 8. Resumo de FRs cobertos pelo data model

| FR | Mapeamento |
|---|---|
| FR-001, FR-031 | `ux_ai_agents_orchestrator` partial unique + provisionamento copia template |
| FR-002 | Aplicação resolve `current_agent_id IS NULL → orchestrator` |
| FR-003, FR-006 | `ai_threads.current_agent_id` muta sem afetar `openai_thread_id` |
| FR-004 | `idx_ai_agents_type_active` filtra `is_active = true` |
| FR-005 | `ai_threads.openai_thread_id UNIQUE` por conversa |
| FR-007 | `UpdateAiAgentValidator` bloqueia `type` change |
| FR-008, FR-009 | colunas obrigatórias + `chk_orchestrator_no_dept` |
| FR-010 | `deleted_at` + serviço impede delete físico se há refs em `ai_threads`/`agent_activity_logs` |
| FR-012 | `PromptVariableSubstitutor` (não em data model) |
| FR-016 | `tenants.default_department_id` |
| FR-022, FR-023 | `ai_settings.context_window_messages` |
| FR-024 | `ai_settings.available_models` |
| FR-025 | `tenants.openai_api_key_enc` (já existe) + `OpenAiKeyResolver` |
| FR-028, FR-029 | `ai_agents.openai_assistant_id` |
| FR-030 | `agent_activity_logs` Mongo |
| FR-032 | aplicação detecta `is_active=false` na próxima leitura → roteia ao Orchestrator |
| FR-033 | aplicação injeta system message no thread após `transfer_to_human` |

Cobertura completa. Próximo: contratos.
