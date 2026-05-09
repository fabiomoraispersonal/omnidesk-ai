# Contract — CRM WebSocket (`/ws/crm`)

Canal em tempo real atendente ↔ backend. Uma conexão por sessão CRM (atendente logado). Roteia eventos das conversas atribuídas ao atendente OU às que correspondem ao seu departamento (Spec 005).

**URL**: `wss://api.omnicare.ia.br/ws/crm?token=<jwt_access_token>`

**Auth**: JWT Bearer **na query** (browser WS não permite header). Validação na conexão; se inválido → close 4401.

**Handshake**:

1. Backend valida JWT → extrai `user_id`, `tenant_slug`, `role` ∈ `{tenant_admin, tenant_attendant}`.
2. Lê `attendants.department_id` para o `user_id` (Spec 005).
3. Assina canais Redis:
   - `{slug}:crm:user:{user_id}` (eventos da própria caixa de entrada).
   - `{slug}:crm:dept:{department_id}` (filas do departamento — eventos de novas conversas não atribuídas).
4. Conexão fica aberta com heartbeat similar ao widget.

---

## Eventos: backend → CRM

### `chat.new_conversation`

Conversa nova chega ao departamento ou foi explicitamente atribuída ao atendente. Inclui dados suficientes para a lista esquerda renderizar sem GET adicional.

```json
{
  "type": "chat.new_conversation",
  "payload": {
    "conversation_id": "conv-uuid",
    "channel": "live_chat",
    "visitor_name": "Maria",
    "visitor_anonymous": false,
    "department_id": "dept-uuid",
    "attendant_id": "att-uuid",
    "page_url": "https://www.clinica.com.br/agendamento",
    "last_message_preview": "quero marcar consulta",
    "last_message_at": "2026-05-09T12:00:00Z",
    "transferred_from": "ai_agent"
  }
}
```

### `chat.message_received`

Nova mensagem em conversa que o atendente está acompanhando.

```json
{
  "type": "chat.message_received",
  "payload": {
    "conversation_id": "conv-uuid",
    "message": {
      "id": "msg-uuid",
      "sender_type": "visitor",
      "content_type": "text",
      "content": "Quero saber se posso reagendar.",
      "created_at": "2026-05-09T12:00:00Z"
    }
  }
}
```

### `chat.visitor_typing`

```json
{
  "type": "chat.visitor_typing",
  "payload": { "conversation_id": "conv-uuid" }
}
```

### `chat.browser_notify`

Gatilho explícito para o CRM emitir `Notification` nativa (controle fica no cliente — abas focadas ignoram).

```json
{
  "type": "chat.browser_notify",
  "payload": {
    "trigger": "new_conversation",
    "title": "Nova conversa",
    "body": "Nova conversa de Maria",
    "conversation_id": "conv-uuid"
  }
}
```

`trigger` ∈ `{new_conversation, new_message, transferred}` — corresponde aos 3 eventos da spec §8.

### `chat.conversation_resolved`

Conversa encerrada (atendente, IA, sistema). Lista esquerda remove a entrada.

```json
{
  "type": "chat.conversation_resolved",
  "payload": {
    "conversation_id": "conv-uuid",
    "ended_by": "attendant"
  }
}
```

---

## Eventos: CRM → backend

### `attendant.typing`

```json
{ "type": "attendant.typing", "payload": { "conversation_id": "conv-uuid" } }
```

Backend publica `agent.typing {sender_type: "attendant"}` no canal `{slug}:conv:{conversation_id}` para o widget.

### `conversation.send`

Atendente envia mensagem.

```json
{
  "type": "conversation.send",
  "payload": {
    "conversation_id": "conv-uuid",
    "client_message_id": "uuid-gerado-no-crm",
    "content_type": "text",
    "content": "Olá Maria, como posso ajudar?"
  }
}
```

Backend:

1. Valida que `conversation.attendant_id == request.user_id` (atendente só pode responder o que assumiu).
2. INSERT message com `sender_type=attendant`, `sender_id=user_id`.
3. Publica `message.new` no canal do widget.

### `conversation.resolve`

Atendente clica "Encerrar conversa".

```json
{ "type": "conversation.resolve", "payload": { "conversation_id": "conv-uuid" } }
```

Backend marca `status=resolved`, `ended_by=attendant`, `ended_at=NOW()`. Publica `conversation.resolved` no widget e `chat.conversation_resolved` em outros CRMs do mesmo dept (caso vissem em modo "fila").

### `messages.read`

Atendente abriu/clicou na conversa.

```json
{ "type": "messages.read", "payload": { "conversation_id": "conv-uuid" } }
```

Marca todas as mensagens não-lidas com `is_read=true` (ainda relevante para futuros indicadores de "lida pelo atendente").

---

## Backpressure / fila por atendente

- Mesma conexão WS pode receber mensagens de múltiplas conversas. CRM mantém estado local de "conversa selecionada" e atualiza cada uma na lista.
- Eventos do canal `{slug}:crm:dept:{dept_id}` chegam para **todos** atendentes do depto — quando atribuição vai para um específico, o evento `chat.new_conversation` carrega `attendant_id`; CRMs ignoram o evento se o `attendant_id` não bate (filtro client-side).

---

## Tabela de close codes

| Cenário | Code | Reason |
|---|---|---|
| JWT inválido/expirado | 4401 | `INVALID_OR_EXPIRED_TOKEN` |
| Usuário não tem role válido | 4403 | `FORBIDDEN_ROLE` |
| Idle (sem pong em 60s) | 4408 | `IDLE_TIMEOUT` |
| Tenant suspenso/desativado | 4423 | `TENANT_SUSPENDED` |
