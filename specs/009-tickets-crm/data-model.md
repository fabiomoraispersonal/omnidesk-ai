# Data Model — 009 Tickets / CRM

Entidades, migrations (SQL), índices, transições e regras de integridade. Todas as tabelas vivem em `tenant_{slug}.*` (Princípio I). MongoDB collection idem em `{slug}_*`.

---

## Entidades

### 1. `tickets` (EXPANDIDA — substitui scaffold Spec 005)

| Campo | Tipo | NN | Default | Descrição |
|---|---|---|---|---|
| `id` | `uuid` | ✅ | `gen_random_uuid()` | PK |
| `protocol` | `varchar(20)` | ✅ | — (gerado pelo `TicketProtocolService`) | Formato `TK-YYYYMMDD-XXXXX`. Imutável. UNIQUE. |
| `channel` | `varchar(16)` | ✅ | — | `live_chat` / `whatsapp` / `manual` |
| `status` | `varchar(16)` | ✅ | `new` | `new` / `in_progress` / `waiting_client` / `resolved` / `cancelled` |
| `priority` | `varchar(8)` | ✅ | `normal` | `low` / `normal` / `high` / `urgent` |
| `conversation_id` | `uuid` | ❌ | — | FK lógica → `conversations(id)`. NULL em tickets manuais sem conversa. |
| `contact_id` | `uuid` | ❌ | — | FK → `contacts(id)`. NULL antes da identificação. |
| `department_id` | `uuid` | ✅ | — | FK → `departments(id) ON DELETE RESTRICT` |
| `attendant_id` | `uuid` | ❌ | — | FK → `attendants(id) ON DELETE SET NULL`. NULL = na fila. |
| `assigned_at` | `timestamptz` | ❌ | — | Timestamp da atribuição corrente (mudado em cada transferência/atribuição). |
| `tags` | `text[]` | ❌ | `'{}'` | Tags livres (lowercase, hífen/underscore permitidos). |
| `subject` | `varchar(255)` | ❌ | `''` | Auto-preenchido com primeiras 100 chars; editável. |
| `number` | `bigint` | ✅ | `nextval('ticket_number_seq')` | **PRESERVADO** do scaffold Spec 005 (interno; visível em logs). `protocol` é o identificador público. |
| `resolved_at` | `timestamptz` | ❌ | — | Preenchido em `status → resolved`. |
| `cancelled_at` | `timestamptz` | ❌ | — | Preenchido em `status → cancelled`. |
| `first_response_at` | `timestamptz` | ❌ | — | Preenchido na primeira mensagem `sender_type=attendant` em conversa vinculada. |
| `sla_first_response_deadline` | `timestamptz` | ❌ | — | Calculado na atribuição (`assigned_at + dept.sla_first_response_minutes`). |
| `sla_resolution_deadline` | `timestamptz` | ❌ | — | Calculado na criação (`created_at + dept.sla_resolution_minutes`). |
| `sla_paused_duration_minutes` | `int` | ✅ | `0` | Acumulador de minutos pausados em `waiting_client`. |
| `sla_started_at` | `timestamptz` | ❌ | — | **PRESERVADO** do scaffold (Spec 005). Alinhado com `created_at` em V2; mantido para compat. |
| `waiting_client_since` | `timestamptz` | ❌ | — | Preenchido ao entrar em `waiting_client`, zerado ao sair. |
| `has_reminder_alert` | `boolean` | ✅ | `false` | Badge ⚠️. Setado por Spec 011, resetado por esta spec no encerramento. |
| `search_vector` | `tsvector` | ❌ | — | `GENERATED ALWAYS AS (...) STORED`. Index GIN. |
| `created_at` | `timestamptz` | ✅ | `now()` | — |
| `updated_at` | `timestamptz` | ✅ | `now()` | Atualizado em todas as escritas. |
| `deleted_at` | `timestamptz` | ❌ | — | Soft delete (Princípio IV). Nunca preenchido em V1 — tickets viram `cancelled`. |

**Constraints**:
- `UNIQUE (protocol)` em `protocol` (parcial, `WHERE protocol IS NOT NULL`).
- `CHECK (status IN ('new','in_progress','waiting_client','resolved','cancelled'))`.
- `CHECK (priority IN ('low','normal','high','urgent'))`.
- `CHECK (channel IN ('live_chat','whatsapp','manual'))`.
- `CHECK ((resolved_at IS NULL) = (status != 'resolved'))`.
- `CHECK ((cancelled_at IS NULL) = (status != 'cancelled'))`.
- `CHECK ((waiting_client_since IS NULL) = (status != 'waiting_client'))`.

**Index**:
- `idx_tickets_status` em `status` (já existe — Spec 005).
- `idx_tickets_department_id` em `department_id` (idem).
- `idx_tickets_attendant_id` em `attendant_id` (já existe como `assigned_attendant_id`, renomeado).
- `idx_tickets_contact_id` em `contact_id`.
- `idx_tickets_conversation_id` em `conversation_id`.
- `idx_tickets_created_at` em `created_at DESC` (Kanban + busca).
- `idx_tickets_search_vector` GIN em `search_vector`.
- `idx_tickets_tags` GIN em `tags`.

### 2. `contacts` (NOVA)

| Campo | Tipo | NN | Default | Descrição |
|---|---|---|---|---|
| `id` | `uuid` | ✅ | `gen_random_uuid()` | PK |
| `name` | `varchar(255)` | ❌ | — | — |
| `email` | `varchar(255)` | ❌ | — | Dedup key P1. Case-insensitive index. |
| `phone` | `varchar(20)` | ❌ | — | Original (com máscara). |
| `phone_normalized` | `varchar(20)` | ❌ | — | Apenas dígitos. Dedup key P2. |
| `notes` | `text` | ❌ | — | Observações internas (NUNCA enviado ao cliente). |
| `source_channels` | `text[]` | ❌ | `'{}'` | `{live_chat,whatsapp,manual}` |
| `created_at` | `timestamptz` | ✅ | `now()` | — |
| `updated_at` | `timestamptz` | ✅ | `now()` | — |
| `deleted_at` | `timestamptz` | ❌ | — | Soft delete. |

**Constraints**:
- `UNIQUE` parcial: `lower(email)` `WHERE email IS NOT NULL AND deleted_at IS NULL`.
- `UNIQUE` parcial: `phone_normalized` `WHERE phone_normalized IS NOT NULL AND deleted_at IS NULL`.

**Index**:
- `idx_contacts_email_lower` em `lower(email)` (defensivo + busca).
- `idx_contacts_phone_normalized` em `phone_normalized`.
- `idx_contacts_name_trgm` GIN em `name` com `gin_trgm_ops` (busca fuzzy V1.1; opcional).

### 3. `ticket_notes` (NOVA)

| Campo | Tipo | NN | Default | Descrição |
|---|---|---|---|---|
| `id` | `uuid` | ✅ | `gen_random_uuid()` | PK |
| `ticket_id` | `uuid` | ✅ | — | FK → `tickets(id) ON DELETE CASCADE`. |
| `attendant_id` | `uuid` | ✅ | — | FK → `attendants(id) ON DELETE RESTRICT`. |
| `content` | `text` | ✅ | — | Append-only (sem update, sem delete). |
| `created_at` | `timestamptz` | ✅ | `now()` | — |

**Constraints**:
- `CHECK (length(content) BETWEEN 1 AND 10000)`.

**Index**:
- `idx_ticket_notes_ticket_id` em `ticket_id, created_at`.

**Regra de domínio (não em SQL, em código)**:
- `ITicketNoteRepository` **não expõe** método `Update` nem `Delete`. Apenas `Add` e `ListByTicket`.

### 4. `pipelines` (NOVA)

| Campo | Tipo | NN | Default | Descrição |
|---|---|---|---|---|
| `id` | `uuid` | ✅ | `gen_random_uuid()` | PK |
| `department_id` | `uuid` | ✅ | — | FK → `departments(id) ON DELETE CASCADE`. UNIQUE (1:1). |
| `name` | `varchar(100)` | ✅ | `'Pipeline'` | — |
| `created_at` | `timestamptz` | ✅ | `now()` | — |
| `updated_at` | `timestamptz` | ✅ | `now()` | — |
| `deleted_at` | `timestamptz` | ❌ | — | Soft delete via cascade do departamento. |

**Constraints**:
- `UNIQUE (department_id)` parcial `WHERE deleted_at IS NULL`.

### 5. `pipeline_columns` (NOVA)

| Campo | Tipo | NN | Default | Descrição |
|---|---|---|---|---|
| `id` | `uuid` | ✅ | `gen_random_uuid()` | PK |
| `pipeline_id` | `uuid` | ✅ | — | FK → `pipelines(id) ON DELETE CASCADE`. |
| `name` | `varchar(100)` | ✅ | — | Editável pelo tenant. |
| `status_mapping` | `varchar(16)` | ✅ | — | `new` / `in_progress` / `waiting_client`. Único por pipeline. |
| `order` | `int` | ✅ | — | 1, 2, 3… |
| `color` | `varchar(7)` | ❌ | — | Hex (`#RRGGBB`). |
| `created_at` | `timestamptz` | ✅ | `now()` | — |
| `updated_at` | `timestamptz` | ✅ | `now()` | — |

**Constraints**:
- `CHECK (status_mapping IN ('new','in_progress','waiting_client'))`.
- `UNIQUE (pipeline_id, status_mapping)`.
- `CHECK (order >= 1)`.
- `CHECK (color IS NULL OR color ~ '^#[0-9A-Fa-f]{6}$')`.

### 6. `conversations` (MODIFICADA — Spec 007)

Coluna **adicionada**:
| Campo | Tipo | NN | Default | Descrição |
|---|---|---|---|---|
| `ticket_id` | `uuid` | ❌ | — | FK → `tickets(id) ON DELETE SET NULL`. Index. |

> **Nota**: a Spec 007 já comentou no SQL: `ticket_id uuid, -- FK lógica → tickets (Spec 008+)`. Esta migration **adiciona** a coluna oficialmente e o constraint FK.

### 7. `visitors` (MODIFICADA — Spec 007)

Coluna **adicionada**:
| Campo | Tipo | NN | Default | Descrição |
|---|---|---|---|---|
| `contact_id` | `uuid` | ❌ | — | FK → `contacts(id) ON DELETE SET NULL`. Index. |

### 8. `{tenant}_ticket_events` (MongoDB collection)

```json
{
  "_id": ObjectId,
  "tenant_slug": "clinica-abc",
  "ticket_id": "uuid",
  "protocol": "TK-20260503-00042",
  "event_type": "status_changed" | "attendant_assigned" | "transferred" | "priority_changed" | "tag_added" | "tag_removed" | "subject_changed" | "note_added" | "sla_breached" | "ticket_created" | "ticket_resolved" | "ticket_cancelled",
  "actor_type": "attendant" | "system" | "ai",
  "actor_id": "uuid?",
  "actor_name": "string?",
  "from": "string?",
  "to": "string?",
  "department_from_id": "uuid?",
  "department_to_id": "uuid?",
  "attendant_from_id": "uuid?",
  "attendant_to_id": "uuid?",
  "tag_added": "string?",
  "tag_removed": "string?",
  "note_id": "uuid?",
  "sla_type": "first_response" | "resolution",
  "reason": "string?",
  "timestamp": ISODate
}
```

**Index** (Mongo):
- `{ ticket_id: 1, timestamp: -1 }` — leitura do histórico.
- `{ tenant_slug: 1, event_type: 1, timestamp: -1 }` — analytics.
- `{ timestamp: -1 }` — varredura de auditoria global.

---

## Transições de Status (validadas pelo `ChangeStatusValidator`)

```text
                       ┌────────────┐
                       │  cancelled │  ← qualquer estado exceto resolved
                       └────────────┘
                              ▲
       ┌──────────┐    auto   │    manual    ┌─────────────────┐
       │   new    │──────────►│◄─────────────│  waiting_client │
       └──────────┘  assign   │              └─────────────────┘
                              ▼                  ▲       │
                       ┌──────────┐              │       │
                       │ in_progr │──────────────┘       │
                       │   ess    │  manual              │
                       └──────────┘                      │
                              │                          │
                              │  cliente envia mensagem  │
                              │◄─────────────────────────┘
                              │     (auto)
                              ▼
                       ┌──────────┐
                       │ resolved │  ← terminal
                       └──────────┘
```

| De ↓ \ Para → | new | in_progress | waiting_client | resolved | cancelled |
|---|---|---|---|---|---|
| **(criação)** | ✅ | ✅ (se atribuído) | ❌ | ❌ | ❌ |
| **new** | — | ✅ auto (assign) | ❌ | ❌ | ✅ |
| **in_progress** | ❌ | — | ✅ manual | ✅ manual | ✅ |
| **waiting_client** | ❌ | ✅ auto (mensagem cliente) ou manual | — | ✅ manual | ✅ |
| **resolved** | ❌ | ❌ | ❌ | — | ❌ (terminal) |
| **cancelled** | ❌ | ❌ | ❌ | ❌ | — (terminal) |

**Side-effects das transições**:
- `→ in_progress` (a partir de waiting_client): calcula pausa, soma a `sla_paused_duration_minutes`, zera `waiting_client_since`.
- `→ waiting_client`: preenche `waiting_client_since = now()`.
- `→ resolved`: preenche `resolved_at`, calcula pausa final (se em `waiting_client`), atualiza conversa em cascata (`conversations.status = resolved`).
- `→ cancelled`: preenche `cancelled_at`. Conversa **não** atualizada (atendimento abortado, não concluído).
- **Qualquer transição**: registra evento `status_changed` em Mongo.

---

## Side-effects de outras operações

| Operação | Side-effect |
|---|---|
| `attendant_id` muda (de NULL para X) | `assigned_at = now()`. `sla_first_response_deadline = assigned_at + dept.sla_first_response_minutes`. Status `new → in_progress` (se ainda new). Evento `attendant_assigned` em Mongo. WebSocket `ticket.assigned`. |
| `attendant_id` muda (de X para Y, mesmo depto) | `assigned_at = now()`. Evento `transferred` em Mongo. WebSocket `ticket.transferred`. Sem reset de SLA. |
| `department_id` muda | Recalcula `sla_first_response_deadline` e `sla_resolution_deadline` com novas metas. **Zera** `sla_paused_duration_minutes`. **Preserva** `first_response_at` se já existir. Evento `transferred`. |
| `priority` muda | Evento `priority_changed` em Mongo. |
| `subject` muda | Evento `subject_changed` em Mongo. |
| `tags` muda (add) | Evento `tag_added` por tag. |
| `tags` muda (remove) | Evento `tag_removed` por tag. |
| `first_response_at` é preenchido (primeira mensagem do atendente) | Não é evento explícito — é deduzido de `message.created` por `OutgoingMessageWorker`. |
| `note` adicionada | Evento `note_added` em Mongo (somente `note_id`, sem conteúdo). |
| SLA cruza 80% | Redis flag setada. WebSocket `ticket.sla_warning`. Nenhum evento Mongo (warning é volátil). |
| SLA expira | Evento `sla_breached` em Mongo + WebSocket `ticket.sla_breached`. |

---

## Deduplicação de Contato — Algoritmo

```pseudo
fn FindOrCreateContact(hints: { email?, phone?, name?, channel: SourceChannel }) -> Contact:
    lock_key = (hints.email != null)
             ? "{slug}:contact:dedup:lock:email:{sha256(lower(email))}"
             : (hints.phone != null)
               ? "{slug}:contact:dedup:lock:phone:{normalize(phone)}"
               : null

    if lock_key != null:
        with redis.lock(lock_key, ttl=3s, max_wait=3s):
            existing = query_by_email_or_phone(hints)
            if existing:
                update_empty_fields(existing, hints)
                add_channel_to_source_channels(existing, channel)
                return existing
            else:
                return insert_new_contact(hints)
    else:
        // Sem email nem phone → sempre cria
        return insert_new_contact(hints)


fn query_by_email_or_phone(hints) -> Contact?:
    return SELECT * FROM contacts
           WHERE deleted_at IS NULL
             AND (
               (email IS NOT NULL AND lower(email) = lower(:email))
               OR
               (phone_normalized IS NOT NULL AND phone_normalized = :phone_normalized)
             )
           ORDER BY (email IS NOT NULL) DESC, created_at ASC
           LIMIT 1
```

**Prioridade**: e-mail bate primeiro. Se houver match por email **E** outro contato bater por phone (mas não email), o email vence.

---

## Migrations SQL (esqueleto)

### `Add_Tickets_FullModel.sql` (modifica `tickets`)

```sql
-- Spec 009 — Tickets v2: expansão do scaffold Spec 005.
-- Idempotente, transacional, com data migration in-line.

BEGIN;

-- 1) Drop CHECK antigo
ALTER TABLE {TENANT_SCHEMA}.tickets DROP CONSTRAINT IF EXISTS tickets_status_check;

-- 2) Map status antigos → novos
UPDATE {TENANT_SCHEMA}.tickets SET status = CASE
    WHEN status = 'queued'   THEN 'new'
    WHEN status = 'assigned' THEN 'in_progress'
    WHEN status = 'open'     THEN 'in_progress'
    WHEN status = 'resolved' THEN 'resolved'
    WHEN status = 'closed'   THEN 'cancelled'
    ELSE status
END;

-- 3) Novo CHECK
ALTER TABLE {TENANT_SCHEMA}.tickets ADD CONSTRAINT tickets_status_check
    CHECK (status IN ('new','in_progress','waiting_client','resolved','cancelled'));

-- 4) Novas colunas
ALTER TABLE {TENANT_SCHEMA}.tickets
    ADD COLUMN IF NOT EXISTS protocol                       varchar(20),
    ADD COLUMN IF NOT EXISTS channel                        varchar(16)
        CHECK (channel IN ('live_chat','whatsapp','manual')) DEFAULT 'manual',
    ADD COLUMN IF NOT EXISTS priority                       varchar(8)
        CHECK (priority IN ('low','normal','high','urgent')) DEFAULT 'normal',
    ADD COLUMN IF NOT EXISTS conversation_id                uuid,
    ADD COLUMN IF NOT EXISTS contact_id                     uuid,
    ADD COLUMN IF NOT EXISTS tags                           text[] DEFAULT '{}',
    ADD COLUMN IF NOT EXISTS resolved_at                    timestamptz,
    ADD COLUMN IF NOT EXISTS cancelled_at                   timestamptz,
    ADD COLUMN IF NOT EXISTS first_response_at              timestamptz,
    ADD COLUMN IF NOT EXISTS sla_first_response_deadline    timestamptz,
    ADD COLUMN IF NOT EXISTS sla_resolution_deadline        timestamptz,
    ADD COLUMN IF NOT EXISTS sla_paused_duration_minutes    int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS waiting_client_since           timestamptz,
    ADD COLUMN IF NOT EXISTS has_reminder_alert             boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS deleted_at                     timestamptz,
    ADD COLUMN IF NOT EXISTS search_vector                  tsvector
        GENERATED ALWAYS AS (
            setweight(to_tsvector('portuguese', coalesce(protocol, '')), 'A') ||
            setweight(to_tsvector('portuguese', coalesce(subject, '')), 'B')
        ) STORED;

-- 5) Rename `assigned_attendant_id` → `attendant_id` (alinhar ao spec)
ALTER TABLE {TENANT_SCHEMA}.tickets RENAME COLUMN assigned_attendant_id TO attendant_id;
ALTER INDEX IF EXISTS {TENANT_SCHEMA}.idx_tickets_assigned_attendant_id
                  RENAME TO idx_tickets_attendant_id;

-- 6) Index novos
CREATE INDEX IF NOT EXISTS idx_tickets_contact_id ON {TENANT_SCHEMA}.tickets (contact_id);
CREATE INDEX IF NOT EXISTS idx_tickets_conversation_id ON {TENANT_SCHEMA}.tickets (conversation_id);
CREATE INDEX IF NOT EXISTS idx_tickets_created_at ON {TENANT_SCHEMA}.tickets (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_tickets_search_vector ON {TENANT_SCHEMA}.tickets USING GIN (search_vector);
CREATE INDEX IF NOT EXISTS idx_tickets_tags ON {TENANT_SCHEMA}.tickets USING GIN (tags);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tickets_protocol
    ON {TENANT_SCHEMA}.tickets (protocol) WHERE protocol IS NOT NULL;

-- 7) FKs adicionais (deferidas — adicionadas após Add_Contacts.sql rodar)
-- ALTER TABLE {TENANT_SCHEMA}.tickets ADD CONSTRAINT fk_tickets_contact
--     FOREIGN KEY (contact_id) REFERENCES {TENANT_SCHEMA}.contacts(id) ON DELETE SET NULL;

-- 8) Constraints semânticas
ALTER TABLE {TENANT_SCHEMA}.tickets
    ADD CONSTRAINT chk_tickets_resolved_at CHECK ((resolved_at IS NULL) = (status != 'resolved')),
    ADD CONSTRAINT chk_tickets_cancelled_at CHECK ((cancelled_at IS NULL) = (status != 'cancelled')),
    ADD CONSTRAINT chk_tickets_waiting_client_since CHECK ((waiting_client_since IS NULL) = (status != 'waiting_client'));

COMMIT;

-- NOTA: `protocol` permanece NULL temporariamente. `BackfillTicketProtocolJob`
-- preenche em batch logo após a migration. Em V1.1, adicionar:
-- ALTER TABLE {TENANT_SCHEMA}.tickets ALTER COLUMN protocol SET NOT NULL;
```

### `Add_Contacts.sql`

```sql
BEGIN;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.contacts (
    id                uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    name              varchar(255),
    email             varchar(255),
    phone             varchar(20),
    phone_normalized  varchar(20),
    notes             text,
    source_channels   text[]       NOT NULL DEFAULT '{}',
    created_at        timestamptz  NOT NULL DEFAULT now(),
    updated_at        timestamptz  NOT NULL DEFAULT now(),
    deleted_at        timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_contacts_email_lower
    ON {TENANT_SCHEMA}.contacts (lower(email))
    WHERE email IS NOT NULL AND deleted_at IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_contacts_phone_normalized
    ON {TENANT_SCHEMA}.contacts (phone_normalized)
    WHERE phone_normalized IS NOT NULL AND deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_contacts_email_lower
    ON {TENANT_SCHEMA}.contacts (lower(email));
CREATE INDEX IF NOT EXISTS idx_contacts_phone_normalized
    ON {TENANT_SCHEMA}.contacts (phone_normalized);

-- FK em tickets (deferida — agora que contacts existe)
ALTER TABLE {TENANT_SCHEMA}.tickets
    ADD CONSTRAINT fk_tickets_contact
    FOREIGN KEY (contact_id) REFERENCES {TENANT_SCHEMA}.contacts(id) ON DELETE SET NULL;

COMMIT;
```

### `Add_TicketNotes.sql`

```sql
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ticket_notes (
    id            uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    ticket_id     uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.tickets(id) ON DELETE CASCADE,
    attendant_id  uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.attendants(id) ON DELETE RESTRICT,
    content       text         NOT NULL CHECK (length(content) BETWEEN 1 AND 10000),
    created_at    timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_ticket_notes_ticket_id
    ON {TENANT_SCHEMA}.ticket_notes (ticket_id, created_at);
```

### `Add_Pipelines.sql`

```sql
BEGIN;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.pipelines (
    id             uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    department_id  uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.departments(id) ON DELETE CASCADE,
    name           varchar(100) NOT NULL DEFAULT 'Pipeline',
    created_at     timestamptz  NOT NULL DEFAULT now(),
    updated_at     timestamptz  NOT NULL DEFAULT now(),
    deleted_at     timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_pipelines_department_id
    ON {TENANT_SCHEMA}.pipelines (department_id)
    WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.pipeline_columns (
    id              uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    pipeline_id     uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.pipelines(id) ON DELETE CASCADE,
    name            varchar(100) NOT NULL,
    status_mapping  varchar(16)  NOT NULL
                    CHECK (status_mapping IN ('new','in_progress','waiting_client')),
    "order"         int          NOT NULL CHECK ("order" >= 1),
    color           varchar(7)   CHECK (color IS NULL OR color ~ '^#[0-9A-Fa-f]{6}$'),
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_pipeline_columns_pipeline_status
    ON {TENANT_SCHEMA}.pipeline_columns (pipeline_id, status_mapping);

-- Bootstrap: criar 1 pipeline por departamento existente, com 3 colunas default.
-- (Provisioning de novos depts via Spec 003 chamará PipelineProvisioningService.)
INSERT INTO {TENANT_SCHEMA}.pipelines (department_id, name)
SELECT id, 'Pipeline'
FROM {TENANT_SCHEMA}.departments
WHERE id NOT IN (SELECT department_id FROM {TENANT_SCHEMA}.pipelines WHERE deleted_at IS NULL)
ON CONFLICT DO NOTHING;

INSERT INTO {TENANT_SCHEMA}.pipeline_columns (pipeline_id, name, status_mapping, "order")
SELECT p.id, c.name, c.status_mapping, c.order
FROM {TENANT_SCHEMA}.pipelines p
CROSS JOIN (VALUES
    ('Na Fila',           'new',            1),
    ('Em Andamento',      'in_progress',    2),
    ('Aguardando Cliente','waiting_client', 3)
) AS c(name, status_mapping, order)
ON CONFLICT (pipeline_id, status_mapping) DO NOTHING;

COMMIT;
```

### `Add_ContactId_To_Visitors.sql`

```sql
ALTER TABLE {TENANT_SCHEMA}.visitors
    ADD COLUMN IF NOT EXISTS contact_id uuid REFERENCES {TENANT_SCHEMA}.contacts(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_visitors_contact_id
    ON {TENANT_SCHEMA}.visitors (contact_id);
```

### `Add_TicketId_To_Conversations.sql`

```sql
ALTER TABLE {TENANT_SCHEMA}.conversations
    ADD COLUMN IF NOT EXISTS ticket_id uuid REFERENCES {TENANT_SCHEMA}.tickets(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_conversations_ticket_id
    ON {TENANT_SCHEMA}.conversations (ticket_id);
```

### Ordem de execução (tasks)

1. `Add_Tickets_FullModel.sql` — rename + status migration + colunas novas (FK contact_id deferida).
2. `Add_Contacts.sql` — cria contacts + adiciona FK em tickets.
3. `Add_TicketNotes.sql`.
4. `Add_Pipelines.sql` — cria + bootstrap pipelines/columns para depts existentes.
5. `Add_ContactId_To_Visitors.sql`.
6. `Add_TicketId_To_Conversations.sql`.
7. **Job pós-migration**: `BackfillTicketProtocolJob` (gera protocols para tickets antigos).
8. **Job pós-migration**: `ContactBackfillJob` (cria contacts a partir de visitors identificados).

---

## Coluna `search_vector` em `conversation_messages` (Spec 007 — adendo)

Migration adicional **fora desta spec mas dependente**: adicionar coluna `content_tsv tsvector GENERATED ALWAYS AS (to_tsvector('portuguese', content)) STORED` em `conversation_messages` mais index GIN. Decisão coordenada com mantenedor da Spec 007.

Se a coluna não existir ao deploy da Spec 009, a busca por mensagens é graciosamente degradada (filtra apenas em `tickets.search_vector + contacts.name`). Falha não-bloqueante.

---

## Decisões de Domain Model (C#)

### TicketStatus enum (rewrite)

```csharp
public enum TicketStatus { New, InProgress, WaitingClient, Resolved, Cancelled }

public static class TicketStatusExtensions
{
    public const string New           = "new";
    public const string InProgress    = "in_progress";
    public const string WaitingClient = "waiting_client";
    public const string Resolved      = "resolved";
    public const string Cancelled     = "cancelled";

    public static string ToWireValue(this TicketStatus s) => s switch
    {
        TicketStatus.New           => New,
        TicketStatus.InProgress    => InProgress,
        TicketStatus.WaitingClient => WaitingClient,
        TicketStatus.Resolved      => Resolved,
        TicketStatus.Cancelled     => Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(s))
    };

    public static bool IsTerminal(this TicketStatus s) =>
        s == TicketStatus.Resolved || s == TicketStatus.Cancelled;

    public static bool IsActive(this TicketStatus s) =>
        s == TicketStatus.New || s == TicketStatus.InProgress || s == TicketStatus.WaitingClient;
}
```

### TicketPriority enum

```csharp
public enum TicketPriority { Low, Normal, High, Urgent }
public static class TicketPriorityExtensions
{
    public const string Low = "low", Normal = "normal", High = "high", Urgent = "urgent";
    public static string ToWireValue(this TicketPriority p) => ...;
}
```

### TicketChannel enum

```csharp
public enum TicketChannel { LiveChat, WhatsApp, Manual }
public static class TicketChannelExtensions
{
    public const string LiveChat = "live_chat", WhatsApp = "whatsapp", Manual = "manual";
    public static string ToWireValue(this TicketChannel c) => ...;
}
```

### TicketEventType const set

```csharp
public static class TicketEventType
{
    public const string TicketCreated       = "ticket_created";
    public const string AttendantAssigned   = "attendant_assigned";
    public const string StatusChanged       = "status_changed";
    public const string Transferred         = "transferred";
    public const string PriorityChanged     = "priority_changed";
    public const string SubjectChanged      = "subject_changed";
    public const string TagAdded            = "tag_added";
    public const string TagRemoved          = "tag_removed";
    public const string NoteAdded           = "note_added";
    public const string SlaBreached         = "sla_breached";
    public const string TicketResolved      = "ticket_resolved";
    public const string TicketCancelled     = "ticket_cancelled";
}
```

### PipelineDefaults

```csharp
public static class PipelineDefaults
{
    public const string PipelineName = "Pipeline";
    public static readonly (string Name, string StatusMapping, int Order)[] DefaultColumns =
    {
        ("Na Fila",           "new",            1),
        ("Em Andamento",      "in_progress",    2),
        ("Aguardando Cliente","waiting_client", 3)
    };
}
```

### TicketCrmEvents (WebSocket)

```csharp
public static class TicketCrmEvents
{
    public const string TicketCreated        = "ticket.created";
    public const string TicketAssigned       = "ticket.assigned";
    public const string TicketStatusChanged  = "ticket.status_changed";
    public const string TicketTransferred    = "ticket.transferred";
    public const string TicketSlaWarning     = "ticket.sla_warning";
    public const string TicketSlaBreached    = "ticket.sla_breached";
}
```

---

## Resumo de impacto

| Recurso | Antes (Spec 005/007) | Depois (Spec 009) |
|---|---|---|
| Tabela `tickets` | 11 colunas, 5 status antigos | 30+ colunas, 5 status novos, tsvector |
| Enum `TicketStatus` | Queued/Assigned/Open/Resolved/Closed | New/InProgress/WaitingClient/Resolved/Cancelled |
| Tabela `contacts` | — | Nova (10 colunas) |
| Tabela `ticket_notes` | — | Nova (5 colunas, append-only) |
| Tabela `pipelines` | — | Nova (6 colunas) + bootstrap |
| Tabela `pipeline_columns` | — | Nova (8 colunas, 3 rows/depto) |
| `visitors.contact_id` | — | Adicionada |
| `conversations.ticket_id` | comentado | Adicionada com FK |
| Mongo `ticket_events` | — | Nova collection |
| Sequence Postgres | `ticket_number_seq` | `ticket_number_seq` (preservada) + `ticket_protocol_seq_YYYYMMDD` (on-demand) |
| `StubTicketCreationGateway` | em uso | Substituído por `TicketCreationGateway` |
