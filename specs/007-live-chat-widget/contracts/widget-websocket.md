# Contract — Widget WebSocket (`/ws/widget/{conversation_id}`)

Canal em tempo real visitante ↔ backend. Cada conexão equivale a 1 visitante × 1 conversa × 1 aba.

**URL**: `wss://api.omnicare.ia.br/ws/widget/{conversation_id}?token={widget_token}&anonymous_id={uuid}&since={last_message_id?}`

**Handshake (HTTP 101)**:

1. Backend extrai `widget_token` da query → resolve tenant.
2. Valida `Origin` contra `widget_config.allowed_domains`.
3. Valida que `conversation_id` pertence ao tenant E ao `visitor` com `anonymous_id` informado.
4. Valida que `conversations.lgpd_consent_at IS NOT NULL` (defesa em profundidade — sem aceite, sem WS).
5. Se `since=<last_message_id>`, envia mensagens criadas após esse ID via `message.new` (replay para reconexão).
6. Backend assina canal Redis Pub/Sub `{slug}:conv:{conversation_id}` e proxia eventos para o WS.
7. Conexão fica aberta até timeout (60s sem ping) ou fechamento.

**Falhas no handshake** (códigos custom no fechamento):

| Erro | Close code | Reason |
|---|---|---|
| Token inválido | 4401 | `INVALID_WIDGET_TOKEN` |
| Origin bloqueada | 4403 | `ORIGIN_NOT_ALLOWED` |
| Conversation não pertence ao visitor | 4404 | `CONVERSATION_NOT_FOUND` |
| LGPD não aceito | 4422 | `LGPD_CONSENT_REQUIRED` |
| Conversation `resolved`/`abandoned` | 4409 | `CONVERSATION_CLOSED` (cliente decide reabrir nova) |

---

## Frame format (JSON)

Todos os eventos seguem:

```json
{
  "type": "<event_type>",
  "payload": { ... },
  "timestamp": "2026-05-09T12:00:00Z"
}
```

---

## Eventos: backend → widget

### `message.new`

Nova mensagem criada (de qualquer sender — IA, atendente, sistema).

```json
{
  "type": "message.new",
  "payload": {
    "message_id": "msg-uuid",
    "sender_type": "ai_agent",
    "sender_id": "agent-uuid",
    "content_type": "text",
    "content": "Como posso ajudar?",
    "attachment_url": null,
    "attachment_name": null,
    "attachment_size_bytes": null,
    "client_message_id": "uuid-do-widget-se-for-eco-da-propria-mensagem-do-visitante"
  },
  "timestamp": "2026-05-09T12:01:02Z"
}
```

> Se `client_message_id` está presente E é igual a um envio anterior do visitante, o widget marca a mensagem como confirmada (em vez de duplicar).

### `agent.typing`

Agente ou atendente está compondo. Recebido em rajadas; widget exibe "digitando…" e oculta após 5s sem novo evento.

```json
{ "type": "agent.typing", "payload": { "sender_type": "ai_agent" } }
```

### `conversation.assigned`

Atendente humano assumiu (após transbordo).

```json
{ "type": "conversation.assigned", "payload": { "attendant_name": "João" } }
```

### `conversation.resolved`

Conversa encerrada. Widget exibe mensagem apropriada e oferece "Iniciar nova conversa".

```json
{
  "type": "conversation.resolved",
  "payload": { "ended_by": "ai_agent" }
}
```

`ended_by` ∈ `{attendant, ai_agent, system_inactivity, system_disable}`. Widget renderiza mensagens diferentes:

| `ended_by` | Mensagem ao visitante |
|---|---|
| `ai_agent` | "Atendimento finalizado. Obrigado!" |
| `attendant` | "O atendimento foi encerrado." |
| `system_inactivity` | "Atendimento encerrado por inatividade." |
| `system_disable` | "O atendimento foi encerrado pelo sistema." |

---

## Eventos: widget → backend

### `message.send`

Visitante envia mensagem.

```json
{
  "type": "message.send",
  "payload": {
    "client_message_id": "uuid-gerado-no-widget",
    "content_type": "text",
    "content": "Olá",
    "attachment_url": null
  }
}
```

Para anexo, o widget já fez upload via REST (`POST /api/public/widget/upload`) e inclui `attachment_url`, `attachment_name`, `attachment_size_bytes`, `content_type` ∈ `{image, file}`:

```json
{
  "type": "message.send",
  "payload": {
    "client_message_id": "uuid",
    "content_type": "image",
    "content": null,
    "attachment_url": "https://minio.../foto.jpg",
    "attachment_name": "foto.jpg",
    "attachment_size_bytes": 234567
  }
}
```

**Backend**:

1. Idempotência: se `(conversation_id, client_message_id)` já existe em `messages`, ignora (re-entrega da fila local).
2. Valida que conversa está `open`.
3. INSERT em `messages`, atualiza `last_message_at`.
4. Se for primeira mensagem da conversa, persiste `lgpd_consent_at` se ainda não foi.
5. Enfileira `IncomingMessage` na fila Hangfire `{slug}:incoming_messages` (Spec 006) — somente se `attendant_id IS NULL` (caso contrário, atendente humano lê via CRM e responde manualmente).
6. Broadcast `message.new` no canal Redis (eco para outras abas + CRM).

### `visitor.typing`

Debounce de 1s — widget envia somente quando o visitante está ativamente digitando.

```json
{ "type": "visitor.typing", "payload": {} }
```

Backend publica `chat.visitor_typing` no canal Redis CRM (`{slug}:crm:dept:{dept_id}` ou `{slug}:crm:user:{attendant_id}`).

### `messages.read`

Visitante abriu o widget — zera badge de não-lidas no CRM e marca `is_read=true` nas mensagens entregues.

```json
{ "type": "messages.read", "payload": {} }
```

### `messages.replay`

Solicitação de replay após reconexão (alternativa a usar query `since=`).

```json
{ "type": "messages.replay", "payload": { "since_message_id": "msg-uuid" } }
```

Backend responde com sequência de `message.new` para mensagens posteriores ao ID.

---

## Heartbeat / keep-alive

- Backend envia `{type: "ping"}` a cada 30s.
- Widget responde `{type: "pong"}`.
- Sem pong em 60s → conexão fechada (4408 `IDLE_TIMEOUT`).

---

## Reconexão (cliente)

Conforme R6:

```
attempt 1 → wait 1000ms (jitter ±200ms)
attempt 2 → wait 2000ms
attempt 3 → wait 4000ms
attempt 4 → wait 8000ms
attempt 5 → wait 16000ms
attempt n>5 → wait 30000ms
```

Reabre com `since=<last_message_id_local>` para receber gap.

---

## Multi-aba

Mesma `conversation_id` em duas abas → ambas se conectam no mesmo canal Redis e recebem `message.new` em paralelo. Sem coordenação — cada aba é responsiva independentemente.

`messages.read` enviado por uma aba → zera badge para todas (broadcast Pub/Sub).
