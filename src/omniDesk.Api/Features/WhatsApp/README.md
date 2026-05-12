# Features/WhatsApp

Canal WhatsApp Business — segundo adapter de canal do OmniDesk após Live Chat (Spec 007).

Cumpre o Princípio §III (Channel Agnosticism): traduz eventos da Meta Cloud API em `IncomingMessage`/`OutgoingMessage` agnósticos. Zero alteração no `AgentOrchestrator`, `IncomingMessageWorker`, `OutgoingMessageWorker`, `LiveChatConversationGateway`.

## Estrutura

| Pasta | Conteúdo |
|---|---|
| `Webhook/` | Endpoints públicos (HMAC + verify_token) + processador async via Hangfire |
| `Config/` | CRM endpoints `/api/whatsapp/config` (autenticados via JWT) |
| `Templates/` | CRM endpoints `/api/whatsapp/templates` (CRUD + submit Meta) |
| `Send/` | Endpoint interno `/api/whatsapp/send` + guards (`SessionWindowGuard`, `WaOutgoingGuard`) |
| `Adapters/` | `WhatsAppIncomingAdapter` + `WhatsAppOutgoingAdapter` (cumprem contratos Spec 006) |
| `Jobs/` | 4 Hangfire jobs (session expiring notifier, token revoked detector, template status poller, media download) |

## Documentação

- [Spec](../../../../specs/008-whatsapp-channel/spec.md) — requisitos e user stories
- [Plan](../../../../specs/008-whatsapp-channel/plan.md) — Constitution Check + decisões arquiteturais
- [Research](../../../../specs/008-whatsapp-channel/research.md) — R1–R10 decisões técnicas
- [Data Model](../../../../specs/008-whatsapp-channel/data-model.md) — entidades, migrations, transições
- [Contracts](../../../../specs/008-whatsapp-channel/contracts/) — webhook, config, templates, Meta Graph, adapters, WS events
- [Quickstart](../../../../specs/008-whatsapp-channel/quickstart.md) — 14 roteiros de validação manual
- [Tasks](../../../../specs/008-whatsapp-channel/tasks.md) — breakdown de implementação (149 tarefas)

## Configuração — chaves `WhatsApp:` em `IConfiguration`

`appsettings.json` (committado, sem segredos) define defaults editáveis:

| Chave | Default | O que controla |
|---|---|---|
| `WhatsApp:GraphApiBaseUrl` | `https://graph.facebook.com/v19.0` | Base URL da Meta Cloud API. Override em testes (sandbox) ou para upgrade de versão. |
| `WhatsApp:WebhookProcessingTimeoutSeconds` | `5` | SLO interno do controller (Meta timeout = 20s). Apenas observabilidade — não enforced em código. |
| `WhatsApp:SessionWindowHours` | `24` | Janela Meta. Hard-coded em 24h em produção; configurável apenas para testes. |
| `WhatsApp:SessionExpiringThresholdMinutes` | `60` | Quando emitir `wa.session_expiring` (default 1h antes de expirar). |

Não há segredos no `appsettings.WhatsApp` — credenciais Meta (`access_token`/`app_secret`) vivem em `tenant_{slug}.whatsapp_config` cifradas com AES-256-GCM.

## Chave-mestra de criptografia

Reuso de `Infrastructure/Security/AesEncryptionService` (Spec 003). Lê env var **`AES_ENCRYPTION_KEY`** (32 bytes base64). Sem default — startup falha se ausente.

### Dev (user-secrets)

```bash
cd src/omniDesk.Api
dotnet user-secrets set "AES_ENCRYPTION_KEY" "$(openssl rand -base64 32)"
```

### Produção (env var no container)

```bash
docker run -e AES_ENCRYPTION_KEY="$(cat secrets/aes-key.b64)" ... omnidesk-api
```

⚠️ **Rotação de chave**: AES_ENCRYPTION_KEY é **estável**. Rotar invalida todos os `access_token`/`app_secret` cifrados (cada tenant precisaria reinserir credenciais Meta). Justificativa em [research.md R3](../../../../specs/008-whatsapp-channel/research.md#r3-aes-256-gcm-vs-dataprotection-aspnet).
