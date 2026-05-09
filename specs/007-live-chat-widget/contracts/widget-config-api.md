# Contract — Widget Config API (CRM)

Endpoints autenticados (JWT do tenant_admin) para configurar o widget.

**Base path**: `/api/widget/config`
**Auth**: JWT Bearer + `RequireAuthorization("tenant_admin")` (V1 — ver R11)

---

## GET `/api/widget/config`

Retorna a configuração atual do tenant logado (1:1 — sempre 1 registro).

**Response 200**:

```json
{
  "success": true,
  "data": {
    "is_enabled": true,
    "primary_color": "#7A9E7E",
    "launcher_icon": "support",
    "company_name": "Clínica Teste",
    "welcome_message": "Olá! Bem-vindo à Clínica Teste.",
    "input_placeholder": "Digite uma mensagem…",
    "position": "bottom_left",
    "require_identification": false,
    "identification_fields": null,
    "allowed_domains": ["www.clinica-teste.com.br","clinica-teste.com.br"],
    "privacy_policy_text": "...",
    "privacy_policy_url": "https://www.clinica-teste.com.br/privacidade",
    "abandonment_timeout_hours": 8,
    "inactivity_close_hours": 24,
    "widget_token": "8d2c6f1e-...",
    "installation_snippet": "<script>window.OmniDeskConfig = { token: \"8d2c6f1e-...\" };</script>\n<script src=\"https://cdn.omnicare.ia.br/widget/v1/loader.js\" async></script>",
    "updated_at": "2026-05-09T12:00:00Z"
  }
}
```

> `widget_token` retornado é o do `public.tenants.widget_token`. `installation_snippet` é montado pelo backend usando `WIDGET_CDN_BASE_URL` env var.

---

## PUT `/api/widget/config`

Atualiza configuração inteira (PUT — substitui campos editáveis).

**Request**:

```json
{
  "primary_color": "#7A9E7E",
  "launcher_icon": "support",
  "company_name": "Clínica Teste",
  "welcome_message": "Olá!",
  "input_placeholder": "Digite uma mensagem…",
  "position": "bottom_left",
  "require_identification": true,
  "identification_fields": [
    { "field": "name",  "label": "Seu nome",   "required": true },
    { "field": "email", "label": "Seu e-mail", "required": false }
  ],
  "allowed_domains": ["www.clinica-teste.com.br"],
  "privacy_policy_text": "...",
  "privacy_policy_url": "https://www.clinica-teste.com.br/privacidade",
  "abandonment_timeout_hours": 8,
  "inactivity_close_hours": 24
}
```

**Response 200**: mesma shape do GET após salvar.

**Response 400** (FluentValidation `UpdateWidgetConfigValidator`):

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Validation failed.",
    "details": [
      { "field": "primary_color", "message": "Must match #RRGGBB." },
      { "field": "abandonment_timeout_hours", "message": "Must be between 1 and 168." }
    ]
  }
}
```

### Validações

| Campo | Regra |
|---|---|
| `primary_color` | `^#[0-9A-Fa-f]{6}$` |
| `launcher_icon` | ∈ `{chat, message, support}` |
| `company_name` | obrigatório, 1..100 chars |
| `welcome_message` | obrigatório, 1..1000 chars |
| `input_placeholder` | opcional, ≤ 150 chars |
| `position` | ∈ `{bottom_right, bottom_left}` |
| `require_identification` | bool |
| `identification_fields` | array de objetos `{field, label, required}` quando `require_identification=true`. Sem duplicatas em `field`. `field` ∈ `{name, email, phone}`. `label` 1..50 chars. |
| `allowed_domains` | array opcional. Cada item: hostname válido (sem schema, sem path, sem porta para wildcard). |
| `privacy_policy_text` | opcional. Quando vazio, alerta no UI; widget exibe texto genérico (FR-020). |
| `privacy_policy_url` | URL absoluta `^https?://.+` se preenchida. |
| `abandonment_timeout_hours` / `inactivity_close_hours` | 1..168 |

---

## PATCH `/api/widget/config/toggle`

Liga/desliga widget. Quando desliga, dispara `WidgetDisableEnforcementJob`.

**Request**: vazio (toggle do estado atual) **OU**:

```json
{ "is_enabled": false }
```

**Response 200**:

```json
{
  "success": true,
  "data": {
    "is_enabled": false,
    "affected_conversations": 7
  }
}
```

> `affected_conversations` é o nº de conversas `open` encerradas com `ended_by='system_disable'` (apenas no caso de desligamento).

**Effects** (apenas quando `is_enabled` muda para `false`):

1. Enfileira `WidgetDisableEnforcementJob`.
2. Job para cada `conversation` com `status='open'`:
   - INSERT mensagem `system` com `content_type='system_event'`, `content='widget_disabled'` e versão visível "O atendimento foi encerrado pelo sistema."
   - UPDATE status `resolved`, `ended_by='system_disable'`, `ended_at=NOW()`.
   - Publica evento `conversation.resolved {ended_by:'system_disable'}` no canal Redis `{slug}:conv:{id}` para o widget reagir.

---

## Tabela de status

| Cenário | Status | Code |
|---|---|---|
| Configuração retornada | 200 | — |
| Configuração atualizada | 200 | — |
| Validação falhou | 400 | `VALIDATION_ERROR` |
| Auth ausente/inválida | 401 | `UNAUTHORIZED` |
| Role insuficiente | 403 | `FORBIDDEN` |
| Widget config não existe (tenant não provisionado) | 404 | `WIDGET_CONFIG_NOT_FOUND` |
