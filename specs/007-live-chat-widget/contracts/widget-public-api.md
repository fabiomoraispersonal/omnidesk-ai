# Contract — Widget Public API

Endpoints chamados pelo bundle do widget rodando no site do tenant. Autenticados via `widget_token` (público).

**Base path**: `/api/public/widget`
**Auth**: header `X-Widget-Token: <uuid>` ou query `?token=<uuid>` (fallback). Resolvido por `WidgetTokenAuthHandler`.
**Origin**: header `Origin` validado contra `widget_config.allowed_domains` (se preenchido). Falha → `403`.
**Rate limit**: 30 req/min por `anonymous_id` (Redis `INCR` com TTL 60s) — exceto `/init`.

Todas as respostas seguem o envelope de sucesso/erro (CLAUDE.md §6).

---

## GET `/api/public/widget/init`

Primeira chamada do widget ao carregar. Retorna config pública e (se aplicável) info da conversa ativa do mesmo navegador.

**Headers**: `X-Widget-Token`, `Origin`, `X-Anonymous-Id` (UUID do localStorage; opcional na primeiríssima visita).

**Response 200**:

```json
{
  "success": true,
  "data": {
    "tenant": { "slug": "clinica-test", "company_name": "Clínica Teste" },
    "config": {
      "is_enabled": true,
      "primary_color": "#7A9E7E",
      "launcher_icon": "support",
      "welcome_message": "Olá!",
      "input_placeholder": "Digite uma mensagem…",
      "position": "bottom_left",
      "require_identification": false,
      "identification_fields": null,
      "privacy_policy_text": "...",
      "privacy_policy_url": "https://...",
      "max_upload_bytes": 10485760,
      "allowed_mime_types": ["image/jpeg","image/png","image/gif","image/webp","application/pdf","application/vnd.openxmlformats-officedocument.wordprocessingml.document","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]
    },
    "active_conversation": {
      "id": "conv-uuid",
      "status": "open",
      "has_attendant": false,
      "lgpd_consent_at": "2026-05-09T12:00:00Z"
    }
  }
}
```

> `active_conversation` é `null` se não houver `X-Anonymous-Id` ou se nenhuma conversa estiver `open` para esse visitor.

**Response 200 (widget desabilitado)** — campos extras:

```json
{
  "success": true,
  "data": {
    "tenant": { "slug": "clinica-test", "company_name": "Clínica Teste" },
    "config": { "is_enabled": false },
    "active_conversation": null,
    "disabled_message": "No momento o atendimento está indisponível."
  }
}
```

---

## POST `/api/public/widget/conversations`

Cria nova conversa **ou** retorna a `open` existente (idempotente para uma janela de 5s). Chamado quando o visitante aceita LGPD e envia a primeira mensagem.

**Request**:

```json
{
  "anonymous_id": "uuid-gerado-pelo-widget",
  "lgpd_consent": true,
  "identification": {
    "name": "Maria",
    "email": "maria@exemplo.com",
    "phone": null
  },
  "metadata": {
    "page_url": "https://www.clinica-test.com.br/agendamento",
    "page_title": "Agendamento",
    "referrer": "https://google.com/"
  }
}
```

> `metadata.user_agent` e `metadata.ip_partial` são extraídos pelo backend; cliente NÃO os envia.
> `identification` é opcional (apenas quando `require_identification=true`).

**Response 201**:

```json
{
  "success": true,
  "data": {
    "conversation_id": "conv-uuid",
    "status": "open",
    "ws_url": "/ws/widget/conv-uuid",
    "ws_token": "<widget_token>"
  }
}
```

**Response 422** se `lgpd_consent=false` ou ausente:

```json
{ "success": false, "error": { "code": "LGPD_CONSENT_REQUIRED", "message": "Consent must be granted." } }
```

**Response 503** se `widget_config.is_enabled=false`:

```json
{ "success": false, "error": { "code": "WIDGET_DISABLED", "message": "Service unavailable." } }
```

---

## GET `/api/public/widget/conversations/{id}/messages`

Histórico paginado da conversa (ordem cronológica ascendente). Carga inicial ao retomar.

**Query**:

- `limit` (default 50, max 100)
- `before` (UUID de mensagem — paginação para cima)

**Response 200**:

```json
{
  "success": true,
  "data": {
    "messages": [
      {
        "id": "msg-uuid-1",
        "sender_type": "visitor",
        "sender_id": null,
        "content_type": "text",
        "content": "Olá",
        "attachment_url": null,
        "created_at": "2026-05-09T12:01:00Z"
      },
      {
        "id": "msg-uuid-2",
        "sender_type": "ai_agent",
        "sender_id": "agent-uuid",
        "content_type": "text",
        "content": "Como posso ajudar?",
        "created_at": "2026-05-09T12:01:02Z"
      }
    ],
    "has_more": false,
    "next_cursor": null
  }
}
```

**Response 403** se a conversa não pertence ao `anonymous_id` enviado no header.

---

## POST `/api/public/widget/upload`

Upload de anexo. Deve ser chamado **antes** de enviar a mensagem via WebSocket; retorna URL que será incluída no payload da mensagem.

**Request**: `multipart/form-data`:

- `file`: arquivo binário (max 10 MB).
- `conversation_id`: UUID.

**Headers**: `X-Widget-Token`, `X-Anonymous-Id`, `Origin`.

**Response 201**:

```json
{
  "success": true,
  "data": {
    "url": "https://minio.omnicare.ia.br/tenant-clinica-test/widget-uploads/conv-uuid/8a-foto.jpg",
    "name": "foto.jpg",
    "size_bytes": 234567,
    "mime_type": "image/jpeg"
  }
}
```

**Validações**:

| Erro | Code | Status |
|---|---|---|
| Arquivo > 10 MB | `FILE_TOO_LARGE` | 413 |
| MIME real não permitido | `UNSUPPORTED_MIME_TYPE` | 415 |
| Sem `conversation_id` válido / não pertence ao visitor | `CONVERSATION_NOT_FOUND` | 404 |
| Conversa `resolved` ou `abandoned` | `CONVERSATION_CLOSED` | 409 |

> Tipo MIME **real** detectado por magic bytes (research R5). Extensão NÃO é confiável.

---

## Fluxo de auth + rate limit

```
Request →
  ┌─────────────────────────────┐
  │ WidgetTokenAuthHandler      │ → token UUID em public.tenants → tenant_slug; popula ITenantContext
  └─────────────────────────────┘
              ↓ falha → 401 INVALID_WIDGET_TOKEN
  ┌─────────────────────────────┐
  │ OriginValidator             │ → header Origin ∈ allowed_domains? (lista vazia = pula)
  └─────────────────────────────┘
              ↓ falha → 403 ORIGIN_NOT_ALLOWED
  ┌─────────────────────────────┐
  │ PublicRateLimiter           │ → INCR {slug}:widget:rate:{anonymous_id} TTL=60; ≤ 30/min
  └─────────────────────────────┘
              ↓ falha → 429 RATE_LIMIT_EXCEEDED
  ┌─────────────────────────────┐
  │ Endpoint                    │
  └─────────────────────────────┘
```

> `/init` está fora do rate limit (carregamento da página não conta).

---

## Tabela de status

| Cenário | Code | Status |
|---|---|---|
| OK | — | 200/201 |
| Token inválido / ausente | `INVALID_WIDGET_TOKEN` | 401 |
| Origin bloqueada | `ORIGIN_NOT_ALLOWED` | 403 |
| LGPD não aceito | `LGPD_CONSENT_REQUIRED` | 422 |
| Widget desabilitado | `WIDGET_DISABLED` | 503 |
| Rate limit | `RATE_LIMIT_EXCEEDED` | 429 |
| Arquivo muito grande | `FILE_TOO_LARGE` | 413 |
| MIME inválido | `UNSUPPORTED_MIME_TYPE` | 415 |
| Conversa não encontrada / não pertence | `CONVERSATION_NOT_FOUND` | 404 |
| Conversa encerrada | `CONVERSATION_CLOSED` | 409 |
