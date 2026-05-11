# Spec 008 — Critérios de Aceite

**Source**: [spec.md §11](../spec.md)
**Status**: backend completo (91/149 tasks); frontend Angular + integration tests Testcontainers pendentes.

## Critérios e cobertura

| # | Critério | Status backend | Onde |
|---|---|---|---|
| 1 | Webhook verificado corretamente com `webhook_verify_token` durante setup Meta | ✅ | `WhatsAppWebhookEndpoints.VerifyAsync` (US1 T053) — `FixedTimeEquals` em UTF-8 bytes |
| 2 | Assinatura HMAC-SHA256 (`X-Hub-Signature-256`) validada em cada POST; inválidos = 403 | ✅ | `MetaWebhookSignatureValidator.Validate` (Phase 2 T035) — 9 unit tests cobertos |
| 3 | Backend retorna `200 OK` imediatamente ao receber webhook, antes de processar | ✅ | `WhatsAppWebhookEndpoints.ReceiveAsync` (US1 T053) — enfileira Hangfire, retorna 200 inline |
| 4 | Mensagens texto/imagem/documento recebidas são salvas e exibidas no CRM | ✅ backend / ⏳ CRM UI | `WhatsAppIncomingAdapter` (US1 T054 + US6 T128); CRM exibe via `chat.message_received` WS |
| 5 | Mensagens áudio recebidas e armazenadas no MinIO; CRM exibe como player | ✅ backend / ⏳ CRM UI | `WaMediaDownloadJob` (US6 T129) — magic byte audio detection via `MimeTypeDetector` |
| 6 | Tipos não suportados (video, sticker, location etc.) silenciosamente ignorados | ✅ | `WaUnsupportedTypes` set + early-return em `HandleSingleMessageAsync` (US1 T054) — log info, sem persist |
| 7 | `wa_session_expires_at` atualizado a cada mensagem recebida do cliente | ✅ | `WhatsAppIncomingAdapter` linha ~160 — `_clock.GetUtcNow().AddHours(24)` |
| 8 | Dentro janela 24h: mensagens livres enviadas normalmente | ✅ | `SessionWindowGuard.Validate` (US3 T080) — Text + expiresAt > now passa |
| 9 | Fora janela: CRM bloqueia envio e exige template | ✅ backend / ⏳ CRM UI | `SessionWindowGuard` lança `WaWindowExpiredException` → endpoint retorna 422 `WA_OUTSIDE_WINDOW` |
| 10 | A IA não envia templates — bloqueado no backend | ✅ | `WaOutgoingGuard.Validate` (US3 T081) — `MessageSenderType.AiAgent + Template` → `WaAiTemplateForbiddenException`; aplicado no `WhatsAppOutgoingAdapter` |
| 11 | Status de entrega (sent/delivered/read/failed) exibido no CRM por mensagem | ✅ backend / ⏳ CRM UI | `WhatsAppOutgoingAdapter` + `WaWebhookProcessorJob.HandleStatuses` — WS `wa.message_status` + MongoDB `wa_message_statuses` |
| 12 | `access_token` nunca retornado em texto plano pela API | ✅ | `WhatsAppConfigDto` expõe apenas `access_token_configured` bool; Serilog `Destructure.ByTransforming` mascara em logs (T040) |
| 13 | `supervisor` não pode editar `access_token` — somente visualizar configurado | ✅ | Policies `CanViewChannelStatus` (Supervisor+) vs `CanEditChannelConfig` (TenantAdmin only) — já registradas em `AuthorizationPoliciesRegistration` |
| 14 | Templates com tipo pré-definido têm corpo pré-preenchido e variáveis fixas | ✅ | `PredefinedTemplates` static factory (Phase 2 T017) + `CreateTemplateValidator` enforça variable_count por tipo |
| 15 | Tipo `custom` permite corpo livre | ✅ | `PredefinedTemplates.IsPredefined(Custom) = false`; `CreateTemplateValidator` aplica variable_count check apenas a tipos pré-definidos |
| 16 | Submissão de template para Meta feita via botão no CRM | ✅ backend / ⏳ CRM UI | `SubmitTemplateCommand` (US5 T114) — `POST /api/whatsapp/templates/{id}/submit` chama Meta `POST /message_templates` |
| 17 | Status do template atualizado via webhook Meta (approved/rejected) | ✅ | `WaTemplateStatusHandler.HandleAsync` (US5 T117) — plug em `WaWebhookProcessorJob` para `message_template_status_update` |
| 18 | Templates `rejected` exibem motivo da rejeição no CRM | ✅ backend / ⏳ CRM UI | `WhatsAppTemplate.RejectionReason` setado pelo handler + retornado em `WhatsAppTemplateDto` |
| 19 | Somente templates `approved` ficam disponíveis para seleção (envio fora janela) | ✅ | `ListTemplatesQuery` força `status=approved` para role Attendant; `SendWhatsAppMessageCommand.ExecuteTemplateAsync` valida `template.Status == Approved` |
| 20 | Status `read` exibido visualmente — sem impacto em SLA ou fluxo | ✅ | `WaWebhookProcessorJob` persiste em Mongo + WS broadcast; **nenhum** trigger de SLA/ticket está conectado a status=read |
| 21 | Evento WS `wa.session_expiring` emitido quando janela 24h expira em < 1h | ✅ | `WaSessionExpiringNotifierJob` (US4 T095) cron `*/5min`; idempotência via Redis flag `WaExpiringEmitted` TTL 1h |

## Resumo

- **21/21 critérios atendidos pelo backend** (✅).
- **Critérios com componente frontend pendente** (⏳ CRM UI): #4, #5, #9, #11, #16, #18 — backend está pronto e fornece os dados/WS events; falta apenas o componente Angular consumir.
- **Critérios sem ressalva** (backend autossuficiente): #1, #2, #3, #6, #7, #8, #10, #12, #13, #14, #15, #17, #19, #20, #21.

## O que falta para considerar Spec 008 COMPLETE end-to-end

1. **Frontend Angular (29 tasks)** — `whatsapp-config/`, `whatsapp-templates/`, extensões em `live-chat-inbox/` (ícones delivery, banner janela, template picker, audio player).
2. **Integration tests Testcontainers (16 tasks)** — valida fluxos webhook+IA, RBAC, secrets leak, mídia download, session sweep, token revoked com Postgres/Redis/Mongo/MinIO reais.
3. **Polish restante** — load tests (T134), origin IP audit (T135), E2E integration (T136), manual quickstart run (T145), Docker ARM64 build (T146), staging smoke (T147).

Ver [tasks.md](../tasks.md) para o detalhamento completo.
