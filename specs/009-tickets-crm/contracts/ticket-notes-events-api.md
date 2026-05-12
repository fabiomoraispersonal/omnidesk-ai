# Contract — Ticket Notes & Events API

Endpoints REST autenticados.

---

## `POST /api/tickets/{id}/notes`

Adiciona uma anotação interna ao ticket. **Append-only** — não há `PUT`/`DELETE`.

### Acesso

- Qualquer role com permissão de ver o ticket (`tenant_attendant` no depto, `supervisor`, `tenant_admin`).
- Mesmo se o ticket estiver `resolved`/`cancelled` (audit pós-encerramento permitido).

### Request

```json
{ "content": "Cliente já solicitou desconto antes. Verificar com gerência antes de oferecer." }
```

### Response

`201 Created`:

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "ticket_id": "uuid",
    "attendant_id": "uuid",
    "attendant_name": "Maria Silva",
    "content": "...",
    "created_at": "2026-05-11T13:35:00Z"
  }
}
```

### Validações

- `content` ≥ 1, ≤ 10.000 chars.

### Erros

- `400 VALIDATION_ERROR`.
- `404 TICKET_NOT_FOUND`.
- `403 FORBIDDEN_DEPARTMENT`.

### Side-effects

- Evento Mongo `note_added` registrado em `{slug}_ticket_events` (com `note_id`, sem conteúdo).
- **NÃO** emite evento WebSocket (anotações são privadas, sem necessidade de broadcast).

### Garantias de Segurança (FR-025 + SC-006)

- Conteúdo da nota **NUNCA** é incluído em:
  - Prompts da IA (`AgentOrchestrator` filtra ao montar contexto).
  - Payload de mensagem enviada ao cliente (canais Live Chat / WhatsApp).
  - Eventos WebSocket de tickets (eventos não carregam content de notes).
  - Logs estruturados (Serilog `Destructure` ignora campo `content` de `TicketNote`).

---

## `GET /api/tickets/{id}/notes`

Lista anotações internas do ticket. Ordem cronológica crescente (mais antigas primeiro).

### Acesso

Idem `POST`.

### Response

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "ticket_id": "uuid",
      "attendant_id": "uuid",
      "attendant_name": "Maria Silva",
      "content": "...",
      "created_at": "2026-05-11T13:35:00Z"
    }
  ]
}
```

### Erros

- `404 TICKET_NOT_FOUND`.
- `403 FORBIDDEN_DEPARTMENT`.

---

## `GET /api/tickets/{id}/events`

Lista o histórico de eventos do ticket (audit log do Mongo `{slug}_ticket_events`).

### Acesso

- `tenant_attendant` no depto do ticket.
- `supervisor`, `tenant_admin`.

### Query string

| Param | Default | Descrição |
|---|---|---|
| `from` | — | ISO datetime — filtro |
| `to` | — | — |
| `event_type` | — | Filtro por tipo (ver TicketEventType const set) |

### Response

```json
{
  "success": true,
  "data": [
    {
      "id": "ObjectId hex",
      "event_type": "transferred",
      "actor_type": "attendant",
      "actor_id": "uuid",
      "actor_name": "Maria Silva",
      "from": null,
      "to": null,
      "department_from_id": "uuid",
      "department_to_id": "uuid",
      "attendant_from_id": "uuid",
      "attendant_to_id": null,
      "reason": "Cliente quer falar com financeiro",
      "timestamp": "2026-05-11T14:00:00Z"
    },
    {
      "id": "ObjectId hex",
      "event_type": "status_changed",
      "actor_type": "system",
      "from": "in_progress",
      "to": "waiting_client",
      "timestamp": "2026-05-11T13:50:00Z"
    },
    {
      "id": "ObjectId hex",
      "event_type": "ticket_created",
      "actor_type": "ai",
      "actor_id": "uuid",
      "actor_name": "Agente Comercial",
      "reason": "Cliente solicitou atendente humano",
      "timestamp": "2026-05-11T13:30:00Z"
    }
  ]
}
```

### Erros

- `404 TICKET_NOT_FOUND`.
- `403 FORBIDDEN_DEPARTMENT`.

### Observações

- **Eventos são imutáveis** — não há endpoint de delete/update.
- Conteúdos de mensagens e notas **não** aparecem aqui (apenas IDs).
- Ordem padrão: mais recente primeiro.
