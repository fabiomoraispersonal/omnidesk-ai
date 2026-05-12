# Data Model — Spec 008 WhatsApp

**Owner**: Phase 1 do `/speckit-plan`. Define entidades, migrations e transições de estado.

---

## 1. Entidades novas (PostgreSQL — schema `tenant_{slug}`)

### 1.1 `whatsapp_config` (1:1 com tenant)

| Coluna | Tipo PG | NULL | Default | Notas |
|---|---|---|---|---|
| `tenant_id` | uuid | not null | — | **PK natural** (1:1 com tenant). FK lógica → `public.tenants.id` (sem FK física pois cross-schema). |
| `is_enabled` | boolean | not null | `false` | Toggle do canal. |
| `phone_number` | varchar(20) | null | — | E.164 (`+5511999999999`). |
| `display_name` | varchar(100) | null | — | Nome WhatsApp Business. |
| `waba_id` | varchar(100) | null | — | WABA ID Meta. |
| `phone_number_id` | varchar(100) | null | — | Phone Number ID Meta (path do envio). |
| `access_token_ciphertext` | text | null | — | AES-256-GCM `(nonce, ct, tag)` base64. **Nunca em response API.** |
| `app_secret_ciphertext` | text | null | — | Mesmo formato. Usado para HMAC do webhook. **R4.** |
| `webhook_verify_token` | varchar(64) | not null | — | 32 bytes random base64. Imutável após gerado. |
| `business_hours_enabled` | boolean | not null | `false` | Reservado — efeito real depende da Spec 005 Departments. |
| `created_at` | timestamptz | not null | `now()` | — |
| `updated_at` | timestamptz | not null | `now()` | Touch a cada UPDATE. |
| `deleted_at` | timestamptz | null | — | Soft delete (Const §IV). |

**Constraints**:
- `PRIMARY KEY (tenant_id)` — garante 1:1.
- `CHECK (phone_number IS NULL OR phone_number ~ '^\+[1-9]\d{6,18}$')` — formato E.164 simples.
- Trigger `update_updated_at_column` (já existe no padrão do projeto).

**Provisionamento**: `TenantProvisioningJob` (Spec 003) MUST inserir linha com `is_enabled=false`, `webhook_verify_token = base64(RandomNumberGenerator.GetBytes(32))`, demais campos null.

---

### 1.2 `whatsapp_templates`

| Coluna | Tipo PG | NULL | Default | Notas |
|---|---|---|---|---|
| `id` | uuid | not null | `gen_random_uuid()` | PK. |
| `tenant_id` | uuid | not null | — | redundante para queries; FK lógica. |
| `meta_template_id` | varchar(100) | null | — | Preenchido após approval. |
| `type` | varchar(40) | not null | — | enum: `appointment_reminder`, `appointment_confirmation`, `appointment_cancellation`, `follow_up`, `custom`. |
| `name` | varchar(100) | not null | — | snake_case auto. Único por tenant. |
| `category` | varchar(20) | not null | `'utility'` | V1 fixo. |
| `language` | varchar(10) | not null | `'pt_BR'` | V1 fixo. |
| `status` | varchar(20) | not null | `'draft'` | enum: `draft`, `pending_meta`, `approved`, `rejected`. |
| `body_template` | text | not null | — | Texto com placeholders `{{1}}` etc. |
| `variable_labels` | text[] | not null | `'{}'` | Length deve casar com count de placeholders. |
| `rejection_reason` | text | null | — | Motivo Meta quando `rejected`. |
| `submitted_at` | timestamptz | null | — | Set ao mover para `pending_meta`. |
| `approved_at` | timestamptz | null | — | Set ao mover para `approved`. |
| `rejected_at` | timestamptz | null | — | Set ao mover para `rejected`. |
| `created_at` | timestamptz | not null | `now()` | — |
| `updated_at` | timestamptz | not null | `now()` | — |
| `deleted_at` | timestamptz | null | — | Soft delete. |

**Constraints**:
- `PRIMARY KEY (id)`.
- `UNIQUE (tenant_id, name) WHERE deleted_at IS NULL` — partial unique para permitir recriação após delete.
- `CHECK (type IN (...))`.
- `CHECK (status IN (...))`.
- `CHECK (category = 'utility')` — V1 fixo.
- `CHECK (language = 'pt_BR')` — V1 fixo.
- Index `idx_wa_templates_status` em `(status)` para listagens filtradas.

**State transitions**:

```
draft ──submit──▶ pending_meta ──webhook approved──▶ approved
                       │                              │
                       └──webhook rejected──▶ rejected┘
                                                │
                                                ▼
                                        (tenant deleta)
                                                │
                                                ▼
                                          deleted_at set
```

- `draft` → editável, deletável.
- `pending_meta` → **imutável** (sem update); transição automática via webhook ou poller.
- `approved` → **imutável** (sem update); permanece selecionável no envio.
- `rejected` → deletável (usuário recria como draft); apenas leitura.

Enforced por `Domain/WhatsApp/TemplateStateMachine.cs`:

```csharp
public static bool CanEdit(TemplateStatus s) => s == TemplateStatus.Draft;
public static bool CanDelete(TemplateStatus s) => s == TemplateStatus.Draft || s == TemplateStatus.Rejected;
public static bool CanSubmit(TemplateStatus s) => s == TemplateStatus.Draft;
```

---

## 2. Entidades modificadas

### 2.1 `conversations` (já existe — Spec 007)

**Adições** (migration `20260510_002_AddWaFieldsToConversations.sql`):

```sql
ALTER TABLE tenant_{slug}.conversations
    ADD COLUMN wa_contact_phone     varchar(20)  NULL,
    ADD COLUMN wa_session_expires_at timestamptz NULL;

CREATE INDEX idx_conversations_wa_session_expiring
    ON tenant_{slug}.conversations (wa_session_expires_at)
    WHERE channel = 'whatsapp' AND status = 'open';
```

**Notas**:
- Ambos NULL para conversas LiveChat existentes — **sem migração de dados**.
- Index parcial otimiza o sweep do `WaSessionExpiringNotifierJob` (50 conversas ativas/tenant em pico).
- `channel` enum existente (Spec 007) já contempla `whatsapp`.

---

### 2.2 `messages` (já existe — Spec 007)

**Sem alteração de schema**. Reuso completo:
- `sender_type` (`visitor`, `ai_agent`, `attendant`, `system`) — atendente humano cai em `attendant`; mensagens do cliente WhatsApp em `visitor`.
- `content_type` (`text`, `image`, `file`, `system_event`) — `audio` é mapeado para `file` com `metadata.media_type = "audio"`.
- `attachment_url` — preenchido pelo `WaMediaDownloadJob` após upload MinIO.
- `metadata` (jsonb) — ganha chaves novas para WhatsApp:
    - `wa_message_id` (string) — ID Meta para link 1:1.
    - `wa_attachment_status` (`pending|ready|failed`) — só em mensagens com mídia.
    - `wa_attachment_meta_id` (string) — ID Meta da mídia (uso interno do download job).
    - `wa_template_id` (uuid) — apenas quando mensagem de saída usa template.
    - `wa_template_variables` (object) — valores das variáveis preenchidas pelo atendente.

---

### 2.3 `visitors` (já existe — Spec 007)

**Sem alteração de schema**. Reuso:
- `wa_contact_phone` é a chave primária de identidade WhatsApp; quando o cliente envia 1ª mensagem, criamos `visitor` com `metadata.wa_phone = '+55...'` se não existir.
- Visitantes Live Chat e WhatsApp **podem** ser merged em V2 (vincular visitor por email/telefone) — V1 mantém visitantes separados.

---

## 3. Entidades MongoDB (auditoria)

### 3.1 `{tenant_slug}_wa_message_statuses`

```typescript
{
  _id: ObjectId,
  tenant_slug: string,        // redundante, facilita queries cross-collection
  message_id: UUID,           // FK lógica para tenant_{slug}.messages.id
  wa_message_id: string,      // ex: "wamid.HBgL..."
  conversation_id: UUID,      // facilita queries por conversa
  status: 'sent' | 'delivered' | 'read' | 'failed',
  error_code: string | null,  // ex: '131047' (re-engagement message)
  error_message: string | null,
  recipient_id: string | null, // wa_contact_phone (E.164)
  timestamp: Date,            // quando Meta gerou o status
  received_at: Date           // quando OmniDesk recebeu
}
```

**Indexes**:
- `{ message_id: 1, status: 1 }` — query por mensagem.
- `{ wa_message_id: 1 }` unique — dedupe.
- `{ tenant_slug: 1, received_at: -1 }` — auditoria por tenant.

**Retention**: sem TTL automático em V1 (auditoria perpétua); Spec 015 (Audit) define política de retenção por tenant.

---

### 3.2 `{tenant_slug}_wa_incidents` (auditoria de problemas)

```typescript
{
  _id: ObjectId,
  type: 'token_revoked' | 'webhook_signature_invalid' | 'unsupported_message' | 'media_download_failed',
  occurred_at: Date,
  tenant_slug: string,
  details: object  // estrutura varia por tipo
}
```

Usado em V1.1 (não bloqueia V1). Schema flexível.

---

### 3.3 `{tenant_slug}_wa_webhook_audit` (raw payloads — opcional V1.1)

Não bloqueia V1. Apenas escreve quando `WaWebhookProcessorJob` falha após retries. Estrutura: `{ payload, error, timestamp }`.

---

## 4. Redis keys

| Padrão | TTL | Uso |
|---|---|---|
| `{slug}:incoming_messages` | — (lista persistente) | Fila Hangfire (Spec 006). |
| `{slug}:outgoing_messages` | — | Fila Hangfire (Spec 006). |
| `wa_webhook_processing` (sem prefix) | — | Fila Hangfire **global** (não tenant-scoped) — payload contém `tenant_slug`. Justificativa: o webhook controller **não pode** entrar no schema antes do dedup, então a fila vive em `public`. |
| `{slug}:wa:dedup:{wa_message_id}` | 86400 (24h) | Dedup de mensagem inbound. |
| `{slug}:wa:expiring_emitted:{conversation_id}` | 3600 (1h) | Evita spam de `wa.session_expiring` no cron 5min. |
| `{slug}:wa:expired_emitted:{conversation_id}` | 86400 (24h) | Evita spam de `wa.session_expired`. |
| `{slug}:wa:rate:webhook` | 60 | Rate limit defensivo (default 600/min). |

**Justificativa para fila global `wa_webhook_processing`**: o webhook chega em `/api/public/whatsapp/webhook/{tenant_slug}` — o `tenant_slug` está no path, **mas a validação HMAC precisa do `app_secret`** que vive em `whatsapp_config`. Para manter o controller leve (≤ 100 ms), validamos HMAC inline (precisa carregar o config — uma query indexada, ~5ms) e, se válido, enfileiramos em fila global. O `WaWebhookProcessorJob` então re-resolve o tenant para escrita. Fila tenant-scoped seria possível mas não traria isolamento real (Hangfire compartilha workers).

Atualização: o controller faz **duas** queries antes do enqueue: (1) `tenant by slug` — public schema; (2) `whatsapp_config` para HMAC. Cache Redis 60s para esse par reduz custo: `{slug}:wa:config_cache` com `{tenant_id, app_secret_ciphertext, webhook_verify_token, is_enabled}`.

---

## 5. Migrations

### 5.1 `20260510_001_AddWhatsAppTables.sql` (tenant-scoped)

```sql
-- Aplicar em CADA schema tenant_{slug}.

-- 1. whatsapp_config
CREATE TABLE tenant_{slug}.whatsapp_config (
    tenant_id              uuid         PRIMARY KEY,
    is_enabled             boolean      NOT NULL DEFAULT false,
    phone_number           varchar(20)      NULL,
    display_name           varchar(100)     NULL,
    waba_id                varchar(100)     NULL,
    phone_number_id        varchar(100)     NULL,
    access_token_ciphertext text             NULL,
    app_secret_ciphertext  text             NULL,
    webhook_verify_token   varchar(64)  NOT NULL,
    business_hours_enabled boolean      NOT NULL DEFAULT false,
    created_at             timestamptz  NOT NULL DEFAULT now(),
    updated_at             timestamptz  NOT NULL DEFAULT now(),
    deleted_at             timestamptz      NULL,
    CONSTRAINT chk_wa_phone_format
        CHECK (phone_number IS NULL OR phone_number ~ '^\+[1-9]\d{6,18}$')
);

CREATE TRIGGER trg_whatsapp_config_updated
    BEFORE UPDATE ON tenant_{slug}.whatsapp_config
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- 2. whatsapp_templates
CREATE TABLE tenant_{slug}.whatsapp_templates (
    id                 uuid         PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id          uuid         NOT NULL,
    meta_template_id   varchar(100)     NULL,
    type               varchar(40)  NOT NULL,
    name               varchar(100) NOT NULL,
    category           varchar(20)  NOT NULL DEFAULT 'utility',
    language           varchar(10)  NOT NULL DEFAULT 'pt_BR',
    status             varchar(20)  NOT NULL DEFAULT 'draft',
    body_template      text         NOT NULL,
    variable_labels    text[]       NOT NULL DEFAULT '{}',
    rejection_reason   text             NULL,
    submitted_at       timestamptz      NULL,
    approved_at        timestamptz      NULL,
    rejected_at        timestamptz      NULL,
    created_at         timestamptz  NOT NULL DEFAULT now(),
    updated_at         timestamptz  NOT NULL DEFAULT now(),
    deleted_at         timestamptz      NULL,
    CONSTRAINT chk_wa_tpl_type    CHECK (type IN ('appointment_reminder','appointment_confirmation','appointment_cancellation','follow_up','custom')),
    CONSTRAINT chk_wa_tpl_status  CHECK (status IN ('draft','pending_meta','approved','rejected')),
    CONSTRAINT chk_wa_tpl_cat     CHECK (category = 'utility'),
    CONSTRAINT chk_wa_tpl_lang    CHECK (language = 'pt_BR')
);

CREATE UNIQUE INDEX uniq_wa_templates_tenant_name
    ON tenant_{slug}.whatsapp_templates (tenant_id, name)
    WHERE deleted_at IS NULL;

CREATE INDEX idx_wa_templates_status
    ON tenant_{slug}.whatsapp_templates (status)
    WHERE deleted_at IS NULL;

CREATE TRIGGER trg_whatsapp_templates_updated
    BEFORE UPDATE ON tenant_{slug}.whatsapp_templates
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
```

### 5.2 `20260510_002_AddWaFieldsToConversations.sql` (tenant-scoped)

```sql
ALTER TABLE tenant_{slug}.conversations
    ADD COLUMN wa_contact_phone      varchar(20)   NULL,
    ADD COLUMN wa_session_expires_at timestamptz   NULL;

ALTER TABLE tenant_{slug}.conversations
    ADD CONSTRAINT chk_conv_wa_phone_format
    CHECK (wa_contact_phone IS NULL OR wa_contact_phone ~ '^\+[1-9]\d{6,18}$');

CREATE INDEX idx_conversations_wa_session_expiring
    ON tenant_{slug}.conversations (wa_session_expires_at)
    WHERE channel = 'whatsapp' AND status = 'open';

CREATE INDEX idx_conversations_wa_contact_phone
    ON tenant_{slug}.conversations (wa_contact_phone)
    WHERE channel = 'whatsapp' AND wa_contact_phone IS NOT NULL;
```

### 5.3 Aplicação a todos os tenants existentes

Padrão do projeto: `Infrastructure/Migrations/MigrationRunner.cs` itera sobre `public.tenants` e aplica o template SQL em cada schema. Implementado pela Spec 003 — esta spec apenas adiciona os arquivos `.sql` no diretório consumido pelo runner.

---

## 6. Diagrama de relacionamento

```
public.tenants (existente)
   │
   │ 1:1 (logical)
   ▼
tenant_{slug}.whatsapp_config (novo)         tenant_{slug}.whatsapp_templates (novo)
                                                       │
                                                       │ N:M (logical via metadata)
                                                       ▼
tenant_{slug}.conversations (modificada: + wa_*) ◀── tenant_{slug}.messages (sem mudança schema; metadata.wa_* novos)
       ▲                                                      │
       │                                                      │
   tenant_{slug}.visitors (sem mudança)                       │
                                                              │
                                                              ▼
                                              MongoDB {slug}_wa_message_statuses (novo)
```

---

## 7. Validação cruzada com FRs do spec

| FR | Atendido por |
|---|---|
| FR-001 (config provisioning vazio + verify_token) | `whatsapp_config` 1:1 + `TenantProvisioningJob` modificado |
| FR-002 (RBAC tenant_admin write, supervisor read) | Policies R10 + endpoints `/api/whatsapp/config*` |
| FR-003 (AES-256 em access_token, never plain) | `access_token_ciphertext` + `AesGcmEncryptionService` (R3) |
| FR-004 (verify_token imutável) | Sem endpoint que atualiza; provisioning é único set |
| FR-005 (toggle is_enabled) | `PATCH /api/whatsapp/config/toggle` |
| FR-006 (HMAC validate) | `app_secret_ciphertext` + `MetaWebhookSignatureValidator` |
| FR-007 (200 OK ≤ 5s) | Async fila `wa_webhook_processing` (R2) |
| FR-008 (verify GET) | `WhatsAppWebhookEndpoints.VerifyAsync` |
| FR-009 (dedup) | Redis `{slug}:wa:dedup:{wa_message_id}` |
| FR-010 (tipos suportados/ignorados) | Switch no adapter + log auditoria |
| FR-011 (visitor + conversation create/reuse) | `WhatsAppIncomingAdapter` |
| FR-012 (`wa_session_expires_at = now()+24h`) | Update na conversation no incoming |
| FR-013 (mídia → MinIO) | `WaMediaDownloadJob` (R6) |
| FR-014 (janela check antes de envio) | `SessionWindowGuard` |
| FR-015 (envio Graph API + persist `wa_message_id`) | `WhatsAppMetaClient.SendAsync` + `messages.metadata.wa_message_id` |
| FR-016 (IA não envia template) | `WaOutgoingGuard` |
| FR-017 (janela expira durante IA → escala humano) | Trigger no `WaSessionExpiringNotifierJob` |
| FR-018 (failed → motivo no CRM) | MongoDB `wa_message_statuses.error_*` + WS event |
| FR-019 (status updates em Mongo) | `{slug}_wa_message_statuses` |
| FR-020 (WS `wa.message_status`) | `WhatsAppCrmEvents.WaMessageStatus` |
| FR-021 (read sem efeito além visual) | Sem trigger no domain — apenas Mongo + WS |
| FR-022 (WS `wa.session_expiring`) | `WaSessionExpiringNotifierJob` |
| FR-023 (WS `wa.session_expired`) | Mesmo job, branch < now() |
| FR-024 (5 tipos pré-definidos) | `PredefinedTemplates` static (R7) |
| FR-025 (estrutura fixa por tipo, custom livre) | `CreateTemplateValidator` |
| FR-026 (name auto snake_case + slug) | `TemplateNameGenerator.Generate(type, slug)` |
| FR-027 (RBAC templates) | Policies R10 |
| FR-028 (state machine) | `TemplateStateMachine` |
| FR-029 (submit Meta) | `WhatsAppMetaClient.SubmitTemplateAsync` |
| FR-030 (webhook approved/rejected) | `WaWebhookProcessorJob.HandleTemplateStatus` (R9) |
| FR-031 (somente approved selecionável) | Query filter `WHERE status = 'approved'` |
| FR-032 (1 número por tenant) | PK `whatsapp_config(tenant_id)` |
| FR-033 (escopo schema) | EF DbContext + `TenantResolverMiddleware` |
| FR-034 (access_token nunca em logs) | Serilog `Destructure.ByTransforming` |
