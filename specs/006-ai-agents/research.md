# Research — Spec 006 (Agentes de IA)

**Phase 0 output** — resolve unknowns e ancora decisões técnicas antes do Phase 1 (Design & Contracts).

Cada seção segue: **Decisão** · **Justificativa** · **Alternativas consideradas** · **Impacto na Spec 006** · _(quando aplicável)_ **Constitution touchpoint**.

---

## R1 — OpenAI: Assistants v2 vs. Responses/Chat Completions

**Decisão**: Usar **OpenAI Assistants API v2** via `openai-dotnet` 2.x (`OpenAI.Assistants.AssistantClient`).

**Justificativa**:
- A Spec 006 §2.2 e §7 exigem **thread persistente por conversa**, com handoff entre agentes preservando contexto sem reenviar histórico — comportamento nativo do modelo Assistants/Threads/Runs.
- `gpt-4o` é constituído (Tech Stack §AI engine) e Assistants v2 suporta `gpt-4o` com `tool_call`s (`function`).
- `openai-dotnet` 2.x expõe `AssistantClient`, `ThreadClient`, `RunClient` com tipagem forte e streaming opcional via `RunStream`.

**Alternativas consideradas**:
- **Chat Completions** + histórico mantido pelo backend — viola "thread persiste e backend não reenvia histórico" (Spec §2.2). Custo por token cresce linearmente.
- **Responses API** (preview na época da redação) — não-GA na época da redação; Assistants é estável.
- **AzureOpenAI Assistants** — adiciona Azure como dependência sem necessidade; constituição prefere OpenAI direto.

**Impacto**: Ver `Infrastructure/OpenAi/AssistantsApi.cs` no plano. As 4 tools são registradas no Assistant via `tools: [{type: function, function: {…}}]`.

---

## R2 — `gpt-4o` confirmado como default; `available_models` é allowlist por tenant

**Decisão**: Default global `OPENAI_DEFAULT_MODEL=gpt-4o`. Por agente, o tenant pode escolher modelo via `ai_agents.model`. A lista exibida no seletor é a interseção entre `tenants.ai_settings.available_models` e a lista global do sistema (`appsettings:OpenAi:GlobalAllowedModels`); se `available_models` está vazia, exibe a lista global.

**Justificativa**:
- Constituição fixa `gpt-4o` como engine padrão.
- Modelos disponíveis em Assistants v2: `gpt-4o`, `gpt-4o-mini`, `gpt-4-turbo`, `gpt-4.1`. A allowlist global protege contra o tenant escolher modelo legado/instável; a allowlist por tenant é controle adicional para tenants com restrição contratual.

**Alternativas consideradas**:
- Hardcode `gpt-4o` para todos sem permitir override — fere FR-024 e Spec §11 P1.
- Sem allowlist global — tenant pode escolher modelos legados que não suportam tool calls (`gpt-3.5`).

**Impacto**: Validador de criação de agente recusa modelo fora da interseção das allowlists.

---

## R3 — Arquitetura do `AgentOrchestrator` (state machine de turno)

**Decisão**: Por mensagem recebida, o `AgentOrchestrator.ProcessAsync(IncomingMessage)` segue **fluxo linear, sem state-machine externa**:

```
1. Adquire lock {slug}:agent_run:{conversation_id} (SET NX EX 60); se já existe, enfileira a mensagem para retry em 1 s.
2. Verifica idempotência {slug}:msg_idempo:{message_id}; se já processada, retorna.
3. Carrega/cria AiThread (1 thread OpenAI por conversa).
4. Verifica se conversa já está sob controle humano (handed_off_to_human_at != null) → envia auto-reply do sistema, sai.
5. Detecta palavra-chave de transbordo no texto puro (HandoffKeywordDetector). Se positivo → injeta system message no thread: "[INSTRUÇÃO DO SISTEMA] O cliente solicitou transbordo. Execute transfer_to_human imediatamente."
6. Resolve agente atual: AiThread.current_agent_id; se null → Orchestrator.
7. Resolve tenant API key (OpenAiKeyResolver: tenant > global).
8. Adiciona mensagem do usuário ao thread (threads.messages.create).
9. Cria run (runs.create) com Assistant correspondente ao agente atual.
10. Polling do run com timeout AI_RUN_TIMEOUT_SECONDS (default 30 s):
    - Se status=requires_action → tool_call → ToolCallDispatcher.
      - handoff_to_agent → atualiza current_agent_id; submit_tool_outputs com {success: true, agent: {name, description}}; volta ao polling no MESMO run (se runs.submit_tool_outputs reabre o mesmo run) — caso contrário, novo run no mesmo thread.
      - transfer_to_human → executa transbordo (cria ticket via gateway, marca handed_off_to_human_at, anexa mensagem ao thread como assistant message): "Vou transferir você para nossa equipe de [Departamento]. Aguarde um momento.", submit_tool_outputs, finaliza.
    - Se status=completed → extrai assistant message → enfileira em outgoing → grava activity log.
    - Se status=failed/cancelled/expired → trata como erro técnico → RetryPolicy.
11. Em qualquer exceção (HTTP 5xx/timeout/rate limit) → RetryPolicy: 1 retry após 3 s; se persistir → transbordo automático com motivo "Falha técnica no agente de IA" + mensagem do sistema "Estamos com uma instabilidade…"; loga em activity_logs com action=ApiError.
12. Em 401/403 → sem retry, transbordo imediato.
13. Libera lock.
```

**Justificativa**: Pseudo-stateful (lock + idempotência) sem framework de state machine (Statecharts/MediatR/Saga) — mantém complexidade local e testável com Testcontainers + mock HTTP.

**Alternativas consideradas**:
- MediatR + handlers — adiciona pacote sem benefício claro para fluxo linear.
- Workflow externo (Temporal) — overkill para V1.

**Impacto**: Toda lógica em `AgentOrchestrator`, sub-componentes nomeados em `Features/AgentRuntime/`.

---

## R4 — Execução de tool call `handoff_to_agent`: novo run vs. submit_tool_outputs

**Decisão**: Após `handoff_to_agent` ter sucesso, o backend faz `runs.submit_tool_outputs` retornando `{success: true, message: "Handoff realizado para <agent_name>. Aguarde a resposta dele."}`. Em seguida abre **novo run** no mesmo thread com o **Assistant do sub-agente destino**, sem reenviar mensagem do usuário (a última user message já está no thread).

**Justificativa**:
- O run atual está rodando com o Assistant origem. Mudar de Assistant exige novo run — a Assistants API não permite "trocar" o Assistant do run em andamento.
- O thread preserva integralmente o contexto, então o novo run lê o histórico completo (Spec FR-006).

**Alternativas consideradas**:
- Manter o mesmo run e fingir que o Assistant trocou — não suportado pela API.
- Forçar o sub-agente a "re-ler" o histórico via system message — ruidoso e desnecessário.

**Impacto**: `ToolCallDispatcher.HandleHandoffToAgent` retorna sinal para `AgentOrchestrator` reabrir loop de polling com o novo Assistant.

---

## R5 — Lock de execução por conversa

**Decisão**: Lock Redis `SET {slug}:agent_run:{conversation_id} <hangfire_job_id> NX EX 60`. Se a aquisição falhar, a mensagem volta à fila com delay de 1 s (Hangfire `Schedule(..., TimeSpan.FromSeconds(1))`), até 5 tentativas. Após 5 falhas, loga warning e descarta (TTL natural do lock = 60 s evita deadlock).

**Justificativa**:
- A Assistants API rejeita `runs.create` em thread com run ativo (`thread already has an active run`). Lock na camada do worker evita o erro.
- Stale locks são liberados pelo TTL.

**Alternativas consideradas**:
- Lock em Postgres (`SELECT FOR UPDATE`) — mais lento, prende conexão.
- Sem lock, tratando erro da OpenAI — frágil; o erro vem com latência alta, gastando o orçamento de retry.

**Impacto**: `IncomingMessageWorker` adquire/libera lock; falhas re-enfileiram com backoff.

---

## R6 — Idempotência de fila

**Decisão**: Cada `IncomingMessage` recebe `message_id` (UUID) gerado pelo adapter de canal. Antes de processar, o worker tenta `SET {slug}:msg_idempo:{message_id} 1 NX EX 86400`. Se a chave já existia (`NX` falhou), é re-entrega → ignora.

**Justificativa**:
- Hangfire pode re-entregar jobs em caso de falha do worker. Sem chave de idempotência, dispararíamos 2 runs.
- TTL 24 h é amplo o bastante para qualquer reentrega plausível.

**Alternativas consideradas**:
- Idempotência no banco (`processed_messages`) — mais lento, exige migration.
- Sem idempotência — risco de duplicação.

**Impacto**: `IncomingMessageWorker.ProcessAsync` faz check antes de qualquer trabalho.

---

## R7 — Resolução de chave OpenAI por tenant

**Decisão**: `OpenAiKeyResolver.ResolveAsync(tenantId)` faz:
1. Carrega `Tenant.OpenAiApiKeyEnc` do `public.tenants` (já existe).
2. Se vazio/null → retorna `OPENAI_API_KEY` do `IConfiguration` (env).
3. A descriptografia da chave do tenant usa `IDataProtectionProvider` da Spec 003 (mesmo provider que cifra outros segredos do tenant). _Reuso explícito — não há novo mecanismo de criptografia._

**Justificativa**:
- FR-025 + Spec §7.3 prioridade tenant > global.
- Reaproveita criptografia já em uso, conforme constituição IV (sem segredos em código).

**Alternativas consideradas**:
- Criptografia caseira — vedada pela constituição.
- Chave em variável de ambiente por tenant — não escala (centenas de tenants).

**Impacto**: `Infrastructure/OpenAi/OpenAiKeyResolver.cs`. Cada `AssistantsApi` instância recebe a chave por execução (não há singleton com chave fixa).

**Edge case**: tenant define chave inválida → 401 OpenAI → transbordo imediato (FR-019). Não cair para a chave global por questão de segurança/controle do tenant (Spec §10 acceptance).

---

## R8 — Substituição de variáveis de prompt

**Decisão**: Pré-processamento server-side **antes** de cada `runs.create` (Assistant v2 suporta `instructions` por run). Regex `\{\{(\w+)\}\}` resolve `company_name` (de `Tenant.RazaoSocial`/`NomeFantasia`), `department_name` (de `Department.Name` do agente, se sub-agente), `attendant_name` (vazio quando não há atendente atribuído).

**Justificativa**:
- Variáveis fora dessa lista permanecem literais — não bloqueia o run, mas registra warning.
- Resolução em runtime permite uso de `{{department_name}}` em instruções dinâmicas sem ter que regenerar o Assistant a cada mudança.
- Pré-processamento server-side garante que o playground também respeita variáveis (FR-012).

**Alternativas consideradas**:
- Substituir no momento de criar/atualizar o Assistant (estático) — perde-se valor dinâmico (`attendant_name` muda com a atribuição).
- Templating no SDK — `openai-dotnet` não tem.

**Impacto**: `PromptVariableSubstitutor.Apply(string prompt, AgentVariablesContext ctx)` retorna prompt resolvido; usado em `AgentOrchestrator` antes de `runs.create` com `instructions` override.

---

## R9 — Política de retry e timeouts

**Decisão**:

| Cenário | Política |
|---|---|
| Timeout HTTP > 30 s ou status 5xx | 1 retry após 3 s |
| Rate limit 429 | 1 retry após 3 s |
| Auth 401/403 | sem retry — transbordo imediato |
| Run status `failed`/`cancelled`/`expired` retornado pela OpenAI | sem retry — transbordo imediato (já é estado terminal) |
| Tool call malformada (parâmetros inválidos) | sem retry — registra activity_log e o agente recebe `submit_tool_outputs` com erro (oportunidade de auto-corrigir) |

**Justificativa**:
- FR-018/019/020 — política mínima viável. Polly seria sobre-engenharia para 1 retry simples.
- Run terminal = a IA não responderá; tentar novamente provavelmente gerará o mesmo resultado.
- Tool call malformada é problema de prompt; submetemos erro estruturado e o LLM tem chance de tentar outro caminho dentro do mesmo run.

**Alternativas consideradas**:
- Retry exponencial com 3 tentativas — dilatação de latência (até 21 s só de retry) ultrapassa SC-005 (10 s).
- Polly — pacote desnecessário.

**Impacto**: Classe `RetryPolicy` em `AgentRuntime/`; isolada por agent_run.

---

## R10 — `AiThread` como transitional bridge para Spec 007

**Decisão**: A spec atual cria a entidade `AiThread` em `tenant_{slug}.ai_threads` com:

| Campo | Tipo | Notas |
|---|---|---|
| `id` | uuid PK | gerado |
| `external_conversation_ref` | varchar(100) | id externo do canal (Live Chat session id, WhatsApp wa_id) — placeholder até Spec 007 |
| `openai_thread_id` | varchar(100) | id retornado por `threads.create` |
| `current_agent_id` | uuid (FK ai_agents) | nullable: null = Orchestrator OU humano |
| `handed_off_to_human_at` | timestamptz | nullable: marca o transbordo |
| `created_at`, `updated_at` | timestamptz | — |

Quando a Spec 007 introduzir `tenant_{slug}.conversations`, será gerada migration que move os campos `openai_thread_id`, `current_agent_id`, `handed_off_to_human_at` para `conversations` e remove `ai_threads`. A spec atual não persiste mensagens nem histórico — esse é trabalho da Spec 007.

**Justificativa**:
- Spec 006 foi planejada **antes** da 007 (DEPENDENCIES.md). Implementar o pipeline completo sem ter a entidade `Conversation` exigiria stub elaborado; é mais limpo criar bridge mínima.
- Permite que os workers funcionem fim-a-fim contra `IConversationGateway`/`ITicketCreationGateway` reais (não-mocks) durante os testes de integração desta spec.

**Alternativas consideradas**:
- Bloquear Spec 006 até que Spec 007 esteja pronta — quebraria a ordem de implementação do projeto.
- Persistir mensagens já em `ai_threads` — duplicaria o trabalho a ser feito na 007.

**Impacto**: Documentado em `data-model.md` + `cross-spec-pendencies.md`. Migration de remoção será emitida pela Spec 007.

---

## R11 — Worker Hangfire vs. processamento inline

**Decisão**: Usar **Hangfire** (já configurado nas Specs 003/005) com 2 filas dedicadas: `incoming_messages` e `outgoing_messages`. Cada uma com worker count = 4 (configurável via `HANGFIRE_WORKERS_INCOMING/OUTGOING`).

**Justificativa**:
- Mensagens podem ser bursty (ex.: webhook do WhatsApp em rajada).
- Worker isolado evita bloquear thread do request HTTP e respeita SLA do canal de origem.
- Constituição já estabeleceu Hangfire (Tech Stack §Background jobs).

**Alternativas consideradas**:
- Processamento inline na request — mata p95 e congestiona Kestrel.
- Channels do .NET (`System.Threading.Channels`) — perde durabilidade entre restarts.

**Impacto**: `IncomingMessageWorker` é classe Hangfire `[Queue("incoming_messages")]`; `OutgoingMessageWorker` análogo. Métricas Hangfire dashboard úteis para observability.

---

## R12 — Playground: thread temporário com TTL

**Decisão**: `POST /api/agents/{id}/test {message, session_id?}` opera assim:
1. Se `session_id` ausente → cria thread OpenAI novo + grava em Redis `{slug}:playground:{session_id}` com TTL 1800 s e retorna `session_id`.
2. Se `session_id` presente → lê do Redis e usa o thread existente.
3. Adiciona mensagem ao thread, cria run com Assistant do agente, faz polling, retorna texto da resposta.
4. **Não** persiste em `ai_threads` nem em `agent_activity_logs` (FR-026).
5. Quando a chave Redis expira, um job Hangfire de cleanup (recurring 1 h) chama `threads.delete` na OpenAI para liberar.

**Justificativa**:
- TTL via Redis dá auto-cleanup; cleanup job só apara o que ficou órfão.
- Não logar em activity_logs honra o invariante de FR-026/SC-012 (auditável: nada do playground em entidades persistentes).

**Alternativas consideradas**:
- Manter thread eternamente — ferimento à FR-027.
- Limpar thread síncrono no fechamento do diálogo de teste — frágil; usuário pode fechar a aba.

**Impacto**: `Playground/PlaygroundEndpoint.cs` + `PlaygroundCleanupJob.cs`.

---

## Resumo de NEEDS CLARIFICATION resolvidos

| Origem | Pergunta | Resolução |
|---|---|---|
| Spec §3.4 | "Logs em MongoDB — coleção segregada por tenant" | R-confirma: `{slug}_agent_activity_logs` (constituição I) |
| Spec §5.1 | "Lista de palavras-chave — PT-BR estática" | R-confirma; lista declarada em `HandoffKeywords.cs` (sem magic strings) |
| Spec §6.5 | "Thread temporário descartado ao sair da tela" | R12 — TTL 30 min + cleanup job |
| Spec §7.2 | "Handoff é mudar Assistant que processa próximo run" | R4 — submit_tool_outputs + novo run no mesmo thread |
| Spec §7.3 | "Key tenant > global" | R7 — `OpenAiKeyResolver` |
| Spec Edge Case | "Sub-agente cujo Assistant foi apagado externamente" | Detectado via 404 da OpenAI → recriado lazy via `EnsureAssistantAsync` antes do `runs.create` |
| Plan FR-031 | "Prompt-base do Orchestrator vem do template global" | Já implementado em `TenantProvisioningJob.CopyAgentTemplatesAsync` (Spec 003); este spec apenas cria a tabela `ai_agents` que aquele código já espera |

Nenhum NEEDS CLARIFICATION pendente entrando em Phase 1.
