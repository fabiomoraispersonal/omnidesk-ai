# Phase 1 — Data Model: Departamentos e Atendentes

Esta spec introduz **5 tabelas** no schema `tenant_{slug}` e **2 coleções** no MongoDB. Reusa entidades existentes (`public.users` da Spec 002, `tenant_{slug}.tickets` e `conversations` da Spec 008 — ainda não implementada, esta spec apenas referencia FK).

---

## 1. Tabelas em `tenant_{slug}` (PostgreSQL)

### 1.1 `departments`

| Coluna | Tipo | Constraints | Descrição |
|---|---|---|---|
| `id` | `uuid` | PK, default `gen_random_uuid()` | — |
| `name` | `varchar(100)` | NOT NULL | Nome do departamento |
| `description` | `text` | NULL | Descrição interna |
| `business_hours_start` | `time` | NULL | Início do expediente (ex.: `08:00`) |
| `business_hours_end` | `time` | NULL | Fim do expediente (ex.: `18:00`) |
| `business_days` | `int[]` | NULL | Dias úteis: `0=Dom … 6=Sáb`. Ex.: `{1,2,3,4,5}` |
| `sla_first_response_minutes` | `int` | NULL, CHECK > 0 | Meta de SLA primeira resposta |
| `sla_resolution_minutes` | `int` | NULL, CHECK > 0 | Meta de SLA resolução |
| `is_active` | `boolean` | NOT NULL, DEFAULT `true` | Soft delete |
| `created_at` | `timestamptz` | NOT NULL, DEFAULT `now()` | — |
| `updated_at` | `timestamptz` | NOT NULL, DEFAULT `now()` | — |

**Índices**: `idx_departments_is_active` em `is_active` (filtro de listagem).

**Constraints de domínio (aplicação)**:

- `business_hours_start < business_hours_end` quando ambos definidos.
- Os três campos de horário (`start`, `end`, `days`) **devem** ser definidos juntos ou todos vazios; mistura é rejeitada pelo validator.

### 1.2 `attendants`

| Coluna | Tipo | Constraints | Descrição |
|---|---|---|---|
| `id` | `uuid` | PK, default `gen_random_uuid()` | — |
| `user_id` | `uuid` | NOT NULL, FK → `public.users(id)` ON DELETE RESTRICT | Vínculo com auth (Spec 002) |
| `name` | `varchar(255)` | NOT NULL | Nome de exibição |
| `avatar_url` | `varchar(500)` | NULL | Caminho relativo no bucket MinIO (ex.: `avatars/attendants/{id}/256x256.jpg`) |
| `max_simultaneous_chats` | `int` | NOT NULL, DEFAULT `5`, CHECK BETWEEN 1 AND 100 | — |
| `is_active` | `boolean` | NOT NULL, DEFAULT `true` | — |
| `created_at` | `timestamptz` | NOT NULL, DEFAULT `now()` | — |
| `updated_at` | `timestamptz` | NOT NULL, DEFAULT `now()` | — |

**Índices**:

- `uniq_attendants_user_id` UNIQUE em `user_id` (um atendente por usuário).
- `idx_attendants_is_active`.

### 1.3 `attendant_departments`

Relacionamento N:N. Um atendente pode estar em N departamentos; um departamento tem N atendentes.

| Coluna | Tipo | Constraints | Descrição |
|---|---|---|---|
| `attendant_id` | `uuid` | FK → `attendants(id)` ON DELETE CASCADE | — |
| `department_id` | `uuid` | FK → `departments(id)` ON DELETE RESTRICT | — |
| `is_primary` | `boolean` | NOT NULL, DEFAULT `false` | Marca o departamento principal do atendente |
| `created_at` | `timestamptz` | NOT NULL, DEFAULT `now()` | — |

**PK composta**: `(attendant_id, department_id)`.

**Constraint de aplicação**: cada `attendant_id` **deve** ter exatamente 1 vínculo com `is_primary=true` quando tem ≥ 1 departamento. O validator do `UpdateAttendant` força a atomicidade (transação que primeiro zera todos os `is_primary` do atendente, depois marca o escolhido).

### 1.4 `attendant_status`

Estado atual de presença por atendente (uma linha por atendente).

| Coluna | Tipo | Constraints | Descrição |
|---|---|---|---|
| `attendant_id` | `uuid` | PK, FK → `attendants(id)` ON DELETE CASCADE | — |
| `status` | `varchar(10)` | NOT NULL, CHECK IN (`online`, `away`, `offline`) | — |
| `changed_at` | `timestamptz` | NOT NULL, DEFAULT `now()` | Última transição |
| `changed_by` | `varchar(8)` | NOT NULL, CHECK IN (`manual`, `system`) | Origem da última mudança |
| `last_heartbeat_at` | `timestamptz` | NULL | Última interação no CRM (renovada via `PATCH /heartbeat`) |

**Índice**: `idx_attendant_status_status` em `status` (filtra elegíveis para distribuição).

> Read path em hot path **vai sempre ao Redis** (chave `{slug}:attendant_status:{attendant_id}`). Esta tabela é o registro de longo prazo para relatórios e fallback.

### 1.5 `canned_responses`

| Coluna | Tipo | Constraints | Descrição |
|---|---|---|---|
| `id` | `uuid` | PK, default `gen_random_uuid()` | — |
| `title` | `varchar(100)` | NOT NULL | Título para busca |
| `content` | `text` | NOT NULL | Conteúdo com variáveis `{{...}}` |
| `department_id` | `uuid` | NULL, FK → `departments(id)` ON DELETE CASCADE | NULL = global |
| `created_by` | `uuid` | NOT NULL, FK → `attendants(id)` ON DELETE RESTRICT | Autor |
| `created_at` | `timestamptz` | NOT NULL, DEFAULT `now()` | — |
| `updated_at` | `timestamptz` | NOT NULL, DEFAULT `now()` | — |

**Índices**:

- `idx_canned_responses_department_id` em `department_id`.
- `idx_canned_responses_title_trgm` GIN trigram em `title` (busca por título).

---

## 2. Coleções MongoDB

### 2.1 `{slug}_attendant_status_logs`

Cada transição de status gera um documento.

```json
{
  "_id": ObjectId,
  "attendant_id": "uuid",
  "attendant_name": "Maria",
  "from_status": "online",
  "to_status": "away",
  "changed_by": "manual",
  "timestamp": ISODate("2026-06-02T14:30:00Z"),
  "tenant_slug": "clinica-abc"
}
```

**Índices**:

- `{ attendant_id: 1, timestamp: -1 }` — histórico por atendente.
- `{ timestamp: -1 }` — auditoria geral.

**Retenção**: alinhada à política do tenant (Spec 011 — Auditoria). MVP: 90 dias.

### 2.2 `{slug}_ai_suggestion_logs`

Cada uso da feature de sugestão IA gera um documento.

```json
{
  "_id": ObjectId,
  "conversation_id": "uuid",
  "ticket_id": "uuid",
  "attendant_id": "uuid",
  "department_id": "uuid",
  "sub_agent_id": "uuid|null",
  "context_message_count": 12,
  "suggestion_text": "...",
  "human_action": "approved|edited|discarded|sent_unchanged",
  "human_action_at": ISODate("..."),
  "final_message_text": "..." | null,
  "model": "gpt-4o",
  "input_tokens": 320,
  "output_tokens": 84,
  "elapsed_ms": 1430,
  "timestamp": ISODate("..."),
  "tenant_slug": "clinica-abc"
}
```

**Índices**:

- `{ conversation_id: 1, timestamp: -1 }`
- `{ attendant_id: 1, timestamp: -1 }` — análise individual

**Retenção**: 180 dias.

---

## 3. Estado em Redis

| Chave | Tipo | TTL | Conteúdo |
|---|---|---|---|
| `{slug}:attendant_status:{attendant_id}` | string (JSON) | 5 min | `{ status, changed_at, changed_by, last_heartbeat_at }` |
| `{slug}:rr:{department_id}` | integer | 1 h | Cursor incremental para round-robin |
| `{slug}:ticket_lock:{ticket_id}` | string | 10 s | `attendant_id` que está tentando assumir |
| `{slug}:ws:tenant` | pub/sub channel | — | Eventos broadcast para tenant_admin/supervisor |
| `{slug}:ws:dept:{department_id}` | pub/sub channel | — | Eventos para o departamento |
| `{slug}:ws:attendant:{attendant_id}` | pub/sub channel | — | Eventos diretos ao atendente |

> **Princípio I**: cada chave é prefixada por `{slug}:` sem exceção. Validador estático em `Infrastructure/Authorization/RedisKeys.cs` enforça via constantes.

---

## 4. Estados e transições

### 4.1 Ciclo de vida do departamento

```
[Ativo] ──desativar──▶ [Inativo]
            ◀──reativar──
```

- `Ativo → Inativo`: aceito a qualquer momento; tickets em andamento permanecem com seus atendentes.
- `Inativo`: não aparece em listagens padrão; novos tickets são bloqueados.
- **Exclusão física**: bloqueada se houver `tickets` ou `attendant_departments` ainda referenciando.

### 4.2 Ciclo de vida do atendente

```
[Convidado (Spec 002)] ──aceita──▶ [Ativo] ──desativar──▶ [Inativo]
                                            ◀──reativar──
```

- Atendente desativado é removido das listas de elegíveis para distribuição imediatamente.
- Tickets ativos do atendente desativado são liberados para reatribuição (transferidos automaticamente para a fila do departamento).

### 4.3 Status de presença

```
        ┌────────── manual ───────────┐
        ▼                             │
   [offline] ──manual──▶ [online] ─manual──▶ [away] ──manual──▶ [offline]
                            │                    │
                            └─── 15 min sem ─────┘
                                heartbeat
                            (changed_by=system)
                                                 └─── 30 min ──▶ [offline]
                                                  (changed_by=system)
```

- Toda transição **sempre** grava em `attendant_status_logs`.
- Toda transição **sempre** publica evento `attendant.status_changed` no WebSocket.

---

## 5. Validações e regras de domínio

| Regra | Implementação | FR/SC |
|---|---|---|
| Departamento sem horário = 24/7 | `BusinessHoursEvaluator.IsAvailable(now)` retorna `true` quando `business_hours_*` é null | FR-002 |
| Departamento com tickets vinculados não é deletável | Validator chama `IDepartmentRepository.HasTicketsAsync(id)` | FR-003 |
| `is_primary` único por atendente | Transação UPDATE em `attendant_departments` | (premissa) |
| `max_simultaneous_chats` respeitado | `TicketAssignmentService.IsEligibleAsync` filtra | FR-018, SC-002 |
| Round-robin justo | Cursor `INCR mod len(eligible)` | SC-003 |
| Lock atômico | `SET NX EX 10` em `{slug}:ticket_lock:{ticket_id}` | FR-016, SC-002 |
| Heartbeat → away após 15 min sem atividade | Hangfire recurring 1 min | FR-008, SC-005 |
| Away → offline após 30 min | Mesmo job | FR-009 |
| Substituição de variáveis sem placeholder literal | Regex puro + fallback | FR-033, FR-034, SC-006 |
| Sugestão IA nunca envia sem ação humana | Endpoint só retorna texto; mensagem real só ao atendente clicar "Enviar" | FR-038, SC-007 |
| SLA pausado fora do horário | `BusinessHoursEvaluator.ElapsedBusinessMinutes(start, now, hours)` | FR-043, SC-008 |
| Transferência entre depts recalcula SLA | `TicketTransferCommand` reseta `sla_started_at` no momento da transferência | FR-026 |

---

## 6. Diagrama relacional simplificado

```
public.users (Spec 002)
   │ id (uuid, PK)
   │
   └─────────┐
             ▼
tenant_{slug}.attendants (esta spec)
   │ id (uuid, PK)
   │ user_id (uuid, FK)
   │ ...
   ├──► attendant_departments (N:N)
   │      │ attendant_id, department_id, is_primary
   │      ▼
   │   tenant_{slug}.departments
   │      │ id (uuid, PK)
   │      │ business_hours_*, sla_*, is_active
   │      ▼
   │   tenant_{slug}.canned_responses
   │      │ department_id (FK NULL=global), created_by → attendants.id
   │
   └──► attendant_status (1:1)
          │ status, changed_at, changed_by, last_heartbeat_at
```

Cross-spec (referências sem FK enforced no schema, pois entidades pertencem a outras specs):

```
attendants ──(soft FK)──▶ tickets, conversations  (Spec 008 — Tickets)
canned_responses ──(consumido por)──▶ chat box (Spec 008 — Tickets)
SuggestReply ──(consome)──▶ sub-agents (Spec 002 — Agentes de IA)
```

---

## 7. Considerações sobre dados sensíveis

- **Avatares** ficam atrás de URL assinada de 7 dias (não são públicos). Refresh automático no frontend.
- **Logs de status** (Mongo) **não** carregam mensagens de conversas — apenas metadata de presença.
- **Logs de sugestão IA** carregam `suggestion_text` e `final_message_text`. Ambos podem conter dados do cliente (nomes, contexto). Retenção 180 dias e bucket lógico por tenant atendem LGPD; export/exclusão é via Spec 011.
- **Heartbeat** não persiste IP nem user-agent — apenas timestamp.
