# Quickstart — Spec 006 (Agentes de IA)

Roteiros de validação manual para demonstrar que cada User Story e Critério de Aceite da spec funcionam fim-a-fim. Use após `dotnet build && docker compose up -d` (Postgres, Redis, Mongo, Hangfire dashboard).

> Pré-requisitos:
> 1. Tenant `clinica-abc` provisionado (Spec 003).
> 2. Pelo menos 2 departamentos ativos no tenant (Spec 005).
> 3. Variáveis de ambiente do `.env` preenchidas — incluindo `OPENAI_API_KEY` global.
> 4. Logado como `tenant_admin` (`fabio@clinica-abc.com`) em `https://clinica-abc.omnicare.ia.br`.

---

## QS-1 — Tenant recém-provisionado já tem Orchestrator (US1, FR-001, FR-031, SC-001)

1. Provisionar tenant novo: `POST /api/admin/tenants {slug:"qs-test", razao_social:"QS Test"}` (logado como `saas_admin`).
2. Aguardar Hangfire concluir o `TenantProvisioningJob` (consultar dashboard `/hangfire`).
3. Como `tenant_admin` do `qs-test`, abrir `https://qs-test.omnicare.ia.br/configuracoes/agentes-de-ia`.

**Esperado**:
- Lista exibe 1 card único: badge **Orchestrator**, status ativo, prompt-base preenchido a partir do template global do Admin.
- Nenhum sub-agente.
- Botão "Excluir" ausente no card do Orchestrator.

**Verificação técnica**:
```sql
SELECT type, name, openai_assistant_id IS NOT NULL AS has_assistant, deleted_at
FROM tenant_qs_test.ai_agents;
-- Deve retornar 1 linha: type=orchestrator, has_assistant=true (ou false se lazy), deleted_at=null
```

---

## QS-2 — Cliente é atendido pelo Orchestrator na primeira mensagem (US1, SC-002)

1. Preparar request manual ao gateway (Spec 007 não pronta — usar curl ao endpoint stub):
   ```sh
   curl -X POST https://api.omnicare.ia.br/api/internal/test-incoming \
     -H "X-Tenant-Slug: qs-test" \
     -d '{"external_ref":"livechat:test-001","content":"Olá!"}'
   ```
   > _O endpoint `/api/internal/test-incoming` é exposto apenas em `Development` env (DI flag) — atalho para validação manual sem depender da Spec 007._
2. Observar Hangfire: job `IncomingMessageWorker` consome em <2s.
3. Aguardar até 5s e consultar:
   ```sh
   curl https://api.omnicare.ia.br/api/internal/test-thread?external_ref=livechat:test-001 \
     -H "X-Tenant-Slug: qs-test"
   ```

**Esperado**:
- `ai_threads` contém 1 row com `openai_thread_id` preenchido, `current_agent_id=null`, `handed_off_to_human_at=null`.
- `agent_activity_logs` em `qs-test_agent_activity_logs` Mongo contém 1 doc `action=respond`.
- p95 entre envio e resposta ≤ 5s (mensurável via `latency_ms` no log).

---

## QS-3 — Transbordo por palavra-chave + auto-reply pós-handoff (US2, FR-013, FR-015)

Pré: `qs-test` tem `default_department_id` apontando para "Comercial".

1. Configurar dept padrão:
   ```sh
   curl -X PATCH https://api.omnicare.ia.br/api/admin/tenants/qs-test/default-department \
     -d '{"department_id":"<uuid-comercial>"}'
   ```
2. Enviar via canal stub:
   ```sh
   curl -X POST .../test-incoming -d '{"external_ref":"livechat:test-002","content":"quero falar com um atendente"}'
   ```
3. Aguardar processamento.

**Esperado**:
- Mongo log: 1 doc `action=transfer_to_human`, `handoff_target_department_id=<comercial>`.
- `ai_threads`: `handed_off_to_human_at` preenchido.
- `tickets` (qs-test schema): 1 ticket em `status=queued`, `department_id=comercial`.
- `ai_handoff_snapshots`: 1 row com `history_json` contendo a mensagem do cliente.
- Mensagem do agente ao cliente foi enfileirada em `outgoing_messages` (verificar Hangfire log) com formato "Vou transferir você para nossa equipe de Comercial. Aguarde um momento."

4. Enviar segunda mensagem na mesma conversa:
   ```sh
   curl -X POST .../test-incoming -d '{"external_ref":"livechat:test-002","content":"olá??"}'
   ```

**Esperado**:
- **Nenhum** novo doc em `agent_activity_logs` (IA não processou).
- Mensagem auto-reply enfileirada em outgoing: "Sua mensagem foi recebida. Um atendente responderá em breve."

---

## QS-4 — Sub-agente: criação, handoff e desativação (US3, FR-004, FR-032)

1. CRM: criar sub-agente "Suporte" (Comercial NÃO):
   - Nome: "Agente Suporte"
   - Descritivo: "Atende dúvidas técnicas, problemas de acesso, configuração."
   - Departamento: "Suporte"
   - Prompt: "Você é o Agente Suporte da {{company_name}}. Responda dúvidas técnicas e oriente sobre acesso. Se for pedido administrativo, faça handoff para o Orchestrator."
2. Validar via API: `GET /api/agents` → 2 itens (orchestrator + suporte).

3. Disparar mensagem para uma nova thread:
   ```sh
   curl -X POST .../test-incoming -d '{"external_ref":"livechat:test-003","content":"meu login não funciona"}'
   ```

**Esperado**:
- Mongo log do Orchestrator: `action=handoff_to_agent`, `handoff_target_agent_id=<suporte>`.
- Mongo log seguinte: do Suporte, `action=respond`.
- `ai_threads.current_agent_id = <suporte>`.

4. Desativar Suporte: `PATCH /api/agents/<suporte>/toggle {is_active:false}`.

5. Enviar nova mensagem na mesma thread (`livechat:test-003`):
   ```sh
   curl -X POST .../test-incoming -d '{"external_ref":"livechat:test-003","content":"obrigado, agora consegui."}'
   ```

**Esperado**:
- Mensagem cai no Orchestrator (porque sub-agente está inativo). Mongo log do Orchestrator com `action=respond`.
- `ai_threads.current_agent_id` agora é null (Orchestrator) — atualizado quando o Orchestrator recebeu.

---

## QS-5 — Playground não cria conversa real (US4, FR-026, FR-027, SC-012)

1. CRM: editar Orchestrator → digitar "Quero remarcar consulta" no playground → "Testar".

**Esperado**:
- Resposta exibida em <5s.
- Verificar Postgres `tenant_qs_test.ai_threads` — **nenhuma row criada para esta sessão**.
- Verificar Mongo `qs-test_agent_activity_logs` — **nenhum doc novo** (playground não loga).
- Verificar Redis: chave `qs-test:playground:<sessionId>` existe, TTL ≈ 1800s.
- Buscar conteúdo da mensagem em qualquer entidade persistente — **não encontrado**.

2. Após 30 min (ou forçar TTL): verificar Redis chave expirou.
3. Aguardar `PlaygroundCleanupJob` (recurring 1h) → thread OpenAI deletada.

---

## QS-6 — Configurações avançadas: chave própria + janela de contexto (US5, FR-022..025)

1. CRM: Configurações Avançadas → cadastrar chave OpenAI própria do tenant.
2. `POST /api/ai-settings/openai-credentials` valida → retorna `key_set: true, key_preview: "sk-...x4Q2"`.
3. Enviar nova mensagem (test-incoming).
4. Inspecionar log Serilog (`{slug}_serilog_logs`): atributo `openai_key_source: "tenant"`.
5. `DELETE /api/ai-settings/openai-credentials` → próxima mensagem usa chave global (`openai_key_source: "global"`).

6. Alterar `context_window_messages` para 5 via `PUT /api/ai-settings`.
7. Enviar mensagem em thread com >10 mensagens prévias.

**Esperado**:
- `agent_activity_logs.input_tokens` cai significativamente vs. mensagem prévia (porque contexto enviado é menor — apenas 5 mensagens).
- Threshold inválido: `PUT /api/ai-settings {context_window_messages: 200}` → 400 `VALIDATION_FAILED`.

---

## QS-7 — Resiliência: falha OpenAI → retry → transbordo (US6, FR-018..021, SC-005)

> _Requer ambiente de teste com `IAssistantsApi` substituível por mock controlado. Disponível via DI flag `ASSISTANTS_API_FAULT_INJECTOR=true`._

1. Configurar mock para retornar 503 nas próximas 2 chamadas:
   ```sh
   curl -X POST .../internal/fault-injector \
     -d '{"openai_status_code":503,"count":2}'
   ```
2. Enviar mensagem (test-incoming).
3. Cronometrar.

**Esperado**:
- Primeiro pedido retorna 503.
- Após 3s, segundo pedido (retry) também 503.
- Após segundo failure, sistema:
  - Mongo log 1: `action=api_error, error.status=503`.
  - Mongo log 2: `action=api_error, error.status=503` (retry).
  - Mongo log 3: `action=transfer_to_human, handoff_target_department_id=<default>` (transbordo automático).
  - Outgoing enfileirado: "Estamos com uma instabilidade técnica no momento. Vou transferir você para um de nossos atendentes."
  - Ticket criado.
- Tempo total: < 10s.

4. Configurar mock para 401:
   ```sh
   curl -X POST .../internal/fault-injector \
     -d '{"openai_status_code":401,"count":1}'
   ```
5. Enviar mensagem.

**Esperado**:
- 1 doc `api_error` (sem retry) + 1 doc `transfer_to_human`. Tempo total: < 3s.

---

## QS-8 — Verificação de invariantes globais (cross-test)

Após executar QS-1..7, executar este script de verificação (`scripts/qs-verify.sh`):

```bash
#!/bin/bash
# Verifica invariantes da Spec 006 após bateria de quickstart.
psql "$PG" -c "SELECT COUNT(*) FROM tenant_qs_test.ai_agents WHERE type='orchestrator' AND deleted_at IS NULL;"  # = 1
psql "$PG" -c "SELECT COUNT(*) FROM tenant_qs_test.ai_threads;"                                                   # ≥ 3
mongosh --eval "db.qs-test_agent_activity_logs.find({}).count()" "$MONGO"                                         # > 0
redis-cli KEYS 'qs-test:playground:*' | wc -l                                                                     # = 0 após TTL
```

Tudo PASS → Spec 006 tem implementação consistente para o roteiro.

---

## Limites do quickstart V1

- **Sem WhatsApp**: somente Live Chat stub via `test-incoming` (Spec 008 entrega o caminho real).
- **Sem CRM real do atendente**: tickets aparecem no banco; a tela de fila/atribuição vem da Spec 005.
- **Sem distribuição automática**: o ticket fica `queued`; o round-robin da Spec 005 não roda automaticamente em tickets criados por IA porque o gateway de evento Redis ainda precisa ser consumido — pendência cruzada documentada em `cross-spec-pendencies.md` item 005-B.
