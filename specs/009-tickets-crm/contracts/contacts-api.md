# Contract — Contacts API (CRM)

Endpoints REST autenticados. Visibilidade por papel:

- `tenant_attendant` — pode listar e ver contatos. Pode editar contatos que apareceram em tickets dos seus departamentos (V1: simplificar — todos podem editar todos).
- `supervisor`, `tenant_admin` — todos os contatos.

---

## `GET /api/contacts`

Lista paginada com busca opcional.

### Query string

| Param | Tipo | Default | Descrição |
|---|---|---|---|
| `page` | int | 1 | — |
| `per_page` | int | 20 (max 100) | — |
| `q` | string | — | Busca por nome, e-mail, telefone (full-text + ILIKE como fallback). |
| `source_channel` | enum | — | `live_chat` / `whatsapp` / `manual` |

### Response

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "name": "João Silva",
      "email": "joao@email.com",
      "phone": "(11) 99999-9999",
      "source_channels": ["live_chat", "whatsapp"],
      "tickets_count": 5,
      "last_interaction_at": "2026-05-10T18:00:00Z",
      "created_at": "2026-04-01T10:00:00Z"
    }
  ],
  "meta": { "page": 1, "per_page": 20, "total": 230 }
}
```

`tickets_count` e `last_interaction_at` são agregados (COUNT + MAX) — calculados na query.

---

## `GET /api/contacts/{id}`

Detalhe do contato.

### Response

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "name": "João Silva",
    "email": "joao@email.com",
    "phone": "(11) 99999-9999",
    "phone_normalized": "5511999999999",
    "notes": "Cliente VIP — já solicitou desconto antes.",
    "source_channels": ["live_chat", "whatsapp"],
    "tickets_count": 5,
    "conversations_count": 8,
    "last_interaction_at": "2026-05-10T18:00:00Z",
    "created_at": "2026-04-01T10:00:00Z",
    "updated_at": "2026-05-10T18:00:00Z"
  }
}
```

### Erros

- `404 CONTACT_NOT_FOUND`.

---

## `POST /api/contacts`

Cria contato manualmente. Aplica **deduplicação automática** (R9).

### Request

```json
{
  "name": "João Silva",
  "email": "joao@email.com?",
  "phone": "(11) 99999-9999?",
  "notes": "...?",
  "source_channel": "manual"
}
```

### Response

- `201 Created` com o contato (novo ou existente após dedup).
- O cliente front-end deve verificar `id` retornado vs. `id` originalmente esperado para detectar dedup.

### Erros

- `400 VALIDATION_ERROR` — `name`, `email` ou `phone` deve estar presente.
- `400 INVALID_EMAIL` / `INVALID_PHONE`.

### Side-effects

- Se contato existente foi encontrado por dedup: campos vazios são preenchidos com os novos valores; `source_channels` recebe append do novo canal.

---

## `PUT /api/contacts/{id}`

Edita contato.

### Request

```json
{
  "name": "João Silva da Costa",
  "email": "joao@email.com",
  "phone": "(11) 99999-9999",
  "notes": "Atualização: VIP confirmado."
}
```

### Response

`200 OK`.

### Erros

- `404 CONTACT_NOT_FOUND`.
- `400 VALIDATION_ERROR`.
- `409 EMAIL_CONFLICT` — outro contato já usa o e-mail.
- `409 PHONE_CONFLICT` — idem para telefone.

### Side-effects

- `phone_normalized` é recalculado se `phone` mudou.
- `updated_at = now()`.

---

## `GET /api/contacts/{id}/tickets`

Histórico paginado de tickets do contato. 20/página por padrão. Ordem decrescente por `created_at`.

### Query string

| Param | Default | Descrição |
|---|---|---|
| `page` | 1 | — |
| `per_page` | 20 (max 100) | — |
| `include_terminal` | true (neste endpoint) | Inclui `resolved` e `cancelled`. |

### Response

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "protocol": "TK-20260511-00042",
      "channel": "live_chat",
      "status": "resolved",
      "subject": "...",
      "department_name": "Comercial",
      "attendant_name": "Maria Silva",
      "created_at": "2026-05-11T13:30:00Z",
      "resolved_at": "2026-05-11T15:00:00Z"
    }
  ],
  "meta": { "page": 1, "per_page": 20, "total": 5 }
}
```

### Erros

- `404 CONTACT_NOT_FOUND`.

---

## `GET /api/contacts/{id}/conversations`

Histórico paginado de conversas. Inclui conversas com e sem ticket vinculado.

### Query string

| Param | Default | — |
|---|---|---|
| `page` | 1 | — |
| `per_page` | 20 (max 100) | — |

### Response

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "channel": "live_chat",
      "status": "resolved",
      "ticket_id": "uuid?",
      "ticket_protocol": "TK-20260511-00042?",
      "message_count": 14,
      "started_at": "2026-05-11T13:30:00Z",
      "ended_at": "2026-05-11T15:00:00Z"
    }
  ],
  "meta": { "page": 1, "per_page": 20, "total": 8 }
}
```

### Erros

- `404 CONTACT_NOT_FOUND`.

---

## Comportamento de Deduplicação (FR-026/027)

Detalhe operacional para clientes desta API (front-end e gateway IA):

1. **POST /contacts** sempre passa pelo `ContactDeduplicationService` (R9).
2. **Como criar ticket por transbordo da IA**: use o gateway interno (`TicketCreationGateway`) que invoca dedup automaticamente. Frontend não precisa pré-criar contato.
3. **Como atendente cria ticket manual com novo contato**: envia `POST /api/tickets` com `contact: { name, email?, phone? }` aninhado — backend chama dedup e retorna o ticket ligado ao contato resolvido.
4. **Conflito de identidade** (mesmo e-mail mas nomes diferentes em contatos antigos): mantém o primeiro contato, anexa o segundo nome ao campo `notes` automaticamente: `"Outro nome usado: <nome novo>"` (V1.1 — merge manual).

A operação de **merge manual** de dois contatos é V1.1 (endpoint `POST /api/contacts/{primary_id}/merge { secondary_id }`).
