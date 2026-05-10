# Spec 007 — Follow-up issues

Itens deferidos da Spec 007 que devem virar tickets/PRs separados. Ordenados por
prioridade (1 = mais crítico para experiência completa).

---

## P1 — Bloqueia funcionalidades naturais que o usuário esperaria

### 1. Spec 006: tool `end_conversation` para o Orchestrator

**Origem**: T162 (deferred), T163 (preparado).

**Estado atual**: a Spec 007 cobre apenas timeouts automáticos (8h IA / 24h humano).
Se a IA decide que a conversa terminou (cliente disse "obrigado, tchau"), ela hoje
envia uma mensagem de despedida mas não fecha a conversa.

**Solução**:
- Adicionar tool `end_conversation` na lista de tools do `AgentOrchestrator` (Spec 006).
- Quando a tool é invocada, chamar `POST /api/internal/livechat/conversations/{id}/end`
  (já implementado em `InternalEndConversationEndpoint` — só precisa do consumidor).
- Atualizar `ToolCallDispatcher` em Spec 006 para reconhecer e despachar a tool.

**Esforço**: ~1 sessão.

### 2. Resumed context no thread OpenAI (US4 polish)

**Origem**: T151, T152 (deferred).

**Estado atual**: `LiveChatConversationGateway.GetResumedContextAsync` retorna o tail
da conversa anterior corretamente. Mas o `AgentOrchestrator` (Spec 006) não consome
esse contexto antes do primeiro Run da nova thread OpenAI. Resultado: a IA "esquece"
o cliente quando ele retorna.

**Solução**:
- Estender `IncomingMessage` com `ResumedContext: IReadOnlyList<ConversationMessage>?`.
- Em `LiveChatIncomingAdapter.EnqueueAsync`, quando a conversa é nova e há resolved
  anterior do mesmo visitor, chamar `gateway.GetResumedContextAsync` e propagar.
- Em `AgentOrchestrator.ProcessAsync`, antes do `AppendUserMessageAsync`, se há
  contexto retomado, fazer N `AppendUserMessageAsync` com prefixo
  `[contexto anterior] {role}: {content}`.

**Esforço**: ~1 sessão (toca Spec 006).

---

## P2 — Robustez / observabilidade

### 3. Drop da tabela transitória `ai_threads`

**Origem**: contracts/conversation-gateway-impl.md §Mapeamento + Spec 006 cleanup.

**Estado atual**: `LiveChatConversationGateway` substitui `ChannelStubGateway` em DI,
mas a tabela `ai_threads` continua existindo no schema do tenant. `ChannelStubGateway`
permanece no código como fallback de testes.

**Solução**:
- Migration tenant-scoped que dropa `ai_threads`.
- Remover `ChannelStubGateway`, `AiThread` entity e `IAiThreadRepository`.
- Atualizar `TenantSchemaFixture.TruncateTenantTablesAsync` para remover a referência.
- Spec 006 tests que ainda usam `new AiThread(...)` (3 arquivos: `AgentTransbordoMessageTests`,
  `ToolCallDispatcherTransferToHumanTests`, `ToolCallDispatcherHandoffTests`) precisam
  migrar para Conversation seeds.

**Esforço**: ~1 sessão (cuidado: muitos testes).

### 4. Mongo `{slug}_widget_events` para audit trail

**Origem**: data-model §11 (out of V1).

**Estado atual**: eventos estruturados (handoff, resolve, abandonment) só ficam como
linhas em `messages` com `content_type=system_event`. Auditoria longitudinal é difícil
sem indexação adequada.

**Solução**:
- Coleção `{slug}_widget_events` no Mongo do tenant.
- `LiveChatOutgoingAdapter`, `WidgetDisableEnforcementJob`, `Abandonment/InactivitySweepJob`,
  `ResolveConversationCommand` emitem documentos com `{conversation_id, event_type,
  timestamp, actor, payload}`.
- Endpoint admin `GET /api/widget/audit?conversation_id=...` ou exportação CSV.

**Esforço**: ~1 sessão.

### 5. Live preview iframe na tela de configuração (US2 polish)

**Origem**: T119, T120, T121 (deferred).

**Estado atual**: admin precisa salvar e abrir `dev-test.html` em outra aba para ver o
efeito visual.

**Solução**:
- `widget.ts` aceita `?preview=1` e escuta `window.message` com `kind=omnidesk-preview-override`.
- `widget-preview.html` (servida pelo CRM Angular) carrega o widget com `?preview=1`.
- `live-chat-config.component.ts` ganha um `<iframe>` lateral; reactive form value
  changes → `iframe.contentWindow.postMessage({kind:'omnidesk-preview-override', payload})`.

**Esforço**: ~0.5 sessão.

---

## P3 — Polimento / tooling

### 6. Sub-componentes de tabs separados (US2)

**Origem**: T113-T118.

**Estado atual**: `live-chat-config.component.ts` consolida 6 tabs em um arquivo.

**Solução**: extrair cada `<p-tabPanel>` em um componente standalone — refatoração
cosmética, sem mudança de comportamento.

**Esforço**: ~0.5 sessão.

### 7. Sub-componentes do inbox (US3)

**Origem**: T142, T143.

**Estado atual**: `live-chat-inbox.component.ts` consolida lista + detalhe.

**Solução**: extrair `conversation-list.component.ts` + `conversation-detail.component.ts`.

**Esforço**: ~0.5 sessão.

### 8. Sidebar entries do CRM

**Origem**: T123 (live-chat-config), T144 (live-chat-inbox).

**Estado atual**: rotas registradas mas sem entrada no menu lateral porque o CRM
ainda não tem componente `layout/sidebar`.

**Solução**: depende da entrega de um shell de layout no CRM (provavelmente fora do
escopo da Spec 007).

### 9. WS inbound handlers no CRM (typing, send, resolve)

**Origem**: T136 (deferred).

**Estado atual**: `CrmWebSocketEndpoint` é outbound-only no V1; send/resolve usam REST.

**Solução**: handlers para `attendant.typing` (publica `chat.attendant_typing` no
canal do widget) + opcional WS-based send/resolve para reduzir um round-trip.

### 10. Backend tests JWT-aware

**Origem**: T053-T056 já implementados; T096-T098, T124-T128 stubbed.

**Estado atual**: testes do widget público (T053-T056) e validador (T097) cobrem
muito; tudo que precisa de JWT autêntico (admin GET/PUT/PATCH, inbox endpoints) está
como `[Fact(Skip)]`.

**Solução**: estender `Spec007WebFactory` para emitir JWTs sintéticos com claims
`sub`, `tenant_id`, `tenant_slug`, `dept_id` repetidos. Reutilizar `AuthorizationFixture`
do Spec 002/004 se possível.

**Esforço**: ~1 sessão.

### 11. CRM unit tests (Karma)

**Origem**: T100, T101, T129, T130.

**Estado atual**: workspace CRM Angular não tem Karma cabeado.

**Solução**: configurar `ng test` (gerar `karma.conf.js` + `test.ts`); escrever specs
para os signal stores (mais valor) + componentes (menos valor; muito DOM).

**Esforço**: ~1 sessão (setup) + por componente.
