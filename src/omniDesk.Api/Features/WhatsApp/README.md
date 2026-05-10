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
