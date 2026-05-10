# Contract — WhatsApp Webhook (Public)

**Audience**: Meta Cloud API → OmniDesk API. Sem autenticação de usuário; validado por verify_token (GET) e HMAC-SHA256 (POST).

---

## 1. Endpoints

| Método | Path | Auth | Uso |
|---|---|---|---|
| `GET` | `/api/public/whatsapp/webhook/{tenant_slug}` | verify_token | Setup inicial Meta. |
| `POST` | `/api/public/whatsapp/webhook/{tenant_slug}` | HMAC-SHA256 | Recepção de mensagens + status updates. |

**Raw body**: o middleware `RawBodyCaptureMiddleware` deve estar ativo nessa rota — `Request.Body` é lido e armazenado em `HttpContext.Items["RawBody"]` antes de qualquer model binding, pois HMAC é calculado sobre os bytes brutos.

---

## 2. GET — Webhook verification (Meta setup)

### Request

```
GET /api/public/whatsapp/webhook/clinica-abc?hub.mode=subscribe&hub.verify_token=THE_VERIFY_TOKEN&hub.challenge=1234567890
```

| Query param | Tipo | Notas |
|---|---|---|
| `hub.mode` | string | Sempre `subscribe`. Outros valores → 403. |
| `hub.verify_token` | string | Comparar com `whatsapp_config.webhook_verify_token` (constant-time). |
| `hub.challenge` | string | Echo de volta no body. |

### Response

| Status | Body | Quando |
|---|---|---|
| `200 OK` | `1234567890` (texto plano) | `hub.mode=subscribe` && verify_token confere. Content-Type `text/plain`. |
| `403 Forbidden` | vazio | `hub.mode != subscribe` ou verify_token não confere ou tenant inexistente. |
| `404 Not Found` | vazio | Slug inexistente. |

**Implementação**:

```csharp
group.MapGet("/{slug}", async (string slug, HttpContext ctx, IWhatsAppConfigRepository repo) =>
{
    var mode      = ctx.Request.Query["hub.mode"].ToString();
    var token     = ctx.Request.Query["hub.verify_token"].ToString();
    var challenge = ctx.Request.Query["hub.challenge"].ToString();

    if (mode != "subscribe") return Results.StatusCode(403);

    var config = await repo.FindByTenantSlugAsync(slug);
    if (config is null) return Results.NotFound();

    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(config.WebhookVerifyToken)))
        return Results.StatusCode(403);

    return Results.Text(challenge, "text/plain");
});
```

---

## 3. POST — Webhook recepção

### Request headers

| Header | Obrigatório | Notas |
|---|---|---|
| `X-Hub-Signature-256` | sim | Format: `sha256=<hex_hmac>`. |
| `Content-Type` | sim | `application/json`. |

### Request body — exemplo (mensagem texto)

```json
{
  "object": "whatsapp_business_account",
  "entry": [
    {
      "id": "WHATSAPP_BUSINESS_ACCOUNT_ID",
      "changes": [
        {
          "value": {
            "messaging_product": "whatsapp",
            "metadata": {
              "display_phone_number": "5511999999999",
              "phone_number_id": "PHONE_NUMBER_ID"
            },
            "contacts": [
              {
                "profile": { "name": "João Silva" },
                "wa_id": "5511988887777"
              }
            ],
            "messages": [
              {
                "from": "5511988887777",
                "id": "wamid.HBgL...",
                "timestamp": "1717418461",
                "text": { "body": "Olá, gostaria de marcar consulta" },
                "type": "text"
              }
            ]
          },
          "field": "messages"
        }
      ]
    }
  ]
}
```

### Request body — exemplo (status update)

```json
{
  "object": "whatsapp_business_account",
  "entry": [
    {
      "id": "WABA_ID",
      "changes": [
        {
          "value": {
            "messaging_product": "whatsapp",
            "metadata": {
              "display_phone_number": "5511999999999",
              "phone_number_id": "PHONE_NUMBER_ID"
            },
            "statuses": [
              {
                "id": "wamid.HBgL...",
                "status": "delivered",
                "timestamp": "1717418500",
                "recipient_id": "5511988887777",
                "conversation": { "id": "..." },
                "pricing": { "category": "service" }
              }
            ]
          },
          "field": "messages"
        }
      ]
    }
  ]
}
```

### Request body — exemplo (template approval)

```json
{
  "object": "whatsapp_business_account",
  "entry": [
    {
      "id": "WABA_ID",
      "changes": [
        {
          "value": {
            "event": "APPROVED",
            "message_template_id": 1234567890,
            "message_template_name": "lembrete_consulta_clinicaabc",
            "message_template_language": "pt_BR",
            "reason": null
          },
          "field": "message_template_status_update"
        }
      ]
    }
  ]
}
```

### Response

| Status | Quando |
|---|---|
| `200 OK` | Sempre, **incluindo** payload de tenant `is_enabled=false` (Meta exige para não retentar). |
| `403 Forbidden` | HMAC inválido OU faltante OU tenant inexistente. |

**Body sempre vazio.** Meta não usa o body da resposta.

**SLO**: ≤ 5 s p95 (interno); Meta timeout 20 s.

### Fluxo do controller

```
1. Capturar raw body (middleware).
2. Resolver tenant pelo slug (cache Redis 60s; fallback `public.tenants`).
3. Carregar whatsapp_config (cache Redis 60s).
4. Se `is_enabled = false` → retornar 200 OK (silently dropped).
5. Decifrar app_secret (AES-256-GCM).
6. Calcular HMAC-SHA256(rawBody, app_secret).
7. FixedTimeEquals(computed_hmac, header_hmac.Substring("sha256=".Length)).
8. Se diferente → 403.
9. Parse JSON parcial: extrair primeiro `wa_message_id` (de messages[]) ou `id` (de statuses[]).
10. Dedup: SET NX EX 86400 em {slug}:wa:dedup:{id}. Se existia → 200 OK silently.
11. Enqueue Hangfire WaWebhookProcessorJob com payload bruto + tenant_slug + tenant_id.
12. Retornar 200 OK.
```

---

## 4. Validation rules (HMAC)

```csharp
public bool Validate(string headerSignature, byte[] rawBody, byte[] appSecret)
{
    if (string.IsNullOrEmpty(headerSignature)) return false;
    if (!headerSignature.StartsWith("sha256=", StringComparison.Ordinal)) return false;

    var providedHexUtf8 = Encoding.UTF8.GetBytes(headerSignature.AsSpan(7).ToString());
    using var hmac = new HMACSHA256(appSecret);
    var computed = hmac.ComputeHash(rawBody);
    var computedHex = Convert.ToHexString(computed).ToLowerInvariant();
    var computedHexUtf8 = Encoding.UTF8.GetBytes(computedHex);

    return CryptographicOperations.FixedTimeEquals(providedHexUtf8, computedHexUtf8);
}
```

**Constant-time** é mandatório para prevenir timing attack contra app_secret.

---

## 5. Erros — códigos semânticos

| Erro | Status | Ação interna |
|---|---|---|
| Slug não existe em `public.tenants` | `404` | Log warn; sem incident (atacante varrendo). |
| Tenant existe mas `whatsapp_config` não foi provisionado (race) | `403` | Log error (deveria ser impossível pós-Spec 003 fix). |
| HMAC inválido | `403` | Log warn + incident `webhook_signature_invalid` em Mongo. |
| Body malformado JSON | `200 OK` (silêncio) | Log error + incident `webhook_payload_invalid` (não conta como ataque, conta como bug Meta). |
| Tipo desconhecido em `change.field` | `200 OK` | Log info — Meta pode adicionar campos novos. |

---

## 6. Cassetes de teste obrigatórios

Em `tests/Helpers/Fixtures/WhatsApp/`:

- `webhook-text-message.json` — texto simples.
- `webhook-image-message.json` — com `image.id` Meta.
- `webhook-document-message.json`.
- `webhook-audio-message.json`.
- `webhook-status-sent.json`.
- `webhook-status-delivered.json`.
- `webhook-status-read.json`.
- `webhook-status-failed.json` (com `errors[]`).
- `webhook-template-approved.json`.
- `webhook-template-rejected.json` (com `reason`).
- `webhook-unsupported-sticker.json` — para verificar silent ignore.
- `webhook-malformed.json` — JSON quebrado para erro 200 silent.

Cada cassete vem com seu HMAC pré-computado em arquivo `.sig` paralelo (gerado em build time com app_secret de teste).
