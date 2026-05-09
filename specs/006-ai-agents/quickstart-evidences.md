# Quickstart Evidences — Spec 006 (Agentes de IA)

**Last run**: _(not yet executed — fill in when build runs locally)_
**Run by**: _(your name)_
**Environment**: dev local + Docker compose

> Template para registrar evidências de execução de [`quickstart.md`](quickstart.md). Marque cada cenário com `[X]` ao concluir e cole as evidências mínimas (SQL output, log Hangfire, etc.). Mesmo padrão da Spec 005.

---

## QS-1 — Tenant recém-provisionado já tem Orchestrator

- [ ] Provisioned tenant `qs-test` via `POST /api/admin/tenants`.
- [ ] CRM lista 1 card Orchestrator + 0 sub-agentes.
- [ ] SQL evidence:

```
SELECT type, name, openai_assistant_id IS NOT NULL AS has_assistant, deleted_at
  FROM tenant_qs_test.ai_agents;
-- expected: 1 row, type=orchestrator
```

Evidence:
```
(paste output)
```

---

## QS-2 — Cliente atendido pelo Orchestrator (US1)

- [ ] `POST /api/internal/test-incoming` com `external_ref=livechat:test-001`.
- [ ] Hangfire dashboard mostrou job `IncomingMessageWorker` consumido.
- [ ] `ai_threads` ganha 1 row com `openai_thread_id` preenchido.
- [ ] `qs-test_agent_activity_logs` Mongo recebe 1 doc `action=respond`.
- [ ] `latency_ms` ≤ 5000 (SC-002).

Evidence:
```
(paste mongosh output)
```

---

## QS-3 — Transbordo por palavra-chave (US2)

- [ ] Configurado `default_department_id` para Comercial.
- [ ] Mensagem "quero falar com um atendente" → ticket criado.
- [ ] `ai_threads.handed_off_to_human_at` preenchido.
- [ ] `ai_handoff_snapshots` tem 1 row com history_json.
- [ ] Segunda mensagem na conversa → auto-reply do sistema, sem novo doc activity_log.

Evidence:
```
(paste SQL + mongo)
```

---

## QS-4 — Sub-agentes especializados (US3)

- [ ] Criados sub-agentes "Comercial" e "Suporte".
- [ ] Mensagem "quero saber preços" → handoff para Comercial.
- [ ] Mensagem "minha conta não acessa" → handoff para Suporte.
- [ ] Desativado Suporte → próxima mensagem volta ao Orchestrator.

---

## QS-5 — Playground (US4)

- [ ] Mensagem de teste retorna em <5s.
- [ ] **Zero rows** em `ai_threads` para a sessão de teste.
- [ ] **Zero docs** em `agent_activity_logs`.
- [ ] Chave Redis `qs-test:playground:<sid>` existe com TTL ≈ 1800s.

---

## QS-6 — Configurações avançadas (US5)

- [ ] `context_window_messages = 5` salvo via `PUT /api/ai-settings`.
- [ ] Próxima mensagem do cliente: `input_tokens` cai significativamente.
- [ ] Cadastrada chave OpenAI própria → log Serilog `openai_key_source: "tenant"`.
- [ ] Removida chave → próxima execução `openai_key_source: "global"`.

---

## QS-7 — Resiliência: falha OpenAI → transbordo (US6)

- [ ] Injetado fault 503 via `POST /api/internal/fault-injector {openAiStatusCode:503,count:2}`.
- [ ] Retry após ~3s, segunda falha → transbordo automático.
- [ ] Mongo log: `api_error` (×2) + `transfer_to_human`.
- [ ] Mensagem ao cliente: "Estamos com uma instabilidade técnica…"
- [ ] Tempo total ≤ 10s (SC-005).

- [ ] Injetado fault 401 → transbordo imediato (sem retry).

---

## QS-8 — Verificação de invariantes globais

```bash
psql "$PG" -c "SELECT COUNT(*) FROM tenant_qs_test.ai_agents WHERE type='orchestrator' AND deleted_at IS NULL;"  # = 1
psql "$PG" -c "SELECT COUNT(*) FROM tenant_qs_test.ai_threads;"                                                   # ≥ 3
mongosh --eval "db.qs-test_agent_activity_logs.find({}).count()" "$MONGO"                                         # > 0
redis-cli KEYS 'qs-test:playground:*' | wc -l                                                                     # = 0 após TTL
```

Evidence:
```
(paste output)
```

---

## Resumo

| QS | Status | Notas |
|---|---|---|
| QS-1 | ⬜ | |
| QS-2 | ⬜ | |
| QS-3 | ⬜ | |
| QS-4 | ⬜ | |
| QS-5 | ⬜ | |
| QS-6 | ⬜ | |
| QS-7 | ⬜ | |
| QS-8 | ⬜ | |

**Spec 006 PASS**: ☐ — preencher quando todos os QS passam.
