# Contract: WebSocket Events

Eventos publicados via Redis Pub/Sub e entregues aos clientes WebSocket conectados ao tenant.

## Conexão

```
GET wss://api.omnideskcrm.com.br/ws?token={access_token}
```

Após handshake, o cliente envia:

```json
{ "type": "subscribe", "channels": ["tenant", "dept:{id}", "attendant:self"] }
```

O backend valida cada channel contra as claims do JWT:

- `tenant`: requer `tenant_admin` ou `supervisor`
- `dept:{id}`: requer vínculo do atendente com o dept OU `supervisor`
- `attendant:self`: sempre permitido (atendente subscreve apenas seu próprio canal)
- `attendant:{other_id}`: bloqueado (403)

## Formato comum

Todos os eventos seguem a estrutura:

```json
{
  "type": "<event_type>",
  "payload": { ... },
  "timestamp": "2026-05-07T14:30:00.123Z",
  "tenant_slug": "clinica-abc"
}
```

## Eventos

### 1. `attendant.status_changed`

Disparado em toda transição de status. Publicado em `tenant` e `dept:{X}` para cada dept do atendente.

```json
{
  "type": "attendant.status_changed",
  "payload": {
    "attendant_id": "uuid",
    "attendant_name": "Maria",
    "from_status": "online",
    "to_status": "away",
    "changed_by": "system",
    "changed_at": "2026-05-07T14:30:00Z"
  }
}
```

### 2. `ticket.assigned`

Publicado em `attendant:{id}` (notificar destinatário) e `dept:{id}` (atualizar fila no painel).

```json
{
  "type": "ticket.assigned",
  "payload": {
    "ticket_id": "uuid",
    "ticket_number": 1234,
    "subject": "Dúvida sobre plano",
    "department_id": "uuid",
    "attendant_id": "uuid",
    "assignment_method": "auto" | "manual",
    "assigned_at": "..."
  }
}
```

### 3. `ticket.transferred`

Publicado em `attendant:{from_id}`, `attendant:{to_id}` (se atendente específico), `dept:{from_id}`, `dept:{to_id}`.

```json
{
  "type": "ticket.transferred",
  "payload": {
    "ticket_id": "uuid",
    "from_attendant_id": "uuid|null",
    "to_attendant_id": "uuid|null",
    "from_department_id": "uuid",
    "to_department_id": "uuid",
    "reason": "cliente quer suporte técnico" | null,
    "transferred_at": "..."
  }
}
```

### 4. `ticket.queued`

Publicado em `dept:{id}` quando um ticket entra em fila por ausência de elegíveis.

```json
{
  "type": "ticket.queued",
  "payload": {
    "ticket_id": "uuid",
    "ticket_number": 1234,
    "department_id": "uuid",
    "reason": "no_attendants_online" | "all_at_capacity" | "outside_business_hours_no_one_online",
    "next_business_window_start": "2026-05-08T08:00:00-03:00" | null,
    "queued_at": "..."
  }
}
```

## Garantias

- **Latência p95 ≤ 1 s** entre o trigger no backend e a renderização no cliente (SC-004).
- **Nenhum evento é perdido em failover** — Redis pub/sub garante entrega para todos os subscribers ativos no momento da publicação. Conexões que reconectam **não** recebem histórico (premissa V1; clients fazem GET no estado atual ao reconectar).
- **Cada evento carrega o `tenant_slug`** — clients fazem assertion local para defense-in-depth.
