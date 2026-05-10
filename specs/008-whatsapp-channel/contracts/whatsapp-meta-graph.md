# Contract — WhatsApp Meta Graph API (Outbound)

**Audience**: `WhatsAppMetaClient` (typed `HttpClient`) → Meta Cloud API.

**Base URL**: `WhatsApp:GraphApiBaseUrl` (default `https://graph.facebook.com/v19.0`).

**Auth**: `Authorization: Bearer {access_token}` em todas as chamadas. Access token decifrado em memória apenas no momento da chamada.

**Retry policy** (Polly):
- Apenas em 5xx e timeout (não em 4xx).
- 3 tentativas, backoff exponencial 1s/2s/4s.
- 401/403 NÃO retentam — tratados como token inválido (R8 → `WaTokenRevokedDetectorJob`).

---

## 1. Enviar mensagem de texto livre

```
POST /{phone_number_id}/messages
Content-Type: application/json
Authorization: Bearer {access_token}

{
  "messaging_product": "whatsapp",
  "recipient_type": "individual",
  "to": "5511988887777",
  "type": "text",
  "text": { "preview_url": false, "body": "Olá! Como posso ajudar?" }
}
```

### Response 200

```json
{
  "messaging_product": "whatsapp",
  "contacts": [{ "input": "5511988887777", "wa_id": "5511988887777" }],
  "messages": [{ "id": "wamid.HBgL..." }]
}
```

### Response 4xx

```json
{
  "error": {
    "message": "(#131047) Re-engagement message",
    "type": "OAuthException",
    "code": 131047,
    "fbtrace_id": "..."
  }
}
```

**Mapeamento de erros Meta → códigos OmniDesk**:

| Meta code | OmniDesk code | Significado |
|---|---|---|
| `131047` | `WA_OUTSIDE_WINDOW` | Fora da janela 24h. Não deveria acontecer (já bloqueamos no SessionWindowGuard). Caso aconteça → log error + falha mensagem. |
| `131026` | `WA_RECIPIENT_NOT_OPTED_IN` | Cliente não autorizou. |
| `190`    | `WA_TOKEN_REVOKED`    | Access token expirado. Trigger `WaTokenRevokedDetectorJob`. |
| `100`    | `WA_INVALID_PARAMETER` | Payload malformado — bug nosso. |
| outros   | `WA_GENERIC_ERROR`     | Mensagem da Meta passada adiante. |

---

## 2. Enviar template aprovado

```
POST /{phone_number_id}/messages

{
  "messaging_product": "whatsapp",
  "to": "5511988887777",
  "type": "template",
  "template": {
    "name": "lembrete_consulta_clinicaabc",
    "language": { "code": "pt_BR" },
    "components": [
      {
        "type": "body",
        "parameters": [
          { "type": "text", "text": "João Silva" },
          { "type": "text", "text": "10/06/2026" },
          { "type": "text", "text": "14:00" }
        ]
      }
    ]
  }
}
```

### Response shape

Mesma do envio de texto.

---

## 3. Enviar mídia

```
POST /{phone_number_id}/messages

{
  "messaging_product": "whatsapp",
  "to": "5511988887777",
  "type": "image",
  "image": { "link": "https://api.omnicare.ia.br/api/public/widget/upload/...", "caption": "Receita" }
}
```

**Nota V1**: envio de mídia **pelo atendente** é fora de escopo desta spec — o input do atendente envia apenas texto. Suporte a anexos para envio é roadmap V1.1. O método `SendMediaAsync` existe no client mas não é exposto pelo CRM em V1.

---

## 4. Submeter template para aprovação

```
POST /{waba_id}/message_templates
Content-Type: application/json

{
  "name": "lembrete_consulta_clinicaabc",
  "category": "UTILITY",
  "language": "pt_BR",
  "components": [
    {
      "type": "BODY",
      "text": "Olá, {{1}}! Lembramos que você tem uma consulta agendada para {{2}} às {{3}}. Confirme com SIM ou cancele com NÃO.",
      "example": {
        "body_text": [["João Silva", "10/06/2026", "14:00"]]
      }
    }
  ]
}
```

### Response 200

```json
{
  "id": "1234567890",
  "status": "PENDING",
  "category": "UTILITY"
}
```

### Response 4xx — exemplo

```json
{
  "error": {
    "message": "Template name is invalid",
    "code": 100
  }
}
```

---

## 5. Polling de status de template (fallback)

```
GET /{waba_id}/message_templates?name=lembrete_consulta_clinicaabc
```

### Response

```json
{
  "data": [
    {
      "name": "lembrete_consulta_clinicaabc",
      "language": "pt_BR",
      "status": "APPROVED",
      "id": "1234567890"
    }
  ]
}
```

Usado pelo `WaTemplateStatusPollerJob` para templates que ficaram > 1h em `pending_meta`.

---

## 6. Download de mídia recebida

### Etapa 1: obter URL temporária

```
GET /{media_id}
Authorization: Bearer {access_token}
```

Response:

```json
{
  "url": "https://lookaside.fbsbx.com/whatsapp_business/attachments/?...",
  "mime_type": "image/jpeg",
  "sha256": "...",
  "file_size": 245031,
  "id": "media_id",
  "messaging_product": "whatsapp"
}
```

### Etapa 2: baixar bytes

```
GET <url>
Authorization: Bearer {access_token}
```

Retorna bytes brutos. URL expira em ~5 minutos.

### Pipeline

1. `WaMediaDownloadJob.RunAsync(messageId, mediaId, mimeTypeFromWebhook, fileNameFromWebhook)`.
2. Etapa 1 → obter URL.
3. Etapa 2 → bytes (max 100 MB — limite Meta).
4. Validar magic bytes via `MimeTypeDetector.Detect(bytes)` → mime real.
5. Se mime real ∈ allowlist → upload MinIO; caso contrário → falha + incident `unsupported_media_type`.
6. Update `messages.attachment_url`, `messages.metadata.wa_attachment_status='ready'`.
7. Emit WS event `wa.message_status` com `payload = { conversation_id, message_id, attachment_ready: true }`.

**Allowlist MIME** (mesmo da Spec 007):
- `image/jpeg`, `image/png`, `image/gif`, `image/webp`
- `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
- `audio/ogg`, `audio/mpeg`, `audio/aac`, `audio/mp4` (NOVOS na Spec 008 — específicos para áudio recebido)

---

## 7. HTTP timeouts e cancellation

| Operação | Timeout | Cancellation |
|---|---|---|
| `SendTextAsync` | 10s | CancellationToken propagado |
| `SendTemplateAsync` | 10s | id. |
| `SubmitTemplateAsync` | 30s (Meta pode demorar) | id. |
| `GetTemplateStatusAsync` | 10s | id. |
| `DownloadMediaAsync` | 60s (mídia até 100MB) | id. |

---

## 8. MockHttpMessageHandler para testes

```csharp
public sealed class MockMetaHttpHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = _ => throw new InvalidOperationException("No handler set");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        => Task.FromResult(Handler(req));
}
```

Cassetes em `tests/Helpers/Fixtures/WhatsApp/MetaResponses/`:

- `send-text-200.json`
- `send-text-401-token-revoked.json`
- `send-template-200.json`
- `submit-template-200.json`
- `submit-template-400-name-invalid.json`
- `template-status-approved.json`
- `template-status-rejected.json`
- `media-meta-200.json` (com URL fictícia)
- `media-bytes-200.bin` (bytes reais de imagem JPEG pequena para validar magic bytes)
