# Quickstart — Spec 008 WhatsApp

**Owner**: Phase 1. Roteiros de validação manual + checklist de smoke após cada wave do `/speckit-tasks`.

---

## 0. Setup local (uma única vez)

```bash
cd src/omniDesk.Api

# 1. User-secrets para chave AES + sandbox Meta
dotnet user-secrets set "Security:DataProtectionKey" "$(openssl rand -base64 32)"
dotnet user-secrets set "WhatsApp:GraphApiBaseUrl"   "https://graph.facebook.com/v19.0"

# 2. Subir docker-compose com Postgres + Redis + Mongo + MinIO (já existente)
cd ../../infra
docker compose up -d

# 3. Rodar migrations
cd ../src/omniDesk.Api
dotnet ef database update

# 4. Iniciar API
dotnet run

# 5. (em outro terminal) Iniciar CRM
cd src/omniDesk.Crm
npm install
npm start
```

API expõe `https://localhost:5001` (ou conforme `launchSettings.json`).
CRM em `http://localhost:4200`.

---

## 1. Provisionar tenant de teste

```bash
# Via API (assumindo Spec 003 OK)
curl -X POST https://localhost:5001/api/saas/tenants \
  -H "Authorization: Bearer $SAAS_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "slug": "clinica-abc",
    "display_name": "Clínica ABC",
    "primary_email": "admin@clinica-abc.com.br"
  }'
```

**Smoke check**: tabela `tenant_clinica_abc.whatsapp_config` deve ter 1 linha com:
- `is_enabled = false`
- `webhook_verify_token` preenchido (32+ chars)
- demais campos null

```sql
SELECT is_enabled, length(webhook_verify_token), phone_number_id IS NULL AS no_phone, access_token_ciphertext IS NULL AS no_token
FROM tenant_clinica_abc.whatsapp_config;
-- esperado: false | 64 | true | true
```

---

## 2. Configurar canal pelo CRM (US2)

1. Login no CRM como `tenant_admin@clinica-abc.com.br`.
2. Ir em **Configurações → WhatsApp**.
3. Verificar:
   - Badge **🔴 Não configurado**.
   - Webhook URL e Verify Token visíveis e copiáveis.
   - Campos editáveis vazios.
4. Preencher:
   - Phone Number ID: `123456789012345` (sandbox Meta)
   - WABA ID: `987654321098765`
   - Access Token: `EAAB...test_token...` (token de sandbox)
   - App Secret: `abcdef0123456789abcdef0123456789` (32 hex)
   - Display Name: `Clínica ABC Saúde`
5. Clicar **Salvar** → toast de sucesso → badge muda para **🟡 Configurado / Inativo**.
6. Clicar **Ativar** → backend faz `GET /me` na Meta → se OK, badge muda para **🟢 Ativo**.

**Smoke check**:

```bash
curl https://localhost:5001/api/whatsapp/config -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
# Esperado:
# - access_token_configured: true
# - app_secret_configured: true
# - access_token NOT in response (validar grep)
# - is_enabled: true
# - channel_status: "active"
```

```bash
# Confirmar ciphertext em DB
psql -c "SELECT access_token_ciphertext FROM tenant_clinica_abc.whatsapp_config;"
# Esperado: string base64 ~120 chars (não plain text)
```

---

## 3. Verify webhook GET (US1)

```bash
VERIFY_TOKEN=$(psql -t -c "SELECT webhook_verify_token FROM tenant_clinica_abc.whatsapp_config;" | xargs)
CHALLENGE="1234567890"

curl -i "https://localhost:5001/api/public/whatsapp/webhook/clinica-abc?hub.mode=subscribe&hub.verify_token=$VERIFY_TOKEN&hub.challenge=$CHALLENGE"
# Esperado: 200 OK + body: 1234567890
```

```bash
# Token errado
curl -i "https://localhost:5001/api/public/whatsapp/webhook/clinica-abc?hub.mode=subscribe&hub.verify_token=WRONG&hub.challenge=$CHALLENGE"
# Esperado: 403
```

---

## 4. Webhook POST — mensagem de texto (US1)

Usar fixture `tests/Helpers/Fixtures/WhatsApp/webhook-text-message.json`:

```bash
APP_SECRET=$(cat tests/Helpers/Fixtures/WhatsApp/app_secret_test.txt)
PAYLOAD=$(cat tests/Helpers/Fixtures/WhatsApp/webhook-text-message.json)
SIG="sha256=$(echo -n "$PAYLOAD" | openssl dgst -sha256 -hmac "$APP_SECRET" | awk '{print $2}')"

curl -i -X POST https://localhost:5001/api/public/whatsapp/webhook/clinica-abc \
  -H "Content-Type: application/json" \
  -H "X-Hub-Signature-256: $SIG" \
  -d "$PAYLOAD"
# Esperado: 200 OK em < 500 ms
```

**Smoke check** (após ~5 segundos):

```sql
-- Visitor criado
SELECT id, name FROM tenant_clinica_abc.visitors WHERE metadata->>'wa_phone' = '+5511988887777';

-- Conversation criada com janela
SELECT channel, status, wa_contact_phone, wa_session_expires_at
FROM tenant_clinica_abc.conversations
WHERE wa_contact_phone = '+5511988887777';
-- esperado: channel=whatsapp | status=open | wa_session_expires_at ≈ now+24h

-- Message persistida
SELECT content_type, content, metadata->>'wa_message_id'
FROM tenant_clinica_abc.messages
WHERE conversation_id = (SELECT id FROM tenant_clinica_abc.conversations WHERE wa_contact_phone = '+5511988887777');
-- esperado: text | "Olá, gostaria de marcar consulta" | wamid.HBgL...
```

```bash
# IA processou (logs Serilog)
docker logs omnidesk-api 2>&1 | grep "AgentOrchestrator.ProcessAsync.*conv_"
```

CRM (logado como `tenant_admin` ou `tenant_attendant` no dept correto):
- A nova conversa aparece na inbox em ≤ 10 s.
- Mensagem do visitante visível.
- Mensagem da IA aparece logo após (depende do mock OpenAI ou OpenAI real).

---

## 5. HMAC inválido (segurança)

```bash
curl -i -X POST https://localhost:5001/api/public/whatsapp/webhook/clinica-abc \
  -H "Content-Type: application/json" \
  -H "X-Hub-Signature-256: sha256=deadbeef..." \
  -d "$PAYLOAD"
# Esperado: 403
```

```sql
-- Confirma que NADA foi persistido
SELECT count(*) FROM tenant_clinica_abc.messages WHERE created_at > now() - interval '1 minute';
-- esperado: igual ao baseline (nenhum aumento)
```

---

## 6. Atendente envia mensagem dentro da janela (US3)

CRM, conversa aberta:
1. Atendente clica "Assumir conversa" (ou conversa já está com humano).
2. Digita "Olá! Como posso ajudar?" e envia.
3. Ícone aparece como `✓` (sent) imediatamente.
4. Aguardar webhook Meta de `delivered` chegar (em sandbox, simular via curl).

```bash
# Simular webhook delivered
PAYLOAD=$(cat tests/Helpers/Fixtures/WhatsApp/webhook-status-delivered.json | sed "s/WAMID_PLACEHOLDER/$WAMID_FROM_LAST_SEND/")
SIG=$(...)
curl -X POST https://localhost:5001/api/public/whatsapp/webhook/clinica-abc -H "X-Hub-Signature-256: $SIG" -d "$PAYLOAD"
```

**Smoke check**:
- CRM atualiza ícone para `✓✓` em ≤ 3 s.
- MongoDB `clinica_abc_wa_message_statuses` tem registro `status=delivered`.

Repetir para `status=read` → ícone vira `✓✓` azul.

---

## 7. Janela 24h expirada (US4)

```bash
# Simular janela expirada
psql -c "UPDATE tenant_clinica_abc.conversations SET wa_session_expires_at = now() - interval '1 hour' WHERE wa_contact_phone = '+5511988887777';"

# Aguardar até 5 min OU rodar manualmente o job:
curl -X POST https://localhost:5001/api/jobs/wa-session-expiring-notifier/run -H "Authorization: Bearer $SAAS_ADMIN_TOKEN"
```

CRM:
- Banner vermelho aparece: "🚫 A janela de 24h expirou. Selecione um template para enviar."
- Input de texto livre fica disabled.
- Botão "Selecionar template" aparece.

Tentar enviar texto livre via API direta:

```bash
curl -X POST https://localhost:5001/api/whatsapp/send \
  -H "Authorization: Bearer $TENANT_ATTENDANT_TOKEN" \
  -d '{"conversation_id":"...","content":"texto livre"}'
# Esperado: 422 WA_OUTSIDE_WINDOW
```

---

## 8. Criar e submeter template (US5)

CRM, **Configurações → WhatsApp → Templates**:

1. Clicar "Novo template".
2. Selecionar tipo `appointment_reminder`.
3. Body pré-preenchido com texto padrão; 3 variáveis `{{1}}`, `{{2}}`, `{{3}}`.
4. Editar texto sem mudar variáveis.
5. Clicar "Submeter à Meta".

**Smoke check**:

```sql
SELECT name, status, meta_template_id, submitted_at
FROM tenant_clinica_abc.whatsapp_templates
WHERE type = 'appointment_reminder';
-- esperado: lembrete_consulta_clinicaabc | pending_meta | <id Meta> | <agora>
```

Simular webhook `APPROVED`:

```bash
PAYLOAD=$(cat tests/Helpers/Fixtures/WhatsApp/webhook-template-approved.json)
SIG=$(...)
curl -X POST https://localhost:5001/api/public/whatsapp/webhook/clinica-abc -H "X-Hub-Signature-256: $SIG" -d "$PAYLOAD"
```

CRM atualiza badge para 🟢 **Aprovado** em ≤ 5 s. Template aparece na lista de seleção (US4).

---

## 9. Enviar template fora da janela (US4 + US5)

CRM, conversa com banner vermelho:
1. Clicar "Selecionar template".
2. Escolher `lembrete_consulta_clinicaabc`.
3. Preencher variáveis: `{{1}}` = "Maria", `{{2}}` = "20/05/2026", `{{3}}` = "10:00".
4. Clicar "Enviar".

**Smoke check**:
- Mensagem aparece na conversa com ícone `✓`.
- Após delivered (simulado): `✓✓`.
- DB: `messages.metadata->>'wa_template_id' = '<uuid>'` e `wa_template_variables = {"1":"Maria",...}`.

---

## 10. Cliente reabre janela

```bash
# Simular nova mensagem do cliente
PAYLOAD=$(cat tests/Helpers/Fixtures/WhatsApp/webhook-text-message.json | sed 's/Olá, gostaria de marcar/Obrigada/')
SIG=$(...)
curl -X POST .../webhook/clinica-abc -H "X-Hub-Signature-256: $SIG" -d "$PAYLOAD"
```

CRM:
- Banner vermelho some.
- Input de texto livre re-habilita.
- `wa_session_expires_at` atualizado para now+24h.

---

## 11. Recepção de mídia (US6)

```bash
# Webhook image
PAYLOAD=$(cat tests/Helpers/Fixtures/WhatsApp/webhook-image-message.json)
SIG=$(...)
curl -X POST .../webhook/clinica-abc -H "X-Hub-Signature-256: $SIG" -d "$PAYLOAD"
```

**Smoke check**:
- Em ≤ 5 s, MinIO tem objeto em `tenant-clinica-abc/whatsapp-attachments/{conv_id}/`.
- `messages.attachment_url` preenchida.
- CRM exibe preview da imagem inline.

Repetir para `webhook-document-message.json` (PDF) e `webhook-audio-message.json` (audio):
- PDF → link de download.
- Audio → player inline.

Tipo não suportado:

```bash
PAYLOAD=$(cat tests/Helpers/Fixtures/WhatsApp/webhook-unsupported-sticker.json)
SIG=$(...)
curl -X POST .../webhook/clinica-abc -H "X-Hub-Signature-256: $SIG" -d "$PAYLOAD"
# Esperado: 200 OK; nenhuma mensagem nova no CRM; log de auditoria 'WaUnsupportedMessageType'
```

---

## 12. RBAC

```bash
# supervisor tenta editar config → 403
curl -X PUT .../api/whatsapp/config -H "Authorization: Bearer $SUPERVISOR_TOKEN" -d '{...}'
# Esperado: 403 FORBIDDEN

# tenant_attendant tenta criar template → 403
curl -X POST .../api/whatsapp/templates -H "Authorization: Bearer $ATTENDANT_TOKEN" -d '{...}'
# Esperado: 403 FORBIDDEN

# tenant_attendant lista templates approved → OK (somente leitura para envio)
curl .../api/whatsapp/templates?status=approved -H "Authorization: Bearer $ATTENDANT_TOKEN"
# Esperado: 200 com array
```

---

## 13. Token revogado

```bash
# Forçar 401 da Meta (mock)
# Em env de teste, configurar mock para retornar 401 code=190 no próximo SendAsync.
# Atendente envia mensagem.

# Esperado:
# - Mensagem fica failed.
# - WaTokenRevokedDetectorJob roda.
# - whatsapp_config.is_enabled = false (após confirmação 401 em /me).
# - Notificação in-app + email para tenant_admin.
# - CRM mostra banner de canal desativado.
```

---

## 14. Checklist final de pronto-pra-merge

- [ ] Todos 6 user stories validados manualmente neste quickstart.
- [ ] `dotnet test src/omniDesk.Api/tests/` passa 100%.
- [ ] `npm test --prefix src/omniDesk.Crm` passa 100%.
- [ ] Migrations rodaram em tenant existente (não-novo) sem erro.
- [ ] `/api/whatsapp/config` GET nunca retorna `access_token` plain (grep no body).
- [ ] Logs Serilog mostram `access_token` mascarado em todos os sinks.
- [ ] Cassetes de teste cobrem todos os webhooks (text, mídia, status, template, unsupported, malformed).
- [ ] HMAC inválido sempre retorna 403, sem persistência.
- [ ] Webhook 200 OK em < 5s p95 (medido em load test).
- [ ] Build Docker ARM64 sucesso (`docker buildx build --platform linux/arm64`).
- [ ] AC (acceptance criteria) da spec.md todos verificados.
