# Data Model — Spec 007 Live Chat (Widget)

Tabelas, relações, enums e migrations. Toda tabela de feature vive em `tenant_{slug}.*`. Uma única coluna em `public.tenants` é adicionada (justificativa em research R2).

---

## 1. Schema visual

```text
public.tenants                                tenant_{slug}.widget_config
┌─────────────────────────┐                   ┌───────────────────────────────┐
│ id           UUID PK    │ ◀──── 1:1 ───────│ tenant_id      UUID PK        │
│ widget_token UUID UNIQ  │                   │ widget_token   UUID (cache?)  │ ← redundância removida; vive só em public.tenants
│ ...                     │                   │ is_enabled     BOOL           │
└─────────────────────────┘                   │ primary_color  VARCHAR(7)     │
                                              │ launcher_icon  ENUM           │
                                              │ company_name   VARCHAR(100)   │
                                              │ welcome_message TEXT          │
                                              │ input_placeholder VARCHAR(150)│
                                              │ position       ENUM           │
                                              │ require_identification BOOL   │
                                              │ identification_fields JSONB   │
                                              │ allowed_domains TEXT[]        │
                                              │ privacy_policy_text TEXT      │
                                              │ privacy_policy_url VARCHAR(500)│
                                              │ abandonment_timeout_hours INT │
                                              │ inactivity_close_hours INT    │
                                              │ updated_at TIMESTAMPTZ        │
                                              └───────────────────────────────┘

tenant_{slug}.visitors                        tenant_{slug}.conversations
┌──────────────────────────┐                  ┌────────────────────────────────────┐
│ id           UUID PK     │ ◀──── 1:N ──────│ id              UUID PK            │
│ anonymous_id UUID UNIQ   │                  │ visitor_id      UUID FK            │
│ name         VARCHAR(255)│                  │ contact_id      UUID FK NULL       │ → contacts (Spec 008/futuro)
│ email        VARCHAR(255)│                  │ channel         ENUM               │
│ phone        VARCHAR(20) │                  │ status          ENUM               │
│ created_at   TIMESTAMPTZ │                  │ agent_id        UUID FK NULL       │ → ai_agents (Spec 006)
└──────────────────────────┘                  │ attendant_id    UUID FK NULL       │ → attendants (Spec 005)
                                              │ department_id   UUID FK NULL       │ → departments (Spec 005)
                                              │ ticket_id       UUID FK NULL       │ → tickets (Spec 008+)
                                              │ openai_thread_id VARCHAR(64) NULL  │ herda da Spec 006
                                              │ lgpd_consent_at TIMESTAMPTZ NULL   │
                                              │ ended_by        ENUM NULL          │
                                              │ ended_at        TIMESTAMPTZ NULL   │
                                              │ metadata        JSONB              │
                                              │ last_message_at TIMESTAMPTZ        │ ← coluna materializada (R9)
                                              │ created_at      TIMESTAMPTZ        │
                                              │ updated_at      TIMESTAMPTZ        │
                                              └────────────────────────────────────┘
                                                          │
                                                          │ 1:N
                                                          ▼
                                              tenant_{slug}.messages
                                              ┌────────────────────────────────────┐
                                              │ id              UUID PK            │
                                              │ conversation_id UUID FK            │
                                              │ sender_type     ENUM               │
                                              │ sender_id       UUID NULL          │
                                              │ client_message_id UUID NULL        │ idempotência (R3)
                                              │ content_type    ENUM               │
                                              │ content         TEXT NULL          │
                                              │ attachment_url  VARCHAR(500) NULL  │
                                              │ attachment_name VARCHAR(255) NULL  │
                                              │ attachment_size_bytes INT NULL     │
                                              │ is_read         BOOL DEFAULT FALSE │
                                              │ created_at      TIMESTAMPTZ        │
                                              └────────────────────────────────────┘
```

---

## 2. Enums (Postgres `CHECK` ou `CREATE TYPE`)

| Enum | Valores | Onde |
|---|---|---|
| `channel_type` | `live_chat`, `whatsapp` | `conversations.channel` |
| `conversation_status` | `open`, `resolved`, `abandoned` | `conversations.status` |
| `ended_by_type` | `attendant`, `ai_agent`, `system_inactivity`, `system_disable` | `conversations.ended_by` |
| `message_sender_type` | `visitor`, `ai_agent`, `attendant`, `system` | `messages.sender_type` |
| `message_content_type` | `text`, `image`, `file`, `system_event` | `messages.content_type` |
| `widget_launcher_icon` | `chat`, `message`, `support` | `widget_config.launcher_icon` |
| `widget_position` | `bottom_right`, `bottom_left` | `widget_config.position` |

> **Estratégia de enum**: usar `VARCHAR(N) CHECK (value IN (...))` em vez de `CREATE TYPE` para facilitar migrations futuras (adicionar valores não exige `ALTER TYPE`).

---

## 3. Migrations SQL

### 3.1 `20260509_001_AddLiveChatTables.sql` (tenant-scoped)

Roda **uma vez por tenant** via `TenantProvisioningJob` ou via `EnsureTenantSchemaUpToDateAsync` no startup (padrão estabelecido pela Spec 005).

```sql
-- ================================================================
-- Spec 007 — Live Chat (Widget) — tenant-scoped
-- ================================================================
SET search_path TO :tenant_schema;

-- 3.1.1 widget_config (1:1 tenant)
CREATE TABLE widget_config (
    tenant_id                 UUID         PRIMARY KEY,
    is_enabled                BOOLEAN      NOT NULL DEFAULT TRUE,
    primary_color             VARCHAR(7)   NOT NULL DEFAULT '#2563EB',
    launcher_icon             VARCHAR(16)  NOT NULL DEFAULT 'chat'
                              CHECK (launcher_icon IN ('chat','message','support')),
    company_name              VARCHAR(100) NOT NULL DEFAULT 'Atendimento',
    welcome_message           TEXT         NOT NULL DEFAULT 'Olá! Como posso ajudar?',
    input_placeholder         VARCHAR(150) NULL,
    position                  VARCHAR(16)  NOT NULL DEFAULT 'bottom_right'
                              CHECK (position IN ('bottom_right','bottom_left')),
    require_identification    BOOLEAN      NOT NULL DEFAULT FALSE,
    identification_fields     JSONB        NULL,
    allowed_domains           TEXT[]       NULL,
    privacy_policy_text       TEXT         NULL,           -- nullable; FR-020 trata vazio
    privacy_policy_url        VARCHAR(500) NULL,
    abandonment_timeout_hours INT          NOT NULL DEFAULT 8  CHECK (abandonment_timeout_hours BETWEEN 1 AND 168),
    inactivity_close_hours    INT          NOT NULL DEFAULT 24 CHECK (inactivity_close_hours BETWEEN 1 AND 168),
    updated_at                TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- 3.1.2 visitors
CREATE TABLE visitors (
    id           UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    anonymous_id UUID         NOT NULL UNIQUE,
    name         VARCHAR(255) NULL,
    email        VARCHAR(255) NULL,
    phone        VARCHAR(20)  NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_visitors_anonymous_id ON visitors (anonymous_id);

-- 3.1.3 conversations
CREATE TABLE conversations (
    id                UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    visitor_id        UUID         NOT NULL REFERENCES visitors(id),
    contact_id        UUID         NULL,                     -- FK lógica para contacts (Spec 008+)
    channel           VARCHAR(16)  NOT NULL CHECK (channel IN ('live_chat','whatsapp')),
    status            VARCHAR(16)  NOT NULL DEFAULT 'open'
                      CHECK (status IN ('open','resolved','abandoned')),
    agent_id          UUID         NULL,                     -- FK lógica → ai_agents (Spec 006)
    attendant_id      UUID         NULL,                     -- FK lógica → attendants (Spec 005)
    department_id     UUID         NULL,                     -- FK lógica → departments (Spec 005)
    ticket_id         UUID         NULL,                     -- FK lógica → tickets (Spec 008+)
    openai_thread_id  VARCHAR(64)  NULL,                     -- 1:1 com OpenAI Thread (Spec 006)
    lgpd_consent_at   TIMESTAMPTZ  NULL,
    ended_by          VARCHAR(24)  NULL
                      CHECK (ended_by IN ('attendant','ai_agent','system_inactivity','system_disable')),
    ended_at          TIMESTAMPTZ  NULL,
    metadata          JSONB        NULL,
    last_message_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_conversations_visitor_id        ON conversations (visitor_id);
CREATE INDEX ix_conversations_status_channel    ON conversations (status, channel);
CREATE INDEX ix_conversations_attendant_id      ON conversations (attendant_id) WHERE attendant_id IS NOT NULL;
CREATE INDEX ix_conversations_department_id     ON conversations (department_id) WHERE department_id IS NOT NULL;
CREATE INDEX ix_conversations_open_idle         ON conversations (status, last_message_at) WHERE status = 'open';
CREATE INDEX ix_conversations_openai_thread     ON conversations (openai_thread_id) WHERE openai_thread_id IS NOT NULL;

-- 3.1.4 messages
CREATE TABLE messages (
    id                    UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id       UUID         NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
    sender_type           VARCHAR(16)  NOT NULL
                          CHECK (sender_type IN ('visitor','ai_agent','attendant','system')),
    sender_id             UUID         NULL,
    client_message_id     UUID         NULL,
    content_type          VARCHAR(16)  NOT NULL
                          CHECK (content_type IN ('text','image','file','system_event')),
    content               TEXT         NULL,
    attachment_url        VARCHAR(500) NULL,
    attachment_name       VARCHAR(255) NULL,
    attachment_size_bytes INT          NULL,
    is_read               BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_messages_conversation_id_created ON messages (conversation_id, created_at);
CREATE UNIQUE INDEX uq_messages_idempotency
       ON messages (conversation_id, client_message_id)
       WHERE client_message_id IS NOT NULL;

-- 3.1.5 trigger para `last_message_at` materializado
CREATE OR REPLACE FUNCTION trg_update_last_message_at() RETURNS TRIGGER AS $$
BEGIN
    UPDATE conversations
       SET last_message_at = NEW.created_at,
           updated_at      = NOW()
     WHERE id = NEW.conversation_id;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_messages_after_insert
AFTER INSERT ON messages
FOR EACH ROW EXECUTE FUNCTION trg_update_last_message_at();
```

### 3.2 `20260509_002_AddWidgetTokenToTenants.sql` (public)

Roda **uma vez por instância** (não por tenant).

```sql
-- ================================================================
-- Spec 007 — adiciona widget_token em public.tenants
-- ================================================================
ALTER TABLE public.tenants
    ADD COLUMN widget_token UUID UNIQUE NOT NULL DEFAULT gen_random_uuid();

CREATE INDEX ix_tenants_widget_token ON public.tenants (widget_token);

-- Backfill: tenants existentes recebem token gerado pelo DEFAULT.
-- Tornar DEFAULT explícito não-volátil para próximos provisionamentos.
ALTER TABLE public.tenants
    ALTER COLUMN widget_token DROP DEFAULT;
```

> **Backfill seguro**: o `DEFAULT gen_random_uuid()` durante o `ADD COLUMN` gera um UUID por linha existente. Após isso, removemos o default — provisionamentos futuros geram explicitamente via `TenantProvisioningJob`.

### 3.3 Provisionamento (extensão da Spec 003)

`TenantProvisioningJob` ganha 2 etapas:

1. Geração e persistência do `widget_token` (já feito pelo default da migration; após remoção do default, fica explicit em `Tenant` entity).
2. `INSERT INTO tenant_{slug}.widget_config (tenant_id) VALUES (@tenantId)` — todos os defaults da migration cobrem o resto.

---

## 4. State transitions

### 4.1 `conversations.status`

```
                ┌─────────┐
   create  ────▶│  open   │
                └─────────┘
                  │   │   │
   IA encerra ───┘   │   └─── timeout 8h IA → abandoned
   atendente encerra┘
   sistema desabilita widget
   sistema inatividade 24h humano
                  │
                  ▼
              ┌─────────┐                ┌──────────┐
              │resolved │                │abandoned │
              └─────────┘                └──────────┘

Transições válidas:
  open → resolved      (ended_by ∈ {attendant, ai_agent, system_inactivity, system_disable})
  open → abandoned     (timeout 8h, sem ended_by)

Transições proibidas:
  resolved → open      ❌  (visitante inicia nova conversa, não reabre)
  abandoned → open     ❌  (idem)
  abandoned → resolved ❌
```

### 4.2 `conversations.attendant_id`

- `NULL` → atendente assigned (após `transfer_to_human` da Spec 006). Transição irreversível na mesma conversa.
- Não-NULL → trocar de atendente: cobertura via Spec 008 (transferência interna).
- A partir do momento em que `attendant_id IS NOT NULL`, mensagens do visitante NÃO são processadas pela IA (regra herdada da Spec 006).

### 4.3 `messages` — imutável após insert

- INSERT permitido.
- UPDATE permitido **apenas** em `is_read` (audit trail).
- DELETE proibido em produção (soft delete em conversa cobre o caso). Constraint via convenção do repositório (não SQL).

---

## 5. Validation rules

| Campo | Regra | Onde valida |
|---|---|---|
| `widget_config.primary_color` | regex `^#[0-9A-Fa-f]{6}$` | FluentValidation `UpdateWidgetConfigValidator` |
| `widget_config.allowed_domains[]` | cada item é hostname válido (regex sem schema/path) | FluentValidation |
| `widget_config.identification_fields` | JSONB schema: array de `{field: 'name'\|'email'\|'phone', label: string≤50, required: bool}`. Sem duplicatas. | FluentValidation custom rule |
| `widget_config.privacy_policy_url` | URL absoluta `https://...` | FluentValidation |
| `widget_config.abandonment_timeout_hours` / `inactivity_close_hours` | 1..168 | CHECK + FluentValidation |
| `conversations.lgpd_consent_at` | NOT NULL ao processar primeira mensagem | Application service (defesa em profundidade) |
| `messages.content` | NOT NULL para `content_type IN ('text','system_event')` | CHECK condicional + validator |
| `messages.attachment_*` | NOT NULL para `content_type IN ('image','file')` | idem |
| `messages.attachment_size_bytes` | ≤ `WIDGET_MAX_UPLOAD_BYTES` | application service no upload |

---

## 6. JSONB shapes

### 6.1 `widget_config.identification_fields`

```json
[
  { "field": "name",  "label": "Seu nome",   "required": true },
  { "field": "email", "label": "Seu e-mail", "required": false },
  { "field": "phone", "label": "Telefone",   "required": false }
]
```

### 6.2 `conversations.metadata`

```json
{
  "page_url":   "https://www.clinica-abc.com.br/agendamento",
  "page_title": "Agendamento online",
  "referrer":   "https://www.google.com/",
  "user_agent": "Mozilla/5.0 (Macintosh; ...) Chrome/...",
  "ip_partial": "201.10.45.0"
}
```

> `ip_partial` armazena os 3 primeiros octetos IPv4 com `.0` no quarto, ou prefixo `/48` em IPv6 (`2804:abcd:1234::`).

---

## 7. FK lógicas (cross-schema)

A Constituição §I proíbe FKs físicas cross-schema. Para FKs lógicas (`agent_id`, `attendant_id`, `department_id`, `ticket_id`), a integridade referencial fica a cargo da camada de aplicação:

- `agent_id` → existe em `tenant_{slug}.ai_agents` no mesmo schema; FK física **opcional** (mesmo schema permite).
- `attendant_id`, `department_id` → existem em `tenant_{slug}.attendants` / `departments` (Spec 005, mesmo schema). FK física **OK**.
- `contact_id`, `ticket_id` → Specs 008+. FK física quando essas tabelas chegarem ao mesmo schema; até lá, FK lógica.
- `visitor_id` → mesmo schema. FK física obrigatória (já no DDL).

> Decisão prática: ativar FK física para `visitor_id` desde V1; demais ficam lógicas até as specs respectivas adicionarem migrations.

---

## 8. Migration de transição (Spec 006 → 007)

A coluna `openai_thread_id` em `conversations` (esta spec) **substitui** o uso de `tenant_{slug}.ai_threads.openai_thread_id` da Spec 006. Estratégia:

1. Esta migration cria `conversations` com `openai_thread_id NULL`.
2. `LiveChatConversationGateway.GetOrCreateThreadAsync(...)` passa a operar sobre `conversations` em vez de `ai_threads`.
3. `ai_threads` permanece no schema durante V1 (sem dropar) — pode receber inserções residuais até a transição completa em produção (controlada via flag de configuração `LiveChat:UseConversationsAsThread = true`).
4. Migration de drop de `ai_threads` é deferida para spec posterior, após confirmação de que não há referências.

---

## 9. Capacidade e performance

| Métrica | Estimativa V1 | Suporte do schema |
|---|---|---|
| Conversas/tenant/dia | 100 | índice `(status, last_message_at)` parcial cobre sweep jobs em < 100 ms |
| Mensagens/tenant/dia | 5.000 | índice `(conversation_id, created_at)` cobre paginação reverse-chronological |
| Lookup por `widget_token` | 30 req/s/tenant (rate limit) | índice único em `public.tenants.widget_token` — O(1) |
| Busca de visitor por `anonymous_id` | 30 req/s/tenant | índice único em `tenant_{slug}.visitors.anonymous_id` |
| Sweep job (abandonment + inactivity) | @hourly | índice parcial `WHERE status='open'` mantém o scan em centenas de linhas |

---

## 10. Backfill / migração de dados

- Tenants existentes (5 já provisionados): rodar `EnsureTenantSchemaUpToDateAsync` em startup → cria as 4 tabelas e popula `widget_config` com defaults.
- `widget_token` em `public.tenants` é populado pelo `DEFAULT gen_random_uuid()` durante o `ADD COLUMN` (3.2). Após a migration, o default é removido.
- Nenhuma data existente precisa ser migrada (não há conversas web pré-existentes).
