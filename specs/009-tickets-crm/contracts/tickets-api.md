# Contract — Tickets API (CRM)

Endpoints REST autenticados. Todos exigem JWT Bearer + tenant resolvido pelo `TenantResolverMiddleware`. Visibilidade:

- `tenant_attendant` — apenas tickets dos seus departamentos.
- `supervisor`, `tenant_admin` — todos os tickets do tenant.

Envelope padrão (Spec 001):

```json
{ "success": true, "data": ... , "meta": { "page": 1, "per_page": 20, "total": 152 } }
{ "success": false, "error": { "code": "TICKET_NOT_FOUND", "message": "..." } }
```

---

## `GET /api/tickets`

Lista paginada com filtros. Em modo busca (`?q=...`), retorna lista (não Kanban-shaped).

### Query string

| Parâmetro | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `page` | int | ❌ (default 1) | — |
| `per_page` | int | ❌ (default 20, max 100) | — |
| `sort` | string | ❌ (default `created_at`) | `created_at` / `updated_at` / `sla_resolution_deadline` |
| `order` | string | ❌ (default `desc`) | `asc` / `desc` |
| `department_id` | uuid | ❌ | Filtro |
| `attendant_id` | uuid or `null` | ❌ | `null` = "sem atendente" |
| `channel` | enum | ❌ | `live_chat` / `whatsapp` / `manual` |
| `priority` | enum | ❌ | `low` / `normal` / `high` / `urgent` |
| `status` | enum | ❌ | Restringe a um status específico (default = todos active = new/in_progress/waiting_client) |
| `include_terminal` | bool | ❌ (default false) | Inclui `resolved` e `cancelled` |
| `tag` | string (multi) | ❌ | `?tag=lead&tag=vip` |
| `created_from` | datetime | ❌ | Período personalizado |
| `created_to` | datetime | ❌ | — |
| `period` | enum | ❌ | `today` / `this_week` / `this_month` (atalho) |
| `q` | string | ❌ | Busca full-text — quando preenchido, ignora filtros de status e usa modo lista |

### Response

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "protocol": "TK-20260511-00042",
      "channel": "live_chat",
      "status": "in_progress",
      "priority": "normal",
      "subject": "Cliente quer remarcar agendamento de sábado",
      "department": { "id": "uuid", "name": "Comercial" },
      "attendant": { "id": "uuid", "name": "Maria Silva" },
      "contact": { "id": "uuid", "name": "João Silva", "email": "joao@email.com" },
      "tags": ["agendamento", "vip"],
      "sla": {
        "first_response_deadline": "2026-05-11T14:00:00Z",
        "resolution_deadline_effective": "2026-05-11T18:00:00Z",
        "first_response_at": "2026-05-11T13:45:00Z",
        "paused_minutes": 0,
        "status": "ok" | "warning" | "breached"
      },
      "has_reminder_alert": false,
      "created_at": "2026-05-11T13:30:00Z",
      "updated_at": "2026-05-11T13:45:00Z"
    }
  ],
  "meta": { "page": 1, "per_page": 20, "total": 152 }
}
```

### Erros

- `403 FORBIDDEN_DEPARTMENT` — atendente tentou filtrar por depto fora do seu escopo.

---

## `GET /api/tickets/{id}`

Detalhe completo. Inclui histórico de conversa (se houver), notas internas e dados do contato.

### Response

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "protocol": "TK-20260511-00042",
    "channel": "live_chat",
    "status": "in_progress",
    "priority": "normal",
    "subject": "...",
    "department": {...},
    "attendant": {...},
    "contact": {
      "id": "uuid",
      "name": "João Silva",
      "email": "joao@email.com",
      "phone": "(11) 99999-9999",
      "notes": "Cliente VIP",
      "source_channels": ["live_chat", "whatsapp"]
    },
    "conversation": {
      "id": "uuid",
      "messages": [
        {
          "id": "uuid",
          "sender_type": "visitor" | "ai_agent" | "attendant" | "system",
          "sender_id": "uuid?",
          "sender_name": "string?",
          "content": "...",
          "attachment_url": "string?",
          "sent_at": "2026-05-11T13:30:00Z"
        }
      ]
    },
    "notes": [
      {
        "id": "uuid",
        "attendant_id": "uuid",
        "attendant_name": "Maria",
        "content": "Cliente já solicitou desconto antes.",
        "created_at": "2026-05-11T13:35:00Z"
      }
    ],
    "tags": ["agendamento", "vip"],
    "sla": {
      "first_response": {
        "deadline": "2026-05-11T14:00:00Z",
        "first_response_at": "2026-05-11T13:45:00Z",
        "status": "ok",
        "percent_consumed": 60
      },
      "resolution": {
        "deadline_effective": "2026-05-11T18:00:00Z",
        "paused_minutes": 0,
        "status": "ok",
        "percent_consumed": 25
      }
    },
    "has_reminder_alert": false,
    "created_at": "2026-05-11T13:30:00Z",
    "updated_at": "2026-05-11T13:45:00Z",
    "resolved_at": null,
    "cancelled_at": null
  }
}
```

### Erros

- `404 TICKET_NOT_FOUND`.
- `403 FORBIDDEN_DEPARTMENT`.

---

## `POST /api/tickets`

Cria ticket **manualmente** (atendente). Para criação por transbordo da IA, ver `ticket-creation-gateway.md` (interno).

### Request

```json
{
  "department_id": "uuid",
  "subject": "Atendimento por telefone — solicitação de orçamento",
  "priority": "normal",
  "tags": ["telefone"],
  "contact": {
    "id": "uuid?",
    "name": "João Silva",
    "email": "joao@email.com?",
    "phone": "(11) 99999-9999?"
  },
  "conversation_id": "uuid?",
  "assign_to_me": true
}
```

- Se `contact.id` fornecido: reutiliza contato existente (valida permissão).
- Se `contact.id` omitido: aplica deduplicação por e-mail/telefone; cria se não houver match.
- Se `assign_to_me = true`: atribui ao atendente autenticado (vira `in_progress` direto).
- Se `assign_to_me = false` (default): aplica round-robin (Spec 005) ou fica na fila.
- Se `conversation_id` fornecido: vincula à conversa existente (validar tenant scope); senão, `channel = manual`.

### Response

`201 Created` com o ticket criado (shape do GET /{id}).

### Erros

- `400 VALIDATION_ERROR` — campos inválidos.
- `403 FORBIDDEN_DEPARTMENT` — atendente fora do depto escolhido.
- `404 CONTACT_NOT_FOUND` / `CONVERSATION_NOT_FOUND`.
- `409 CONTACT_ALREADY_LINKED_TO_OTHER` — conflito de identidade ao mergear (V1.1).

---

## `PUT /api/tickets/{id}`

Edita campos editáveis: `subject`, `priority`, `tags`.

### Request

```json
{ "subject": "...", "priority": "high", "tags": ["lead", "novo"] }
```

### Response

`200 OK` com ticket atualizado.

### Erros

- `404 TICKET_NOT_FOUND`.
- `403 FORBIDDEN_DEPARTMENT`.
- `409 TICKET_ALREADY_CLOSED` — ticket em `resolved`/`cancelled`.

### Side-effects

- `subject` mudou → evento Mongo `subject_changed`.
- `priority` mudou → evento Mongo `priority_changed`.
- `tags` mudou → eventos Mongo `tag_added`/`tag_removed` por tag alterada.

---

## `PATCH /api/tickets/{id}/status`

Muda status do ticket. Apenas transições válidas (ver data-model.md).

### Request

```json
{ "status": "waiting_client", "reason": "Aguardando confirmação por e-mail" }
```

### Response

`200 OK` com ticket atualizado.

### Erros

- `400 INVALID_STATUS_TRANSITION` — transição não permitida (ex: `new → waiting_client`).
- `409 TICKET_ALREADY_CLOSED`.

### Side-effects

- `→ waiting_client`: preenche `waiting_client_since = now()`.
- `→ in_progress` (de waiting_client): calcula pausa, soma a `sla_paused_duration_minutes`, zera `waiting_client_since`.
- Evento Mongo `status_changed`. WebSocket `ticket.status_changed`.

---

## `PATCH /api/tickets/{id}/attendant`

Atribui ou reatribui atendente.

### Request

```json
{ "attendant_id": "uuid" }
```

Para "tirar atendente" (voltar à fila): `{ "attendant_id": null }`.

### Response

`200 OK`.

### Erros

- `403 FORBIDDEN_TARGET_ATTENDANT` — atendente alvo não pertence ao depto do ticket.
- `409 ATTENDANT_AT_CAPACITY` — atendente atingiu `max_simultaneous_chats`.

### Side-effects

- Preenche `assigned_at`, `sla_first_response_deadline`.
- Status `new → in_progress`.
- Evento Mongo `attendant_assigned`. WebSocket `ticket.assigned`.

---

## `POST /api/tickets/{id}/transfer`

Transfere o ticket para outro atendente ou departamento.

### Request

```json
{
  "target_type": "attendant" | "department",
  "target_attendant_id": "uuid?",
  "target_department_id": "uuid?",
  "note": "Cliente quer falar com financeiro."
}
```

Regras:
- `target_type = attendant` → exige `target_attendant_id`. Pode ser do mesmo ou outro depto.
- `target_type = department` → exige `target_department_id`. Atribui via round-robin no destino ou fica na fila.

### Response

`200 OK` com ticket atualizado.

### Erros

- `400 INVALID_TRANSFER_TARGET`.
- `403 FORBIDDEN_DEPARTMENT`.
- `404 TARGET_NOT_FOUND`.
- `409 TICKET_ALREADY_CLOSED`.

### Side-effects

- Se mudou depto: recalcula `sla_first_response_deadline` e `sla_resolution_deadline` com metas do novo depto; **zera** `sla_paused_duration_minutes`; preserva `first_response_at`.
- Se `note` preenchido: cria `ticket_note` automática com o conteúdo (autor = atendente atual).
- Evento Mongo `transferred`. WebSocket `ticket.transferred`.

---

## `POST /api/tickets/{id}/resolve`

Encerra o ticket. Status → `resolved`.

### Request

```json
{ "resolution_note": "Cliente confirmou agendamento." }
```

`resolution_note` é opcional — quando preenchida, vira `ticket_note` automática.

### Response

`200 OK`.

### Erros

- `409 TICKET_ALREADY_CLOSED`.

### Side-effects

- Preenche `resolved_at = now()`.
- Se estava em `waiting_client`: calcula pausa final.
- Atualiza conversa vinculada para `resolved` (cascata).
- Reseta `has_reminder_alert = false`.
- Evento Mongo `ticket_resolved`. WebSocket `ticket.status_changed`.

---

## `POST /api/tickets/{id}/cancel`

Cancela o ticket (sem resolução).

### Request

```json
{ "reason": "Cliente desistiu via WhatsApp" }
```

### Response

`200 OK`.

### Erros

- `409 TICKET_ALREADY_CLOSED`.

### Side-effects

- Preenche `cancelled_at = now()`.
- **NÃO** atualiza conversa.
- Reseta `has_reminder_alert = false`.
- Evento Mongo `ticket_cancelled`. WebSocket `ticket.status_changed`.
