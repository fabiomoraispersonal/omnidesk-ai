# Contract — Playground (`POST /api/agents/{id}/test`)

**Auth**: `tenant_admin` e `supervisor` (Spec 004 FR-016).
**Invariante**: nenhum efeito colateral em `ai_threads`, `agent_activity_logs`, ou tickets. Thread temporário em Redis.

---

## POST /api/agents/{id}/test

**Body**:

```json
{
  "message": "Olá, quero saber sobre planos.",
  "session_id": "uuid (opcional)"
}
```

- `message`: required, length 1..5000.
- `session_id`: opcional. Se ausente, novo session_id é gerado e retornado para reuso.

**200**:

```json
{
  "success": true,
  "data": {
    "session_id": "uuid",
    "agent_id": "uuid",
    "agent_name": "Agente Comercial",
    "reply": "Claro! Temos três planos…",
    "tool_calls_observed": [],
    "elapsed_ms": 1240,
    "model": "gpt-4o",
    "tokens": { "input": 320, "output": 95 },
    "expires_at": "2026-05-08T12:30:00Z"
  }
}
```

> `tool_calls_observed` é uma lista informativa de tool calls que o agente fez durante o teste — útil para visualizar handoffs simulados. **Nenhuma tool é executada de fato** no playground (handoff não muta estado, transbordo não cria ticket); o backend retorna `{success: true, simulated: true}` para qualquer tool call no contexto de playground.

**429** `PLAYGROUND_TOO_MANY_SESSIONS` — tenant excedeu 5 sessões ativas. Sistema descarta a mais antiga em LRU automaticamente; este código é apenas warning.

**404** `AGENT_NOT_FOUND` — agente inexistente, deletado, ou de outro tenant.

**500** `OPENAI_TIMEOUT` — falha na OpenAI; o playground **não** aciona transbordo automático (não faria sentido). Retorna erro ao usuário do CRM.

---

## DELETE /api/agents/playground-sessions/{session_id}

Encerra sessão de teste explicitamente — útil quando o admin fecha a aba.

**204** No Content. Idempotente (sessão já expirada → 204 também).

---

## GC automático

Job recurring `PlaygroundCleanupJob` (Hangfire, recurring 1 h) varre threads OpenAI que pertencem a sessions Redis expiradas e os deleta via `threads.delete`.

---

## Testes de contrato

- `PlaygroundEndpointTests.cs` cobre:
  - Sessão nova vs. continuação.
  - TTL Redis (Testcontainers).
  - Verificação de invariante: nenhum doc em `agent_activity_logs`, nenhuma row em `ai_threads`.
  - Tool call simulada retorna `{simulated: true}`.
