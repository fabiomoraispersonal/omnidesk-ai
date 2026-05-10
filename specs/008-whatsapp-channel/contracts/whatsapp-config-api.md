# Contract — WhatsApp Config API (CRM)

**Audience**: CRM Angular → API. Autenticação JWT Bearer (Spec 002). Tenant resolvido por subdomínio.

**RBAC**:

| Endpoint | tenant_admin | supervisor | tenant_attendant | saas_admin |
|---|---|---|---|---|
| GET `/api/whatsapp/config` | ✅ | ✅ (read-only) | ❌ | ✅ (impersonation) |
| PUT `/api/whatsapp/config` | ✅ | ❌ | ❌ | ✅ |
| PATCH `/api/whatsapp/config/toggle` | ✅ | ❌ | ❌ | ✅ |

---

## 1. GET `/api/whatsapp/config`

### Response 200

```json
{
  "success": true,
  "data": {
    "is_enabled": false,
    "phone_number": "+5511999999999",
    "display_name": "Clínica ABC Saúde",
    "waba_id": "WABA_ID_HERE",
    "phone_number_id": "PHONE_NUMBER_ID_HERE",
    "access_token_configured": true,
    "app_secret_configured": true,
    "webhook_verify_token": "8f3b...d2c4",
    "webhook_url": "https://api.omnicare.ia.br/api/public/whatsapp/webhook/clinica-abc",
    "business_hours_enabled": false,
    "channel_status": "configured_inactive",
    "updated_at": "2026-05-10T14:32:11Z"
  }
}
```

**Notas**:
- `access_token_configured` e `app_secret_configured`: bool, indica se há ciphertext salvo. **Tokens nunca retornados em texto plano** (FR-003, SC-004).
- `channel_status` derivado: `not_configured` (sem phone_number_id) | `configured_inactive` (campos preenchidos, `is_enabled=false`) | `active` (`is_enabled=true`).
- `webhook_url` construído a partir de `Frontend:CrmBaseUrl` da config + slug do tenant.
- `webhook_verify_token`: revelado para o tenant copiar e configurar na Meta. **Não é segredo derivado** (Meta o usa apenas no handshake inicial; HMAC posterior usa app_secret).

### Response 403

```json
{ "success": false, "error": { "code": "FORBIDDEN", "message": "Acesso negado" } }
```

---

## 2. PUT `/api/whatsapp/config` (apenas tenant_admin)

### Request

```json
{
  "phone_number": "+5511999999999",
  "display_name": "Clínica ABC Saúde",
  "waba_id": "WABA_ID_HERE",
  "phone_number_id": "PHONE_NUMBER_ID_HERE",
  "access_token": "EAAB...long_token",
  "app_secret": "abc123def456",
  "business_hours_enabled": false
}
```

**Campos opcionais em PATCH semântico**: o frontend envia apenas os campos alterados. `access_token` e `app_secret` só são enviados quando o usuário **digita um novo valor** — strings vazias = "manter o existente".

### Validação (FluentValidation)

| Campo | Regra |
|---|---|
| `phone_number` | E.164 — regex `^\+[1-9]\d{6,18}$`. |
| `display_name` | 1–100 chars; sem zero-width chars. |
| `waba_id` | 1–100 chars; alfanumérico + underscore. |
| `phone_number_id` | 1–100 chars; numérico (Meta usa só dígitos). |
| `access_token` | (se presente) 100–500 chars; começa com `EAA`. |
| `app_secret` | (se presente) 32–64 chars hex. |

### Response 200

```json
{
  "success": true,
  "data": { /* mesmo shape do GET */ }
}
```

### Response 400

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Validação falhou",
    "details": [
      { "field": "phone_number", "code": "INVALID_E164", "message": "Formato deve ser +55..." }
    ]
  }
}
```

### Response 403

`supervisor` recebe 403 com `FORBIDDEN` — não vê quais campos seriam alterados.

### Side effects

- `access_token` e `app_secret` cifrados via `AesGcmEncryptionService` antes de persistir.
- `updated_at = now()`.
- **Não** muda `is_enabled` automaticamente — o tenant precisa fazer PATCH toggle separado.

---

## 3. PATCH `/api/whatsapp/config/toggle` (apenas tenant_admin)

### Request

```json
{ "is_enabled": true }
```

### Response 200 (toggle on)

```json
{
  "success": true,
  "data": { "is_enabled": true, "channel_status": "active", "updated_at": "..." }
}
```

### Response 422 (precondição não atendida)

Quando tenta `is_enabled=true` mas faltam campos obrigatórios:

```json
{
  "success": false,
  "error": {
    "code": "WHATSAPP_NOT_CONFIGURED",
    "message": "Preencha phone_number_id, waba_id, access_token e app_secret antes de ativar.",
    "details": [
      { "field": "phone_number_id", "code": "MISSING" },
      { "field": "access_token", "code": "MISSING" }
    ]
  }
}
```

### Side effects (toggle on)

- Faz validação rápida com Meta: `GET https://graph.facebook.com/v19.0/me?access_token=...` — se 401, retorna 422 `INVALID_TOKEN` e **não** ativa.
- Se OK, set `is_enabled=true`.

### Side effects (toggle off)

- Sem chamada Meta.
- Set `is_enabled=false`.
- Webhooks recebidos passam a ser silenciosamente descartados (com 200 OK ainda — Meta exige).

---

## 4. Pseudo Wireframe (referência para UI-PHASE)

```
┌── CRM > Configurações > WhatsApp ────────────────────────────────────┐
│                                                                       │
│  Status do Canal:  🟡 Configurado / Inativo            [ Ativar  ]    │
│                                                                       │
│  ────────── Credenciais (preenchidas pelo Operador SaaS) ──────────  │
│  Phone Number ID    [____________________________________]            │
│  WABA ID            [____________________________________]            │
│  Access Token       [•••••••••••••••••••••• (configurado)]  [Alterar]│
│  App Secret         [•••••••••••••••••••••• (configurado)]  [Alterar]│
│  Display Name       [____________________________________]            │
│                                                                       │
│  ────────── Webhook (copie para a Meta Business Manager) ──────────  │
│  Webhook URL        https://api.omnicare.ia.br/api/public/...  [📋]  │
│  Verify Token       8f3b...d2c4                                [📋]  │
│                                                                       │
│  ────────── Comportamento ─────────────────────────────────────────  │
│  [ ] Respeitar horário comercial do departamento                     │
│                                                                       │
│  [ Cancelar ]                                            [ Salvar ]   │
└───────────────────────────────────────────────────────────────────────┘

# Para `supervisor`: campos read-only, [Alterar] e [Salvar] escondidos.
```
