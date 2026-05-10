# Implementation Plan: Live Chat (Widget)

**Branch**: `007-live-chat-widget` | **Data**: 2026-05-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/007-live-chat-widget/spec.md`

## Summary

Spec **canal-Web** que entrega o widget JavaScript instalável em sites de tenants e fecha a malha conversacional do OmniDesk em tempo real. O widget se comporta como um adapter de canal (conforme Constituição §III): traduz eventos do navegador do visitante em `IncomingMessage` agnóstico para o `AgentOrchestrator` da Spec 006 e recebe `OutgoingMessage` para entregar ao visitante via WebSocket.

A Spec 006 deixou três stubs explícitos que esta spec substitui:

1. `ChannelStubGateway` → `LiveChatConversationGateway` (impl real do `IConversationGateway`).
2. Tabela transitória `ai_threads` → tabelas reais `conversations` + `messages` (compartilhadas com Spec 008 WhatsApp via campo `channel`).
3. Eventos WebSocket apenas publicados em Redis Pub/Sub → roteados aos canais widget e CRM.

Backend entrega: tabelas tenant-scoped (`widget_config` 1:1, `visitors`, `conversations`, `messages`); endpoints CRM (`/api/widget/config` GET/PUT/PATCH); endpoints públicos autenticados por `widget_token` (`/api/public/widget/init`, `/api/public/widget/conversations`, `/api/public/widget/conversations/{id}/messages`, `/api/public/widget/upload`); WebSocket nativo `/ws/widget/{conversation_id}` (visitante) e `/ws/crm` (atendente); 2 jobs Hangfire (`AbandonmentSweepJob` IA, `InactivitySweepJob` humano); upload MinIO com validação MIME real; provisioning automático de `widget_config` no `TenantProvisioningJob` (extensão da Spec 003).

Frontend CRM (Angular 21) entrega: tela de configuração com 6 abas e preview ao vivo (`features/live-chat-config/`); painel multi-conversas (lista esquerda + conversa selecionada à direita) com browser notifications (`features/live-chat-inbox/`).

Widget (NOVO projeto `src/omniDesk.Widget/`): bundle vanilla TypeScript + Web Components com Shadow DOM (sem framework — atende Princípio V Simplicity); ~30 KB gzipped; servido via CDN Cloudflare. Persistência local em `localStorage` (`omnidesk_visitor_id`, `omnidesk_conversation_id`); reconexão WebSocket com backoff exponencial; consentimento LGPD obrigatório antes de enviar.

Esta spec **encerra os stubs** da Spec 006 e habilita E2E completo: visitante → widget → backend → Orchestrator → resposta no widget.

## Technical Context

**Backend**: C# .NET 10 — Minimal API + Endpoint Groups (continuação)
**Frontend**: TypeScript — Angular 21 Standalone Components + Signals (CRM em `src/omniDesk.Crm/`)
**Widget**: TypeScript ES2022 + Web Components nativos (Shadow DOM) — bundle ESM minificado via esbuild (NOVO `src/omniDesk.Widget/`)
**ORM**: Entity Framework Core 9 + Migrations SQL tenant-scoped (padrão do projeto)
**Storage**:

- PostgreSQL `tenant_{slug}.widget_config` (1:1 tenant), `visitors`, `conversations`, `messages`
- PostgreSQL `public.tenants` ganha `widget_token` (UUID público fixo gerado no provisionamento) — alternativa: viver em `tenant_{slug}.widget_config.widget_token` mas o lookup `widget_token → tenant_slug` precisa de tabela em `public` para evitar varredura cross-schema (decisão R3 em research.md)
- Redis `{slug}:conv:{conversation_id}` (canal Pub/Sub WebSocket — já reservado pela Spec 006), `{slug}:crm:user:{attendant_id}` (canal CRM por atendente), `{slug}:widget:rate:{anonymous_id}` (rate limit de mensagens públicas)
- MongoDB `{slug}_widget_events` (auditoria de aberturas, fechamentos, transbordos vistos pelo widget) — opcional V1.1
- MinIO `tenant-{slug}` (bucket por tenant — convenção CLAUDE.md §4) com prefixo `widget-uploads/{conversation_id}/{uuid}-{filename}`

**Background jobs**: Hangfire — 2 novos jobs de sweep + reuso da fila existente.

| Worker | Fila/Schedule | Responsabilidade |
|---|---|---|
| `AbandonmentSweepJob` | Cron `0 * * * *` (a cada hora) | Marca como `abandoned` conversas com IA inativas há mais de `widget_config.abandonment_timeout_hours` |
| `InactivitySweepJob` | Cron `0 * * * *` | Encerra conversas com humano inativas há mais de `widget_config.inactivity_close_hours` (`ended_by = system_inactivity`) |
| `WidgetDisableEnforcementJob` | Disparado por `PATCH /api/widget/config/toggle` quando `is_enabled = false` | Encerra todas as conversas `open` (`ended_by = system_disable`), envia mensagem automática |
| `IncomingMessageWorker` (Spec 006) | `{slug}:incoming_messages` | **REUTILIZADO**. Live Chat enfileira aqui após persistir mensagem do visitante. |
| `OutgoingMessageWorker` (Spec 006) | `{slug}:outgoing_messages` | **REUTILIZADO**. `LiveChatOutgoingAdapter` consome essa fila e publica em Redis `{slug}:conv:{conversation_id}` para entrega WS. |

**WebSocket**: ASP.NET Core nativo + Redis Pub/Sub (ADR-005). Dois endpoints:

- `/ws/widget/{conversation_id}?token={widget_token}` — visitante. Backend valida token, valida origem (allowed_domains), assina `{slug}:conv:{conversation_id}`. Eventos: `message.new`, `agent.typing`, `conversation.assigned`, `conversation.resolved`. Recebe: `message.send`, `visitor.typing`, `messages.read`.
- `/ws/crm` — atendente autenticado via JWT (Spec 002). Assina `{slug}:crm:user:{user_id}` e `{slug}:crm:dept:{department_id}`. Eventos: `chat.new_conversation`, `chat.message_received`, `chat.visitor_typing`, `chat.browser_notify`. Recebe: `attendant.typing`, `conversation.send`, `conversation.resolve`.

**File upload**: `POST /api/public/widget/upload` (multipart/form-data, max 10 MB) → validação MIME real via `MimeDetective` ou `Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider` + magic bytes → upload MinIO `tenant-{slug}/widget-uploads/...` → retorna `{ url, name, size }`.

**Testing**:

- Backend: xUnit + Testcontainers (Postgres + Redis + Mongo + **MinIO** real — `minio/minio:latest`); WebSocket testado via `WebApplicationFactory` com cliente WS interno; OpenAI seguindo o padrão da Spec 006 (`MockHttpMessageHandler`).
- Widget: Vitest + jsdom + happy-dom para DOM headless; Playwright opcional para 1 smoke test E2E (visitante → IA → mensagem); contratos do widget validados em testes de integração backend.
- CRM: Angular TestBed (`.spec.ts` co-localizado).

**Target Platform**: Linux ARM64 (API); Cloudflare Pages (CRM); Cloudflare CDN (widget bundle estático).

**Project Type**: Web service (API .NET 10) + 1 SPA Angular (CRM) + 1 bundle JS standalone (widget).

**Dependências backend** (zero pacote NuGet novo):

| Pacote | Já em uso desde | Uso nesta spec |
|---|---|---|
| `Microsoft.EntityFrameworkCore` 9.x | Spec 002 | Migrations + DbContext |
| `StackExchange.Redis` | Spec 002 | Pub/Sub WebSocket + locks |
| `Hangfire` | Spec 003 | Sweep jobs + reuso de filas |
| `MongoDB.Driver` | Spec 003 | Eventos (opcional) |
| `FluentValidation.AspNetCore` | Constituição | Payloads de config |
| `Microsoft.AspNetCore.WebSockets` | .NET base | Endpoints WS |
| `Minio` (já em `appsettings`) | Spec 003 (provisioning) | Upload de anexos |

**Dependências widget** (NOVO projeto `src/omniDesk.Widget/`):

- `typescript` 5.x (devDep)
- `esbuild` 0.x (devDep — bundle/minify)
- `vitest` (devDep — testes)
- **Zero runtime dependencies** — usa apenas APIs de browser (`fetch`, `WebSocket`, `crypto.randomUUID`, `localStorage`, `customElements`).
- Atende Princípio V (Simplicity) e mantém bundle ≤ 30 KB gzipped.

**Dependências frontend CRM** (built-ins + libs em uso):

- PrimeNG 21+ (Tabs, ColorPicker, InputSwitch, InputText, Textarea, InputNumber, Chip, Button, Toast, Dialog, ScrollPanel, Avatar, Badge, Listbox)
- Angular `@HostListener` + `Notification API` (browser nativo) para notificações.
- `date-fns` + `date-fns-tz` (já em uso) para timestamps de mensagens.
- WebSocket nativo do browser.

**Variáveis de ambiente** (4 novas):

| Variável | Default | Uso |
|---|---|---|
| `WIDGET_RESUMED_CONTEXT_MESSAGE_LIMIT` | `50` | Limite de mensagens da conversa anterior anexadas ao contexto da IA na reabertura (FR-017) |
| `WIDGET_MAX_UPLOAD_BYTES` | `10485760` (10 MB) | Tamanho máximo de anexo (FR-040) |
| `WIDGET_CDN_BASE_URL` | `https://cdn.omnicare.ia.br/widget/v1` | URL base injetada na aba "Instalação" do CRM |
| `WIDGET_PUBLIC_RATE_LIMIT_PER_MINUTE` | `30` | Mensagens públicas/min por `anonymous_id` (defesa contra abuso de token público) |

**Performance Goals**:

- Carregamento do widget na página host: p95 ≤ **500 ms** após `loader.js` ser fetched (SC-001).
- Tempo até primeira resposta da IA via widget: p95 ≤ **5 s** (herdado da Spec 006).
- Reconexão de WebSocket após queda: p99 ≤ **30 s** (SC-007).
- Browser notification para atendente em CRM minimizado: p95 ≤ **2 s** após atribuição (SC-009).
- Retomada de conversa `open` (carregar histórico via REST + abrir WS): p95 ≤ **1 s** (SC-010).
- Preview ao vivo no CRM: latência de propagação ≤ **200 ms** após edição de campo (SC-013).

**Constraints**:

- **Token público fixo**: `widget_token` é UUID v4 gerado no provisionamento do tenant. **Imutável**, **não secreto**, vive em `public.tenants.widget_token` para permitir lookup `token → tenant` sem percorrer schemas. Defesa de abuso: rate limit por `anonymous_id` (30 msg/min default) + `allowed_domains` opcional.
- **Origin validation**: header `Origin` validado em **todas** as requisições públicas REST e na conexão WebSocket inicial (handshake HTTP). Falha → 403. Lista vazia (`allowed_domains = []`) significa sem restrição (modo dev/preview).
- **LGPD enforcement em camadas**: (1) widget desabilita botão até checkbox marcado; (2) backend valida `lgpd_consent_at NOT NULL` em `POST /api/public/widget/conversations` antes de criar a conversa e em cada mensagem entregue ao Orchestrator. Defesa em profundidade.
- **MIME validation real**: backend lê magic bytes (primeiros 12 bytes) via `MimeDetective` em vez de confiar em `Content-Type` enviado pelo browser. Lista permitida: `image/jpeg`, `image/png`, `image/gif`, `image/webp`, `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
- **Idempotência de mensagens**: cada `message.send` carrega `client_message_id` (UUID gerado no widget) — backend deduplica por `(conversation_id, client_message_id)` durante 24h em Redis para tolerar reenvios da fila local após reconexão.
- **WebSocket re-attach**: ao reconectar, widget envia `messages.replay since={last_message_id}` e backend retorna mensagens criadas após esse ID (impede perda de mensagens durante a queda).
- **Multi-aba**: mesma `omnidesk_conversation_id` em duas abas → ambas conectam mesmo canal Redis e recebem eventos espelhados. Sem coordenação de "líder" — basta os eventos chegarem em ambas.
- **Shadow DOM obrigatório**: widget renderiza dentro de `customElement` com `attachShadow({ mode: 'closed' })` para isolar CSS do site host.
- **Soft delete**: `widget_config` e `visitors` nunca apagados fisicamente em produção (Constituição IV).
- **Resumed context**: ao iniciar conversa nova após `resolved`, **apenas** os últimos 50 messages são anexados como `system message` no thread OpenAI (Spec 006 monta o contexto). Limite controlado via `WIDGET_RESUMED_CONTEXT_MESSAGE_LIMIT`.

**Scale/Scope**:

- ~100 conversas ativas/tenant simultâneas (carga V1).
- ~5.000 mensagens/dia/tenant.
- ~50 visitantes únicos/dia/tenant.
- Bundle widget ≤ 30 KB gzipped (transferido em < 100 ms em 4G).
- 1 conexão WebSocket por aba aberta no widget; 1 conexão WebSocket por sessão CRM ativa.

## Constitution Check

*GATE: deve passar antes de Phase 0 e ser reavaliado após Phase 1.*

| Princípio | Status | Observação |
|---|---|---|
| I. Multi-Tenant Isolation (NN) | ✅ PASS | Todas as tabelas de feature em `tenant_{slug}.*` (`widget_config`, `visitors`, `conversations`, `messages`). **Apenas** `widget_token` (UUID + FK para `tenant_id`) vive em `public.tenants` (campo nullable, gerado no provisionamento). Justificativa: o widget chega na API sem subdomínio (lookup `token → tenant`), portanto o resolver precisa de tabela em `public`. Filas Redis e canais Pub/Sub sempre prefixados (`{slug}:conv:*`, `{slug}:crm:*`, `{slug}:widget:*`). MinIO bucket `tenant-{slug}` com prefixo `widget-uploads/`. `TenantResolverMiddleware` mantém-se primeiro middleware; criamos `WidgetTenantResolver` que opera no **mesmo lugar** mas resolve via `widget_token` query/body, não subdomínio. |
| II. AI-First, Human-Assisted | ✅ PASS | Esta spec não modifica regras de IA. Apenas implementa o canal de entrada que enfileira `IncomingMessage` → fila Hangfire da Spec 006. Mantém a regra de gatilhos hardcoded de palavras-chave (PT-BR) e detecção de frustração via prompt (PATCH 1.0.1). FR-017 (50 mensagens de contexto) atende ao espírito: handoff/retomada nunca perde contexto — apenas limita por custo. |
| III. Channel Agnosticism | ✅ PASS | **Cumprida exemplarmente**: Live Chat é adapter (`LiveChatIncomingAdapter`, `LiveChatOutgoingAdapter`, `LiveChatConversationGateway`). Zero alteração no `AgentOrchestrator`, `IncomingMessageWorker`, `OutgoingMessageWorker`, `ToolCallDispatcher`. Tabela `messages` é canal-agnóstica via campo `channel` em `conversations`. WhatsApp (Spec 008) reusará `IncomingMessageWorker` apenas adicionando seu adapter. |
| IV. Security e LGPD (NN) | ✅ PASS | (a) **Consentimento opt-in obrigatório** antes de qualquer mensagem (FR-018, validado em widget e backend). (b) **IP parcial** apenas (3 octetos IPv4) em `metadata` — sem fingerprinting de dispositivo (FR-021). (c) **Token público não-secreto** com defesas: `allowed_domains` + rate limit por `anonymous_id` + sem credenciais associadas. (d) **Refresh tokens nunca em `localStorage`** — widget usa apenas `widget_token` público; sessão de atendente segue Spec 002 (httpOnly cookie). (e) **Cloudflare Turnstile** não se aplica ao widget — o `widget_token` + `allowed_domains` cobrem o vetor; aplicar Turnstile no widget quebraria UX e o token público já cumpre função similar. (f) **Soft delete** em todas as entidades. (g) **MIME real** validado no backend, não na extensão. (h) **Dados em ARM64 BR** (Oracle Cloud + MinIO local). |
| V. Simplicity | ✅ PASS | **Zero pacote NuGet novo**. Widget = vanilla TS + Web Components nativos (sem framework — Princípio V). 4 variáveis de ambiente novas, todas com default seguro. 2 jobs Hangfire novos (sweep), reuso completo das filas existentes. **Reaproveita** `IncomingMessageWorker` / `OutgoingMessageWorker` da Spec 006 — não duplica pipeline. |
| VI. Observability e Auditability | ✅ PASS | Cada conversa registra `metadata` (page_url, referrer, UA, IP parcial), `lgpd_consent_at`, `ended_by`, `ended_at`. `messages` imutável após inserção (sem update). Eventos discretos timestamp-ados (`conversation.created`, `conversation.assigned`, `conversation.resolved`, `attendant.took_over`) podem opcionalmente ir para Mongo `{slug}_widget_events` (V1.1). Métrica primária da Constituição §VI já é coberta pela Spec 006 (`agent_activity_logs`). Atendente vê histórico **completo** em CRM — incluindo mensagens de IA antes do transbordo. |
| VII. Test Discipline | ✅ PASS | Testcontainers para Postgres/Redis/Mongo/**MinIO** (este último adicionado nesta spec). WebSocket testado via `WebApplicationFactory.Server.CreateWebSocketClient()` (.NET 10). Widget tem suite Vitest + 1 Playwright smoke. **Zero magic strings**: `Domain/LiveChat/MessageSenderTypes.cs`, `Domain/LiveChat/ConversationStatus.cs`, `Domain/LiveChat/EndedBy.cs`, `Domain/LiveChat/ChannelTypes.cs`, `Infrastructure/WebSockets/WidgetEvents.cs`, `Infrastructure/WebSockets/CrmEvents.cs`. Frontend CRM `.spec.ts` co-localizado. |

**Resultado**: Constitution Check **APROVADO sem desvios**. Reavaliação pós-Phase 1 — sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/007-live-chat-widget/
├── plan.md                          # Este arquivo
├── research.md                      # Phase 0 — decisões técnicas (R1–R10)
├── data-model.md                    # Phase 1 — entidades, migrations, transições
├── quickstart.md                    # Phase 1 — fluxos de validação manual
├── contracts/
│   ├── widget-config-api.md         # CRM: GET/PUT /api/widget/config + PATCH toggle
│   ├── widget-public-api.md         # Público: /api/public/widget/{init,conversations,upload}
│   ├── widget-websocket.md          # /ws/widget/{conversation_id} — eventos visitante↔backend
│   ├── crm-websocket.md             # /ws/crm — eventos atendente↔backend
│   ├── conversation-gateway-impl.md # LiveChatConversationGateway (impl real do IConversationGateway da 006)
│   └── widget-installation.md       # Snippet HTML + parâmetros window.OmniDeskConfig
├── checklists/
│   └── requirements.md              # validado no /speckit-specify
└── tasks.md                         # Phase 2 — gerado por /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── omniDesk.Api/
│   ├── Domain/
│   │   ├── LiveChat/                                       # NOVO
│   │   │   ├── WidgetConfig.cs                             # entity 1:1 tenant
│   │   │   ├── Visitor.cs
│   │   │   ├── Conversation.cs                             # canal-agnóstica (channel = LiveChat | WhatsApp)
│   │   │   ├── Message.cs
│   │   │   ├── ConversationStatus.cs                       # enum: Open, Resolved, Abandoned
│   │   │   ├── EndedBy.cs                                  # enum: Attendant, AiAgent, SystemInactivity, SystemDisable
│   │   │   ├── MessageSenderType.cs                        # enum: Visitor, AiAgent, Attendant, System
│   │   │   ├── MessageContentType.cs                       # enum: Text, Image, File, SystemEvent
│   │   │   ├── ChannelType.cs                              # enum: LiveChat, WhatsApp (futuro)
│   │   │   ├── LauncherIcon.cs                             # enum: Chat, Message, Support
│   │   │   ├── WidgetPosition.cs                           # enum: BottomRight, BottomLeft
│   │   │   ├── IdentificationField.cs                      # value object — name/email/phone + required
│   │   │   ├── ConversationMetadata.cs                     # value object — page_url, page_title, referrer, ua, ip_partial
│   │   │   ├── IWidgetConfigRepository.cs
│   │   │   ├── IVisitorRepository.cs
│   │   │   ├── IConversationRepository.cs
│   │   │   └── IMessageRepository.cs
│   │   └── Tenants/Tenant.cs                                # MODIFICADO: + WidgetToken (Guid, único, gerado no provisioning)
│   │
│   ├── Features/
│   │   ├── LiveChat/                                       # NOVO
│   │   │   ├── Config/                                     # CRM endpoints (autenticado JWT)
│   │   │   │   ├── WidgetConfigEndpoints.cs                # GET / PUT / PATCH toggle
│   │   │   │   ├── Commands/{UpdateWidgetConfig,ToggleWidget}Command.cs
│   │   │   │   ├── Queries/GetWidgetConfig.cs
│   │   │   │   └── Validators/UpdateWidgetConfigValidator.cs
│   │   │   ├── Public/                                     # endpoints autenticados pelo widget_token
│   │   │   │   ├── WidgetPublicEndpoints.cs                # /init, /conversations, /messages, /upload
│   │   │   │   ├── WidgetTokenAuthHandler.cs               # autentica via query/header `X-Widget-Token`
│   │   │   │   ├── OriginValidator.cs                      # valida header Origin contra allowed_domains
│   │   │   │   ├── PublicRateLimiter.cs                    # Redis 30/min por anonymous_id
│   │   │   │   └── Validators/{StartConversation,SendMessage,Upload}Validator.cs
│   │   │   ├── Inbox/                                      # endpoints CRM para atendente (multi-conv)
│   │   │   │   ├── ConversationListEndpoints.cs            # GET /api/conversations (paginado, filtrado por user/dept)
│   │   │   │   ├── ConversationDetailEndpoints.cs          # GET /api/conversations/{id}/messages
│   │   │   │   ├── Commands/{SendAttendantMessage,ResolveConversation}Command.cs
│   │   │   │   └── Queries/{ListActiveConversations,GetConversationDetail}.cs
│   │   │   ├── Adapters/                                   # implementa contratos da Spec 006
│   │   │   │   ├── LiveChatIncomingAdapter.cs              # converte mensagem visitante → IncomingMessage + enfileira
│   │   │   │   ├── LiveChatOutgoingAdapter.cs              # consome OutgoingMessage → publica em Redis Pub/Sub
│   │   │   │   └── LiveChatConversationGateway.cs          # impl real de IConversationGateway (substitui ChannelStubGateway)
│   │   │   ├── Uploads/
│   │   │   │   ├── MimeTypeDetector.cs                     # magic bytes + allowlist
│   │   │   │   ├── MinioUploader.cs                        # upload para tenant-{slug}/widget-uploads/...
│   │   │   │   └── UploadEndpoint.cs                       # POST /api/public/widget/upload
│   │   │   └── Jobs/
│   │   │       ├── AbandonmentSweepJob.cs                  # cron @hourly — IA inativa
│   │   │       ├── InactivitySweepJob.cs                   # cron @hourly — humano inativo
│   │   │       └── WidgetDisableEnforcementJob.cs          # disparado em toggle off
│   │   └── TenantProvisioning/                              # MODIFICADO (Spec 003)
│   │       └── TenantProvisioningJob.cs                    # + criar WidgetConfig + gerar WidgetToken
│   │
│   ├── Hubs/                                               # NOVO (rota WebSocket)
│   │   ├── WidgetWebSocketEndpoint.cs                      # /ws/widget/{conversation_id}?token=...
│   │   ├── CrmWebSocketEndpoint.cs                         # /ws/crm (JWT)
│   │   ├── WebSocketBroker.cs                              # facade Redis Pub/Sub ↔ WS conexões locais
│   │   ├── Events/
│   │   │   ├── WidgetEvents.cs                             # const: MessageNew, AgentTyping, ConversationAssigned, ConversationResolved
│   │   │   └── CrmEvents.cs                                # const: ChatNewConversation, ChatMessageReceived, ChatVisitorTyping, ChatBrowserNotify
│   │   └── WidgetConnectionRegistry.cs                     # tracking de conexões ativas (in-memory + Redis para multi-instância)
│   │
│   ├── Infrastructure/
│   │   ├── LiveChat/                                       # NOVO
│   │   │   ├── WidgetConfigConfiguration.cs                # EF Core
│   │   │   ├── VisitorConfiguration.cs
│   │   │   ├── ConversationConfiguration.cs
│   │   │   ├── MessageConfiguration.cs
│   │   │   └── Migrations/
│   │   │       └── 20260509_001_AddLiveChatTables.sql      # cria 4 tabelas tenant-scoped
│   │   ├── Tenants/
│   │   │   └── Migrations/
│   │   │       └── 20260509_002_AddWidgetTokenToTenants.sql # adiciona public.tenants.widget_token
│   │   └── Storage/
│   │       └── MinioFileService.cs                         # upload/get URL (já existe parcial — extender)
│   │
│   └── tests/omniDesk.Api.Tests/
│       ├── Domain/LiveChat/
│       │   ├── WidgetConfigTests.cs
│       │   └── ConversationStateTransitionsTests.cs
│       ├── Features/LiveChat/
│       │   ├── Config/                                     # endpoints CRM
│       │   ├── Public/                                     # endpoints públicos
│       │   ├── Inbox/                                      # endpoints atendente
│       │   ├── Adapters/                                   # testa LiveChatConversationGateway substitui stub
│       │   ├── Uploads/                                    # MIME real, 10MB limit, MinIO upload
│       │   └── Jobs/                                       # AbandonmentSweep, InactivitySweep, WidgetDisable
│       ├── Infrastructure/
│       │   ├── LiveChat/Migrations/                        # smoke test EF
│       │   └── Hubs/                                       # WS endpoints (visitor + CRM)
│       └── Helpers/
│           ├── WidgetTestHelpers.cs                        # cria tenant + widget_config + token
│           ├── WebSocketTestClient.cs                      # cliente WS para testes
│           └── MinioTestcontainerFixture.cs                # container MinIO (NOVO)
│
├── omniDesk.Crm/                                           # Angular 21 — features novas
│   └── src/app/features/
│       ├── live-chat-config/                               # NOVO — CRM → Configurações → Live Chat
│       │   ├── live-chat-config.component.ts               # standalone, signals, lazy
│       │   ├── live-chat-config.component.html
│       │   ├── live-chat-config.component.scss
│       │   ├── live-chat-config.component.spec.ts
│       │   ├── tabs/
│       │   │   ├── appearance-tab.component.ts             # cor, ícone, posição, nome, mensagens
│       │   │   ├── identification-tab.component.ts         # toggle + campos
│       │   │   ├── privacy-tab.component.ts                # textarea LGPD + URL
│       │   │   ├── behavior-tab.component.ts               # timeouts
│       │   │   ├── security-tab.component.ts               # allowed_domains
│       │   │   └── installation-tab.component.ts           # snippet HTML copy
│       │   ├── preview/widget-preview.component.ts         # renderiza widget real via iframe + widget_token
│       │   └── services/widget-config.service.ts           # signal store + HTTP
│       └── live-chat-inbox/                                # NOVO — CRM → Conversas
│           ├── live-chat-inbox.component.ts                # lista esquerda + conversa direita
│           ├── live-chat-inbox.component.html
│           ├── live-chat-inbox.component.scss
│           ├── live-chat-inbox.component.spec.ts
│           ├── components/
│           │   ├── conversation-list.component.ts          # signals — ativas
│           │   ├── conversation-detail.component.ts        # histórico + envio
│           │   └── browser-notification.service.ts         # Notification API + permissão
│           └── services/
│               ├── inbox.service.ts                        # signal store de conversas
│               └── crm-websocket.service.ts                # /ws/crm conexão singleton
│
└── omniDesk.Widget/                                        # NOVO PROJETO — bundle vanilla
    ├── package.json                                        # apenas devDeps (typescript, esbuild, vitest)
    ├── tsconfig.json                                       # strict: true
    ├── esbuild.config.mjs                                  # bundle ESM minificado
    ├── public/loader.js                                    # 1KB — carrega o bundle principal
    ├── src/
    │   ├── widget.ts                                       # entry — define <omnidesk-widget> custom element
    │   ├── ui/
    │   │   ├── launcher.ts                                 # botão flutuante
    │   │   ├── panel.ts                                    # painel principal
    │   │   ├── message-list.ts                             # lista de mensagens com auto-scroll
    │   │   ├── input-area.ts                               # textarea + anexo + enviar
    │   │   ├── pre-chat-form.ts                            # formulário de identificação
    │   │   ├── lgpd-consent.ts                             # checkbox + link
    │   │   └── styles.ts                                   # CSS injetado no Shadow DOM (template literal)
    │   ├── api/
    │   │   ├── http-client.ts                              # fetch wrapper com widget_token
    │   │   └── ws-client.ts                                # WebSocket com reconexão exponencial
    │   ├── state/
    │   │   ├── visitor-store.ts                            # localStorage anonymous_id
    │   │   ├── conversation-store.ts                       # localStorage conversation_id + status
    │   │   └── message-queue.ts                            # buffer mensagens durante desconexão
    │   ├── lib/
    │   │   ├── crypto-uuid.ts                              # crypto.randomUUID() com polyfill mínimo
    │   │   ├── debounce.ts                                 # 1s para visitor.typing
    │   │   └── mime-detect.ts                              # validação client-side (UX) — backend tem palavra final
    │   └── types.ts                                        # ConversationStatus, MessageEvent, etc.
    └── tests/
        ├── widget.spec.ts
        ├── ws-client.spec.ts                               # reconexão + replay
        ├── visitor-store.spec.ts
        └── lgpd-consent.spec.ts
```

**Structure Decision**: 3 alvos de build distintos (API .NET, CRM Angular, Widget vanilla) — refletindo a separação `apps/admin`, `apps/crm` já existente acrescida do novo `omniDesk.Widget/`. Adapters (Channel Agnosticism §III) ficam em `Features/LiveChat/Adapters/` para deixar claro que substituem stubs da Spec 006. WebSocket endpoints em `Hubs/` (consistente com convenção da Spec 006). Migrations SQL tenant-scoped seguem padrão `Add_*_Scaffold.sql` do projeto.

## Complexity Tracking

> Apenas violações **justificadas** do Constitution Check. Como o gate passou sem desvios, esta tabela documenta decisões de escopo que poderiam parecer violações mas não são.

| Decisão | Por que é necessária | Alternativa rejeitada |
|---|---|---|
| `widget_token` em `public.tenants` (e não em `tenant_{slug}.widget_config`) | Lookup `token → tenant` precisa ser O(1) sem percorrer schemas. O resolver de tenant entra **antes** do resolver de schema. | Tabela `public.widget_tokens(token, tenant_id)` separada — adiciona join sem ganho; `widget_token` é 1:1 com tenant. |
| Novo projeto `src/omniDesk.Widget/` (vanilla TS) em vez de Angular separado | Angular vazio adiciona ~140KB; widget injetado em sites de terceiros precisa ser tiny. Web Components nativos cobrem 100% dos requisitos. | Angular Elements: aceitável tecnicamente mas viola Princípio V (zlib do Angular runtime > 100 KB). Lit/Preact: dependência runtime nova — viola Princípio V. |
| Testcontainer MinIO (novo) | Spec 006 não usou MinIO; aqui é central (uploads). Test contra mock = falso positivo (Princípio VII). | Mock `IMinioClient`: rejeitado. Vivo registro de incidente da Constituição (database mock). |
| 1 Playwright smoke test (E2E real) | Widget renderiza dentro de Shadow DOM em página HTML real — Vitest + jsdom não cobre isolamento real do shadow tree. | Skip E2E: rejeitado — risco de quebrar em browsers reais (Safari shadow tree quirks). |
