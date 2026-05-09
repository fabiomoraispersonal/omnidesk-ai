# Contract — Eventos de Runtime do Agent (Mongo + Redis Pub/Sub)

## 1. `agent_activity_logs` (MongoDB)

Coleção `{slug}_agent_activity_logs`. **Insert por run** (1 doc por execução de agente OU por erro de API).

Schema completo em [data-model.md §3.1](../data-model.md). Resumo dos casos de uso:

| Caso | `action` | Campos relevantes |
|---|---|---|
| Resposta direta ao cliente | `respond` | `input_tokens`, `output_tokens`, `latency_ms`, `model` |
| Handoff para outro agente | `handoff_to_agent` | `handoff_target_agent_id`, mesmos tokens/latência |
| Transbordo para humano | `transfer_to_human` | `handoff_target_department_id`, mesmos tokens/latência |
| Falha de API (5xx, timeout, 401/403) | `api_error` | `error: {type, status, message}`, latência (até o erro) |
| Loop de tool inválida | `api_error` | `error.type: tool_loop` |

**Imutabilidade**: documentos nunca são atualizados ou deletados (constituição §VI). Para correção, novos documentos com `action: corrected_*` (não usado no V1).

---

## 2. Redis Pub/Sub — eventos publicados pelo runtime

Todos os canais seguem o padrão `{slug}:ws:<scope>`. Spec 006 **publica** apenas; consumidores ficam nas Specs 005/007/010.

| Canal | Quando publica | Payload |
|---|---|---|
| `{slug}:ws:thread:{thread_id}` | Resposta da IA enfileirada | `{type: "agent_message", thread_id, agent_name, content_preview, sent_at}` |
| `{slug}:ws:thread:{thread_id}` | Handoff entre agentes | `{type: "agent_handoff", from_agent_name, to_agent_name, reason, at}` |
| `{slug}:ws:thread:{thread_id}` | Transbordo para humano | `{type: "human_handoff", department_id, ticket_id, ticket_number, at}` |
| `{slug}:ws:dept:{department_id}` | Ticket criado por transbordo | `{type: "ticket_created_from_ai", ticket_id, ticket_number, originating_agent_name, at}` |

> `content_preview` é os primeiros 80 caracteres do conteúdo da mensagem do agente; o conteúdo completo viaja pelo canal de origem (Spec 007/008).

---

## 3. Telemetria observável

Spec 006 expõe métricas via Serilog → Mongo `{slug}_serilog_logs` (existente). Indicadores chave:

| Métrica | Origem | Constituição §VI |
|---|---|---|
| `MedianTimeToFirstAiResponseMs` | derivado de `agent_activity_logs` (action=respond, primeira do thread) | Sim — < 5 s |
| `AiResolutionRateWithoutHandoff` | (#threads sem `transfer_to_human`) / (#threads totais por janela) | Sim — > 40% |
| `AgentApiErrorRate` | (action=api_error) / (todos) por janela 1h | Operacional |
| `MeanInputTokensPerRun` | derivado de input_tokens | Custos |
| `MeanOutputTokensPerRun` | derivado de output_tokens | Custos |

A Spec 006 não constroi dashboards; apenas garante que dados existem. Dashboards entram na Spec 011 (futura, observabilidade).

---

## 4. Testes de contrato

- `AgentActivityLoggerTests.cs`:
  - cada caso de `action` produz exatamente 1 documento, com campos certos.
  - PII zero (verifica que `content` da mensagem do cliente NUNCA aparece em nenhum campo).
- `RedisPubSubPublishingTests.cs`:
  - 4 canais e payloads publicados conforme tabela acima — verificado via `ISubscriber.SubscribeAsync` em fixture Redis.
