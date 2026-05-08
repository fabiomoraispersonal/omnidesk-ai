# Contract: AI Suggestion API

Endpoint único para sugerir uma resposta ao atendente humano com base no contexto da conversa.

```
POST /api/conversations/{conversation_id}/suggest-reply
```

| Campo | Política |
|---|---|
| Policy | autenticado, atendente da conversa OU `Policies.CanViewAllConversations` |
| Rate limit | 30 requisições por atendente por minuto (por enquanto local — V2: por tenant) |

## Request

```json
{
  "context_message_count": 20
}
```

- `context_message_count`: opcional, default = `MAX_SUGGESTION_CONTEXT_MESSAGES` (env, default 20). Cap em 50.

## Response 200

```json
{
  "success": true,
  "data": {
    "suggestion_id": "log-uuid",
    "text": "Posso confirmar para você que a renovação foi concluída e o boleto seguinte vence em 10 de junho. Precisa de mais alguma coisa?",
    "model": "gpt-4o",
    "elapsed_ms": 1430,
    "input_tokens": 320,
    "output_tokens": 84,
    "context_used": {
      "sub_agent_id": "uuid|null",
      "sub_agent_name": "Suporte" | null,
      "messages_used": 12
    }
  }
}
```

- `suggestion_id` é a chave do log no Mongo (`{slug}_ai_suggestion_logs`); o atendente envia esse id de volta ao confirmar a ação humana.
- `text` é truncado a 1000 caracteres (sanity guard).

## Acompanhamento da ação humana

Após o atendente decidir, o frontend deve registrar a ação para alimentar SC-010 (taxa de aprovação ≥ 70 %):

```
PATCH /api/conversations/{conversation_id}/suggestions/{suggestion_id}
{
  "human_action": "approved|edited|discarded|sent_unchanged",
  "final_message_text": "..." | null
}
```

- `approved`: enviou exatamente a sugestão sem editar
- `edited`: enviou versão modificada (envia `final_message_text`)
- `discarded`: descartou e digitou outra coisa
- `sent_unchanged`: alias de `approved` mantido para clareza no UI

Esta chamada **não** envia mensagem ao cliente — apenas grava a decisão. O envio real ocorre pelo endpoint de mensagens da Spec 008.

## Erros

| Código | Status | Significado |
|---|---|---|
| `CONVERSATION_NOT_FOUND` | 404 | conversation_id inexistente |
| `CONVERSATION_NOT_OWNED` | 403 | atendente não está atribuído à conversa e não tem `CanViewAllConversations` |
| `AI_PROVIDER_TIMEOUT` | 504 | timeout do OpenAI (> 10 s) |
| `AI_PROVIDER_ERROR` | 502 | erro 5xx do OpenAI |
| `AI_RATE_LIMIT` | 429 | rate limit por atendente excedido |

Todos os erros gravam log estruturado (Serilog → Mongo) com `conversation_id`, `attendant_id` e `error_code`.

## Garantias críticas

- **Nunca envia mensagem ao cliente.** O endpoint apenas retorna texto.
- **Nunca cria registro na conversa** — só no log de telemetria.
- **Falha silenciosa não é aceita.** Se o provedor cair, o atendente recebe mensagem clara e a conversa segue normalmente (FR-040).
