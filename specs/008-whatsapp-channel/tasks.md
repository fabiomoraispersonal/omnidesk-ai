---

description: "Task list for WhatsApp Channel implementation"
---

# Tasks: Canal WhatsApp Business

**Input**: Design documents from `/specs/008-whatsapp-channel/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/{whatsapp-webhook,whatsapp-config-api,whatsapp-templates-api,whatsapp-meta-graph,whatsapp-adapter-contracts,whatsapp-websocket-events}.md, quickstart.md

**Tests**: A constituição (princípio VII — Test Discipline) torna testes **obrigatórios**. Backend: xUnit + Testcontainers (Postgres + Redis + Mongo + MinIO reais). Meta API mockada via `MockHttpMessageHandler` + cassetes JSON canônicos. CRM: Angular TestBed (`.spec.ts` co-localizado).

**Organization**: Tarefas agrupadas por user story para entrega independente.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Pode rodar em paralelo (arquivo distinto, sem dependência pendente)
- **[Story]**: Mapeia para user story (US1–US6) — ausente em Setup/Foundational/Polish
- Caminhos relativos do repo: `src/omniDesk.Api/...`, `src/omniDesk.Crm/...`

## Path Conventions

- Backend: `src/omniDesk.Api/{Domain,Features,Hubs,Infrastructure}/`
- Backend tests: `src/omniDesk.Api/tests/omniDesk.Api.Tests/{Domain,Features,Hubs,Infrastructure,Helpers}/`
- CRM Angular: `src/omniDesk.Crm/src/app/features/{whatsapp-config,whatsapp-templates,live-chat-inbox}/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Variáveis de configuração e estrutura de pastas.

- [X] T001 Adicionar em `src/omniDesk.Api/appsettings.Development.json` e `src/omniDesk.Api/appsettings.json` as chaves novas: `WhatsApp:GraphApiBaseUrl=https://graph.facebook.com/v19.0`, `WhatsApp:WebhookProcessingTimeoutSeconds=5`, `WhatsApp:SessionWindowHours=24`, `WhatsApp:SessionExpiringThresholdMinutes=60`. **Sem chave nova de criptografia** — `AesEncryptionService` já existe e usa env var `AES_ENCRYPTION_KEY` (ver `Infrastructure/Security/AesEncryptionService.cs`); reuso direto
- [X] T002 [P] Criar estrutura de pastas backend: `src/omniDesk.Api/Domain/WhatsApp/`, `src/omniDesk.Api/Features/WhatsApp/{Webhook,Config,Templates,Send,Adapters,Jobs}/`, `src/omniDesk.Api/Infrastructure/WhatsApp/`. Pasta `src/omniDesk.Api/Infrastructure/Security/` já existe (ver `using omniDesk.Api.Infrastructure.Security;` em `Infrastructure/Provisioning/TenantProvisioningJob.cs`). **Migrations vivem em `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` (single folder, convenção do projeto — ex: `Add_LiveChat_Tables.sql`).**
- [X] T003 [P] Criar estrutura de pastas CRM: `src/omniDesk.Crm/src/app/features/whatsapp-config/{components,services}/` e `src/omniDesk.Crm/src/app/features/whatsapp-templates/{components,services}/`
- [X] T004 [P] Criar estrutura de pastas de teste backend: `src/omniDesk.Api/tests/omniDesk.Api.Tests/Domain/WhatsApp/`, `Features/WhatsApp/{Webhook,Config,Templates,Send,Adapters,Jobs}/`, `Infrastructure/{WhatsApp,Security}/`, `Helpers/Fixtures/WhatsApp/{MetaResponses,Webhooks}/`
- [X] T005 [P] Documentar setup local em `specs/008-whatsapp-channel/quickstart.md` está atualizado (já criado pelo plan); adicionar README curto em `src/omniDesk.Api/Features/WhatsApp/README.md` linkando ao plan + research

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Migrations, domínio compartilhado, criptografia AES-GCM, validador HMAC, cliente Meta typed, jobs scheduling, RBAC. Bloqueia TODAS as user stories.

**⚠️ CRITICAL**: Nenhuma user story pode começar antes deste bloco completar.

### Migrations

- [X] T006 Criar migration tenant-scoped `Add_WhatsApp_Tables.sql` em `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` com tabelas `{TENANT_SCHEMA}.whatsapp_config` (PK = tenant_id) e `{TENANT_SCHEMA}.whatsapp_templates` + check constraints + unique parcial `(tenant_id, name) WHERE deleted_at IS NULL` + índice `idx_wa_templates_status` + triggers `updated_at` (data-model §5.1). Usa placeholder `{TENANT_SCHEMA}` (substituído em runtime pelas fixtures de teste; ver `Add_LiveChat_Tables.sql` como referência)
- [X] T007 Criar migration tenant-scoped `Add_WhatsApp_Conversation_Fields.sql` em `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` adicionando colunas `wa_contact_phone varchar(20)`, `wa_session_expires_at timestamptz` em `{TENANT_SCHEMA}.conversations`, check constraint E.164, índices parciais `idx_conversations_wa_session_expiring` e `idx_conversations_wa_contact_phone` (data-model §5.2)
- [X] T008 Atualizar arrays de migrations nas fixtures de teste: em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Helpers/LiveChatTestcontainerFixture.cs` (~linha 123) e `Helpers/TenantSchemaFixture.cs` (~linha 111) adicionar `"Add_WhatsApp_Tables.sql"` e `"Add_WhatsApp_Conversation_Fields.sql"` à lista de SQL files aplicados após `Add_LiveChat_Tables.sql`. **Mecânica de migration de produção é opaca (não há runner unificado de SQL files); manter consistência com test fixtures é o que importa para CI/local-dev.**
- [X] T009 Atualizar `src/omniDesk.Api/Infrastructure/Provisioning/TenantProvisioningJob.cs` (Spec 003 — usa `AppDbContext db` injetado) para inserir linha em `whatsapp_config` com `is_enabled=false`, `webhook_verify_token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))`, demais campos null. Usar `db.Database.ExecuteSqlRawAsync` com schema do tenant (mesma convenção das chamadas existentes nas linhas ~130, 152, 165 do arquivo)

### Domínio — enums + value objects (sem magic strings — princípio VII)

- [X] T010 [P] Criar `TemplateStatus.cs` em `src/omniDesk.Api/Domain/WhatsApp/TemplateStatus.cs` com enum `Draft`, `PendingMeta`, `Approved`, `Rejected` + helper `ToSnake()` (`draft`, `pending_meta`, `approved`, `rejected`)
- [X] T011 [P] Criar `TemplateType.cs` em `src/omniDesk.Api/Domain/WhatsApp/TemplateType.cs` com enum `AppointmentReminder`, `AppointmentConfirmation`, `AppointmentCancellation`, `FollowUp`, `Custom` + helper `ToSnake()`
- [X] T012 [P] Criar `TemplateCategory.cs` em `src/omniDesk.Api/Domain/WhatsApp/TemplateCategory.cs` com enum `Utility` (V1 fixo)
- [X] T013 [P] Criar `WaMessageStatus.cs` em `src/omniDesk.Api/Domain/WhatsApp/WaMessageStatus.cs` com enum `Sent`, `Delivered`, `Read`, `Failed` + `ToSnake()`
- [X] T014 [P] Criar `WaSupportedMessageType.cs` em `src/omniDesk.Api/Domain/WhatsApp/WaSupportedMessageType.cs` com enum `Text`, `Image`, `Document`, `Audio`
- [X] T015 [P] Criar `WaUnsupportedTypes.cs` em `src/omniDesk.Api/Domain/WhatsApp/WaUnsupportedTypes.cs` com `static IReadOnlySet<string> All = { "video","sticker","location","contacts","reaction","interactive" }`
- [X] T016 [P] Criar `PredefinedTemplate.cs` em `src/omniDesk.Api/Domain/WhatsApp/PredefinedTemplate.cs` (record `(string DefaultBody, string[] VariableLabels, int VariableCount)`)
- [X] T017 [P] Criar `PredefinedTemplates.cs` em `src/omniDesk.Api/Domain/WhatsApp/PredefinedTemplates.cs` static factory mapeando os 5 tipos (research R7) — `appointment_reminder` (3 vars), `appointment_confirmation` (3 vars), `appointment_cancellation` (2 vars), `follow_up` (1 var), `custom` (0 vars)
- [X] T018 [P] Criar `TemplateNameGenerator.cs` em `src/omniDesk.Api/Domain/WhatsApp/TemplateNameGenerator.cs` com método `Generate(TemplateType type, string slug, string? customSuffix)` retornando snake_case (ex: `lembrete_consulta_clinicaabc`, `custom_primeira_consulta_clinicaabc`)
- [X] T019 [P] Criar `TemplateStateMachine.cs` em `src/omniDesk.Api/Domain/WhatsApp/TemplateStateMachine.cs` com `static bool CanEdit/CanDelete/CanSubmit(TemplateStatus s)` (data-model §1.2)
- [X] T020 [P] Criar `WhatsAppCrmEvents.cs` em `src/omniDesk.Api/Hubs/Events/WhatsAppCrmEvents.cs` com constantes `WaMessageStatus`, `WaSessionExpiring`, `WaSessionExpired` (contracts/whatsapp-websocket-events.md)
- [X] T021 [P] Criar `MetaApi.cs` em `src/omniDesk.Api/Infrastructure/WhatsApp/MetaApi.cs` com const: paths (`Messages = "/{0}/messages"`, `MessageTemplates = "/{0}/message_templates"`, `Media = "/{0}"`, `Me = "/me"`), headers (`HubSignature256 = "X-Hub-Signature-256"`), hub params (`HubMode = "hub.mode"`, `HubVerifyToken = "hub.verify_token"`, `HubChallenge = "hub.challenge"`, `HubModeSubscribe = "subscribe"`), error codes Meta (`TokenRevoked = 190`, `OutsideWindow = 131047`, `RecipientNotOptedIn = 131026`)
- [X] T022 [P] Criar `RedisKeys.cs` adições em `src/omniDesk.Api/Infrastructure/WhatsApp/RedisKeys.cs` com helpers `WaDedup(slug, waMessageId)`, `WaExpiringEmitted(slug, convId)`, `WaExpiredEmitted(slug, convId)`, `WaConfigCache(slug)`, `WaWebhookRateLimit(slug)`

### Domínio — entidades + repositórios

- [X] T023 [P] Criar entidade `WhatsAppConfig.cs` em `src/omniDesk.Api/Domain/WhatsApp/WhatsAppConfig.cs` (data-model §1.1) + `IWhatsAppConfigRepository.cs` interface (`GetByTenantIdAsync`, `GetByTenantSlugAsync`, `UpdateAsync`, `ToggleEnabledAsync`)
- [X] T024 [P] Criar entidade `WhatsAppTemplate.cs` em `src/omniDesk.Api/Domain/WhatsApp/WhatsAppTemplate.cs` (data-model §1.2) + `IWhatsAppTemplateRepository.cs` interface (`ListAsync(filter)`, `GetByIdAsync`, `GetByNameAsync`, `GetByMetaIdAsync`, `CreateAsync`, `UpdateAsync`, `SoftDeleteAsync`)

### EF Core configurations

- [X] T025 [P] Criar `WhatsAppConfigConfiguration.cs` em `src/omniDesk.Api/Infrastructure/WhatsApp/WhatsAppConfigConfiguration.cs` mapeando colunas + check E.164 + PK `tenant_id`
- [X] T026 [P] Criar `WhatsAppTemplateConfiguration.cs` em `src/omniDesk.Api/Infrastructure/WhatsApp/WhatsAppTemplateConfiguration.cs` mapeando enums via converter, array `variable_labels` via PostgreSQL text[], unique parcial via `HasFilter("deleted_at IS NULL")`
- [X] T027 Modificar `src/omniDesk.Api/Domain/LiveChat/Conversation.cs` (Spec 007) para adicionar propriedades `WaContactPhone (string?)` e `WaSessionExpiresAt (DateTimeOffset?)` + atualizar `ConversationConfiguration.cs` da Spec 007 para mapear as duas colunas (sem alterar comportamento existente — campos opcionais)
- [X] T028 Atualizar `src/omniDesk.Api/Infrastructure/Persistence/AppDbContext.cs` (DbContext único do projeto — `TenantDbContext` é stripped, usado apenas pelo `TenantSchemaProvisioner`) adicionando `public DbSet<WhatsAppConfig> WhatsAppConfigs => Set<WhatsAppConfig>();` e `public DbSet<WhatsAppTemplate> WhatsAppTemplates => Set<WhatsAppTemplate>();` na seção dedicada à Spec 008 (`// Spec 008 — WhatsApp (tenant_{slug} schema; resolved at runtime).`). EF configurations (T025/T026) são auto-aplicadas via `ApplyConfigurationsFromAssembly` já presente no `OnModelCreating`

### Repositórios — implementações EF

- [X] T029 [P] Criar `WhatsAppConfigRepository.cs` em `src/omniDesk.Api/Infrastructure/WhatsApp/WhatsAppConfigRepository.cs` implementando `IWhatsAppConfigRepository`. Método `GetByTenantSlugAsync` faz lookup `public.tenants` → set schema → query `whatsapp_config`
- [X] T030 [P] Criar `WhatsAppTemplateRepository.cs` em `src/omniDesk.Api/Infrastructure/WhatsApp/WhatsAppTemplateRepository.cs` implementando `IWhatsAppTemplateRepository`. Filtros: status, type, paginação
- [X] T031 [P] Criar `WaMessageStatusesRepository.cs` em `src/omniDesk.Api/Infrastructure/WhatsApp/WaMessageStatusesRepository.cs` (MongoDB) — collection `{tenant_slug}_wa_message_statuses` com `InsertAsync`, `ListByMessageAsync`, índice unique `wa_message_id` criado em ensure-indexes startup hook

### Criptografia AES-256-GCM (research R3 — REUSO)

- [X] T032 **Reuso — sem código novo.** `AesEncryptionService` já existe em `src/omniDesk.Api/Infrastructure/Security/AesEncryptionService.cs` com AES-256-GCM, formato de armazenamento `nonceHex:ciphertextHex:tagHex`, key 32 bytes via env var `AES_ENCRYPTION_KEY`. Usado por `Features/Admin/Tenants/TenantsEndpoints.cs` (Spec 003). Esta spec injeta o serviço onde precisar (Encrypt em UpdateConfig, Decrypt em SendAsync/HMAC validation)
- [X] T033 **Já registrado.** `services.AddSingleton<AesEncryptionService>()` em `src/omniDesk.Api/Infrastructure/Tenants/TenantInfrastructureExtensions.cs:74`. Sem mudança

### HMAC validation (research R4) + raw body middleware

- [X] T034 Criar `RawBodyCaptureMiddleware.cs` em `src/omniDesk.Api/Features/WhatsApp/Webhook/RawBodyCaptureMiddleware.cs` que, **apenas** em rotas `/api/public/whatsapp/webhook/*`, faz `Request.EnableBuffering()`, lê o `Body` para byte[], armazena em `HttpContext.Items["RawBody"]` e devolve o stream rebobinado via `Body.Position = 0`
- [X] T035 Criar `MetaWebhookSignatureValidator.cs` em `src/omniDesk.Api/Features/WhatsApp/Webhook/MetaWebhookSignatureValidator.cs` com método `bool Validate(string headerSignature, byte[] rawBody, byte[] appSecret)` — extrai prefixo `sha256=`, calcula HMAC-SHA256 do rawBody com appSecret, compara via `CryptographicOperations.FixedTimeEquals` em hex utf8 (contracts/whatsapp-webhook.md §4)
- [X] T036 Registrar `RawBodyCaptureMiddleware` em `src/omniDesk.Api/Program.cs` aplicado **apenas** em map group de webhook (após `UseRouting` mas antes de qualquer endpoint match) e `MetaWebhookSignatureValidator` como `Singleton`

### RBAC — policies (research R10)

- [X] T037 **Verificação apenas — policies já existem.** As 5 policies WhatsApp já estão definidas em `src/omniDesk.Api/Domain/Authorization/Permissions.cs` (`Policies.CanViewChannelStatus`, `CanEditChannelConfig`, `CanViewAccessToken`, `CanToggleChannel`, `CanManageTemplates`) e registradas em `src/omniDesk.Api/Features/Authorization/Authz/AuthorizationPoliciesRegistration.cs` linhas ~67–80. Mapeamento para esta spec: `CanViewChannelStatus` (read GET /config — Attendant+), `CanEditChannelConfig` (PUT /config — TenantAdmin only, ForbidsDuringImpersonation), `CanToggleChannel` (PATCH /config/toggle — TenantAdmin only), `CanManageTemplates` (POST/PUT/DELETE/submit templates — Supervisor+). Para `GET /api/whatsapp/templates?status=approved` (atendente listando para envio): usar `[Authorize]` simples (autenticado) sem policy específica + filtro server-side por `status=approved` quando role = Attendant. **Sem mudança de código aqui** — apenas confirmar existência. Roles disponíveis: `Roles.SaasAdmin`, `Roles.TenantAdmin`, `Roles.Supervisor`, `Roles.Attendant` (sem prefix `Tenant`)

### Cliente HTTP Meta typed (contracts/whatsapp-meta-graph.md)

- [X] T038 Criar `WhatsAppMetaClient.cs` em `src/omniDesk.Api/Infrastructure/WhatsApp/WhatsAppMetaClient.cs` typed client com construtor recebendo `HttpClient`. Métodos: `Task<MetaSendResponse> SendTextAsync(string phoneNumberId, string accessToken, string toE164, string body, CancellationToken ct)`, `SendTemplateAsync(...)`, `SendMediaAsync(...)` (V1 only define interface), `SubmitTemplateAsync(string wabaId, string accessToken, TemplateSubmissionPayload payload, CancellationToken ct)`, `GetTemplateStatusAsync(...)`, `Task<MetaMediaInfo> GetMediaInfoAsync(string mediaId, string accessToken, CancellationToken ct)`, `Task<byte[]> DownloadMediaBytesAsync(string url, string accessToken, CancellationToken ct)`, `Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken ct)` (chama `/me`). Em 4xx, lança `MetaApiException(code, message, fbtraceId, statusCode)`. Em 5xx/timeout, deixa Polly retentar (não captura)
- [X] T039 Registrar `WhatsAppMetaClient` em `src/omniDesk.Api/Program.cs` via `services.AddHttpClient<WhatsAppMetaClient>("WhatsAppGraph", c => { c.BaseAddress = new Uri(config["WhatsApp:GraphApiBaseUrl"]!); c.Timeout = TimeSpan.FromSeconds(10); })` + Polly retry (3 tries, exponential 1s/2s/4s, **apenas em 5xx e timeout**) + Polly timeout(10s)

### Logger sanitizer

- [X] T040 Configurar Serilog `Destructure.ByTransforming<WhatsAppConfig>` e `Destructure.ByTransforming<UpdateWhatsAppConfigRequest>` em `src/omniDesk.Api/Program.cs` — substituir `AccessToken*` e `AppSecret*` por `"***"` em qualquer sink (FR-034). Adicionar teste de sanitização em `tests/Infrastructure/Serilog/SecretsSanitizationTests.cs`

### Helpers de teste

- [X] T041 [P] Criar `WhatsAppTestHelpers.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Helpers/WhatsAppTestHelpers.cs` com `CreateTenantWithWhatsApp(slug, displayName, ...)` (cria tenant + whatsapp_config com access_token/app_secret cifrados via AES-GCM real), `MakeFakeAccessToken()`, `MakeFakeAppSecret()`, `ComputeMetaSignature(payload, secret)`
- [X] T042 [P] Criar `MetaWebhookFixtures.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Helpers/MetaWebhookFixtures.cs` com loaders `LoadTextMessage()`, `LoadImageMessage()`, `LoadDocumentMessage()`, `LoadAudioMessage()`, `LoadStatus(string status)`, `LoadTemplateApproved()`, `LoadTemplateRejected(string reason)`, `LoadUnsupportedSticker()`, `LoadMalformed()` — lê arquivos de `Helpers/Fixtures/WhatsApp/Webhooks/*.json`
- [X] T043 [P] Criar arquivos JSON de fixture em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Helpers/Fixtures/WhatsApp/Webhooks/`: `webhook-text-message.json`, `webhook-image-message.json`, `webhook-document-message.json`, `webhook-audio-message.json`, `webhook-status-sent.json`, `webhook-status-delivered.json`, `webhook-status-read.json`, `webhook-status-failed.json`, `webhook-template-approved.json`, `webhook-template-rejected.json`, `webhook-unsupported-sticker.json`, `webhook-malformed.json` (contracts/whatsapp-webhook.md §6)
- [X] T044 [P] Criar `MockMetaHttpHandler.cs` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Helpers/MockMetaHttpHandler.cs` (contracts/whatsapp-meta-graph.md §8) e cassetes em `Helpers/Fixtures/WhatsApp/MetaResponses/`: `send-text-200.json`, `send-text-401-token-revoked.json`, `send-template-200.json`, `submit-template-200.json`, `submit-template-400-name-invalid.json`, `template-status-approved.json`, `template-status-rejected.json`, `media-meta-200.json`, `media-bytes-200.bin` (JPEG real ~2KB para validar magic bytes)

**Checkpoint**: Foundation pronta. User stories podem começar em paralelo.

---

## Phase 3: User Story 1 — Recepção de mensagem WhatsApp e atendimento pela IA (Priority: P1) 🎯 MVP

**Goal**: Cliente envia mensagem para o número WhatsApp da clínica → backend recebe via webhook (HMAC validado), persiste, e a IA responde via Meta Graph API.

**Independent Test**: Configurar tenant com canal ativo, simular POST de webhook texto com HMAC válido. Confirmar (a) 200 OK ≤ 5s, (b) conversation criada, (c) message persistida, (d) `wa_session_expires_at` ≈ now+24h, (e) IA processou e respondeu via Meta API (mock).

### Tests for User Story 1 (princípio VII — TDD)

- [ ] T045 [P] [US1] Criar `WhatsAppWebhookEndpointTests.cs` em `tests/Features/WhatsApp/Webhook/WhatsAppWebhookEndpointTests.cs` cobrindo: GET verify happy path retorna challenge, GET verify token errado retorna 403, GET tenant inexistente 404, POST HMAC válido retorna 200 < 1s, POST HMAC inválido retorna 403 sem persistência, POST HMAC ausente retorna 403, POST com canal `is_enabled=false` retorna 200 silently dropped, POST mensagem duplicada (mesmo wa_message_id) retorna 200 sem reprocessar (dedup Redis), POST malformed JSON retorna 200 com log de incident
- [ ] T046 [P] [US1] Criar `WaWebhookProcessorJobTests.cs` em `tests/Features/WhatsApp/Webhook/WaWebhookProcessorJobTests.cs` cobrindo: payload com 1 mensagem texto cria visitor + conversation + message + atualiza `wa_session_expires_at`; payload com mensagem em conversa existente reusa visitor e conversation; payload com tipo não suportado é ignorado silenciosamente com log auditoria; payload com `field='message_template_status_update'` roteia para template handler
- [ ] T047 [P] [US1] Criar `WhatsAppIncomingAdapterTests.cs` em `tests/Features/WhatsApp/Adapters/WhatsAppIncomingAdapterTests.cs` cobrindo: `HandleMessageAsync` cria visitor com `metadata.wa_phone`, conversation `channel=whatsapp`, message com `metadata.wa_message_id`; janela atualizada para now+24h; IncomingMessage enfileirado em `{slug}:incoming_messages`; broadcast `chat.new_conversation` para WS CRM
- [ ] T048 [P] [US1] Criar `WhatsAppOutgoingAdapterTextTests.cs` em `tests/Features/WhatsApp/Adapters/WhatsAppOutgoingAdapterTextTests.cs` cobrindo apenas o caminho IA → text dentro da janela: `OutgoingMessage` com `sender_type=ai_agent`, `content_type=text` chama `WhatsAppMetaClient.SendTextAsync`, persiste `wa_message_id` em `messages.metadata`, broadcast `wa.message_status` status=sent
- [ ] T049 [P] [US1] Criar `MetaWebhookSignatureValidatorTests.cs` em `tests/Features/WhatsApp/Webhook/MetaWebhookSignatureValidatorTests.cs` com casos: assinatura válida → true; assinatura inválida → false; assinatura sem prefixo `sha256=` → false; header null/vazio → false; tempo de comparação constant-time (smoke test)
- [ ] T050 [P] [US1] Criar `AesGcmEncryptionServiceTests.cs` em `tests/Infrastructure/Security/AesGcmEncryptionServiceTests.cs` cobrindo: roundtrip encrypt→decrypt retorna mesmo plaintext; ciphertext modificado lança `CryptographicException` (tamper detection); 100 encrypts geram 100 nonces distintos; chave inválida na construção falha rápido

### Implementation for User Story 1

- [ ] T051 [US1] Criar `WaWebhookTenantResolver.cs` em `src/omniDesk.Api/Features/WhatsApp/Webhook/WaWebhookTenantResolver.cs` com método `Task<(Guid TenantId, string Slug, byte[] AppSecret, string VerifyToken, bool IsEnabled)?> ResolveAsync(string slug)`. Cache Redis 60s em `{slug}:wa:config_cache`. Lookup `public.tenants` → `tenant_{slug}.whatsapp_config` → decrypt `app_secret_ciphertext` (AES-GCM) → retornar tupla. Null se slug inexistente ou config ausente
- [ ] T052 [US1] Criar `WaWebhookProcessorJob.cs` em `src/omniDesk.Api/Features/WhatsApp/Webhook/WaWebhookProcessorJob.cs` com método `Task RunAsync(string tenantSlug, byte[] rawPayload, CancellationToken ct)`. Hangfire `[AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]`. Faz: parse JSON estruturado → loop em `entry[].changes[]` → switch `change.field`: `messages` chama `WhatsAppIncomingAdapter.HandleMessagesAsync(value)` (com `messages[]` ou `statuses[]`), `message_template_status_update` chama `HandleTemplateStatusAsync`. Tipos não suportados em `messages[i].type` são ignorados (log info)
- [ ] T053 [US1] Criar `WhatsAppWebhookEndpoints.cs` em `src/omniDesk.Api/Features/WhatsApp/Webhook/WhatsAppWebhookEndpoints.cs` com 2 endpoints públicos:
    - `GET /api/public/whatsapp/webhook/{slug}` — implementa contracts/whatsapp-webhook.md §2 (verify): valida `hub.mode=subscribe`, `FixedTimeEquals(verify_token, config.WebhookVerifyToken)`, retorna `Results.Text(challenge, "text/plain")` ou 403/404
    - `POST /api/public/whatsapp/webhook/{slug}` — implementa contracts/whatsapp-webhook.md §3 (recepção): lê raw body do `HttpContext.Items["RawBody"]`, resolve tenant via `WaWebhookTenantResolver`, valida HMAC, dedup `SET NX EX 86400 {slug}:wa:dedup:{wa_message_id}`, `BackgroundJob.Enqueue<WaWebhookProcessorJob>(j => j.RunAsync(slug, rawPayload, default))`, retorna 200 OK
- [ ] T054 [US1] Criar `WhatsAppIncomingAdapter.cs` em `src/omniDesk.Api/Features/WhatsApp/Adapters/WhatsAppIncomingAdapter.cs` implementando `IIncomingChannelAdapter`. Método `HandleMessagesAsync(MessagesChange change, ...)`:
    - Para cada `msg` em `value.messages[]`:
      - Se `msg.type` em `WaUnsupportedTypes.All` → log info + continue.
      - `visitor = await visitorRepo.GetByWaPhone(msg.From) ?? CreateVisitor(msg.From, contactName)`.
      - `conversation = await convRepo.GetOpenWhatsAppConversation(visitor.Id) ?? CreateConversation(visitor, channel=whatsapp, wa_contact_phone=msg.From)`.
      - `conversation.WaSessionExpiresAt = clock.UtcNow.AddHours(24)`; `convRepo.UpdateAsync`.
      - Persiste mensagem com `sender_type=visitor`, `content_type` apropriado, `metadata = { wa_message_id: msg.Id }`.
      - `incomingQueue.EnqueueAsync(IncomingMessage { ... })` (Spec 006 contract).
      - `wsBroadcaster.BroadcastAsync(crmChannel, "chat.new_conversation" ou "chat.message_received", payload)` (Spec 007 contract)
- [ ] T055 [US1] Estender `WhatsAppIncomingAdapter` com método `HandleStatusesAsync(MessagesChange change, ...)`:
    - Para cada `status` em `value.statuses[]`:
      - Resolve `messages.id` via index `messages.metadata->>'wa_message_id' = status.id`.
      - `waStatusRepo.InsertAsync(...)` em MongoDB.
      - `wsBroadcaster.BroadcastAsync(crmChannel, WhatsAppCrmEvents.WaMessageStatus, { conversation_id, message_id, status, error_code, error_message, timestamp })`
- [ ] T056 [US1] Criar `WhatsAppOutgoingAdapter.cs` em `src/omniDesk.Api/Features/WhatsApp/Adapters/WhatsAppOutgoingAdapter.cs` implementando `IOutgoingChannelAdapter`. `CanHandle("whatsapp") = true`. `SendAsync(OutgoingMessage msg)`:
    - Carrega `conversation` + `whatsapp_config` (decrypt access_token).
    - Se `is_enabled=false` → silently drop + log warn.
    - Se `msg.MessageType=text`: `metaClient.SendTextAsync(...)`.
    - Em sucesso: persistir `wa_message_id` em `messages.metadata`, MongoDB status=sent, WS broadcast `wa.message_status`.
    - Em `MetaApiException(401|190)`: marcar mensagem failed; `BackgroundJob.Enqueue<WaTokenRevokedDetectorJob>(slug)`; `[AutomaticRetry(Attempts=0)]` no método.
    - Em outros 4xx: marcar failed + WS broadcast.
- [ ] T057 [US1] Registrar adapters no DI em `src/omniDesk.Api/Program.cs`: `services.AddScoped<IIncomingChannelAdapter, WhatsAppIncomingAdapter>()`, `services.AddScoped<IOutgoingChannelAdapter, WhatsAppOutgoingAdapter>()`. O `OutgoingMessageDispatcher` (Spec 006) já resolve por `CanHandle(channel)`
- [ ] T058 [US1] Mapear endpoints `/api/public/whatsapp/webhook/{slug}` em `src/omniDesk.Api/Program.cs` via `app.MapGroup("/api/public/whatsapp/webhook").MapWhatsAppWebhookEndpoints()`. Aplicar `RawBodyCaptureMiddleware` no scope desse grupo. **Não** aplicar `[Authorize]` — auth via HMAC

**Checkpoint**: User Story 1 funcional ponta a ponta. Cliente envia → IA responde via Meta. SC-001/SC-002/SC-009 validáveis.

---

## Phase 4: User Story 2 — Configuração do canal WhatsApp pelo tenant (Priority: P1)

**Goal**: `tenant_admin` insere credenciais Meta no CRM, copia Webhook URL/Verify Token, ativa canal. `supervisor` vê read-only. Access Token cifrado at-rest e nunca em response.

**Independent Test**: Login como tenant_admin → CRM → WhatsApp → preencher 4 campos + ativar. Confirmar badge 🟢, GET /config retorna `access_token_configured: true` mas nunca o token plain.

### Tests for User Story 2

- [ ] T059 [P] [US2] Criar `WhatsAppConfigEndpointTests.cs` em `tests/Features/WhatsApp/Config/WhatsAppConfigEndpointTests.cs` cobrindo: GET tenant_admin retorna config sem access_token plain; GET supervisor retorna config sem access_token plain; GET Attendant retorna 403; PUT tenant_admin atualiza com cipher; PUT supervisor retorna 403; PUT com access_token vazio mantém o existente; PATCH toggle on com config completa + Meta /me ok → is_enabled=true; PATCH toggle on com Meta retornando 401 → 422 INVALID_TOKEN sem ativar; PATCH toggle off → is_enabled=false sem chamar Meta
- [ ] T060 [P] [US2] Criar `UpdateWhatsAppConfigValidatorTests.cs` em `tests/Features/WhatsApp/Config/UpdateWhatsAppConfigValidatorTests.cs` cobrindo: phone_number formato E.164 válido/inválido; access_token range 100-500 chars + começa com EAA; app_secret 32-64 hex chars
- [ ] T061 [P] [US2] Criar teste de integração `WhatsAppConfigSecretsLeakTests.cs` em `tests/Features/WhatsApp/Config/WhatsAppConfigSecretsLeakTests.cs` que faz GET após salvar com access_token conhecido e busca a string no body **e** nos logs Serilog (snapshot de `ILogEventSink` mock) — assert que NÃO aparece em texto plano (FR-003, SC-004)

### Implementation for User Story 2

- [ ] T062 [P] [US2] Criar `WhatsAppConfigDto.cs` em `src/omniDesk.Api/Features/WhatsApp/Config/WhatsAppConfigDto.cs` (DTO de saída) com campos `is_enabled`, `phone_number`, `display_name`, `waba_id`, `phone_number_id`, `access_token_configured` (bool derivado de `cipher != null`), `app_secret_configured` (bool), `webhook_verify_token`, `webhook_url` (gerado), `business_hours_enabled`, `channel_status` (`not_configured|configured_inactive|active`), `updated_at`. **Sem** `access_token_ciphertext` ou `app_secret_ciphertext`
- [ ] T063 [P] [US2] Criar `UpdateWhatsAppConfigRequest.cs` em `src/omniDesk.Api/Features/WhatsApp/Config/UpdateWhatsAppConfigRequest.cs` (DTO entrada): `phone_number?`, `display_name?`, `waba_id?`, `phone_number_id?`, `access_token?`, `app_secret?`, `business_hours_enabled?`. Strings vazias significam "não alterar"
- [ ] T064 [P] [US2] Criar `UpdateWhatsAppConfigValidator.cs` em `src/omniDesk.Api/Features/WhatsApp/Config/UpdateWhatsAppConfigValidator.cs` (FluentValidation) implementando regras da contracts/whatsapp-config-api.md §2 (E.164, ranges, hex, prefix EAA)
- [ ] T065 [US2] Criar `GetWhatsAppConfigQuery.cs` + handler em `src/omniDesk.Api/Features/WhatsApp/Config/Queries/GetWhatsAppConfig.cs` que mapeia `WhatsAppConfig` → `WhatsAppConfigDto`. `webhook_url` derivado de `IConfiguration["Frontend:CrmBaseUrl"]` substituindo subdomínio para `api.` + slug do tenant. Channel status: `not_configured` se `phone_number_id IS NULL`; `configured_inactive` se preenchido + `is_enabled=false`; `active` se `is_enabled=true`
- [ ] T066 [US2] Criar `UpdateWhatsAppConfigCommand.cs` + handler em `src/omniDesk.Api/Features/WhatsApp/Config/Commands/UpdateWhatsAppConfigCommand.cs`. Handler: carrega config existente; aplica campos não-vazios; cifra `access_token` e `app_secret` via `AesGcmEncryptionService` apenas se vieram preenchidos; persiste; retorna DTO atualizado
- [ ] T067 [US2] Criar `ToggleWhatsAppChannelCommand.cs` + handler em `src/omniDesk.Api/Features/WhatsApp/Config/Commands/ToggleWhatsAppChannel.cs`. Quando `is_enabled=true`: valida config completa (waba_id, phone_number_id, access_token, app_secret todos preenchidos); decifra access_token; chama `WhatsAppMetaClient.ValidateAccessTokenAsync` → se 401, retorna 422 `INVALID_TOKEN`; senão set `is_enabled=true`. Quando `is_enabled=false`: apenas update sem chamar Meta. Em ambos casos: invalida cache Redis `{slug}:wa:config_cache`
- [ ] T068 [US2] Criar `WhatsAppConfigEndpoints.cs` em `src/omniDesk.Api/Features/WhatsApp/Config/WhatsAppConfigEndpoints.cs` mapeando: `GET /` → `RequireAuthorization(Policies.CanViewChannelStatus)`; `PUT /` → `RequireAuthorization(Policies.CanEditChannelConfig)` + `IValidator<UpdateWhatsAppConfigRequest>`; `PATCH /toggle` → `RequireAuthorization(Policies.CanToggleChannel)`. Mapeia em `Program.cs` via `app.MapGroup("/api/whatsapp/config").MapWhatsAppConfigEndpoints()`
- [ ] T069 [US2] Resposta de erro `WHATSAPP_NOT_CONFIGURED` para PATCH toggle on quando faltam campos, listando campos ausentes em `error.details[]` (contracts/whatsapp-config-api.md §3 Response 422)

### Frontend CRM (Angular)

- [ ] T070 [P] [US2] Criar `whatsapp-config.service.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-config/services/whatsapp-config.service.ts` com signal store: `config = signal<WhatsAppConfigDto | null>(null)`, métodos `load()`, `save(req)`, `toggle(enabled)`. Interceptor JWT já existe (Spec 002)
- [ ] T071 [P] [US2] Criar `channel-status-badge.component.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-config/components/channel-status-badge.component.ts` standalone, exibe badge colorido por `channel_status` (🔴/🟡/🟢) com label correspondente
- [ ] T072 [P] [US2] Criar `webhook-info.component.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-config/components/webhook-info.component.ts` standalone com 2 campos read-only (Webhook URL, Verify Token) + botões "Copiar" usando `navigator.clipboard.writeText`
- [ ] T073 [P] [US2] Criar `credentials-form.component.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-config/components/credentials-form.component.ts` standalone com Reactive Form: `phone_number`, `display_name`, `waba_id`, `phone_number_id`, `access_token` (mostra `••••• (configurado)` se já existe + botão "Alterar"), `app_secret` idem, `business_hours_enabled` toggle. Disabled inputs quando `currentUser.role === 'supervisor'`
- [ ] T074 [US2] Criar `whatsapp-config.component.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-config/whatsapp-config.component.ts` orquestrando os 3 sub-componentes + botão "Ativar/Desativar canal" (Toggle PrimeNG) + Toast de sucesso/erro. Lazy-loaded em `app.routes.ts`
- [ ] T075 [P] [US2] Criar `whatsapp-config.component.spec.ts` co-localizado cobrindo: render badge correto por status; submit form chama service; supervisor sem permissão de edição (inputs disabled); toggle on com config incompleta exibe erro 422 com fields

**Checkpoint**: User Story 2 funcional. CRM consegue configurar e ativar o canal. SC-004 (zero leak) e SC-008 (≤10min setup) validáveis.

---

## Phase 5: User Story 3 — Atendente envia mensagem dentro da janela de 24h (Priority: P1)

**Goal**: Atendente assume conversa, digita texto livre e envia. Sistema valida janela 24h, envia via Meta API, exibe ícones ✓ → ✓✓ → ✓✓ azul → ✗.

**Independent Test**: Em conversa com `wa_session_expires_at` no futuro, enviar "Olá!" via CRM. Confirmar Meta API chamada, `wa_message_id` salvo, ícone ✓ no CRM, e atualização para ✓✓ ao chegar webhook delivered.

### Tests for User Story 3

- [ ] T076 [P] [US3] Criar `SessionWindowGuardTests.cs` em `tests/Features/WhatsApp/Send/SessionWindowGuardTests.cs` cobrindo: dentro da janela permite text; janela expirada bloqueia text com `WaWindowExpiredException`; janela expirada permite template; sem `wa_session_expires_at` (null) bloqueia text
- [ ] T077 [P] [US3] Criar `WhatsAppSendEndpointTests.cs` em `tests/Features/WhatsApp/Send/WhatsAppSendEndpointTests.cs` cobrindo: atendente envia text dentro janela → 200 + Meta chamada + WS event sent; atendente fora janela → 422 `WA_OUTSIDE_WINDOW`
- [ ] T078 [P] [US3] Criar `WhatsAppOutgoingAdapterDeliveryTests.cs` em `tests/Features/WhatsApp/Adapters/WhatsAppOutgoingAdapterDeliveryTests.cs` cobrindo: status delivered chega via webhook → MongoDB upsert + WS broadcast `wa.message_status` status=delivered; status read idem (sem alterar ticket); status failed com error_code → WS broadcast com error_message
- [ ] T079 [P] [US3] Criar `WaTokenRevokedDetectorJobTests.cs` em `tests/Features/WhatsApp/Jobs/WaTokenRevokedDetectorJobTests.cs` cobrindo: 401 isolado dispara job; job confirma com /me; se 401 confirmado → `is_enabled=false` + incident em Mongo + (mock de) email enviado; se /me OK → não desativa (era falha transitória)

### Implementation for User Story 3

- [ ] T080 [US3] Criar `SessionWindowGuard.cs` em `src/omniDesk.Api/Features/WhatsApp/Send/SessionWindowGuard.cs` com `void Validate(Conversation conv, MessageType type, TimeProvider clock)`. Lança `WaWindowExpiredException` se `type=text && (conv.WaSessionExpiresAt is null || conv.WaSessionExpiresAt < clock.GetUtcNow())`
- [ ] T081 [US3] Criar `WaOutgoingGuard.cs` em `src/omniDesk.Api/Features/WhatsApp/Send/WaOutgoingGuard.cs` com `void Validate(OutgoingMessage msg)`. Lança `WaAiTemplateForbiddenException` se `msg.IsTemplate && msg.SenderType == SenderType.AiAgent`
- [ ] T082 [US3] Estender `WhatsAppOutgoingAdapter.SendAsync` (T056) — invocar `WaOutgoingGuard.Validate` no início e `SessionWindowGuard.Validate` antes de chamar Meta
- [ ] T083 [US3] Criar `SendWhatsAppMessageCommand.cs` + handler em `src/omniDesk.Api/Features/WhatsApp/Send/Commands/SendWhatsAppMessage.cs`. Handler: `WaOutgoingGuard` + `SessionWindowGuard` → cria `messages` (sender_type=attendant) → `outgoingQueue.EnqueueAsync` (Spec 006) → retorna `message_id`
- [ ] T084 [US3] Criar `WhatsAppSendEndpoint.cs` em `src/omniDesk.Api/Features/WhatsApp/Send/WhatsAppSendEndpoint.cs` mapeando `POST /api/whatsapp/send` (autenticado, RBAC = atendente do dept dono da conversa). Body: `{ conversation_id, content?, template_id?, template_variables? }`. Mapear em `Program.cs` via `app.MapGroup("/api/whatsapp/send").MapWhatsAppSendEndpoint()`
- [ ] T085 [US3] Criar `WaTokenRevokedDetectorJob.cs` em `src/omniDesk.Api/Features/WhatsApp/Jobs/WaTokenRevokedDetectorJob.cs` (research R8): `RunAsync(slug, attemptedMessageId)`. Carrega config → decrypt access_token → `metaClient.ValidateAccessTokenAsync` → se 401 confirmado: set `is_enabled=false`, insert incident `token_revoked` em `{slug}_wa_incidents`, send email + in-app notify para `tenant_admin` (Spec 010 Notifications — usar interface/stub se não implementado)

### Frontend CRM — extensões na inbox da Spec 007

- [ ] T086 [US3] Estender `crm-websocket.service.ts` em `src/omniDesk.Crm/src/app/features/live-chat-inbox/services/crm-websocket.service.ts` adicionando: `waMessageStatus$ = new Subject<WaMessageStatusEvent>()`, switch no message handler para `WhatsAppCrmEvents.WaMessageStatus` (contracts/whatsapp-websocket-events.md §6)
- [ ] T087 [US3] Estender `conversation-detail.component.ts` em `src/omniDesk.Crm/src/app/features/live-chat-inbox/components/conversation-detail.component.ts`: adicionar `waStatusByMessageId = signal<Map<string, WaStatus>>(new Map())`, effect que consome `crmWs.waMessageStatus$` e atualiza o map; renderizar ícones inline ao lado de cada mensagem `sender_type=attendant|ai_agent` em conversas `channel=whatsapp` (✓/✓✓/✓✓ azul/✗ com tooltip = error_message)
- [ ] T088 [US3] Atualizar template do `conversation-detail.component.html` com badge de canal (`<p-tag>WhatsApp</p-tag>` quando `conversation.channel='whatsapp'`)
- [ ] T089 [P] [US3] Atualizar `conversation-detail.component.spec.ts` co-localizado cobrindo render dos 4 ícones por status; tooltip aparece em failed; sem ícones para mensagens de live_chat

**Checkpoint**: User Story 3 funcional. Atendente envia → Meta entrega → CRM mostra ícones evoluindo. SC-005 (≤3 cliques + ≤2s) e SC-006 (≤3s status) validáveis.

---

## Phase 6: User Story 4 — Janela 24h expirada → atendente usa template (Priority: P2)

**Goal**: Quando `wa_session_expires_at < now()`, CRM bloqueia envio livre, exige template `approved`. Em < 1h restante, banner de aviso. IA é interrompida e escala humano.

**Independent Test**: Conversa com janela expirada → tentar enviar texto via CRM → bloqueado com seletor de template; selecionar approved + variáveis + enviar → mensagem chega via Meta com payload template; cliente reabre janela respondendo.

### Tests for User Story 4

- [ ] T090 [P] [US4] Criar `WaSessionExpiringNotifierJobTests.cs` em `tests/Features/WhatsApp/Jobs/WaSessionExpiringNotifierJobTests.cs` cobrindo: conv com `wa_session_expires_at` em now+30min emite `wa.session_expiring` + flag Redis set; conv já flagged não reemite; conv expirada emite `wa.session_expired` (uma vez); conv `channel=live_chat` ignorada; conv `status=resolved` ignorada
- [ ] T091 [P] [US4] Criar `WhatsAppOutgoingAdapterTemplateTests.cs` em `tests/Features/WhatsApp/Adapters/WhatsAppOutgoingAdapterTemplateTests.cs` cobrindo: outgoing template (sender=attendant) chama Meta com payload template + components.parameters preenchidos; template (sender=ai_agent) bloqueado por `WaOutgoingGuard`; janela expirada permite template (não bloqueia)
- [ ] T092 [P] [US4] Criar `AiTemplateBlockedTests.cs` em `tests/Features/WhatsApp/Adapters/AiTemplateBlockedTests.cs` testando que mesmo se a IA tentar via toolcall enviar template → bloqueio fim a fim (`WaOutgoingGuard` + `OutgoingMessageWorker` rejeita antes de adapter)

### Implementation for User Story 4

- [ ] T093 [US4] Estender `WhatsAppOutgoingAdapter` (T056/T082) com branch para `MessageType=Template`: build payload Meta (contracts/whatsapp-meta-graph.md §2), `metaClient.SendTemplateAsync(...)`, mesma lógica de status update
- [ ] T094 [US4] Estender `SendWhatsAppMessageCommand` (T083) para suportar envio de template: aceita `template_id` + `template_variables` (dict `{"1":"João","2":"10/06/2026",...}`); valida que template está `status=approved`; valida que `variable_count` casa com payload; cria `messages` com `metadata.wa_template_id` + `metadata.wa_template_variables` + `metadata.wa_template_name`
- [ ] T095 [US4] Criar `WaSessionExpiringNotifierJob.cs` em `src/omniDesk.Api/Features/WhatsApp/Jobs/WaSessionExpiringNotifierJob.cs` com Cron `*/5 * * * *`. Para cada tenant em `public.tenants`: query `conversations WHERE channel='whatsapp' AND status='open' AND wa_session_expires_at IS NOT NULL`. Para cada conv:
    - Se `now < wa_session_expires_at <= now+1h` e flag `{slug}:wa:expiring_emitted:{conv_id}` ausente → emit WS `wa.session_expiring`, set flag TTL 1h.
    - Se `wa_session_expires_at < now` e flag `{slug}:wa:expired_emitted:{conv_id}` ausente → emit WS `wa.session_expired`, set flag TTL 24h. **Adicionalmente**: se conversa estava em IA (`current_ai_agent_id IS NOT NULL` da Spec 006), interromper IA + abrir/atualizar ticket + emit `chat.handoff_required` (FR-017)
- [ ] T096 [US4] Registrar `WaSessionExpiringNotifierJob` no Hangfire scheduler em `src/omniDesk.Api/Program.cs`: `RecurringJob.AddOrUpdate<WaSessionExpiringNotifierJob>("wa-session-expiring", j => j.RunAsync(default), "*/5 * * * *")`
- [ ] T097 [US4] Estender `WhatsAppIncomingAdapter.HandleMessagesAsync` (T054) — após atualizar `wa_session_expires_at` para now+24h, deletar flags Redis `{slug}:wa:expiring_emitted:{conv_id}` e `{slug}:wa:expired_emitted:{conv_id}` (reabrir janela limpa)

### Frontend CRM

- [ ] T098 [P] [US4] Estender `crm-websocket.service.ts` (T086) com `waSessionExpiring$` e `waSessionExpired$` Subjects + handlers para os 2 eventos
- [ ] T099 [US4] Criar `session-window-banner.component.ts` em `src/omniDesk.Crm/src/app/features/live-chat-inbox/components/session-window-banner.component.ts` standalone. Input `sessionWindow: { status: 'active'|'expiring'|'expired'; expiresAt?: Date }`. Renderiza banner amarelo (expiring) ou vermelho (expired) com mensagem da contracts/whatsapp-websocket-events.md §2/§3. Em `active`, não renderiza nada
- [ ] T100 [US4] Criar `template-picker-dialog.component.ts` em `src/omniDesk.Crm/src/app/features/live-chat-inbox/components/template-picker-dialog.component.ts` standalone usando PrimeNG Dialog + Listbox. Props: `templates: WhatsAppTemplateDto[]` (filtrado approved), Output: `(send) => { template_id, variables: Record<string,string> }`. Renderiza dropdown de template + form dinâmico de variáveis a partir de `template.variable_labels`
- [ ] T101 [US4] Estender `conversation-detail.component.ts` (T087) com `sessionWindow = signal<{ status; expiresAt? }>({ status: 'active' })`, effects para `waSessionExpiring$` e `waSessionExpired$`; quando `status='expired'`: input de texto livre disabled + botão "Selecionar template" abre `template-picker-dialog`; ao receber `chat.message_received` em conversa expired → re-fetch conversation e voltar para `active` (item §4 do WS contract)
- [ ] T102 [P] [US4] Atualizar `conversation-detail.component.spec.ts` cobrindo: banner renderiza por status; input desabilita em expired; dialog de template abre; envio chama service com payload correto

**Checkpoint**: User Story 4 funcional. Janela expirada bloqueia texto, força template. SC-003 (100% blocked sem template) validável.

---

## Phase 7: User Story 5 — Tenant cria, edita e submete templates à Meta (Priority: P2)

**Goal**: tenant_admin/supervisor cria template (pré-definido ou custom), edita, submete à Meta, recebe webhook approved/rejected, vê na lista com badge.

**Independent Test**: Criar `appointment_reminder` → editar texto → submeter → simular webhook `APPROVED` → template aparece como selecionável na US4. Repetir com `rejected` mostrando motivo.

### Tests for User Story 5

- [ ] T103 [P] [US5] Criar `PredefinedTemplatesTests.cs` em `tests/Domain/WhatsApp/PredefinedTemplatesTests.cs` cobrindo: cada tipo retorna `VariableCount` correto; body contém placeholders sequenciais `{{1}}..{{N}}`; `variable_labels.Length == VariableCount`
- [ ] T104 [P] [US5] Criar `TemplateStateMachineTests.cs` em `tests/Domain/WhatsApp/TemplateStateMachineTests.cs` cobrindo todas combinações `CanEdit/CanDelete/CanSubmit` x 4 estados
- [ ] T105 [P] [US5] Criar `WhatsAppTemplatesEndpointTests.cs` em `tests/Features/WhatsApp/Templates/WhatsAppTemplatesEndpointTests.cs` cobrindo: GET lista paginada filtrada por status; GET Attendant força status=approved; POST tenant_admin cria draft com name auto-gerado; POST com count de variáveis errado retorna 400 `TEMPLATE_VARIABLE_MISMATCH`; POST com nome duplicado retorna 400 `TEMPLATE_NAME_CONFLICT`; PUT em pending_meta retorna 409 `TEMPLATE_NOT_EDITABLE`; submit chama Meta API + persiste meta_template_id; submit com Meta retornando 400 marca rejected com reason; submit sem whatsapp_config retorna 422 `WHATSAPP_NOT_CONFIGURED`; DELETE em approved retorna 409
- [ ] T106 [P] [US5] Criar `WaTemplateStatusHandlerTests.cs` em `tests/Features/WhatsApp/Webhook/WaTemplateStatusHandlerTests.cs` cobrindo: webhook APPROVED atualiza status=approved + approved_at + meta_template_id; webhook REJECTED atualiza status=rejected + rejected_at + rejection_reason; webhook para template inexistente é ignorado com log
- [ ] T107 [P] [US5] Criar `WaTemplateStatusPollerJobTests.cs` em `tests/Features/WhatsApp/Jobs/WaTemplateStatusPollerJobTests.cs` cobrindo: template em pending_meta há > 1h é polled; resposta APPROVED move para approved; resposta REJECTED move para rejected com reason; templates submitted há < 1h são ignorados (cobertura via webhook esperada)

### Implementation for User Story 5

- [ ] T108 [P] [US5] Criar `WhatsAppTemplateDto.cs` em `src/omniDesk.Api/Features/WhatsApp/Templates/WhatsAppTemplateDto.cs` (data-model §1.2 + variable_count derivado)
- [ ] T109 [P] [US5] Criar `CreateTemplateRequest.cs` + `UpdateTemplateRequest.cs` em `src/omniDesk.Api/Features/WhatsApp/Templates/Requests/` (contracts/whatsapp-templates-api.md §2/§3)
- [ ] T110 [P] [US5] Criar `CreateTemplateValidator.cs` + `UpdateTemplateValidator.cs` em `src/omniDesk.Api/Features/WhatsApp/Templates/Validators/`. Regras: para tipos pré-definidos, count de placeholders === `VariableCount` esperado; `variable_labels.Length` == `VariableCount`; placeholders numerados sequencialmente; body ≤ 1024 chars; para `custom`, `name_suffix` snake_case 1–40 chars
- [ ] T111 [US5] Criar `ListTemplatesQuery.cs` + handler em `src/omniDesk.Api/Features/WhatsApp/Templates/Queries/ListTemplates.cs` com filtros + paginação. Para `Attendant` força `status=approved`
- [ ] T112 [US5] Criar `CreateTemplateCommand.cs` + handler em `src/omniDesk.Api/Features/WhatsApp/Templates/Commands/CreateTemplate.cs`. Handler: gera `name` via `TemplateNameGenerator`; valida unicidade; status=draft; persiste
- [ ] T113 [US5] Criar `UpdateTemplateCommand.cs` + handler. Handler valida `TemplateStateMachine.CanEdit`; aplica updates
- [ ] T114 [US5] Criar `SubmitTemplateCommand.cs` + handler em `src/omniDesk.Api/Features/WhatsApp/Templates/Commands/SubmitTemplate.cs`. Handler: carrega template (must be `draft`); carrega `whatsapp_config` (must have access_token); decifra access_token; build payload Meta (contracts/whatsapp-meta-graph.md §4) com `example.body_text` derivado de `variable_labels` (placeholder `{label}` → string fictícia); `metaClient.SubmitTemplateAsync`; em sucesso: status=pending_meta, submitted_at=now, meta_template_id=response.id; em 4xx Meta: status=rejected, rejection_reason=meta error message
- [ ] T115 [US5] Criar `DeleteTemplateCommand.cs` + handler. Soft delete (set `deleted_at`). Valida `TemplateStateMachine.CanDelete`
- [ ] T116 [US5] Criar `WhatsAppTemplatesEndpoints.cs` em `src/omniDesk.Api/Features/WhatsApp/Templates/WhatsAppTemplatesEndpoints.cs` mapeando: `GET /` → `[Authorize]` simples (Attendant+ pode listar; query forçada para `status=approved` quando role = Attendant); `GET /{id}` idem; `POST /` → `RequireAuthorization(Policies.CanManageTemplates)`; `PUT /{id}` idem; `POST /{id}/submit` idem; `DELETE /{id}` idem. Mapear em `Program.cs` via `app.MapGroup("/api/whatsapp/templates").MapWhatsAppTemplatesEndpoints()`
- [ ] T117 [US5] Criar `WaTemplateStatusHandler.cs` em `src/omniDesk.Api/Features/WhatsApp/Webhook/WaTemplateStatusHandler.cs`. Método `HandleAsync(TemplateStatusChange change)`: find template by `meta_template_id` → update status conforme `change.event` (`APPROVED`/`REJECTED`); set `approved_at`/`rejected_at`/`rejection_reason`. Chamado de `WaWebhookProcessorJob.HandleTemplateStatusAsync` (T052)
- [ ] T118 [US5] Criar `WaTemplateStatusPollerJob.cs` em `src/omniDesk.Api/Features/WhatsApp/Jobs/WaTemplateStatusPollerJob.cs` (research R7/R9 — fallback). Cron `0 * * * *`. Para cada tenant: lista templates com `status=pending_meta AND submitted_at < now-1h`; carrega config; chama `metaClient.GetTemplateStatusAsync(waba_id, template.name)`; se response indica APPROVED/REJECTED → atualiza
- [ ] T119 [US5] Registrar `WaTemplateStatusPollerJob` no Hangfire scheduler em `Program.cs`: `RecurringJob.AddOrUpdate<WaTemplateStatusPollerJob>("wa-template-status-poller", j => j.RunAsync(default), "0 * * * *")`

### Frontend CRM

- [ ] T120 [P] [US5] Criar `whatsapp-templates.service.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-templates/services/whatsapp-templates.service.ts` com signal store: `templates = signal<WhatsAppTemplateDto[]>([])`, métodos `list()`, `create()`, `update()`, `submit()`, `delete()`
- [ ] T121 [P] [US5] Criar `template-list.component.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-templates/components/template-list.component.ts` standalone: cards com badge de status (PrimeNG Tag), botões Editar/Excluir (visibilidade via `TemplateStateMachine` mirror em TS) + botão "Submeter Meta" para drafts; filtro por status (Dropdown)
- [ ] T122 [P] [US5] Criar `rejection-reason.component.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-templates/components/rejection-reason.component.ts` standalone: tooltip/expansion exibindo `template.rejection_reason` quando status=rejected
- [ ] T123 [P] [US5] Criar `template-editor.component.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-templates/components/template-editor.component.ts` standalone usando PrimeNG Dialog + Reactive Form. Selector de tipo (5 opções). Body Textarea + counter (max 1024); para tipos pré-definidos, body é pré-preenchido com `defaultBody` (busca em const `PredefinedTemplates` mirror em TS); variáveis são fixas (read-only labels mostrando descrição); para `custom`, variáveis editáveis (add/remove). Submit: calls `service.create()` se draft inicial, ou `service.update()` se editando draft
- [ ] T124 [US5] Criar `whatsapp-templates.component.ts` em `src/omniDesk.Crm/src/app/features/whatsapp-templates/whatsapp-templates.component.ts` orquestrando service + lista + editor + dialog de confirmação para delete + Toast. Lazy-loaded em `app.routes.ts`. RBAC visível: Attendant não tem acesso (route guard); supervisor pode CRUD
- [ ] T125 [P] [US5] Criar `whatsapp-templates.component.spec.ts` co-localizado cobrindo: lista com badges; create de tipo pré-definido pré-preenche body + variáveis read-only; create custom permite variáveis livres; submit move para pending_meta; delete em approved é bloqueado

**Checkpoint**: User Story 5 funcional. Tenant gerencia templates ponta a ponta. SC-007 (5 templates pré-definidos cobrem 100% Spec 11) validável.

---

## Phase 8: User Story 6 — Recepção de mídia (imagem, documento, áudio) (Priority: P3)

**Goal**: Cliente envia foto/PDF/áudio → backend baixa da Meta → MinIO → CRM exibe inline (preview imagem, link doc, player áudio). Tipos não suportados ignorados em silêncio.

**Independent Test**: Webhooks `image`/`document`/`audio` simulados → confirmar binário em MinIO, `attachment_url` populada, exibição correta no CRM. Webhook `sticker` é ignorado.

### Tests for User Story 6

- [ ] T126 [P] [US6] Criar `WaMediaDownloadJobTests.cs` em `tests/Features/WhatsApp/Jobs/WaMediaDownloadJobTests.cs` cobrindo: image válida — chama Meta GET media → GET bytes → magic bytes match → upload MinIO → atualiza `messages.attachment_url` + `metadata.wa_attachment_status='ready'` → emite WS `wa.message_status` com `attachment_ready=true`; document PDF idem; audio idem; mídia com magic bytes não permitidos → `wa_attachment_status='failed'` + incident `unsupported_media_type`; URL Meta expirada (404) → status=failed + retry Hangfire
- [ ] T127 [P] [US6] Criar `WaUnsupportedTypesIntegrationTest.cs` em `tests/Features/WhatsApp/Webhook/WaUnsupportedTypesIntegrationTest.cs` cobrindo: webhook sticker → 200 + nenhuma mensagem persistida + log `WaUnsupportedMessageType`; idem para video, location, contacts, reaction, interactive

### Implementation for User Story 6

- [ ] T128 [US6] Estender `WhatsAppIncomingAdapter.HandleMessagesAsync` (T054) — para tipos `image`/`document`/`audio`:
    - Persistir `messages` com `content_type` apropriado (`image`/`file`), `content` = caption (se existir) ou null, `metadata.wa_message_id`, `metadata.wa_attachment_meta_id = msg.MediaId`, `metadata.wa_attachment_filename = msg.Filename`, `metadata.wa_attachment_status = "pending"`, `metadata.wa_media_type = msg.Type` (audio diferenciado de image dentro do file)
    - `BackgroundJob.Enqueue<WaMediaDownloadJob>(j => j.RunAsync(slug, message.Id, msg.MediaId, msg.MimeType, msg.Filename))`
- [ ] T129 [US6] Criar `WaMediaDownloadJob.cs` em `src/omniDesk.Api/Features/WhatsApp/Jobs/WaMediaDownloadJob.cs` (research R6). Pipeline:
    1. Carregar config + decrypt access_token.
    2. `metaClient.GetMediaInfoAsync(mediaId)` → URL temporária + mime + size.
    3. Validar size ≤ 100 MB.
    4. `metaClient.DownloadMediaBytesAsync(url)` → bytes.
    5. `MimeTypeDetector.Detect(bytes)` (Spec 007); validar contra allowlist (contracts/whatsapp-meta-graph.md §6).
    6. Upload MinIO via `MinioFileService` (Spec 007) em `tenant-{slug}/whatsapp-attachments/{conversation_id}/{wa_message_id}-{filename}`.
    7. Update `messages.attachment_url`, `messages.metadata.wa_attachment_status='ready'`.
    8. Broadcast WS `wa.message_status` com `attachment_ready=true`.
    9. Em falha, set `wa_attachment_status='failed'` + incident
- [ ] T130 [US6] Estender `MimeTypeDetector` da Spec 007 (`src/omniDesk.Api/Features/LiveChat/Uploads/MimeTypeDetector.cs`) para incluir tipos de áudio: `audio/ogg`, `audio/mpeg`, `audio/aac`, `audio/mp4` na allowlist (com magic bytes correspondentes). Adicionar testes em `tests/Features/LiveChat/Uploads/MimeTypeDetectorTests.cs`
- [ ] T131 [US6] Garantir tipos não suportados (`WaUnsupportedTypes.All`) são silenciosamente ignorados em `WhatsAppIncomingAdapter.HandleMessagesAsync` (T054 já tem early-continue) — adicionar log info estruturado: `_logger.LogInformation("WaUnsupportedMessageType: tenant={Tenant} type={Type} wa_message_id={Id}", slug, type, wa_message_id)` (FR-010)

### Frontend CRM

- [ ] T132 [US6] Estender `conversation-detail.component.ts` (T087) com renderização condicional por `content_type`: `image` → `<img [src]="attachment_url">` com preview clicável; `file` com `metadata.wa_media_type` = audio → `<audio controls [src]="attachment_url">`; `file` outros → link de download com nome do arquivo. Quando `metadata.wa_attachment_status='pending'`: spinner; quando `failed`: placeholder "Falha ao carregar mídia" + botão retry (em V1.1 — em V1 apenas exibe placeholder)
- [ ] T133 [P] [US6] Atualizar `conversation-detail.component.spec.ts` cobrindo: image renderiza preview; pdf renderiza link de download; audio renderiza player; pending mostra spinner; failed mostra placeholder; tipos não suportados não aparecem (test é via dataset de mensagens — confirmar count)

**Checkpoint**: User Story 6 funcional. Mídia recebida e exibida. Tipos não suportados silenciados. SC-009 validável.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Cobertura de edge cases, performance, segurança extra, validação manual completa.

- [ ] T134 [P] Criar testes de carga simples em `tests/Performance/WhatsAppWebhookLoadTests.cs` (BenchmarkDotNet ou bombardier) — webhook POST com 10 msg/s/tenant: assert p95 ≤ 5s, p99 ≤ 10s (SC-001)
- [ ] T135 [P] Criar `WhatsAppWebhookOriginIpAuditTest.cs` em `tests/Features/WhatsApp/Webhook/WhatsAppWebhookOriginIpAuditTest.cs` testando que cabeçalhos de IP de origem são logados (Serilog enrichment) para investigação forense
- [ ] T136 [P] Criar testes de integração end-to-end em `tests/Features/WhatsApp/Integration/WhatsAppEndToEndTests.cs` cobrindo o fluxo SC-008 (setup completo): provision tenant → POST config → toggle on → simulate webhook GET verify → simulate webhook POST text → expect IA response via Meta mock → expect CRM event broadcast. Tudo em ≤ 10 min de wall-time com mocks
- [ ] T137 Adicionar índice unique em `messages.metadata->>'wa_message_id'` em migration adicional `Add_WhatsApp_Message_Id_Index.sql` em `src/omniDesk.Api/Infrastructure/Persistence/Migrations/` para acelerar lookup de status updates: `CREATE UNIQUE INDEX IF NOT EXISTS idx_messages_wa_message_id ON {TENANT_SCHEMA}.messages ((metadata->>'wa_message_id')) WHERE metadata->>'wa_message_id' IS NOT NULL` (CONCURRENTLY não suportado em transaction; sem CONCURRENTLY o lock breve é aceitável em V1). Adicionar à array de fixtures de teste (T008)
- [ ] T138 Adicionar ensure-indexes startup hook em `src/omniDesk.Api/Infrastructure/WhatsApp/MongoIndexInitializer.cs` que cria índices em `{slug}_wa_message_statuses` no startup (idempotente)
- [ ] T139 [P] Atualizar `docs/ARCHITECTURE.md` com novas peças do canal WhatsApp (sob a seção de adapters de canal — após Live Chat 007)
- [ ] T140 [P] Atualizar `docs/specs/02-whatsapp.spec.md` (se ainda existir como spec antiga) substituindo conteúdo por link para `specs/008-whatsapp-channel/spec.md` ou marcando como superseded
- [ ] T141 [P] Adicionar `WhatsApp:` section em `appsettings.json` de produção docs/exemplo com placeholders + comentários sobre uso de user-secrets/env-vars
- [ ] T142 [P] Atualizar `CLAUDE.md` substituindo bloco SPECKIT (já feito em T0) e adicionando uma menção rápida em §8 Agentes de IA explicando que IA jamais envia template (FR-016)
- [ ] T143 Roda `dotnet test src/omniDesk.Api/tests/` e garante 100% pass; quaisquer flaky tests viram issue separado (não bloqueiam a spec se isolados)
- [ ] T144 Roda `npm test --prefix src/omniDesk.Crm` e garante 100% pass
- [ ] T145 Executar `quickstart.md` ponta-a-ponta seguindo os 14 passos (manual). Marca cada item de "Checklist final de pronto-pra-merge" como completo. Anotar evidências em `specs/008-whatsapp-channel/quickstart-evidences.md` (criar arquivo)
- [ ] T146 Build Docker ARM64 do API: `docker buildx build --platform linux/arm64 -f src/omniDesk.Api/Dockerfile .` — sem erros
- [ ] T147 Smoke check final em ambiente staging: provision tenant teste, configurar canal com sandbox Meta, enviar mensagem real → IA responde → atendente envia template → confirma delivery; abrir issue de follow-up em `specs/008-whatsapp-channel/follow-up-issues.md` para qualquer V1.1
- [ ] T148 Atualizar `docs/DEPENDENCIES.md` marcando Spec 008 como **COMPLETE** com link para tasks.md
- [ ] T149 Verificar todos os 21 critérios de aceite da `spec.md` §11 — preencher checklist em `specs/008-whatsapp-channel/checklists/acceptance.md` (criar arquivo)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências; T001–T005 podem rodar em paralelo após T002 (estrutura de pastas).
- **Foundational (Phase 2)**: depende do Setup. Bloqueia TUDO. T006–T044.
    - Migrations (T006–T009) são sequenciais.
    - Domain enums/value objects (T010–T022) podem ir todos em paralelo.
    - Entities + repos (T023–T031) depois dos enums; entidades em paralelo, repos depois.
    - Crypto (T032–T033) e HMAC (T034–T036) independentes — paralelo.
    - RBAC (T037), MetaClient (T038–T039), Logger (T040), test helpers (T041–T044) — paralelo após enums prontos.
- **User Story 1 (Phase 3)**: depende de Foundational (especialmente repos, MetaClient, HMAC, AES-GCM). MVP.
- **User Story 2 (Phase 4)**: depende de Foundational (config repo, AES-GCM, RBAC). Independente de US1, US3, US4, US5, US6.
- **User Story 3 (Phase 5)**: depende de US1 (adapter outgoing já criado em T056) + US2 (config existente para envio). Pode rodar em paralelo com US4/US5/US6 mas depende do US1 completo.
- **User Story 4 (Phase 6)**: depende de US1+US2+US3.
- **User Story 5 (Phase 7)**: depende de Foundational. **Independente** de US3/US4 (templates podem ser gerenciados sem haver atendente enviando). Mas templates approved são pré-requisito **operacional** para US4 funcionar de fato.
- **User Story 6 (Phase 8)**: depende de US1 (webhook + adapter incoming).
- **Polish (Phase 9)**: depende de todas as US desejadas.

### User Story Dependencies

- US1 (P1) é o coração — todos os outros dependem dele em alguma medida.
- US2 (P1) é condição operacional para US1 funcionar (sem config, webhook não consegue validar HMAC). Mas implementação em si é independente.
- US3 (P1) reusa adapter da US1 + config da US2.
- US4 (P2) estende US3 com template path.
- US5 (P2) é independente da inbox; gerencia templates standalone. Operacionalmente conecta-se com US4.
- US6 (P3) é extensão de US1 para mídia.

### Within Each User Story

- Tests primeiro (TDD — princípio VII), garantir RED → implementar → GREEN → refactor.
- Domain → Repositories → Services → Endpoints → WS broadcast → Frontend.

### Parallel Opportunities

- Phase 1: T002–T005 paralelos.
- Phase 2: T010–T022 (enums + helpers) paralelos. T023–T030 (entities + EF configs) paralelos. T032 (crypto), T034 (raw body), T037 (RBAC), T038 (MetaClient), T040 (logger), T041–T044 (helpers de teste) paralelos entre si.
- Phase 3 US1: T045–T050 (testes) paralelos. T051–T058 majoritariamente sequencial por dependência (adapter precisa de helpers).
- Phase 4 US2: T059–T061 (testes) paralelos. T062–T064 (DTOs/validators) paralelos. T070–T075 frontend paralelos.
- Phase 5 US3: T076–T079 (testes) paralelos. T080–T085 sequenciais. T086–T089 frontend paralelos.
- Phase 6 US4: T090–T092 (testes) paralelos. T093–T097 sequenciais. T098–T102 frontend paralelos.
- Phase 7 US5: T103–T107 (testes) paralelos. T108–T119 implementação majoritariamente sequencial por dependência. T120–T125 frontend paralelos.
- Phase 8 US6: T126–T127 (testes) paralelos. T128–T131 sequenciais. T132–T133 frontend paralelos.
- Phase 9: T134–T142 paralelos. T143–T149 sequenciais (testes + manual quickstart + audit final).

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together (TDD red phase):
Task: "Webhook endpoint tests in tests/Features/WhatsApp/Webhook/WhatsAppWebhookEndpointTests.cs"
Task: "WaWebhookProcessorJob tests in tests/Features/WhatsApp/Webhook/WaWebhookProcessorJobTests.cs"
Task: "WhatsAppIncomingAdapter tests in tests/Features/WhatsApp/Adapters/WhatsAppIncomingAdapterTests.cs"
Task: "WhatsAppOutgoingAdapter text tests in tests/Features/WhatsApp/Adapters/WhatsAppOutgoingAdapterTextTests.cs"
Task: "MetaWebhookSignatureValidator tests in tests/Features/WhatsApp/Webhook/MetaWebhookSignatureValidatorTests.cs"
Task: "AesGcmEncryptionService tests in tests/Infrastructure/Security/AesGcmEncryptionServiceTests.cs"

# Foundational entities + EF configs em paralelo (Phase 2):
Task: "WhatsAppConfig entity + repo interface in src/omniDesk.Api/Domain/WhatsApp/WhatsAppConfig.cs"
Task: "WhatsAppTemplate entity + repo interface in src/omniDesk.Api/Domain/WhatsApp/WhatsAppTemplate.cs"
Task: "WhatsAppConfigConfiguration in src/omniDesk.Api/Infrastructure/WhatsApp/WhatsAppConfigConfiguration.cs"
Task: "WhatsAppTemplateConfiguration in src/omniDesk.Api/Infrastructure/WhatsApp/WhatsAppTemplateConfiguration.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup (T001–T005).
2. Phase 2: Foundational (T006–T044) — TUDO. **Crítico: bloqueia US**.
3. Phase 3: User Story 1 (T045–T058).
4. **STOP and VALIDATE**: simular webhook texto → confirmar IA responde via Meta. Demonstrar para Operador SaaS.
5. **Não enviável a tenants reais ainda** — falta US2 (configuração).

### MVP Real (US1 + US2 + US3)

1. Setup + Foundational + US1 + US2 + US3 = **canal funcional para texto bidirecional**.
2. Demonstrar a 1 tenant beta com sandbox Meta.

### Incremental Delivery

1. MVP Real = canal texto livre.
2. + US4 (templates fora janela) = canal **operável** em produção (sem isso, conversas inativas viram dead-end).
3. + US5 (gestão de templates) = autonomia do tenant para criar e aprovar templates próprios.
4. + US6 (mídia) = paridade de feature com Live Chat.
5. + Polish = pronto para release.

### Parallel Team Strategy

Time pequeno (1–2 devs):

1. Setup + Foundational sequencial (~3–5 dias).
2. US1 sequencial (~3 dias).
3. US2 + US3 paralelos (~2 dias cada) — devs trabalham em features independentes.
4. US4 + US5 paralelos (~2 dias cada).
5. US6 sequencial (~2 dias).
6. Polish (~1 dia).

Time maior (3+ devs):

1. Setup + Foundational juntos (~3 dias).
2. Pós-Foundational: 1 dev em US1, 1 dev em US2, 1 dev em US5 (em paralelo).
3. Após US1/US2: dev livre move para US3, depois US4 e US6.

---

## Notes

- [P] tasks = arquivos distintos, sem dependências pendentes.
- [Story] mapeia traceability: cada FR/SC do spec.md tem tarefas em sua user story (ver data-model §7 para mapping FR ↔ implementação).
- Cada user story deve ser independentemente testável e deployable.
- TDD obrigatório (princípio VII) — testes RED antes de implementação.
- Commit após cada task ou grupo lógico (ex.: cada subgrupo "Tests for User Story X" em um commit; cada implementação atômica em commit separado).
- Stop em qualquer checkpoint para validar story isoladamente.
- Avoid: tarefas vagas (escrever "implementar X"), conflitos no mesmo arquivo entre tasks paralelas, dependências cross-story que quebram independência.
