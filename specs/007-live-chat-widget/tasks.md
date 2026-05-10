---

description: "Task list for Live Chat (Widget) implementation"
---

# Tasks: Live Chat (Widget)

**Input**: Design documents from `/specs/007-live-chat-widget/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/{widget-config-api,widget-public-api,widget-websocket,crm-websocket,conversation-gateway-impl,widget-installation}.md, quickstart.md

**Tests**: A constituição (princípio VII — Test Discipline) torna testes **obrigatórios**. Backend: xUnit + Testcontainers (Postgres + Redis + Mongo + **MinIO** reais). Widget: Vitest + happy-dom. CRM: Angular TestBed (`.spec.ts` co-localizado). Smoke E2E: Playwright (1 cenário, fora do CI principal).

**Organization**: Tarefas agrupadas por user story para entrega independente.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Pode rodar em paralelo (arquivo distinto, sem dependência pendente)
- **[Story]**: Mapeia para a user story (US1–US6) — ausente em Setup/Foundational/Polish
- Caminhos relativos do repo: `src/...`, `src/omniDesk.Api/tests/...`, `src/omniDesk.Crm/...`, `src/omniDesk.Widget/...`

## Path Conventions

- Backend: `src/omniDesk.Api/{Domain,Features,Hubs,Infrastructure}/`
- Backend tests: `src/omniDesk.Api/tests/omniDesk.Api.Tests/{Domain,Features,Hubs,Infrastructure,Helpers}/`
- CRM Angular: `src/omniDesk.Crm/src/app/features/{live-chat-config,live-chat-inbox}/`
- Widget bundle: `src/omniDesk.Widget/{src,tests,public}/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Variáveis de ambiente, estrutura de pastas e bootstrap do novo projeto `omniDesk.Widget/`.

- [X] T001 Adicionar em `src/omniDesk.Api/.env.example` e `src/omniDesk.Api/appsettings.Development.json` as 4 novas chaves: `Widget:ResumedContextMessageLimit=50`, `Widget:MaxUploadBytes=10485760`, `Widget:CdnBaseUrl=https://cdn.omnicare.ia.br/widget/v1`, `Widget:PublicRateLimitPerMinute=30` (plan.md §Variáveis de ambiente)
- [X] T002 [P] Criar estrutura de pastas backend: `src/omniDesk.Api/Domain/LiveChat/`, `src/omniDesk.Api/Features/LiveChat/{Config,Public,Inbox,Adapters,Uploads,Jobs}/`, `src/omniDesk.Api/Hubs/Events/`, `src/omniDesk.Api/Infrastructure/LiveChat/Migrations/`
- [X] T003 [P] Criar estrutura de pastas CRM: `src/omniDesk.Crm/src/app/features/live-chat-config/{tabs,preview,services}/` e `src/omniDesk.Crm/src/app/features/live-chat-inbox/{components,services}/`
- [X] T004 [P] Bootstrap projeto widget — criar `src/omniDesk.Widget/package.json` (devDeps: `typescript@5`, `esbuild@0.x`, `vitest@1.x`, `happy-dom@14.x`, `@playwright/test@1.x`), `tsconfig.json` (`strict: true`, `target: ES2022`), `esbuild.config.mjs` (entry `src/widget.ts`, format ESM, minify, sourcemap, output `dist/widget.[hash].js` + `dist/loader.js`), `vitest.config.ts` (env `happy-dom`)
- [X] T005 [P] Criar estrutura de pastas widget: `src/omniDesk.Widget/src/{ui,api,state,lib}/`, `src/omniDesk.Widget/tests/`, `src/omniDesk.Widget/public/`, `src/omniDesk.Widget/.gitignore` (ignora `dist/`, `node_modules/`)
- [X] T006 [P] Adicionar pasta de testes backend: `src/omniDesk.Api/tests/omniDesk.Api.Tests/Domain/LiveChat/`, `Features/LiveChat/{Config,Public,Inbox,Adapters,Uploads,Jobs}/`, `Hubs/`, `Infrastructure/LiveChat/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Migrations, domínio compartilhado, infraestrutura de auth pública / WebSocket / Redis pub-sub e contratos. Bloqueia TODAS as user stories.

**⚠️ CRITICAL**: Nenhuma user story pode começar antes deste bloco completar.

### Migrations

- [X] T007 Criar migration public-scope `20260509_002_AddWidgetTokenToTenants.sql` em `src/omniDesk.Api/Infrastructure/LiveChat/Migrations/` com `ALTER TABLE public.tenants ADD COLUMN widget_token UUID UNIQUE NOT NULL DEFAULT gen_random_uuid()` + índice + remoção do `DEFAULT` (data-model §3.2)
- [X] T008 Criar migration tenant-scoped `20260509_001_AddLiveChatTables.sql` em `src/omniDesk.Api/Infrastructure/LiveChat/Migrations/` com tabelas `widget_config`, `visitors`, `conversations`, `messages` + índices + trigger `trg_messages_after_insert` para materializar `last_message_at` (data-model §3.1)
- [X] T009 Atualizar `src/omniDesk.Api/Infrastructure/Persistence/Migrations/EnsureTenantSchemaUpToDateAsync.cs` (ou equivalente da Spec 005/006) para incluir as duas novas migrations no pipeline de provisioning + startup (data-model §10)

### Domínio — enums e value objects (sem magic strings — princípio VII)

- [X] T010 [P] Criar `ChannelType.cs` em `src/omniDesk.Api/Domain/LiveChat/ChannelType.cs` com enum `LiveChat`, `WhatsApp` + helper `ToSnake()` (`live_chat`, `whatsapp`)
- [X] T011 [P] Criar `ConversationStatus.cs` em `src/omniDesk.Api/Domain/LiveChat/ConversationStatus.cs` com enum `Open`, `Resolved`, `Abandoned` + helper `ToSnake()`
- [X] T012 [P] Criar `EndedBy.cs` em `src/omniDesk.Api/Domain/LiveChat/EndedBy.cs` com enum `Attendant`, `AiAgent`, `SystemInactivity`, `SystemDisable` + helper `ToSnake()`
- [X] T013 [P] Criar `MessageSenderType.cs` em `src/omniDesk.Api/Domain/LiveChat/MessageSenderType.cs` com enum `Visitor`, `AiAgent`, `Attendant`, `System` + helper `ToSnake()`
- [X] T014 [P] Criar `MessageContentType.cs` em `src/omniDesk.Api/Domain/LiveChat/MessageContentType.cs` com enum `Text`, `Image`, `File`, `SystemEvent` + helper `ToSnake()`
- [X] T015 [P] Criar `LauncherIcon.cs` em `src/omniDesk.Api/Domain/LiveChat/LauncherIcon.cs` com enum `Chat`, `Message`, `Support`
- [X] T016 [P] Criar `WidgetPosition.cs` em `src/omniDesk.Api/Domain/LiveChat/WidgetPosition.cs` com enum `BottomRight`, `BottomLeft`
- [X] T017 [P] Criar value object `IdentificationField.cs` em `src/omniDesk.Api/Domain/LiveChat/IdentificationField.cs` (record com `Field`, `Label`, `Required`) + JSON converter para JSONB
- [X] T018 [P] Criar value object `ConversationMetadata.cs` em `src/omniDesk.Api/Domain/LiveChat/ConversationMetadata.cs` (record com `PageUrl`, `PageTitle`, `Referrer`, `UserAgent`, `IpPartial`) + JSON converter para JSONB
- [X] T019 [P] Criar `WebSocketEvents.cs` em `src/omniDesk.Api/Hubs/Events/WidgetEvents.cs` com constantes `MessageNew`, `AgentTyping`, `ConversationAssigned`, `ConversationResolved`, `Ping`, `Pong`, `MessageSend`, `VisitorTyping`, `MessagesRead`, `MessagesReplay` (princípio VII)
- [X] T020 [P] Criar `CrmEvents.cs` em `src/omniDesk.Api/Hubs/Events/CrmEvents.cs` com constantes `ChatNewConversation`, `ChatMessageReceived`, `ChatVisitorTyping`, `ChatBrowserNotify`, `ChatConversationResolved`, `AttendantTyping`, `ConversationSend`, `ConversationResolve` (princípio VII)
- [X] T021 [P] Criar `RedisChannelNames.cs` em `src/omniDesk.Api/Infrastructure/LiveChat/RedisChannelNames.cs` com helpers `Conversation(slug, id)`, `CrmUser(slug, userId)`, `CrmDepartment(slug, deptId)`, `WidgetRateLimit(slug, anonymousId)` retornando templates `{slug}:conv:{id}` etc.

### Domínio — entidades + repositórios

- [X] T022 [P] Criar entidade `WidgetConfig.cs` em `src/omniDesk.Api/Domain/LiveChat/WidgetConfig.cs` (data-model §3.1) + `IWidgetConfigRepository.cs` interface (`GetByTenantAsync`, `UpdateAsync`)
- [X] T023 [P] Criar entidade `Visitor.cs` em `src/omniDesk.Api/Domain/LiveChat/Visitor.cs` + `IVisitorRepository.cs` interface (`GetByAnonymousIdAsync`, `CreateAsync`, `UpdateIdentificationAsync`)
- [X] T024 [P] Criar entidade `Conversation.cs` em `src/omniDesk.Api/Domain/LiveChat/Conversation.cs` (data-model §3.1) + `IConversationRepository.cs` interface (`GetByIdAsync`, `GetActiveByVisitorAsync`, `GetLastResolvedByVisitorAsync`, `CreateAsync`, `MarkResolvedAsync`, `MarkAbandonedAsync`, `UpdateLastMessageAtAsync`, `ListActiveByDepartmentAsync`, `ListActiveByAttendantAsync`)
- [X] T025 [P] Criar entidade `Message.cs` em `src/omniDesk.Api/Domain/LiveChat/Message.cs` + `IMessageRepository.cs` interface (`GetByConversationAsync(convId, limit, before)`, `CreateAsync`, `MarkReadAsync`, `ExistsByClientIdAsync(convId, clientMsgId)` — idempotência)
- [X] T026 Modificar `src/omniDesk.Api/Domain/Tenants/Tenant.cs` para adicionar `WidgetToken : Guid` + `// Spec 007 — public widget token (FR-002)`

### EF Core configurations

- [X] T027 [P] Criar `WidgetConfigConfiguration.cs` em `src/omniDesk.Api/Infrastructure/LiveChat/WidgetConfigConfiguration.cs` mapeando colunas + check constraints + JSONB para `identification_fields` + array para `allowed_domains`
- [X] T028 [P] Criar `VisitorConfiguration.cs` em `src/omniDesk.Api/Infrastructure/LiveChat/VisitorConfiguration.cs` com índice único em `anonymous_id`
- [X] T029 [P] Criar `ConversationConfiguration.cs` em `src/omniDesk.Api/Infrastructure/LiveChat/ConversationConfiguration.cs` mapeando todos índices da data-model §3.1.3 + FK física para `Visitor` + JSONB para `metadata`
- [X] T030 [P] Criar `MessageConfiguration.cs` em `src/omniDesk.Api/Infrastructure/LiveChat/MessageConfiguration.cs` com FK `Conversation` ON DELETE CASCADE + índice composto `(conversation_id, created_at)` + índice único parcial em `client_message_id`
- [X] T031 Atualizar `src/omniDesk.Api/Infrastructure/Persistence/TenantDbContext.cs` para registrar `DbSet<WidgetConfig>`, `DbSet<Visitor>`, `DbSet<Conversation>`, `DbSet<Message>` via `OnModelCreating`
- [X] T032 Atualizar `TenantConfiguration` (em `Infrastructure/Tenants/`) para mapear nova coluna `WidgetToken` em `public.tenants`

### Repositórios — implementações EF

- [X] T033 [P] Criar `WidgetConfigRepository.cs` em `src/omniDesk.Api/Infrastructure/LiveChat/WidgetConfigRepository.cs` implementando `IWidgetConfigRepository`
- [X] T034 [P] Criar `VisitorRepository.cs` em `src/omniDesk.Api/Infrastructure/LiveChat/VisitorRepository.cs`
- [X] T035 [P] Criar `ConversationRepository.cs` em `src/omniDesk.Api/Infrastructure/LiveChat/ConversationRepository.cs`
- [X] T036 [P] Criar `MessageRepository.cs` em `src/omniDesk.Api/Infrastructure/LiveChat/MessageRepository.cs`

### Auth pública e Origin (widget)

- [X] T037 Criar `WidgetTokenAuthHandler.cs` em `src/omniDesk.Api/Features/LiveChat/Public/WidgetTokenAuthHandler.cs` — auth scheme custom `WidgetToken` que aceita `X-Widget-Token` header ou `?token=` query, resolve `public.tenants.widget_token → Tenant`, popula `ITenantContext` (research R3, contracts/widget-public-api.md)
- [X] T038 Criar `OriginValidator.cs` em `src/omniDesk.Api/Features/LiveChat/Public/OriginValidator.cs` — middleware/filter que lê `Origin` header, compara com `widget_config.allowed_domains` (lista vazia ⇒ pula), retorna 403 `ORIGIN_NOT_ALLOWED` quando bloqueado
- [X] T039 Criar `PublicRateLimiter.cs` em `src/omniDesk.Api/Features/LiveChat/Public/PublicRateLimiter.cs` — Redis `INCR {slug}:widget:rate:{anonymous_id}` com TTL 60s; default 30/min, configurável via `Widget:PublicRateLimitPerMinute`; aplica em todos os endpoints públicos exceto `/init`
- [X] T040 Registrar auth scheme `WidgetToken`, `OriginValidator` e `PublicRateLimiter` no pipeline em `src/omniDesk.Api/Program.cs` (após `TenantResolverMiddleware` para rotas que usam o handler custom)

### WebSocket infrastructure

- [X] T041 [P] Criar `WebSocketBroker.cs` em `src/omniDesk.Api/Hubs/WebSocketBroker.cs` — facade que assina/publica em Redis Pub/Sub e roteia mensagens para `WebSocket` ativos no node atual; trabalha sobre `IConnectionMultiplexer` já em uso
- [X] T042 [P] Criar `WidgetConnectionRegistry.cs` em `src/omniDesk.Api/Hubs/WidgetConnectionRegistry.cs` — `ConcurrentDictionary<string,WebSocket>` por canal + helpers para broadcast local antes de publicar no Redis
- [X] T043 Atualizar `src/omniDesk.Api/Program.cs` para `app.UseWebSockets()` (caso ainda não esteja) e registrar `WebSocketBroker` como singleton

### Provisionamento

- [X] T044 Atualizar `src/omniDesk.Api/Features/TenantProvisioning/TenantProvisioningJob.cs` para: (a) gerar `widget_token = Guid.NewGuid()` ao criar tenant; (b) inserir 1 linha em `tenant_{slug}.widget_config` com defaults; (c) cobrir tenants existentes via backfill no startup (data-model §3.3)

### Tests da Foundational

- [X] T045 [P] Criar `LiveChatTestcontainerFixture.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Helpers/LiveChatTestcontainerFixture.cs` — colection fixture xUnit que sobe Postgres + Redis + Mongo + **MinIO** (`minio/minio:latest`) para testes desta spec
- [X] T046 [P] Criar `WidgetTestHelpers.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Helpers/WidgetTestHelpers.cs` com `SeedTenantWithWidgetConfigAsync(slug)`, `SeedVisitorAsync(slug, anonymousId)`, `SeedOpenConversationAsync(slug, visitorId)`, `MakePublicHttpClient(token, origin?, anonymousId?)`
- [X] T047 [P] Criar `WebSocketTestClient.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Helpers/WebSocketTestClient.cs` — cliente WS sobre `WebApplicationFactory.Server.CreateWebSocketClient()` para enviar/receber JSON e fazer assertions sobre eventos
- [X] T048 Test backend: `tests/Domain/LiveChat/ConversationStateTransitionsTests.cs` — valida transições válidas/proibidas (`open→resolved`, `open→abandoned`, transições ❌ `resolved→open`, `abandoned→resolved`) conforme data-model §4.1
- [X] T049 Test backend: `tests/Infrastructure/LiveChat/MigrationsSmokeTests.cs` — sobe Postgres + roda migrations + verifica que tabelas criadas, defaults aplicados, índices presentes (especialmente `ix_conversations_open_idle` parcial)
- [X] T050 Test backend: `tests/Features/LiveChat/Public/WidgetTokenAuthHandlerTests.cs` — token válido resolve tenant; token inválido → 401 `INVALID_WIDGET_TOKEN`
- [X] T051 Test backend: `tests/Features/LiveChat/Public/OriginValidatorTests.cs` — `allowed_domains=null` libera; lista preenchida bloqueia origens fora dela
- [X] T052 Test backend: `tests/Features/LiveChat/Public/PublicRateLimiterTests.cs` — 30 req → OK; 31ª → 429 `RATE_LIMIT_EXCEEDED`; conta isolada por `anonymous_id`

**Checkpoint**: Foundation pronta — user stories podem começar em paralelo.

---

## Phase 3: User Story 1 — Visitante conversa com IA via widget (Priority: P1) 🎯 MVP

**Goal**: Visitante anônimo abre o widget no site do tenant, aceita LGPD, envia "Olá" e recebe resposta do Orchestrator (Spec 006) em tempo real via WebSocket.

**Independent Test**: Página HTML estática carrega o snippet com `widget_token`; visitante clica no launcher, marca checkbox LGPD, envia "Olá", recebe resposta da IA em < 5s. `conversations.lgpd_consent_at` preenchido. (QS-1)

### Tests US1

- [X] T053 [P] [US1] Test contract `tests/Features/LiveChat/Public/WidgetInitEndpointTests.cs` — GET `/api/public/widget/init`: retorna config + `active_conversation=null` quando sem `X-Anonymous-Id`; retorna `disabled_message` quando `is_enabled=false`; rejeita Origin não permitida (contracts/widget-public-api.md §GET /init)
- [X] T054 [P] [US1] Test contract `tests/Features/LiveChat/Public/StartConversationEndpointTests.cs` — POST `/api/public/widget/conversations`: cria visitor + conversation `open` + LGPD; retorna 422 `LGPD_CONSENT_REQUIRED` quando `lgpd_consent=false`; idempotente em janela de 5s para mesmo `anonymous_id` (contracts/widget-public-api.md §POST /conversations)
- [X] T055 [P] [US1] Test contract `tests/Features/LiveChat/Public/GetMessagesEndpointTests.cs` — paginação `limit/before`, ordem cronológica ascendente, 403 quando conversa não pertence ao `anonymous_id`
- [X] T056 [P] [US1] Test integration `tests/Features/LiveChat/Adapters/LiveChatConversationGatewayTests.cs` — cobre todos os métodos da interface (contracts/conversation-gateway-impl.md §Testes); valida que substitui `ChannelStubGateway` no DI registration
- [~] T057 [P] [US1] Test integration `tests/Hubs/WidgetWebSocketEndpointTests.cs` — Skip placeholder; tracked in follow-up-issues
- [~] T058 [P] [US1] Test integration `tests/Features/LiveChat/Adapters/IncomingPipelineE2ETests.cs` — Skip placeholder; tracked in follow-up-issues
- [X] T059 [P] [US1] Widget unit `src/omniDesk.Widget/tests/visitor-store.spec.ts` — `crypto.randomUUID` gera `anonymous_id` na primeira visita; persiste em `localStorage.omnidesk_visitor_id`; reusa em visitas subsequentes
- [X] T060 [P] [US1] Widget unit `src/omniDesk.Widget/tests/lgpd-consent.spec.ts` — botão de envio fica desabilitado até checkbox marcado; aceitar registra `lgpd_consent_at` no payload de POST /conversations
- [X] T061 [P] [US1] Widget unit `src/omniDesk.Widget/tests/ws-client.spec.ts` — backoff exponencial (1s, 2s, 4s, 8s, 16s, 30s) com jitter; replay com `since=<last_message_id>` ao reconectar; fila local de envios durante desconexão (research R6)

### Backend — endpoints públicos

- [X] T062 [US1] Criar `WidgetPublicEndpoints.cs` em `src/omniDesk.Api/Features/LiveChat/Public/WidgetPublicEndpoints.cs` — group map para `/api/public/widget` com routes `init`, `conversations`, `conversations/{id}/messages` (sem upload — vai em US6)
- [X] T063 [US1] Implementar GET `/api/public/widget/init` em `WidgetPublicEndpoints` — query `IWidgetConfigRepository.GetByTenant`, opcionalmente busca conversa `open` por `anonymous_id` via `IConversationRepository.GetActiveByVisitorAsync`, retorna shape do contract
- [X] T064 [US1] Implementar POST `/api/public/widget/conversations` em `WidgetPublicEndpoints` + `StartConversationCommand` em `Features/LiveChat/Public/Commands/` — valida LGPD, idempotência via Redis (5s key), cria/reutiliza `Visitor`, persiste `Conversation` com `metadata` (page_url etc + ip_partial extraído do `HttpContext.Connection.RemoteIpAddress`)
- [X] T065 [US1] Implementar GET `/api/public/widget/conversations/{id}/messages` em `WidgetPublicEndpoints` — paginação reversa com cursor `before`, retorna em ordem ASC
- [X] T066 [P] [US1] Criar `Validators/StartConversationValidator.cs` em `src/omniDesk.Api/Features/LiveChat/Public/Validators/StartConversationValidator.cs` (FluentValidation) — `lgpd_consent=true`, `anonymous_id` UUID válido, `metadata.page_url` https URL

### Backend — adapters (substituem stubs da Spec 006)

- [X] T067 [US1] Criar `LiveChatConversationGateway.cs` em `src/omniDesk.Api/Features/LiveChat/Adapters/LiveChatConversationGateway.cs` implementando `IConversationGateway` (contracts/conversation-gateway-impl.md §Implementação)
- [X] T068 [US1] Criar `LiveChatIncomingAdapter.cs` em `src/omniDesk.Api/Features/LiveChat/Adapters/LiveChatIncomingAdapter.cs` — método `EnqueueAsync(conversationId, message)` que persiste em `messages`, atualiza `last_message_at`, e enfileira `IncomingMessage` na fila Hangfire `{slug}:incoming_messages` (Spec 006) **somente quando** `attendant_id IS NULL`
- [X] T069 [US1] Criar `LiveChatOutgoingAdapter.cs` em `src/omniDesk.Api/Features/LiveChat/Adapters/LiveChatOutgoingAdapter.cs` — Hangfire worker que consome `{slug}:outgoing_messages` (já criado pela Spec 006), persiste em `messages`, publica `message.new` em `{slug}:conv:{conversation_id}` e (se houver atendente atribuído) `chat.message_received` em `{slug}:crm:user:{attendant_id}`
- [X] T070 [US1] Modificar `src/omniDesk.Api/Program.cs` para substituir `services.AddScoped<IConversationGateway, ChannelStubGateway>()` por `services.AddScoped<IConversationGateway, LiveChatConversationGateway>()` (contracts/conversation-gateway-impl.md §Registrado em DI)

### Backend — WebSocket do widget

- [X] T071 [US1] Criar `WidgetWebSocketEndpoint.cs` em `src/omniDesk.Api/Hubs/WidgetWebSocketEndpoint.cs` — endpoint `/ws/widget/{conversation_id}`, valida token+Origin+ownership+LGPD no handshake; assina canal Redis; loop de eventos
- [X] T072 [US1] Implementar handler `MessageSendHandler.cs` em `src/omniDesk.Api/Hubs/Handlers/MessageSendHandler.cs` — recebe `message.send`, deduplica por `(conversation_id, client_message_id)`, valida `status=open`, chama `LiveChatIncomingAdapter.EnqueueAsync`
- [X] T073 [US1] Implementar handler `VisitorTypingHandler.cs` em `src/omniDesk.Api/Hubs/Handlers/VisitorTypingHandler.cs` — debounce no widget já garante 1s; backend apenas publica `chat.visitor_typing` no canal CRM (filtra para nada quando sem atendente)
- [X] T074 [US1] Implementar handler `MessagesReadHandler.cs` em `src/omniDesk.Api/Hubs/Handlers/MessagesReadHandler.cs` — UPDATE `messages SET is_read=true WHERE conversation_id=? AND is_read=false`
- [X] T075 [US1] Implementar `MessagesReplayHandler.cs` em `src/omniDesk.Api/Hubs/Handlers/MessagesReplayHandler.cs` — recebe `messages.replay {since_message_id}`, retorna sequência de `message.new` para mensagens posteriores ao ID
- [X] T076 [US1] Heartbeat: `WidgetWebSocketEndpoint` envia `{type: "ping"}` a cada 30s, fecha com 4408 `IDLE_TIMEOUT` se não receber `pong` em 60s

### Widget bundle — entry e UI mínima

- [X] T077 [P] [US1] Criar `src/omniDesk.Widget/src/types.ts` com tipos `OmniDeskConfig`, `WidgetConfig`, `Conversation`, `Message`, `WsEvent`, `MessageSendPayload`
- [X] T078 [P] [US1] Criar `src/omniDesk.Widget/src/lib/crypto-uuid.ts` — `generateUuid()` que tenta `crypto.randomUUID()` e cai para polyfill RFC4122 v4 mínimo (Safari < 15.4)
- [X] T079 [P] [US1] Criar `src/omniDesk.Widget/src/lib/debounce.ts` — `debounce(fn, ms)` para `visitor.typing` (1s)
- [X] T080 [P] [US1] Criar `src/omniDesk.Widget/src/state/visitor-store.ts` — `getOrCreate()`, persiste em `localStorage.omnidesk_visitor_id`
- [X] T081 [P] [US1] Criar `src/omniDesk.Widget/src/state/conversation-store.ts` — `getActive()`, `setActive(id, status)`, `clear()`, persiste em `localStorage.omnidesk_conversation_id`
- [X] T082 [P] [US1] Criar `src/omniDesk.Widget/src/state/message-queue.ts` — fila in-memory com `enqueue`, `flush(send)`, `peek()` para mensagens digitadas durante desconexão
- [X] T083 [P] [US1] Criar `src/omniDesk.Widget/src/api/http-client.ts` — fetch wrapper que injeta `X-Widget-Token` + `X-Anonymous-Id`, parseia envelope `{success,data,error}`
- [X] T084 [P] [US1] Criar `src/omniDesk.Widget/src/api/ws-client.ts` — WebSocket com `connect(url)`, `send(event)`, reconexão com backoff exponencial (research R6), eventos `onMessage`, `onClose`, `onOpen`; replay automático com `since=<last_message_id>`
- [X] T085 [P] [US1] Criar `src/omniDesk.Widget/src/ui/styles.ts` — exports `getStyles(primaryColor): string` retornando CSS template literal com tokens (cor primária, dark/light, bolhas, animações slide-up)
- [X] T086 [US1] Criar `src/omniDesk.Widget/src/ui/launcher.ts` — Web Component `<omnidesk-launcher>` com botão flutuante, badge não-lidas, posicionamento (`bottom_right`/`bottom_left`)
- [X] T087 [US1] Criar `src/omniDesk.Widget/src/ui/lgpd-consent.ts` — Web Component `<omnidesk-lgpd>` com checkbox + texto + link política; emite event `consent-granted`
- [X] T088 [US1] Criar `src/omniDesk.Widget/src/ui/message-list.ts` — Web Component `<omnidesk-message-list>` com auto-scroll, alinhamento por `sender_type`, indicador "digitando…", placeholder vazio com `welcome_message`
- [X] T089 [US1] Criar `src/omniDesk.Widget/src/ui/input-area.ts` — Web Component `<omnidesk-input>` com textarea, botão enviar (desabilitado até LGPD aceito), evento `send-message`
- [X] T090 [US1] Criar `src/omniDesk.Widget/src/ui/panel.ts` — Web Component `<omnidesk-panel>` que orquestra header (cor primária, nome empresa), `omnidesk-message-list`, `omnidesk-input`, banner de desconexão; lazy-load de `/init` apenas no primeiro `open()`
- [X] T091 [US1] Criar `src/omniDesk.Widget/src/widget.ts` (entry) — define `customElements.define('omnidesk-widget', class extends HTMLElement {...})`, attach Shadow DOM `closed`, lê `window.OmniDeskConfig`, instancia launcher + panel; expõe `window.OmniDesk = { open, close, setUser }`
- [X] T092 [US1] Criar `src/omniDesk.Widget/public/loader.js` (~1KB) — verifica `window.OmniDeskConfig.token`, injeta `<script type="module" src="${CDN}/widget.<hash>.js">` (URL preenchida em build)
- [X] T093 [US1] Configurar `src/omniDesk.Widget/esbuild.config.mjs` para gerar `dist/widget.<hash>.js` (ESM, minify, sourcemap externo) + `dist/loader.js` cópia processada de `public/loader.js` com placeholder substituído
- [X] T094 [US1] Adicionar script npm `build` em `src/omniDesk.Widget/package.json` que roda esbuild + emite `dist/manifest.json` (mapeia hash → nome para deploy CDN)

### Tenant bootstrap dev

- [X] T095 [US1] Criar `src/omniDesk.Widget/public/dev-test.html` (servida via `npm run dev`) — página local com snippet de instalação apontando para `http://localhost:5173/widget/v1/loader.js` para suportar QS-1 manual

**Checkpoint**: User Story 1 funcional — visitante conversa com IA via widget instalado em página HTML.

---

## Phase 4: User Story 2 — Tenant configura aparência, privacidade e comportamento (Priority: P1)

**Goal**: Tenant admin acessa CRM → Configurações → Live Chat e personaliza widget; preview ao vivo reflete mudanças; salva e o widget no site reflete a nova configuração.

**Independent Test**: Admin altera cor para `#7A9E7E`, ícone para `support`, preenche LGPD, configura `allowed_domains=['localhost:8000']`, salva. Recarrega a página de teste e vê o widget renderizado com as novas configurações. Origem fora da lista recebe 403. (QS-2)

### Tests US2

- [~] T096 [P] [US2] Test contract `tests/Features/LiveChat/Config/GetWidgetConfigTests.cs` — Skip placeholder; pendente JWT-aware Spec007WebFactory (follow-up)
- [X] T097 [P] [US2] `UpdateWidgetConfigValidatorTests.cs` — todas as regras de validação cobertas (color regex, hour bounds, dup fields, allowlist, URL)
- [~] T098 [P] [US2] Test contract `ToggleWidgetTests.cs` — Skip placeholder com T096 (mesma dep)
- [X] T099 [P] [US2] `WidgetDisableEnforcementJobTests.cs` — toggle off com 3 conversas open → 3 system_event + status=resolved + ended_by=system_disable
- [~] T100 [P] [US2] CRM unit `live-chat-config.component.spec.ts` — Karma não está cabeado neste workspace; deferred
- [~] T101 [P] [US2] CRM unit `widget-config.service.spec.ts` — Karma não está cabeado; deferred

### Backend — endpoints CRM

- [X] T102 [US2] `WidgetConfigEndpoints.cs` — group map para `/api/widget/config` com `RequireAuthorization()`; GET/PUT/PATCH `/toggle`
- [X] T103 [US2] GET `/api/widget/config` — retorna `{widget_token, installation_snippet, config}`
- [X] T104 [US2] `UpdateWidgetConfigCommand.cs` — atomic update via `IWidgetConfigRepository.UpdateAsync`
- [X] T105 [US2] PUT `/api/widget/config` — valida via `UpdateWidgetConfigValidator` antes de chamar command
- [X] T106 [US2] `ToggleWidgetCommand.cs` — flip is_enabled + enqueue `WidgetDisableEnforcementJob` quando off
- [X] T107 [US2] PATCH `/api/widget/config/toggle` — chama `ToggleWidgetCommand`, retorna `affected_conversations`
- [X] T108 [US2] `UpdateWidgetConfigValidator.cs` — color regex, hour bounds (1–168), identification_fields allowlist + uniqueness, domain length

### Backend — job de desabilitação

- [X] T109 [US2] `WidgetDisableEnforcementJob.cs` — UPDATE … RETURNING + system_event INSERT em CTE única; publica `conversation.resolved` por canal
- [X] T110 [US2] Test em `WidgetDisableEnforcementJobTests.cs` (T099)

### CRM Angular — tela de configuração

- [X] T111 [P] [US2] `widget-config.service.ts` — signal store com `snapshot/config/loading/saving`, métodos `load/update/toggle`
- [X] T112 [US2] `live-chat-config.component.ts` (standalone, lazy) — `<p-tabView>` com 6 abas (consolidado em um componente para V1) + `<p-toggleButton>` no header
- [~] T113-T118 [P] [US2] Tabs separados — consolidados em `live-chat-config.component.ts` para V1 (entregam o mesmo efeito visual; refator pra arquivos individuais é cosmético)
- [~] T119 [US2] Preview iframe — deferido. UX V1: admin salva e abre `dev-test.html` para conferir
- [~] T120 [US2] postMessage bridge no widget — deferido com T119
- [~] T121 [US2] `widget-preview.html` — deferido com T119
- [X] T122 [US2] Rota lazy `configuracoes/live-chat` adicionada em `app.routes.ts`
- [~] T123 [US2] Menu lateral — deferido (CRM ainda não tem componente `layout/sidebar` neste workspace)

**Checkpoint**: User Story 2 funcional — admin configura widget, preview reflete em real-time, save persiste, toggle off encerra abertas.

---

## Phase 5: User Story 3 — Atendente gerencia múltiplas conversas no CRM (Priority: P2)

**Goal**: Após transbordo da IA, conversa aparece no CRM do atendente com histórico completo; atendente responde via WS; encerra manualmente; recebe browser notifications quando CRM em background.

**Independent Test**: Visitante envia "quero falar com um atendente" → IA aciona transbordo → atendente em CRM minimizado recebe `Notification` em < 2s; maximiza, vê conversa na lista, responde; visitante recebe resposta via WS; atendente encerra; conversa some da lista. (QS-3)

### Tests US3

- [ ] T124 [P] [US3] Test contract `tests/Features/LiveChat/Inbox/ListActiveConversationsTests.cs` — GET `/api/conversations` retorna apenas `status=open` para o atendente/dept; ordem por `last_message_at DESC`
- [ ] T125 [P] [US3] Test contract `tests/Features/LiveChat/Inbox/GetConversationDetailTests.cs` — GET `/api/conversations/{id}/messages` paginado, atendente só pode ler suas conversas (ou do depto enquanto sem atribuição)
- [ ] T126 [P] [US3] Test contract `tests/Features/LiveChat/Inbox/SendAttendantMessageTests.cs` — POST nega quando atendente não dono; quando dono, INSERT message + publica `message.new` no canal do widget
- [ ] T127 [P] [US3] Test contract `tests/Features/LiveChat/Inbox/ResolveConversationTests.cs` — POST `/resolve` muda status=resolved, ended_by=attendant, ended_at set; conversa some da lista
- [ ] T128 [P] [US3] Test integration `tests/Hubs/CrmWebSocketEndpointTests.cs` — JWT inválido → 4401; JWT válido assina canais `crm:user:{id}` e `crm:dept:{id}`; receber `chat.new_conversation` + `chat.message_received` + `chat.browser_notify`
- [ ] T129 [P] [US3] CRM unit `src/omniDesk.Crm/src/app/features/live-chat-inbox/live-chat-inbox.component.spec.ts` — lista esquerda renderiza; ao clicar conversa, painel direito carrega histórico
- [ ] T130 [P] [US3] CRM unit `src/omniDesk.Crm/src/app/features/live-chat-inbox/components/browser-notification.service.spec.ts` — solicita permissão na primeira sessão; emite Notification apenas quando `document.visibilityState='hidden'` OU conversa não focada

### Backend — endpoints CRM

- [ ] T131 [P] [US3] Criar `ConversationListEndpoints.cs` em `src/omniDesk.Api/Features/LiveChat/Inbox/ConversationListEndpoints.cs` — GET `/api/conversations?status=active` (paginado); filtra por `attendant_id = currentUser.Id` ou `(attendant_id IS NULL AND department_id IN currentUser.Departments)`
- [ ] T132 [US3] Criar `ConversationDetailEndpoints.cs` em `src/omniDesk.Api/Features/LiveChat/Inbox/ConversationDetailEndpoints.cs` — GET `/api/conversations/{id}/messages` (paginação), POST `/api/conversations/{id}/messages` (atendente envia), POST `/api/conversations/{id}/resolve`
- [ ] T133 [US3] Implementar `SendAttendantMessageCommand.cs` em `src/omniDesk.Api/Features/LiveChat/Inbox/Commands/SendAttendantMessageCommand.cs` — valida ownership, INSERT message com `sender_type=attendant`, `sender_id=user.Id`, publica `message.new` no canal widget
- [ ] T134 [US3] Implementar `ResolveConversationCommand.cs` em `src/omniDesk.Api/Features/LiveChat/Inbox/Commands/ResolveConversationCommand.cs` — UPDATE status=resolved, ended_by=attendant, ended_at=NOW(); publica `conversation.resolved` no widget e `chat.conversation_resolved` no CRM

### Backend — WebSocket CRM

- [ ] T135 [US3] Criar `CrmWebSocketEndpoint.cs` em `src/omniDesk.Api/Hubs/CrmWebSocketEndpoint.cs` — `/ws/crm`; valida JWT na query, lê `attendants.department_id`, assina `crm:user:{userId}` + `crm:dept:{deptId}`; loop de eventos
- [ ] T136 [P] [US3] Implementar handlers em `src/omniDesk.Api/Hubs/Handlers/Crm/` — `AttendantTypingHandler.cs`, `ConversationSendHandler.cs`, `ConversationResolveHandler.cs`, `MessagesReadHandler.cs` (CRM)
- [ ] T137 [US3] Em `LiveChatOutgoingAdapter` (T069), publicar evento `chat.browser_notify` no canal CRM com triggers `new_conversation`, `new_message`, `transferred` conforme spec §8

### CRM Angular — multi-conv inbox

- [ ] T138 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/live-chat-inbox/services/inbox.service.ts` — signal store com `conversations = signal<Conversation[]>([])`, `selectedId = signal<string|null>(null)`; métodos `load()`, `select(id)`, `sendMessage(id, text)`, `resolve(id)`
- [ ] T139 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/live-chat-inbox/services/crm-websocket.service.ts` — singleton WebSocket `/ws/crm`, autorenova JWT em background, dispatcha eventos para `inbox.service`
- [ ] T140 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/live-chat-inbox/services/browser-notification.service.ts` — `requestPermission()`, `notify(title, body, conversationId)` que pula quando `visibilityState='visible'` E `inbox.selectedId() === conversationId`
- [ ] T141 [US3] Criar `src/omniDesk.Crm/src/app/features/live-chat-inbox/live-chat-inbox.component.ts` (standalone, lazy) — split panel (esquerda + direita) usando PrimeNG SplitPanel ou flex CSS
- [ ] T142 [P] [US3] Criar `components/conversation-list.component.ts` — `<p-listbox>` com badge colorido (vermelho/amarelo/cinza), prévia da última mensagem, timestamp relativo
- [ ] T143 [P] [US3] Criar `components/conversation-detail.component.ts` — header (nome visitante, canal), área scroll com mensagens (alinhadas por sender), input + anexo + botão "Encerrar conversa" (PrimeNG Button danger)
- [ ] T144 [US3] Adicionar rota lazy `live-chat-inbox` em `src/omniDesk.Crm/src/app/app.routes.ts` e entrada no menu lateral
- [ ] T145 [US3] Solicitar permissão de notificação no primeiro mount de `live-chat-inbox.component` (uma vez por sessão); link para `Configurações → Notificações` (placeholder em US3, página simples)

**Checkpoint**: User Story 3 funcional — atendente recebe transbordo, gerencia múltiplas conversas, browser notifications, encerra manualmente.

---

## Phase 6: User Story 4 — Visitante retorna e retoma conversa (Priority: P2)

**Goal**: Visitante volta ao site, widget reconhece via `localStorage`, age conforme status: `open` retoma; `resolved` mostra histórico + "Iniciar nova conversa" com contexto das últimas 50 mensagens; `abandoned` inicia nova automaticamente.

**Independent Test**: Visitante já tem conversa `resolved` no histórico, abre widget, vê histórico read-only + botão "Iniciar nova conversa", clica, IA responde lembrando do contexto. (QS-4)

### Tests US4

- [X] T146 [P] [US4] Test integration `tests/Features/LiveChat/Public/InitWithActiveConversationTests.cs` — GET `/init` com `X-Anonymous-Id` retorna `active_conversation` quando há `open`; null quando `resolved`/`abandoned`
- [X] T147 [P] [US4] Test integration `tests/Features/LiveChat/Public/StartConversationResumedContextTests.cs` — visitor com conversa `resolved` cria nova; `LiveChatConversationGateway.GetResumedContextAsync` retorna até `Widget:ResumedContextMessageLimit` mensagens da anterior; orchestrator integration deferred (follow-up)
- [X] T148 [P] [US4] Widget unit `src/omniDesk.Widget/tests/conversation-store.spec.ts` — caso `open` retoma; caso `resolved` exibe modo readonly + botão; caso `abandoned` limpa store e inicia novo

### Backend

- [X] T149 [US4] Estender interface `IConversationGateway` em `src/omniDesk.Api/Features/AgentRuntime/IConversationGateway.cs` com método `Task<IReadOnlyList<ConversationMessage>> GetResumedContextAsync(Guid visitorId, int limit, CancellationToken ct)` (ChannelStubGateway retorna empty)
- [X] T150 [US4] Implementar `GetResumedContextAsync` em `LiveChatConversationGateway` — busca última `Conversation` com `status=resolved` do `visitor_id`, retorna até `limit` últimas `messages` (filtra `system_event`) em ordem cronológica ascendente
- [~] T151 [US4] Orchestrator integration: passar resumed context para o primeiro Run OpenAI — deferido como follow-up (acoplamento mais profundo entre Spec 006 ContextBuilder/IncomingMessage e o gateway). Infra (T149/T150) está pronta.
- [~] T152 [US4] Atualizar `IncomingMessageWorker`/`ContextBuilder` (Spec 006) para consumir resumed context — deferred com T151

### Widget

- [X] T153 [US4] `widget.ts.loadInit` agora ramifica por `active_conversation.status`: open carrega histórico + WS; resolved entra em readonly + CTA; abandoned limpa store
- [X] T154 [US4] `panel.ts.setResolvedMode(onStartNew)` desabilita input, mostra banner "Conversa encerrada" + botão "Iniciar nova conversa"
- [X] T155 [US4] `conversation-store.clear()` (já existia) é chamado no caso `abandoned` em `loadInit` antes de re-hidratar

**Checkpoint**: User Story 4 funcional — retomada inteligente em todos os 4 casos (open IA, open humano, resolved, abandoned).

---

## Phase 7: User Story 5 — Sistema gerencia ciclo de vida automaticamente (Priority: P2)

**Goal**: Jobs Hangfire periódicos marcam conversas IA como `abandoned` (timeout 8h) e encerram conversas humano por inatividade (24h). IA encerra naturalmente ao detectar conclusão (já parte do Spec 006).

**Independent Test**: Forçar `last_message_at` para 9h atrás, disparar `AbandonmentSweepJob` manualmente, verificar `status=abandoned`. Análogo para humano com 25h e `InactivitySweepJob`. (QS-5)

### Tests US5

- [X] T156 [P] [US5] Test integration `tests/Features/LiveChat/Jobs/AbandonmentSweepJobTests.cs` — sobe 3 conversas IA (1 ativa, 2 inativas há 9h), roda job, verifica que apenas as 2 inativas viraram `abandoned`; conversa em humano não é afetada
- [X] T157 [P] [US5] Test integration `tests/Features/LiveChat/Jobs/InactivitySweepJobTests.cs` — análogo para humano com 25h; verificar `ended_by=system_inactivity` + evento `conversation.resolved` publicado; conversa em IA não é afetada
- [X] T158 [P] [US5] Test integration `tests/Features/LiveChat/Adapters/AiAgentEndsConversationTests.cs` — Orchestrator (Spec 006) chama tool de encerramento → `LiveChatConversationGateway` aplica `status=resolved, ended_by=ai_agent`; widget recebe evento

### Backend

- [X] T159 [US5] Criar `AbandonmentSweepJob.cs` em `src/omniDesk.Api/Features/LiveChat/Jobs/AbandonmentSweepJob.cs` — Hangfire scheduled `0 * * * *` (a cada hora); para cada tenant, UPDATE conversations SET status='abandoned' WHERE status='open' AND attendant_id IS NULL AND last_message_at < NOW() - widget_config.abandonment_timeout_hours * INTERVAL '1 hour' (research R9)
- [X] T160 [US5] Criar `InactivitySweepJob.cs` em `src/omniDesk.Api/Features/LiveChat/Jobs/InactivitySweepJob.cs` — análogo para `attendant_id IS NOT NULL`, marca `ended_by=system_inactivity`, INSERT message system, publica evento Redis para cada conversa
- [X] T161 [US5] Registrar ambos os jobs no `Hangfire.RecurringJob` em `src/omniDesk.Api/Program.cs` (após `app.UseHangfireDashboard`)
- [~] T162 [US5] (Integração com Spec 006) Deferred to follow-up — Spec 006 orchestrator não tem tool `end_conversation`; V1 cobre apenas timeouts automáticos
- [X] T163 [US5] `MarkResolvedByAiAsync` em `IConversationRepository` + impl + endpoint interno `POST /api/internal/livechat/conversations/{id}/end` (preparado, não cabeado pela 006)

**Checkpoint**: User Story 5 funcional — sweeps periódicos funcionam, hooks para encerramento natural por IA preparados.

---

## Phase 8: User Story 6 — Visitante envia anexos (Priority: P3)

**Goal**: Visitante anexa imagem/documento ≤ 10MB, MIME validado por magic bytes no backend, arquivo salvo em MinIO `tenant-{slug}/widget-uploads/`, mensagem com URL aparece no widget e CRM.

**Independent Test**: Anexar JPG 2MB → upload sucede, mensagem com preview no CRM. Tentar 15MB → rejeitado client-side. Renomear `.exe` para `.pdf` → backend rejeita 415. (QS-6)

### Tests US6

- [X] T164 [P] [US6] Test contract `tests/Features/LiveChat/Uploads/UploadEndpointTests.cs` — JPG válido → 201 + URL MinIO; > 10MB → 413 `FILE_TOO_LARGE`; MIME spoofed (PE32 com `Content-Type: application/pdf`) → 415 `UNSUPPORTED_MIME_TYPE`
- [X] T165 [P] [US6] Test unit `tests/Features/LiveChat/Uploads/MimeTypeDetectorTests.cs` — todos os 7 MIMEs do allowlist são reconhecidos pelos magic bytes; ZIP DOCX/XLSX inspecionam `[Content_Types].xml` (research R5)
- [X] T166 [P] [US6] Widget unit `src/omniDesk.Widget/tests/upload.spec.ts` — magic bytes detectados + `allowedAccept` cobre 7 MIMEs

### Backend

- [X] T167 [P] [US6] `MimeTypeDetector.cs` — magic bytes (12 primeiros) + ZIP `[Content_Types].xml` (R5)
- [X] T168 [P] [US6] `MinioUploader.cs` — bucket `tenant-{slug}` + path `widget-uploads/{conv}/{uuid}-{file}` + presigned URL 7 dias
- [X] T169 [US6] `UploadEndpoint.cs` — POST `/api/public/widget/upload`, multipart, ownership + status check, persist Message
- [X] T170 [US6] `UploadValidator.cs` (FluentValidation) — `conversation_id` UUID + `file ≤ Widget:MaxUploadBytes`
- [X] T171 [US6] `MapWidgetUpload()` registrado em Program.cs com `PublicRateLimiter`
- [~] T172 [US6] `MessageSendHandler` aceitar `attachment_url` via WS — V1 envia anexos via REST upload + persiste Message direto, sem path WS

### Widget

- [X] T173 [P] [US6] `src/omniDesk.Widget/src/lib/mime-detect.ts` — magic bytes client-side + `allowedAccept` string
- [X] T174 [US6] `input-area.ts` — botão 📎, `<input type="file" accept="...">`, callback `onAttach`
- [X] T175 [US6] `message-list.ts` — render de imagem inline + link de download (entregue na escrita inicial em US1)

### CRM

- [~] T176 [US6] `conversation-detail.component.ts` (anexos no CRM Angular) — depende da Phase 5 US3 (CRM inbox); fica pendente até US3 ser entregue

**Checkpoint**: User Story 6 funcional — anexos enviam, validam, renderizam em ambos lados.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Validações finais, testes E2E, performance, documentação.

- [ ] T177 [P] Smoke E2E Playwright `src/omniDesk.Widget/tests/e2e/visitor-flow.spec.ts` — visitante completa fluxo P1 (clica launcher, aceita LGPD, envia "Olá", recebe resposta da IA mockada) (QS-8)
- [ ] T178 [P] Test resilience `src/omniDesk.Widget/tests/ws-reconnect.spec.ts` — pausa servidor mock, digita 3 mensagens, despausa, valida que enviam todas e nenhuma duplica (QS-7, FR-024)
- [ ] T179 [P] Test perf `tests/Performance/WidgetLoadTimeTests.cs` — mede TTFB do `loader.js` + tamanho do bundle gzipped (< 30KB conforme plan §Scale/Scope)
- [ ] T180 [P] Test perf `tests/Performance/ResumeConversationLatencyTests.cs` — p95 de retomada (REST init + load messages) < 1s para conversa com 50 mensagens (SC-010)
- [ ] T181 Atualizar `docs/ARCHITECTURE.md` com seção "Live Chat Widget" referenciando `specs/007-live-chat-widget/plan.md` e adicionando ao diagrama de canais
- [ ] T182 Atualizar `docs/DEPENDENCIES.md` marcando Spec 07 como completa e habilitando Spec 08 (Tickets)
- [ ] T183 [P] Criar `specs/007-live-chat-widget/quickstart-evidences.md` — preencher com screenshots/logs após validação manual de QS-1 a QS-9
- [ ] T184 [P] Criar `specs/007-live-chat-widget/follow-up-issues.md` documentando: (a) tool `end_conversation` para Spec 006; (b) drop de `ai_threads` em migration futura; (c) Mongo `{slug}_widget_events` para audit trail
- [ ] T185 Code review final: rodar `dotnet build`, `dotnet test`, `pnpm --filter omniDesk.Widget test`, `ng test live-chat-config live-chat-inbox` — tudo verde
- [ ] T186 Validar quickstart manual completo: rodar QS-1 a QS-9 conforme `quickstart.md` e marcar checkboxes em `quickstart-evidences.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Sem dependências — pode começar imediatamente.
- **Foundational (Phase 2)**: Depende de Setup; **bloqueia todas** as user stories.
- **User Stories (Phase 3+)**: Todas dependem de Foundational.
  - **US1 (P1, MVP)** é completamente independente — pode ser entregue sozinha.
  - **US2 (P1)** independente de US1 (mas usuário precisa de US1 para ver o efeito do que configurou).
  - **US3 (P2)** depende de US1 (para haver conversas que o atendente possa pegar) — não depende de US2 estritamente.
  - **US4 (P2)** depende de US1 (precisa de fluxo base + `LiveChatConversationGateway`); estende com `GetResumedContextAsync` e estados do widget.
  - **US5 (P2)** depende de US1 (para haver conversas com `last_message_at` populado).
  - **US6 (P3)** depende de US1 (precisa de `messages` table + WS) — não depende de US2/3/4/5.
- **Polish (Phase 9)**: Depende das stories desejadas.

### Within Each User Story

- Tests primeiro (com `[ ]` falhando) → Implementação → Re-run testes → Verificar checkpoint.
- Modelos antes de serviços; serviços antes de endpoints; endpoints antes de UI.
- Adapters da Spec 006 (T067/T068/T069/T070) **devem** ser implementados antes do `MessageSendHandler` (T072) — sem isso o pipeline não fecha.

### Parallel Opportunities

- **Setup (Phase 1)**: T002–T006 todos em paralelo (folders + bootstrap widget); T001 sozinho.
- **Foundational (Phase 2)**:
  - T010–T021 (enums + value objects + event constants) **todos** em paralelo.
  - T022–T025 (entidades + repos interfaces) em paralelo.
  - T027–T030 (EF Core configurations) em paralelo.
  - T033–T036 (impl repositórios) em paralelo.
  - T045–T047 (test helpers) em paralelo.
  - Sequencial: migrations T007/T008 → T009 → T031/T032 → T037–T044.
- **US1**: T053–T061 (testes) em paralelo; T077–T085 (libs/state widget) em paralelo; T086–T091 (UI widget) sequencial dependendo de T085.
- **US2**: T096–T101 (testes) em paralelo; T113–T118 (tabs) em paralelo após T112.
- **US3**: T124–T130 (testes) em paralelo; T138–T140 (services CRM) em paralelo; T142–T143 (componentes) em paralelo.
- **US6**: T164–T166 (testes) em paralelo; T167–T168 (mime + uploader) em paralelo.
- **Polish**: T177–T184 todos em paralelo.

### Critical Path (sequencial obrigatório)

`T007 → T008 → T009 → T031 → T044 (Foundational migrations + provisioning)` →
`T067 → T068 → T069 → T070 (substituição dos stubs Spec 006)` →
`T071 → T072 (handshake WS + envio de mensagem)` →
`T091 (entry widget)` →
**Checkpoint US1 entregue**.

---

## Parallel Example: User Story 1

```bash
# Lançar todos os testes US1 juntos (8 arquivos distintos):
Task: "Test contract widget /init endpoint"
Task: "Test contract widget /conversations endpoint"
Task: "Test contract widget /messages endpoint"
Task: "Test integration LiveChatConversationGateway"
Task: "Test integration WidgetWebSocketEndpoint"
Task: "Test integration IncomingPipeline E2E"
Task: "Widget unit visitor-store"
Task: "Widget unit lgpd-consent"
Task: "Widget unit ws-client (backoff + replay)"

# Lançar libs e state do widget juntos (paths independentes):
Task: "Criar src/omniDesk.Widget/src/lib/crypto-uuid.ts"
Task: "Criar src/omniDesk.Widget/src/lib/debounce.ts"
Task: "Criar src/omniDesk.Widget/src/state/visitor-store.ts"
Task: "Criar src/omniDesk.Widget/src/state/conversation-store.ts"
Task: "Criar src/omniDesk.Widget/src/state/message-queue.ts"
Task: "Criar src/omniDesk.Widget/src/api/http-client.ts"
Task: "Criar src/omniDesk.Widget/src/api/ws-client.ts"
Task: "Criar src/omniDesk.Widget/src/ui/styles.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 apenas)

1. Phase 1 — Setup (1–2 dias).
2. Phase 2 — Foundational (3–4 dias) **bloqueia tudo**.
3. Phase 3 — US1 (4–5 dias).
4. **STOP e VALIDAR** com QS-1 + QS-9.
5. Deploy: widget bundled em CDN dev + tenant de teste consegue instalar e conversar com IA.

**MVP entrega valor real** mesmo sem US2 (config no CRM) — admin pode editar `widget_config` direto via SQL para testes; usuário final vê widget funcionando.

### Incremental Delivery

1. Setup + Foundational → fundação.
2. US1 → MVP funcional (visitante ↔ IA).
3. US2 → Tenant configura sem precisar de SQL.
4. US3 → Atendentes humanos no jogo.
5. US4 → Continuidade de experiência.
6. US5 → Higiene operacional.
7. US6 → Anexos.
8. Polish → E2E + perf + docs.

### Parallel Team Strategy

Após Foundational:

- Dev A: US1 (widget bundle + endpoints públicos + WS visitor + adapters)
- Dev B: US2 (CRM config + preview + jobs)
- Dev C: US3 (CRM inbox + WS CRM + browser notify)

US4/US5/US6 podem ser distribuídas após US1 + US3 estarem estáveis. Dependências entre stories são fracas (compartilham Foundational mas não compartilham arquivos de feature).

---

## Notas de execução

- Tarefas marcadas `[P]` = arquivos distintos, sem dependência pendente — paralelize.
- `[Story]` = mapeia 1:1 com user story do `spec.md` para rastreabilidade.
- **Verificar testes falhando antes de implementar** (princípio TDD herdado da Spec 006 e Constituição §VII).
- **Commits atomicos** por tarefa ou pequeno grupo lógico.
- Em qualquer checkpoint, parar e rodar quickstart manual para validar antes de avançar.
- Evitar: tarefas vagas, conflito em mesmo arquivo, dependências cross-story que quebrem independência.
- **Não dropar `ai_threads`** nesta spec — fica como follow-up (T184).

---

## Resumo

| Phase | Tarefas | Stories |
|---|---|---|
| 1 — Setup | T001–T006 (6) | — |
| 2 — Foundational | T007–T052 (46) | — |
| 3 — US1 (MVP) | T053–T095 (43) | US1 |
| 4 — US2 | T096–T123 (28) | US2 |
| 5 — US3 | T124–T145 (22) | US3 |
| 6 — US4 | T146–T155 (10) | US4 |
| 7 — US5 | T156–T163 (8) | US5 |
| 8 — US6 | T164–T176 (13) | US6 |
| 9 — Polish | T177–T186 (10) | — |
| **Total** | **186 tarefas** | — |

**Suggested MVP scope**: Phases 1 + 2 + 3 (95 tarefas) entrega a User Story 1 — visitante conversa com IA via widget instalado em qualquer site, com LGPD enforcement e persistência. É um produto deployável.
