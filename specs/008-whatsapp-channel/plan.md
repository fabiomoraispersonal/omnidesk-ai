# Implementation Plan: Canal WhatsApp Business

**Branch**: `008-whatsapp-channel` | **Data**: 2026-05-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/008-whatsapp-channel/spec.md`

## Summary

Spec **canal-WhatsApp** que implementa o segundo adapter de canal do OmniDesk (após Live Chat na Spec 007), atendendo Princípio §III (Channel Agnosticism). O adapter traduz eventos da Meta Cloud API em `IncomingMessage` agnóstico para o `AgentOrchestrator` (Spec 006) e consome `OutgoingMessage` da fila para entregar ao cliente via Meta Graph API.

Reaproveita 100% do pipeline conversacional consolidado em 006+007:

- `IncomingMessageWorker` / `OutgoingMessageWorker` (Hangfire) já consumem as filas `{slug}:incoming_messages` / `{slug}:outgoing_messages`.
- Tabela `conversations` ganha 2 campos (`wa_contact_phone`, `wa_session_expires_at`) — sem nova tabela de mensagem; `messages` continua canal-agnóstica.
- Tabela `visitors` reutilizada (visitante WA é identificado pelo telefone E.164 em `wa_contact_phone`).
- WebSocket CRM `/ws/crm` da Spec 007 ganha 3 eventos novos (`wa.message_status`, `wa.session_expiring`, `wa.session_expired`).

Backend novo entrega:

- 2 tabelas tenant-scoped (`whatsapp_config` 1:1, `whatsapp_templates`) + 2 colunas em `conversations`.
- 1 collection MongoDB `{slug}_wa_message_statuses` (auditoria de status updates).
- Endpoints públicos sem autenticação de usuário, validados por **HMAC-SHA256 + `webhook_verify_token`**: `GET/POST /api/public/whatsapp/webhook/{tenant_slug}`.
- Endpoints autenticados CRM: `/api/whatsapp/{config,templates,send}`.
- Adapters: `WhatsAppIncomingAdapter`, `WhatsAppOutgoingAdapter`, `WhatsAppMetaClient` (HTTP cliente Graph API v19.0).
- 3 jobs Hangfire: `WaSessionExpiringNotifierJob` (cron 5min), `WaTokenRevokedDetectorJob` (disparado em falha 401), `WaTemplateStatusPollerJob` (fallback caso webhook de template status falhe).
- Provisionamento: `TenantProvisioningJob` (Spec 003) ganha geração de `whatsapp_config` vazio + `webhook_verify_token` (32+ chars random).

Frontend CRM (Angular 21) entrega:

- `features/whatsapp-config/` — tela de credenciais (Phone Number ID, WABA ID, Access Token, Display Name) + Webhook URL/Verify Token somente-leitura + toggle ativar/desativar + RBAC visível (supervisor read-only).
- `features/whatsapp-templates/` — CRUD de templates com tipo pré-definido (corpo pré-preenchido por tipo) + botão "Submeter à Meta" + lista com badges de status (`draft|pending_meta|approved|rejected`) + motivo de rejeição.
- Integração na conversa (extensão de `live-chat-inbox` da Spec 007): seletor de template quando janela 24h expirou, ícones de delivery (✓ / ✓✓ / ✓✓ azul / ✗) ao lado de cada mensagem enviada, badge de canal `whatsapp`.

Mídia (`image`, `document`, `audio`) reutiliza o pipeline MinIO + `MimeTypeDetector` da Spec 007 com pequena adição: o backend baixa o binário da Meta (URL temporária assinada com Access Token) antes de subir para o bucket `tenant-{slug}/whatsapp-attachments/{conversation_id}/{wa_message_id}-{filename}`.

Esta spec **fecha o ciclo de canais V1**: Live Chat (007) + WhatsApp (008) cobrem os dois canais omnichannel prometidos pelo PRD.

## Technical Context

**Backend**: C# .NET 10 — Minimal API + Endpoint Groups (continuação)
**Frontend**: TypeScript — Angular 21 Standalone Components + Signals (CRM em `src/omniDesk.Crm/`)
**ORM**: Entity Framework Core 9 + Migrations SQL tenant-scoped (padrão do projeto)

**Storage**:

- PostgreSQL `tenant_{slug}.whatsapp_config` (1:1 tenant), `tenant_{slug}.whatsapp_templates`, mais 2 colunas em `tenant_{slug}.conversations` (`wa_contact_phone varchar(20)`, `wa_session_expires_at timestamptz`).
- Redis `{slug}:incoming_messages` / `{slug}:outgoing_messages` (filas existentes — reutilizadas), `{slug}:wa:dedup:{wa_message_id}` (deduplicação de webhook 24h TTL), `{slug}:wa:rate:webhook` (rate limit defensivo de webhook caso a Meta surte; default 600/min/tenant).
- MongoDB `{slug}_wa_message_statuses` (auditoria — Constituição §VI), opcionalmente `{slug}_wa_webhook_audit` (raw payloads em caso de erro de processamento — V1.1).
- MinIO `tenant-{slug}/whatsapp-attachments/{conversation_id}/{wa_message_id}-{filename}`.

**Background jobs** (Hangfire):

| Worker | Schedule/Trigger | Responsabilidade |
|---|---|---|
| `IncomingMessageWorker` (Spec 006) | Fila `{slug}:incoming_messages` | **REUTILIZADO**. `WhatsAppIncomingAdapter` enfileira aqui. |
| `OutgoingMessageWorker` (Spec 006) | Fila `{slug}:outgoing_messages` | **REUTILIZADO**. `WhatsAppOutgoingAdapter` consome e chama Meta Graph API. |
| `WaWebhookProcessorJob` | Sob demanda (enfileirado pelo controller após 200 OK) | Processa payload Meta: `messages` → `IncomingMessage`; `statuses` → MongoDB + WS event. |
| `WaSessionExpiringNotifierJob` | Cron `*/5 * * * *` (a cada 5 min) | Varre `conversations` com `wa_session_expires_at` cruzando o limiar de 1h e emite `wa.session_expiring`. Para `wa_session_expires_at` < `now()`, emite `wa.session_expired` (uma vez — usa Redis flag). |
| `WaTokenRevokedDetectorJob` | Disparado por `OutgoingMessageWorker` em 401 da Meta | Marca `whatsapp_config.is_enabled = false`, registra incident, envia notificação in-app + email para `tenant_admin`. |
| `WaTemplateStatusPollerJob` | Cron `0 * * * *` (a cada hora) | Fallback: para templates `pending_meta` há > 1h sem webhook de status, faz `GET /message_templates/{id}` na Meta. |
| `WaMediaDownloadJob` | Enfileirado pelo `WaWebhookProcessorJob` ao detectar mídia | Faz GET autenticado na URL temporária da Meta → upload MinIO → atualiza `messages.attachment_url`. |

**WebSocket**: ASP.NET Core nativo + Redis Pub/Sub (ADR-005). **Sem novo endpoint** — reutiliza `/ws/crm` da Spec 007. Eventos novos publicados em `{slug}:crm:dept:{department_id}`:

- `wa.message_status` — payload `{ conversation_id, message_id, status, timestamp, error_code?, error_message? }`. Emitido por `WaWebhookProcessorJob` ao processar `statuses`.
- `wa.session_expiring` — payload `{ conversation_id, expires_at }`. Emitido por `WaSessionExpiringNotifierJob` quando janela cruza 1h restante.
- `wa.session_expired` — payload `{ conversation_id }`. Emitido pelo mesmo job no momento da expiração.

**Meta Graph API integration**: HTTP cliente `WhatsAppMetaClient` em `Infrastructure/WhatsApp/`:

- Base URL configurável `Meta:GraphApiBaseUrl` (default `https://graph.facebook.com/v19.0`).
- `Polly` (já em uso) para retry exponencial: 3 tentativas, backoff 1s/2s/4s, **só em 5xx e timeout**; 4xx (incluindo 401, 403) **não** retentam.
- `IHttpClientFactory` com `HttpClient` named `WhatsAppGraph` — typed client.
- Métodos: `SendTextAsync`, `SendTemplateAsync`, `SendMediaAsync`, `DownloadMediaAsync`, `SubmitTemplateAsync`, `GetTemplateStatusAsync`.

**Crypto**: `Infrastructure/Security/AesGcmEncryptionService` (NOVO):

- AES-256-GCM (não CBC) — autenticated encryption.
- Chave-mestra derivada de `Security:DataProtectionKey` (32 bytes base64 em `appsettings`/user-secrets).
- Cada `access_token` armazenado com `(nonce, ciphertext, tag)` — formato `Convert.ToBase64String(nonce ‖ ciphertext ‖ tag)`.
- Decrypt apenas em memória, dentro de `WhatsAppMetaClient.SendAsync` — nunca serializado em DTO de saída.

**HMAC validation**: `Infrastructure/WhatsApp/MetaWebhookSignatureValidator`:

- Lê header `X-Hub-Signature-256`.
- Computa `HMACSHA256(rawBody, app_secret)` — `app_secret` armazenado por tenant em `whatsapp_config.app_secret` (NOVO campo, criptografado igual ao access_token; complementa `access_token` — Meta exige ambos).
- Compara em **constant time** (`CryptographicOperations.FixedTimeEquals`).

**Testing**:

- Backend: xUnit + Testcontainers (Postgres + Redis + Mongo + MinIO — todos já configurados pela Spec 007).
- Meta API: `MockHttpMessageHandler` no padrão da Spec 006 (sem rede real). Cassetes JSON canônicos da Meta em `tests/Helpers/Fixtures/WhatsApp/` (mensagem texto, image, document, audio, status sent/delivered/read/failed, webhook verification, template approved/rejected).
- WebSocket: reusa `WebSocketTestClient` da Spec 007.
- HMAC: testes garantem rejeição de assinatura inválida, faltante, e caso `app_secret` não configurado.
- CRM: Angular TestBed (`.spec.ts` co-localizado), seguindo padrão estabelecido.

**Target Platform**: Linux ARM64 (API); Cloudflare Pages (CRM). Sem widget nesta spec — webhook é HTTP server-side puro.

**Project Type**: Web service (API .NET 10) + 1 SPA Angular (CRM). Sem novo projeto/bundle.

**Dependências backend** (zero NuGet novo):

| Pacote | Já em uso desde | Uso nesta spec |
|---|---|---|
| `Microsoft.EntityFrameworkCore` 9.x | Spec 002 | Migrations + DbContext |
| `StackExchange.Redis` | Spec 002 | Dedup + sinalizadores de janela |
| `Hangfire` | Spec 003 | 4 jobs novos + reuso de filas |
| `MongoDB.Driver` | Spec 003 | Coleção `wa_message_statuses` |
| `FluentValidation.AspNetCore` | Constituição | Payloads de config + templates |
| `Polly` (`Microsoft.Extensions.Http.Polly`) | Spec 006 | Retry da Meta Graph API |
| `MimeDetective` | Spec 007 | Validação de mídia recebida |
| `Minio` SDK | Spec 003 | Upload de mídia |
| `Serilog` | Spec 002 | Mascaramento de `access_token` (config Destructure.ByTransforming) |

**Dependências frontend CRM** (built-ins + libs em uso na Spec 007):

- PrimeNG 21+ (Tabs, InputText, Textarea, Toast, Dialog, Listbox, Tag, Skeleton, Dropdown).
- Angular `@angular/forms` Reactive Forms (config + variáveis de template).
- `date-fns` + `date-fns-tz` (já em uso) para `wa_session_expires_at` countdown UI.

**Variáveis de configuração** (4 novas em `appsettings.json` / user-secrets — sem `.env`, conforme migração da Spec 006):

| Chave | Default | Uso |
|---|---|---|
| `WhatsApp:GraphApiBaseUrl` | `https://graph.facebook.com/v19.0` | Override para sandbox em testes E2E. |
| `WhatsApp:WebhookProcessingTimeoutSeconds` | `5` | SLO interno: backend deve retornar 200 OK ao webhook em ≤ 5s. |
| `WhatsApp:SessionWindowHours` | `24` | Janela Meta — fixa, exposto só para testes. |
| `WhatsApp:SessionExpiringThresholdMinutes` | `60` | Antecedência do evento `wa.session_expiring`. |
| `Security:DataProtectionKey` | (sem default — falha startup) | Chave-mestra AES-256-GCM. **User-secrets em dev**, env var em produção. |

**Performance Goals**:

- Webhook → 200 OK: p95 ≤ **5 s**, p99 ≤ **10 s** (SC-001).
- Mensagem recebida → visível no CRM: p95 ≤ **10 s** (SC-002).
- Status update Meta → ícone atualizado no CRM: p95 ≤ **3 s** (SC-006).
- Setup completo (zero ao primeiro envio): ≤ **10 min** com credenciais em mãos (SC-008).
- Latência Graph API outgoing (do enqueue ao retorno Meta): p95 ≤ **2 s**.

**Constraints**:

- **Webhook 200 OK síncrono < 5s**: a rota `POST /api/public/whatsapp/webhook/{slug}` faz **apenas**: (1) HMAC validate, (2) parse JSON mínimo (extrair `wa_message_id` para dedup), (3) `LPUSH` em fila Hangfire `wa_webhook_processing`, (4) retorna 200. Processamento real ocorre no `WaWebhookProcessorJob`.
- **Dedup obrigatório**: `wa_message_id` checado em Redis (`SET NX EX 86400`) antes do enqueue. Reentregas Meta = no-op.
- **Tipos não suportados ignorados em silêncio**: o adapter retorna sem persistir, sem responder, sem log de erro. Apenas log estruturado de auditoria (`WaUnsupportedMessageType`).
- **Janela 24h em UTC**: `wa_session_expires_at` é timestamptz UTC. Comparações com `DateTime.UtcNow` em **toda** a stack — nunca local time.
- **IA jamais envia template**: enforced em duas camadas — (1) `AgentOrchestrator` não recebe `TemplateMessage` no contrato; (2) `WaOutgoingGuard.Validate(message)` no início do `OutgoingMessageWorker` rejeita templates cujo `sender_type = ai_agent`.
- **Soft delete**: `whatsapp_config` e `whatsapp_templates` nunca apagados em produção (Constituição §IV). `DELETE /api/whatsapp/templates/{id}` é soft-delete.
- **Access Token nunca em logs**: Serilog `Destructure.ByTransforming<WhatsAppConfigDto>(c => new { c.PhoneNumber, c.DisplayName, AccessToken = "***" })` — propagado para todos os sinks.
- **Migration coexistência com Spec 007**: a coluna `wa_contact_phone` é **nullable** em `conversations`. Conversas LiveChat continuam com null. Idem `wa_session_expires_at`.
- **Único número por tenant V1**: enforced por unique constraint composto: `whatsapp_config(tenant_id)` é PK ou unique — só permite 1 linha. Inserir segunda → erro EF Core, mapeado para `WHATSAPP_CONFIG_ALREADY_EXISTS`.
- **CommonName conflicts**: `messages.sender_type` enum existente ganha valor `system` (já usado em Live Chat). Sem mudança. `conversations.channel` ganha valor `whatsapp` (era `live_chat`).

**Scale/Scope**:

- ~50 conversas WhatsApp ativas/tenant simultâneas em V1.
- ~2.000 mensagens WhatsApp/dia/tenant.
- ~30 templates por tenant (mediano), até 100 (limite hard imposto pela Meta).
- ~10 webhooks/seg/tenant em pico (cliente envia em rajada) — backend deve sustentar com p95 ≤ 5s.
- 1 conexão Hangfire dedicada à fila `wa_webhook_processing` por instância da API.

## Constitution Check

*GATE: deve passar antes de Phase 0 e ser reavaliado após Phase 1.*

| Princípio | Status | Observação |
|---|---|---|
| I. Multi-Tenant Isolation (NN) | ✅ PASS | Todas as tabelas de feature em `tenant_{slug}.*` (`whatsapp_config`, `whatsapp_templates`); colunas adicionais em `conversations` herdam o schema. **Zero modificação** em `public.tenants`. Webhook público recebe `tenant_slug` na URL — `WaWebhookTenantResolver` faz lookup `slug → tenant_id` em `public.tenants` (mesma rota do `TenantResolverMiddleware` original) **antes** de entrar no schema. Filas Redis e canais Pub/Sub prefixados (`{slug}:incoming_messages`, `{slug}:wa:dedup:*`, `{slug}:crm:*`). MongoDB `{slug}_wa_message_statuses`. MinIO bucket `tenant-{slug}` reutilizado. |
| II. AI-First, Human-Assisted | ✅ PASS | Esta spec **não modifica** regras de IA. Apenas adiciona um novo adapter cuja saída é `IncomingMessage` agnóstico. Mantém: gatilhos hardcoded de palavras-chave (PT-BR), detecção de frustração via prompt (PATCH 1.0.1), handoff preserva contexto completo. **Restrição adicional**: IA não envia templates (FR-016) — mas isso **não bloqueia** transbordo: quando janela expirar durante atendimento IA, o sistema escala humano (FR-017), evitando dead-end. Conformidade reforçada. |
| III. Channel Agnosticism | ✅ PASS | **Cumprida exemplarmente**: WhatsApp é adapter (`WhatsAppIncomingAdapter`, `WhatsAppOutgoingAdapter`, `WhatsAppMetaClient`). Zero alteração no `AgentOrchestrator`, `IncomingMessageWorker`, `OutgoingMessageWorker`, `ToolCallDispatcher`, `LiveChatConversationGateway`. `messages` continua canal-agnóstica; `conversations` ganha 2 campos opcionais — não há branching por canal no pipeline central. **Validação**: este é o segundo adapter; se a Spec 007 cumpriu §III, esta deve cumprir reusando o mesmo contrato. Provado por testes de contrato em `tests/Adapters/WhatsAppIncomingAdapterTests.cs`. |
| IV. Security e LGPD (NN) | ✅ PASS | (a) **Access Token AES-256-GCM** at rest + nunca em response/log. (b) **HMAC-SHA256** em todo webhook POST; rejeição com 403 para inválidos; comparação constant-time. (c) **app_secret** armazenado igual ao access_token (criptografado, nunca exposto). (d) **Verify token imutável**, gerado com `RandomNumberGenerator.GetBytes(32)` (≥ 256 bits entropy). (e) **Soft delete** em config + templates. (f) **Refresh tokens** seguem Spec 002 — esta spec não toca auth de usuário. (g) **MIME real** (`MimeTypeDetector` da Spec 007) aplicado em mídia recebida da Meta antes de subir ao MinIO. (h) **Dados em ARM64 BR** (Oracle Cloud + MinIO local) — reuso. (i) **LGPD**: contatos WhatsApp são identificados pelo número que **eles** enviaram à clínica → consentimento é implícito ao enviar; sem coleta proativa de PII pelo sistema. (j) **Token revogado** detectado e canal auto-desativa, evitando vazamento de credenciais antigas. |
| V. Simplicity | ✅ PASS | **Zero pacote NuGet novo** (Polly e MimeDetective já em uso). **Zero novo projeto** (sem widget, sem CLI). Reaproveita `IncomingMessageWorker`/`OutgoingMessageWorker` da Spec 006, `MimeTypeDetector` da Spec 007, `/ws/crm` da Spec 007, `MinioFileService` da Spec 007. 4 chaves de configuração novas, todas com default seguro (a 5ª, `Security:DataProtectionKey`, é obrigatória sem default — falha rápida na inicialização). Adiciona apenas o que a Meta exige (HMAC, dedup, janela 24h, templates, webhook async). 4 jobs novos justificados (3 são side-effects da política Meta: janela, token revogado, status poller; 1 é processamento async do webhook). |
| VI. Observability e Auditability | ✅ PASS | `wa_message_statuses` em MongoDB para auditoria completa de status updates (Constituição VI: "auditável a todo momento"). `messages` permanece imutável após inserção. Cada `wa_message_id` registrado **persiste link 1:1 com `messages.id`** — rastro Meta ↔ OmniDesk. Webhook payloads com erro de processamento opcionalmente vão para `{slug}_wa_webhook_audit` (V1.1 — não bloqueia V1). Atendente vê histórico completo no CRM, incluindo mensagens IA antes do transbordo (já garantido pela Spec 006). Métricas SC-001 a SC-010 são extraíveis de logs Serilog estruturados + MongoDB. |
| VII. Test Discipline | ✅ PASS | Testcontainers para Postgres/Redis/Mongo/MinIO (Spec 007 já configurou todos). Meta API com `MockHttpMessageHandler` + cassetes JSON canônicos (sem rede real). HMAC com casos de teste cobrindo: válido, inválido, faltante, app_secret não configurado. **Zero magic strings**: `Domain/WhatsApp/{TemplateStatus,TemplateType,TemplateCategory,WaMessageStatus,WaUnsupportedTypes}.cs`, `Infrastructure/WhatsApp/MetaApi.cs` (constantes de path/header), `Infrastructure/WebSockets/WhatsAppCrmEvents.cs`. Frontend CRM `.spec.ts` co-localizado. Tests E2E para fluxos webhook+envio em `tests/Features/WhatsApp/Integration/`. |

**Resultado**: Constitution Check **APROVADO sem desvios**. Reavaliação pós-Phase 1 — sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/008-whatsapp-channel/
├── plan.md                          # Este arquivo
├── research.md                      # Phase 0 — decisões técnicas (R1–R10)
├── data-model.md                    # Phase 1 — entidades, migrations, transições
├── quickstart.md                    # Phase 1 — fluxos de validação manual
├── contracts/
│   ├── whatsapp-webhook.md          # Público: GET (verify) / POST (mensagens + status)
│   ├── whatsapp-config-api.md       # CRM: GET/PUT /api/whatsapp/config + PATCH toggle
│   ├── whatsapp-templates-api.md    # CRM: CRUD + submit /api/whatsapp/templates
│   ├── whatsapp-meta-graph.md       # Cliente outbound — POST /messages, /message_templates etc.
│   ├── whatsapp-adapter-contracts.md # WhatsAppIncomingAdapter, WhatsAppOutgoingAdapter (Spec 006 contracts)
│   └── whatsapp-websocket-events.md # 3 eventos novos em /ws/crm
├── checklists/
│   └── requirements.md              # validado no /speckit-specify
└── tasks.md                         # Phase 2 — gerado por /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── omniDesk.Api/
│   ├── Domain/
│   │   ├── WhatsApp/                                          # NOVO
│   │   │   ├── WhatsAppConfig.cs                              # entity 1:1 tenant
│   │   │   ├── WhatsAppTemplate.cs
│   │   │   ├── TemplateStatus.cs                              # enum: Draft, PendingMeta, Approved, Rejected
│   │   │   ├── TemplateType.cs                                # enum: AppointmentReminder, AppointmentConfirmation, AppointmentCancellation, FollowUp, Custom
│   │   │   ├── TemplateCategory.cs                            # enum: Utility (V1 fixo)
│   │   │   ├── WaMessageStatus.cs                             # enum: Sent, Delivered, Read, Failed
│   │   │   ├── WaSupportedMessageType.cs                      # enum: Text, Image, Document, Audio
│   │   │   ├── WaUnsupportedTypes.cs                          # const set: Video, Sticker, Location, Contacts, Reaction, Interactive
│   │   │   ├── PredefinedTemplate.cs                          # value object — body padrão por TemplateType
│   │   │   ├── PredefinedTemplates.cs                         # static factory — body + variable_labels por tipo
│   │   │   ├── IWhatsAppConfigRepository.cs
│   │   │   └── IWhatsAppTemplateRepository.cs
│   │   ├── LiveChat/                                          # MODIFICADO (Spec 007)
│   │   │   ├── Conversation.cs                                # + WaContactPhone, + WaSessionExpiresAt
│   │   │   └── ChannelType.cs                                 # já existe — confirma valor `whatsapp`
│   │   └── Tenants/Tenant.cs                                  # SEM modificação
│   │
│   ├── Features/
│   │   ├── WhatsApp/                                          # NOVO
│   │   │   ├── Webhook/                                       # endpoints públicos (sem auth de user)
│   │   │   │   ├── WhatsAppWebhookEndpoints.cs                # GET /verify-{slug}, POST /{slug}
│   │   │   │   ├── MetaWebhookSignatureValidator.cs           # HMAC-SHA256 constant-time
│   │   │   │   ├── WaWebhookTenantResolver.cs                 # slug → tenant via public.tenants
│   │   │   │   └── WaWebhookProcessorJob.cs                   # processa async fila Hangfire
│   │   │   ├── Config/                                        # CRM endpoints (autenticado)
│   │   │   │   ├── WhatsAppConfigEndpoints.cs                 # GET / PUT / PATCH toggle
│   │   │   │   ├── Commands/{UpdateWhatsAppConfig,ToggleChannel}Command.cs
│   │   │   │   ├── Queries/GetWhatsAppConfig.cs               # NUNCA retorna access_token plain
│   │   │   │   └── Validators/UpdateWhatsAppConfigValidator.cs
│   │   │   ├── Templates/                                     # CRM endpoints (autenticado)
│   │   │   │   ├── WhatsAppTemplatesEndpoints.cs              # CRUD + submit
│   │   │   │   ├── Commands/{CreateTemplate,UpdateTemplate,SubmitTemplate,DeleteTemplate}Command.cs
│   │   │   │   ├── Queries/{ListTemplates,GetTemplate}.cs
│   │   │   │   └── Validators/{CreateTemplate,UpdateTemplate}Validator.cs
│   │   │   ├── Send/                                          # endpoint interno + serviço
│   │   │   │   ├── WhatsAppSendEndpoint.cs                    # POST /api/whatsapp/send (chamado por CRM atendente)
│   │   │   │   ├── SessionWindowGuard.cs                      # valida janela 24h antes de enfileirar outgoing
│   │   │   │   └── WaOutgoingGuard.cs                         # bloqueia template enviado por sender_type=ai_agent
│   │   │   ├── Adapters/                                      # contratos Spec 006
│   │   │   │   ├── WhatsAppIncomingAdapter.cs                 # webhook payload → IncomingMessage + enqueue
│   │   │   │   └── WhatsAppOutgoingAdapter.cs                 # consome OutgoingMessage → WhatsAppMetaClient.Send
│   │   │   └── Jobs/
│   │   │       ├── WaSessionExpiringNotifierJob.cs            # cron */5 min — emite eventos WS
│   │   │       ├── WaTokenRevokedDetectorJob.cs               # disparado por 401 — desativa canal
│   │   │       ├── WaTemplateStatusPollerJob.cs               # cron @hourly — fallback Meta webhook
│   │   │       └── WaMediaDownloadJob.cs                      # baixa mídia Meta → MinIO
│   │   └── TenantProvisioning/                                # MODIFICADO (Spec 003)
│   │       └── TenantProvisioningJob.cs                       # + criar WhatsAppConfig vazio + gerar webhook_verify_token
│   │
│   ├── Hubs/                                                  # MODIFICADO (Spec 007)
│   │   ├── CrmWebSocketEndpoint.cs                            # já existe — sem mudança
│   │   └── Events/
│   │       └── WhatsAppCrmEvents.cs                           # NOVO — const: WaMessageStatus, WaSessionExpiring, WaSessionExpired
│   │
│   ├── Infrastructure/
│   │   ├── WhatsApp/                                          # NOVO
│   │   │   ├── WhatsAppConfigConfiguration.cs                 # EF Core
│   │   │   ├── WhatsAppTemplateConfiguration.cs
│   │   │   ├── WhatsAppMetaClient.cs                          # HTTP typed client (Polly retry)
│   │   │   ├── MetaApi.cs                                     # const: paths, headers, hub.* params
│   │   │   ├── WaMessageStatusesRepository.cs                 # MongoDB
│   │   │   └── Migrations/
│   │   │       ├── 20260510_001_AddWhatsAppTables.sql         # whatsapp_config + whatsapp_templates
│   │   │       └── 20260510_002_AddWaFieldsToConversations.sql
│   │   ├── Security/
│   │   │   └── AesGcmEncryptionService.cs                     # NOVO — AES-256-GCM
│   │   └── Storage/
│   │       └── MinioFileService.cs                            # já existe (Spec 007) — sem mudança
│   │
│   └── tests/omniDesk.Api.Tests/
│       ├── Domain/WhatsApp/
│       │   ├── PredefinedTemplatesTests.cs
│       │   └── TemplateStateTransitionsTests.cs               # draft→pending_meta→approved/rejected
│       ├── Features/WhatsApp/
│       │   ├── Webhook/                                       # HMAC válido/inválido, verify GET, dedup, async 200
│       │   ├── Config/                                        # CRUD + RBAC supervisor read-only + access_token mascarado
│       │   ├── Templates/                                     # CRUD + submit + body fixo por tipo + custom livre
│       │   ├── Send/                                          # janela 24h ok/expirada, IA não envia template
│       │   ├── Adapters/                                      # WhatsAppIncomingAdapter substitui pipeline (contract)
│       │   └── Jobs/                                          # SessionExpiring, TokenRevoked, TemplatePoller, MediaDownload
│       ├── Infrastructure/
│       │   ├── WhatsApp/                                      # MetaClient (cassetes), MetaApi consts, Mongo repo
│       │   └── Security/                                      # AesGcmEncryptionService roundtrip + tamper detection
│       └── Helpers/
│           ├── WhatsAppTestHelpers.cs                         # cria tenant + whatsapp_config + access_token cifrado
│           ├── MetaWebhookFixtures.cs                         # JSON canônicos Meta (texto, mídia, status, template approved)
│           └── MockMetaHttpHandler.cs                         # MockHttpMessageHandler reusável
│
└── omniDesk.Crm/                                              # Angular 21 — features novas
    └── src/app/features/
        ├── whatsapp-config/                                   # NOVO — CRM → Configurações → WhatsApp
        │   ├── whatsapp-config.component.ts                   # standalone, signals, lazy
        │   ├── whatsapp-config.component.html
        │   ├── whatsapp-config.component.scss
        │   ├── whatsapp-config.component.spec.ts
        │   ├── components/
        │   │   ├── credentials-form.component.ts              # 4 campos + validators
        │   │   ├── webhook-info.component.ts                  # URL + verify token (read-only, com botão copiar)
        │   │   └── channel-status-badge.component.ts          # 🔴/🟡/🟢
        │   └── services/whatsapp-config.service.ts            # signal store + HTTP
        │
        ├── whatsapp-templates/                                # NOVO — CRM → Configurações → WhatsApp → Templates
        │   ├── whatsapp-templates.component.ts                # lista + dialog de criação/edição
        │   ├── whatsapp-templates.component.html
        │   ├── whatsapp-templates.component.scss
        │   ├── whatsapp-templates.component.spec.ts
        │   ├── components/
        │   │   ├── template-list.component.ts                 # cards com badge de status
        │   │   ├── template-editor.component.ts               # dialog: tipo + body editável + variable_labels (read-only se não custom)
        │   │   └── rejection-reason.component.ts              # tooltip/expansion para `rejected`
        │   └── services/whatsapp-templates.service.ts         # signal store + HTTP
        │
        └── live-chat-inbox/                                   # MODIFICADO (Spec 007) — extensões WhatsApp
            ├── components/
            │   ├── conversation-detail.component.ts           # + ícones delivery (✓/✓✓/✓✓ azul/✗) por mensagem
            │   ├── session-window-banner.component.ts         # NOVO — alerta "Janela expira em Xh" / "Janela expirada"
            │   └── template-picker-dialog.component.ts        # NOVO — abre quando janela expirada e atendente clica enviar
            └── services/
                └── crm-websocket.service.ts                   # + handlers para wa.message_status/expiring/expired
```

**Structure Decision**: Mantém a topologia da Spec 007 (Domain / Features / Infrastructure / Hubs em `omniDesk.Api`; `features/{whatsapp-config,whatsapp-templates}` em `omniDesk.Crm`). O canal WhatsApp **não introduz novo projeto** — diferente da Spec 007 que precisou do `omniDesk.Widget/`. Adapters em `Features/WhatsApp/Adapters/` para deixar claro que cumprem o contrato `IIncomingChannelAdapter`/`IOutgoingChannelAdapter` do `AgentOrchestrator` (Spec 006). Migrations SQL tenant-scoped seguem padrão `YYYYMMDD_NNN_*.sql` consolidado.

## Complexity Tracking

> Apenas violações **justificadas** do Constitution Check. Como o gate passou sem desvios, esta tabela documenta decisões de escopo que poderiam parecer violações mas não são.

| Decisão | Por que é necessária | Alternativa rejeitada |
|---|---|---|
| `webhook_verify_token` E `app_secret` em `whatsapp_config` (não só access_token) | A Meta exige **dois** segredos distintos: o verify token (assinatura inicial do webhook handshake) e o app_secret (HMAC dos POSTs). Sem app_secret não é possível validar autenticidade de cada POST — risco de payload forjado. | Reaproveitar access_token para HMAC: rejeitado — Meta usa app_secret específico do app, não da página/número. |
| `WaTemplateStatusPollerJob` (cron horário) **além** do webhook de status | Webhook de status de template da Meta tem entrega não garantida (perdas conhecidas em rate limit ou indisponibilidade Meta). Sem poller, templates ficam presos em `pending_meta` indefinidamente. | Esperar webhook apenas: rejeitado — UX ruim e inconsistência com fato Meta. |
| `WaWebhookProcessorJob` async (fila) em vez de processar inline | Meta exige 200 OK em ≤ 20s; nosso SLO interno é ≤ 5s. Processar inline (DB write + IA + MinIO upload) **excede** facilmente. Async permite responder em < 100 ms e processar com calma. | Processar inline: rejeitado — risco de timeout Meta + retries massivos. |
| `AesGcmEncryptionService` novo (em vez de DataProtection ASP.NET) | DataProtection é orientado a tokens efêmeros (cookies, claims) com rotação automática — pode invalidar `access_token` cifrado em rotação de chave. Acess Token Meta não rota; precisamos de chave estável + GCM (autenticated). | DataProtection: rejeitado — risco de invalidar credenciais ao restartar/rotar. AES-256-CBC: rejeitado — sem autenticação, vulnerável a tampering. |
| `whatsapp_config(tenant_id)` PK (não FK) | Garante 1:1 estrito por unique constraint nativa do banco. PK = `tenant_id` evita necessidade de unique index extra e simplifica EF mapping. | `id Guid` + `tenant_id` unique: aceitável mas redundante; PK natural é mais simples. |
| Coluna `app_secret` adicionada apesar de não estar na spec original | A spec original cita só `access_token`, mas implementação correta de HMAC exige app_secret separado (Meta convention). Spec será atualizada via amendment durante research.md (R5). | Não adicionar: rejeitado — webhooks ficariam sem validação de autenticidade. |
