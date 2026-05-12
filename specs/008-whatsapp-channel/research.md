# Research — Spec 008 WhatsApp

**Owner**: Phase 0 do `/speckit-plan`. Resolve incógnitas técnicas antes de Phase 1.
**Status**: aprovado para Phase 1.

---

## R1. Versão da Graph API

**Decisão**: usar `v19.0` da Meta Cloud API.

**Rationale**:
- A spec do user input cita explicitamente `https://graph.facebook.com/v19.0/{phone_number_id}/messages`.
- `v19.0` é GA desde fev/2024 e tem ciclo de vida garantido até ~mai/2026 (Meta mantém versão por 2 anos).
- Estrutura de payload de webhook estável desde `v17`.

**Alternativas consideradas**:
- `v20.0` (mais recente): rejeitado — campos novos não são usados; risco de changes não-óbvios.
- `v17.0` (mais antiga ainda em LTS): rejeitado — depreca antes de `v19.0`, força upgrade dentro da V1.

**Implicação**: chave de configuração `WhatsApp:GraphApiBaseUrl` com default `https://graph.facebook.com/v19.0`. Upgrades futuros = mudar a default + smoke test cassetes.

---

## R2. Estratégia de webhook async (200 OK rápido)

**Decisão**: webhook controller faz **mínimo possível** inline e enfileira o resto:

1. Validar HMAC (constant-time) — ~1 ms.
2. Parse JSON parcial só pra extrair `entry[].changes[].value.messages[].id` (`wa_message_id`).
3. Dedup em Redis (`SET NX EX 86400`).
4. Se novo: `LPUSH` payload bruto para fila Hangfire `wa_webhook_processing`.
5. Retornar 200 OK com body vazio.

Tudo em ≤ 100 ms — bem dentro do SLO interno de 5 s e do timeout Meta de 20 s.

**Rationale**:
- Meta retenta agressivamente em timeout (até 7 dias) → loop de retentativas se inline.
- Processamento (IA, DB writes, MinIO) tem latência variável; não é seguro como caminho síncrono.
- Hangfire já tem fila + retry exponencial maduro.

**Alternativas consideradas**:
- Processar inline em background `Task.Run`: rejeitado — sem persistência de fila, perde em crash.
- Channel `IBackgroundQueue` em-memória: rejeitado — mesma razão; Hangfire já existe.
- Salvar no DB e processar depois: rejeitado — DB write inline já adiciona latência variável.

**Implicação**: novo Hangfire job `WaWebhookProcessorJob` com fila dedicada (`wa_webhook_processing`). Throughput de pico de ~10 msg/s/tenant cabe em 1 worker.

---

## R3. AES-256-GCM vs DataProtection ASP.NET

**Decisão**: implementar `AesGcmEncryptionService` próprio com chave-mestra fixa em `Security:DataProtectionKey`.

**Rationale**:
- DataProtection ASP.NET tem **rotação automática de chaves** (default 90 dias). Tokens cifrados ficam ilegíveis após rotação se a chave antiga não estiver no keyring → quebra de credenciais Meta em produção.
- Access Token Meta **não roda** (é permanente). Precisamos de chave **estável**.
- AES-256-GCM provê autenticated encryption — protege contra tampering (atacante com acesso ao DB não consegue alterar ciphertext sem invalidar a tag).
- `System.Security.Cryptography.AesGcm` é built-in .NET (zero dependência nova).

**Alternativas consideradas**:
- `IDataProtectionProvider`: rejeitado pelo motivo acima (rotação).
- Persisted keys com `PersistKeysToFileSystem`: aceitável mas adiciona ops complexity (gerenciar diretório, backups, sync entre instâncias). GCM com chave única em config é mais simples e suficiente.
- AES-CBC: rejeitado — sem autenticação, vulnerável a chosen-ciphertext attacks; não-recomendado para novos sistemas.
- Hashicorp Vault / AWS KMS: rejeitado — adiciona dependência externa; sobreengineering para V1.

**Implicação**:
- Nova chave `Security:DataProtectionKey` (32 bytes base64). **Sem default** — startup falha se ausente.
- User-secrets em dev; env var em produção (Oracle Cloud).
- Roundtrip test obrigatório em `tests/Infrastructure/Security/`.
- Tamper test: ciphertext modificado deve falhar com `CryptographicException`.

---

## R4. HMAC-SHA256 do webhook — onde armazenar `app_secret`

**Decisão**: armazenar `app_secret` **por tenant** em `whatsapp_config.app_secret`, criptografado com mesmo `AesGcmEncryptionService` do access_token.

**Rationale**:
- Cada tenant pode (em V2+) usar app distinto da Meta com app_secret distinto. Centralizar em um único env var quebra esse modelo.
- Mesma classe de criptografia que access_token — sem nova mecânica.
- Meta documenta que `X-Hub-Signature-256` é HMAC-SHA256 com app_secret como chave sobre o **raw body** (não JSON-parsed).

**Alternativas consideradas**:
- `app_secret` em env var global `WhatsApp:AppSecret`: rejeitado — força todos os tenants a usar o mesmo app Meta, viola §I (Multi-Tenant Isolation) por agregação de credenciais.
- `app_secret` plain text no DB: rejeitado — mesma classe de risco do access_token (LGPD §IV).

**Implicação**:
- Spec original cita apenas `access_token`; **plan.md amenda a tabela** adicionando `app_secret text` (criptografado). Tabela de decisões em Complexity Tracking.
- API pública nunca retorna `app_secret`; apenas indica `app_secret_configured: bool`.
- Validação HMAC requer leitura do **raw body** antes de qualquer model binding — fazer via middleware customizado `RawBodyCaptureMiddleware` aplicado apenas em rotas `/api/public/whatsapp/webhook/*`.

---

## R5. Janela de 24h — fonte da verdade e atualização

**Decisão**: `wa_session_expires_at` é coluna em `conversations` (timestamptz UTC). Atualizada em **dois lugares**:

1. `WaWebhookProcessorJob` ao processar `messages[]` (mensagem do cliente) → `wa_session_expires_at = now() + 24h`.
2. `WhatsAppOutgoingAdapter` **não** atualiza (envio do sistema não estende a janela).

A sentinela `wa.session_expiring` é detectada por `WaSessionExpiringNotifierJob` (cron */5 min) que faz query:

```sql
SELECT id FROM tenant_{slug}.conversations
WHERE channel = 'whatsapp'
  AND status = 'open'
  AND wa_session_expires_at BETWEEN now() AND now() + interval '1 hour'
  AND id NOT IN (SELECT id FROM redis_session_expiring_emitted_for_5min)
```

Para evitar duplicatas no curto prazo (cron a cada 5 min, banda de 1h), usa-se Redis flag `{slug}:wa:expiring_emitted:{conversation_id}` com TTL de 1h.

**Rationale**:
- Pull (cron) é mais simples que push (timer por conversa). 50 conversas ativas/tenant em pico → query barata.
- Window de 5 min para emissão é aceitável: alerta cobre 60-65 min de antecedência.
- Flag em Redis evita spam do mesmo evento em rodadas consecutivas.

**Alternativas consideradas**:
- Background timer por conversa (`Task.Delay(window - 1h)`): rejeitado — não sobrevive a restart, complexidade alta.
- Hangfire `BackgroundJob.Schedule(...)` por conversa: rejeitado — gera milhares de jobs; cron é mais econômico.
- Recalcular janela a partir de `messages[]`: rejeitado — query mais cara que coluna direta; coluna é cache materializado correto.

**Implicação**:
- `wa.session_expired` é emitido pelo **mesmo job**, quando `wa_session_expires_at < now()` e flag `expired_emitted` ainda não setada (TTL 24h).
- Testes: `WaSessionExpiringNotifierJobTests` com seed de conversas em 3 estados (futuro distante, < 1h, expirada).

---

## R6. Mídia recebida — sequência de download da Meta

**Decisão**: pipeline em 2 etapas:

1. `WaWebhookProcessorJob` detecta `messages[].image|document|audio` → cria registro em `messages` com `attachment_url = null` e `attachment_meta_id = {id Meta}` → enfileira `WaMediaDownloadJob`.
2. `WaMediaDownloadJob`:
    a. `GET https://graph.facebook.com/v19.0/{media_id}` com Bearer access_token → retorna `{ url, mime_type, sha256, file_size }`.
    b. `GET {url}` com Bearer (URL é assinada e temporária ~5 min) → bytes.
    c. Validar MIME real via `MimeTypeDetector` (magic bytes — Spec 007).
    d. Upload MinIO `tenant-{slug}/whatsapp-attachments/{conversation_id}/{wa_message_id}-{filename}`.
    e. Atualizar `messages.attachment_url` e emitir `wa.message_status` (variant `attachment_ready`).
    f. Em falha, `messages.attachment_status = failed` e CRM exibe placeholder com retry.

**Rationale**:
- URL Meta tem TTL curto (~5 min) — não dá pra esperar atendente abrir a conversa.
- Mensagem aparece imediatamente no CRM (texto vazio + spinner de mídia) → boa UX em vez de mensagem invisível.
- Validação MIME real protege contra atacante enviando JS/exe disfarçado de imagem.

**Alternativas consideradas**:
- Download síncrono no `WaWebhookProcessorJob`: rejeitado — bloqueia fila se Meta lenta; SLO interno de 10s para mensagem visível pode quebrar.
- Servir URL Meta direta ao CRM: rejeitado — URL expira em 5 min, requer regeneração; vaza access_token se proxy não-autenticado.
- Stream-thru (sem MinIO): rejeitado — perde retenção/auditoria; CRM precisa baixar de novo a cada visualização.

**Implicação**: nova coluna `attachment_status` em `messages` (`pending|ready|failed`); ou reusar campo de erro no JSON metadata. Decisão: reusar `messages.metadata` (jsonb) com chave `wa_attachment_status`. Simplifica schema.

---

## R7. Templates pré-definidos — fonte da verdade

**Decisão**: `Domain/WhatsApp/PredefinedTemplates.cs` é static factory:

```csharp
public static class PredefinedTemplates
{
    public static readonly IReadOnlyDictionary<TemplateType, PredefinedTemplate> ByType = new Dictionary<TemplateType, PredefinedTemplate>
    {
        [TemplateType.AppointmentReminder] = new(
            DefaultBody: "Olá, {{1}}! Lembramos que você tem uma consulta agendada para {{2}} às {{3}}. Confirme com SIM ou cancele com NÃO.",
            VariableLabels: new[] { "nome do cliente", "data da consulta", "horário" },
            VariableCount: 3),
        [TemplateType.AppointmentConfirmation] = new(
            DefaultBody: "Olá, {{1}}! Seu agendamento para {{2}} às {{3}} foi confirmado. Até lá!",
            VariableLabels: new[] { "nome do cliente", "data da consulta", "horário" },
            VariableCount: 3),
        // ... AppointmentCancellation, FollowUp
        [TemplateType.Custom] = new(DefaultBody: "", VariableLabels: Array.Empty<string>(), VariableCount: 0),
    };
}
```

`POST /api/whatsapp/templates` valida:
- Para tipos pré-definidos: `body.Count(c => c == '{') / 2 == VariableCount` (placeholders preservados); `variable_labels.Length == VariableCount`.
- Para `custom`: tenant define quantas variáveis quiser; backend conta os placeholders e exige `variable_labels.Length` correspondente.

**Rationale**:
- Static factory é a forma mais simples e legível (10 linhas vs migration + seed).
- Mudança de body padrão = code change + amendment ADR. Suficiente em V1.

**Alternativas consideradas**:
- Tabela `template_definitions` com seed: rejeitado — adiciona migration e join sem ganho operacional (V1 não muda os padrões em runtime).
- JSON file (`PredefinedTemplates.json`): rejeitado — mais difícil de testar e validar tipos.

**Implicação**: testes em `Domain/WhatsApp/PredefinedTemplatesTests.cs` validam que cada tipo tem variable_count correto e body com placeholders matching.

---

## R8. Detecção de Access Token revogado / expirado

**Decisão**: `WhatsAppOutgoingAdapter` captura `HttpRequestException` da Graph API. Se status `401` ou `190` (sub-error Meta "Access token expirado"), enfileira `WaTokenRevokedDetectorJob` com `tenant_slug` + `attempted_message_id`.

`WaTokenRevokedDetectorJob`:
1. Carrega `whatsapp_config`.
2. Faz **uma** validação rápida (`GET /me` na Graph API).
3. Se 401 confirmado → `is_enabled = false`, registra incident em `{slug}_wa_incidents` (Mongo), envia notificação in-app + email para `tenant_admin`.
4. Marca a mensagem como `failed` com `error_code = TOKEN_REVOKED`.

**Rationale**:
- Não desativar canal na primeira 401 isolada (pode ser bug transitório Meta) — fazer **uma** verificação fora do contexto da mensagem.
- Notificação dupla (in-app + email) garante chance de reação.
- Soft-disable evita perda de mensagens enviadas legitimamente em paralelo.

**Alternativas consideradas**:
- Desativar imediatamente na 1ª 401: rejeitado — fragiliza canal a falhas transitórias.
- Health check periódico: rejeitado — overhead sem benefício; Meta não recomenda polling de `/me`.
- Não desativar: rejeitado — mensagens contínuas falhando geram filas grandes e UX ruim.

**Implicação**: nova collection Mongo `{slug}_wa_incidents` (estrutura simples — `{type, occurred_at, details}`). Reativação manual: `tenant_admin` insere novo Access Token na tela de config e clica "Ativar" — `PATCH /api/whatsapp/config/toggle` valida com `GET /me` antes de setar `is_enabled = true`.

---

## R9. Webhook de status de template — Meta paths

**Decisão**: o webhook **único** `POST /api/public/whatsapp/webhook/{tenant_slug}` recebe **dois tipos** de payload Meta:

- `entry[].changes[].field = "messages"` — mensagens recebidas + status updates de mensagens enviadas.
- `entry[].changes[].field = "message_template_status_update"` — aprovação/rejeição de template.

Roteamento dentro de `WaWebhookProcessorJob`:

```csharp
foreach (var change in payload.Entry.SelectMany(e => e.Changes))
{
    switch (change.Field)
    {
        case "messages":
            await _messageHandler.HandleAsync(change.Value, ct);
            break;
        case "message_template_status_update":
            await _templateStatusHandler.HandleAsync(change.Value, ct);
            break;
        default:
            _logger.LogInformation("WhatsApp webhook field ignored: {Field}", change.Field);
            break;
    }
}
```

**Rationale**:
- Meta entrega tudo no mesmo endpoint. Tentar separar URLs força configuração dupla na Meta (mais setup, mais risco).
- Switch por `change.Field` é claro e testável.

**Alternativas consideradas**:
- Endpoints separados: rejeitado — Meta não suporta múltiplas URLs por WABA.
- Filtro Meta-side por tipo: rejeitado — não é configurável.

**Implicação**: `MetaWebhookFixtures` precisa de cassete específico para `message_template_status_update` (approved + rejected com motivo).

---

## R10. RBAC — diferenciação supervisor vs tenant_admin

**Decisão**: política de autorização declarativa:

```csharp
// Endpoints WhatsApp
group.MapGet("/config", GetConfig).RequireAuthorization("WhatsAppConfigRead");        // tenant_admin, supervisor
group.MapPut("/config", UpdateConfig).RequireAuthorization("WhatsAppConfigWrite");    // tenant_admin only
group.MapPatch("/config/toggle", Toggle).RequireAuthorization("WhatsAppConfigWrite"); // tenant_admin only

group.MapGet("/templates", ListTemplates).RequireAuthorization("WhatsAppTemplatesRead");      // tenant_admin, supervisor, attendant
group.MapPost("/templates", CreateTemplate).RequireAuthorization("WhatsAppTemplatesWrite");   // tenant_admin, supervisor
group.MapPut("/templates/{id}", UpdateTemplate).RequireAuthorization("WhatsAppTemplatesWrite");
group.MapPost("/templates/{id}/submit", SubmitTemplate).RequireAuthorization("WhatsAppTemplatesWrite");
group.MapDelete("/templates/{id}", DeleteTemplate).RequireAuthorization("WhatsAppTemplatesWrite");
```

Polices em `Infrastructure/Auth/AuthorizationPolicies.cs`:
- `WhatsAppConfigRead` = `RequireRole(Roles.TenantAdmin, Roles.Supervisor)`.
- `WhatsAppConfigWrite` = `RequireRole(Roles.TenantAdmin)`.
- `WhatsAppTemplatesRead` = `RequireRole(Roles.TenantAdmin, Roles.Supervisor, Roles.TenantAttendant)`.
- `WhatsAppTemplatesWrite` = `RequireRole(Roles.TenantAdmin, Roles.Supervisor)`.

**Rationale**:
- Spec FR-002 exige `supervisor` ler-mas-não-editar config — política `WhatsAppConfigRead/Write` separadas resolve.
- Spec FR-027 exige attendant **sem acesso** a templates — atendente cai na policy `WhatsAppTemplatesRead` apenas para listar templates `approved` (read-only no momento de envio); criação/edição é negada.

**Alternativas consideradas**:
- Verificação imperativa em handlers (`if (!user.IsInRole(...)) return Forbid();`): rejeitado — espalha lógica RBAC; viola DRY.
- Atributo `[Authorize(Roles = "...")]`: aceitável, mas Minimal API + endpoint groups privilegia policies nomeadas.

**Implicação**: `Roles.cs` (Spec 004) deve ter `TenantAdmin`, `Supervisor`, `TenantAttendant`, `SaasAdmin` como const. Verificar antes de implementar — se não tiver `Supervisor`, precisa amendment Spec 004.

---

## Resolved unknowns

✅ Versão Graph API: `v19.0`.
✅ Async webhook: Hangfire fila `wa_webhook_processing`.
✅ Crypto: AES-256-GCM próprio.
✅ HMAC: app_secret por tenant, criptografado.
✅ Janela 24h: coluna `wa_session_expires_at` + cron 5min.
✅ Mídia: download async + MinIO + magic bytes.
✅ Templates pré-definidos: static factory.
✅ Token revogado: detect-then-confirm + auto-disable + notify.
✅ Webhook routing: `field` switch dentro do processor job.
✅ RBAC: 4 policies (config read/write, templates read/write).

**Sem [NEEDS CLARIFICATION] pendente**. Phase 1 liberada.
