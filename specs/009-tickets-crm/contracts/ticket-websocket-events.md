# Contract — Ticket WebSocket Events

Eventos emitidos em `/ws/crm` (endpoint da Spec 007 — sem mudança no handshake/auth). 6 eventos novos para tickets, publicados via Redis Pub/Sub.

**Canais de publicação** (R6):
- `{slug}:crm:dept:{department_id}` — atendentes do departamento.
- `{slug}:crm:supervisor` — supervisores e admins (recebem tudo do tenant).

**Eventos são "best-effort"** — clientes desconectados não recebem. Reload via REST sincroniza.

---

## Envelope

Todos os eventos seguem o envelope da Spec 007:

```json
{
  "type": "ticket.created" | "ticket.assigned" | "ticket.status_changed" | "ticket.transferred" | "ticket.sla_warning" | "ticket.sla_breached",
  "payload": { ... },
  "timestamp": "2026-05-11T13:30:00.123Z"
}
```

---

## `ticket.created`

Disparado pelo `TicketCreationGateway` ao criar ticket (transbordo ou manual).

### Payload

```json
{
  "ticket_id": "uuid",
  "protocol": "TK-20260511-00042",
  "department_id": "uuid",
  "channel": "live_chat" | "whatsapp" | "manual",
  "subject": "Cliente quer remarcar agendamento",
  "priority": "normal",
  "attendant_id": "uuid?",
  "contact": {
    "id": "uuid?",
    "name": "João Silva?"
  }
}
```

### Comportamento esperado no CRM

- Adiciona um card na coluna "Na Fila" (se `attendant_id` null) ou "Em Andamento" (se atribuído) do Kanban do departamento.
- Se `attendant_id` é o usuário corrente: dispara notificação (sonora/visual) opcional.

---

## `ticket.assigned`

Disparado em atribuição inicial OU reatribuição (PATCH `/attendant`). Distinto de `transferred`.

### Payload

```json
{
  "ticket_id": "uuid",
  "protocol": "TK-20260511-00042",
  "attendant_id": "uuid",
  "attendant_name": "Maria Silva",
  "department_id": "uuid"
}
```

### Comportamento esperado no CRM

- Atualiza o card visualmente (avatar + nome do atendente).
- Se atendente é o usuário corrente e estado anterior era "sem atendente": move card de "Na Fila" → "Em Andamento" (status mudou).
- Dispara `ticket.status_changed` em conjunto (eventos podem chegar próximos).

---

## `ticket.status_changed`

Disparado em toda transição de status (auto ou manual).

### Payload

```json
{
  "ticket_id": "uuid",
  "protocol": "TK-20260511-00042",
  "from_status": "in_progress",
  "to_status": "waiting_client",
  "actor_type": "attendant" | "system" | "ai",
  "actor_id": "uuid?",
  "department_id": "uuid",
  "attendant_id": "uuid?"
}
```

### Comportamento esperado no CRM

- Move o card para a coluna correspondente do `to_status` no Kanban.
- Se `to_status ∈ {resolved, cancelled}`: remove o card do Kanban (não aparece em nenhuma coluna).
- Atualiza badge SLA se aplicável (e.g., `waiting_client` pausa contador visual).

---

## `ticket.transferred`

Disparado em `POST /api/tickets/{id}/transfer`.

### Payload

```json
{
  "ticket_id": "uuid",
  "protocol": "TK-20260511-00042",
  "from_attendant_id": "uuid?",
  "to_attendant_id": "uuid?",
  "from_department_id": "uuid",
  "to_department_id": "uuid",
  "reason": "string?",
  "sla_recalculated": true | false
}
```

`sla_recalculated = true` quando muda de departamento (R-anteriores).

### Comportamento esperado no CRM

- Se a transferência envia o ticket para FORA do depto do atendente: remove card do Kanban dele.
- Se a transferência traz o ticket PARA o depto do atendente: adiciona card.
- Supervisor vê ambos os movimentos.
- Se `sla_recalculated = true`: força refresh do badge SLA (prazo mudou).

---

## `ticket.sla_warning`

Disparado pelo `TicketSlaMonitorJob` ao cruzar 80% do prazo (uma vez por ticket+tipo).

### Payload

```json
{
  "ticket_id": "uuid",
  "protocol": "TK-20260511-00042",
  "sla_type": "first_response" | "resolution",
  "deadline": "2026-05-11T14:00:00Z",
  "percent_consumed": 80,
  "department_id": "uuid",
  "attendant_id": "uuid?"
}
```

### Comportamento esperado no CRM

- Badge SLA do card vira **amarelo**.
- Toast/notificação para o atendente designado (opcional, configurável).

---

## `ticket.sla_breached`

Disparado pelo `TicketSlaMonitorJob` ao expirar.

### Payload

```json
{
  "ticket_id": "uuid",
  "protocol": "TK-20260511-00042",
  "sla_type": "first_response" | "resolution",
  "deadline": "2026-05-11T14:00:00Z",
  "department_id": "uuid",
  "attendant_id": "uuid?"
}
```

### Comportamento esperado no CRM

- Badge SLA do card vira **vermelho**.
- Toast/notificação visual de urgência para o atendente designado.
- Supervisor recebe mesmo evento → vê em dashboard global de SLA breach.

---

## Idempotência

Os eventos `sla_warning` e `sla_breached` são emitidos **uma vez** por ticket+tipo (R7) via Redis flag `{slug}:ticket:{id}:sla_warned:{type}` / `..._breached:{type}` com TTL 24h.

Outros eventos podem ser emitidos múltiplas vezes (ex: várias transferências de um mesmo ticket) — cada operação dispara um evento independente.

---

## Filtragem por papel (R6)

O `CrmWebSocketEndpoint` (Spec 007) inscreve o cliente nos canais Redis apropriados:

| Role | Canais inscritos |
|---|---|
| `tenant_attendant` | `{slug}:crm:dept:{D1}`, `{slug}:crm:dept:{D2}`, ... (1 por depto que pertence) |
| `supervisor`, `tenant_admin` | `{slug}:crm:supervisor` |

`TicketEventPublisher` publica em **todos** os canais relevantes:
- Ticket em depto `D1`: publica em `{slug}:crm:dept:D1` E `{slug}:crm:supervisor`.

Resultado: atendente recebe **apenas** eventos dos seus tickets; supervisor/admin recebem todos.

---

## Fallback de Refresh (R8)

Cliente front-end mantém polling de 30s (`Tickets:KanbanRefreshSeconds`) como fallback:
- Se WebSocket desconectar e reconectar: front faz `GET /api/tickets` para reconciliar.
- Eventos perdidos durante desconexão são reconciliados pelo state diff.
