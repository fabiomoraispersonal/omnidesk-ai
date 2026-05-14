# Contract: GET /api/audit-logs

**Feature**: `012-audit-observabilidade`
**Date**: 2026-05-13

---

## Endpoint

```
GET /api/audit-logs
```

## Autenticação

Aceita **duas formas** de autenticação (OR):

| Método | Header | Quem usa |
|---|---|---|
| JWT Bearer | `Authorization: Bearer {access_token}` | CRM UI (tenant_admin) |
| API Key | `X-Api-Key: omni_{key}` | Ferramentas externas (Metabase, etc.) |

Qualquer uma das duas formas válidas autentica o request. O tenant é determinado pelo JWT claims ou pela API Key.

## Autorização

- JWT: requer role `tenant_admin`
- API Key: requer scope `audit_logs:read` e `revoked = false`

---

## Query Parameters

| Parâmetro | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `event` | string | não | Filtro por tipo de evento. Ver `AuditEventNames` para valores válidos |
| `actor_id` | uuid | não | Filtro por ID do ator (usuário que executou a ação) |
| `from` | date (ISO 8601) | não | Início do intervalo (inclusive). Ex: `2026-06-01` |
| `to` | date (ISO 8601) | não | Fim do intervalo (inclusive). Ex: `2026-06-30` |
| `page` | integer | não | Página atual. Default: `1` |
| `per_page` | integer | não | Itens por página. Default: `20`. Máximo: `100` |

---

## Response — 200 OK

```json
{
  "success": true,
  "data": [
    {
      "id": "664f2a3b1c2d3e4f5a6b7c8d",
      "event": "ticket.status_changed",
      "actor": {
        "user_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
        "name": "Maria Silva",
        "role": "tenant_attendant",
        "impersonated_by": null
      },
      "target": {
        "entity_type": "ticket",
        "entity_id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
        "label": "TK-20260503-00042"
      },
      "metadata": {
        "from": "in_progress",
        "to": "resolved"
      },
      "ip_address": "189.x.x.x",
      "timestamp": "2026-06-03T14:32:00Z"
    }
  ],
  "meta": {
    "page": 1,
    "per_page": 20,
    "total": 1024
  }
}
```

### Campos do item de log

| Campo | Tipo | Sempre presente | Descrição |
|---|---|---|---|
| `id` | string (ObjectId hex) | sim | ID do documento MongoDB |
| `event` | string | sim | Nome do evento |
| `actor.user_id` | uuid \| null | sim | Null para eventos de sistema |
| `actor.name` | string \| null | sim | Nome do usuário (snapshot no momento do evento) |
| `actor.role` | string | sim | Role no momento do evento |
| `actor.impersonated_by` | string \| null | sim | `"saas_admin"` se impersonation, caso contrário null |
| `target` | object \| null | sim | Null se o evento não tem entidade-alvo |
| `metadata` | object \| null | sim | Dados contextuais do evento (estrutura varia por tipo) |
| `ip_address` | string \| null | sim | Null para eventos de background job |
| `timestamp` | ISO 8601 UTC | sim | Momento do evento |

> **Nota**: `user_agent` não é incluído na response (dado operacional interno).

---

## Response — 401 Unauthorized

```json
{
  "success": false,
  "error": {
    "code": "UNAUTHORIZED",
    "message": "Autenticação necessária.",
    "details": []
  }
}
```

## Response — 403 Forbidden

```json
{
  "success": false,
  "error": {
    "code": "FORBIDDEN",
    "message": "Acesso não autorizado a este recurso.",
    "details": []
  }
}
```

## Response — 400 Bad Request (parâmetros inválidos)

```json
{
  "success": false,
  "error": {
    "code": "INVALID_FILTER",
    "message": "Parâmetros de filtro inválidos.",
    "details": [
      { "field": "event", "message": "Evento 'ticket.invalid' não reconhecido." }
    ]
  }
}
```

---

## Exemplos de uso

### CRM UI — últimos 20 eventos

```
GET /api/audit-logs
Authorization: Bearer eyJ...
```

### Metabase — todos os eventos de ticket em junho

```
GET /api/audit-logs?event=ticket.status_changed&from=2026-06-01&to=2026-06-30&per_page=100
X-Api-Key: omni_aB3xZ9mN...
```

### Filtro por usuário específico

```
GET /api/audit-logs?actor_id=a1b2c3d4-e5f6-7890-abcd-ef1234567890&from=2026-06-01
Authorization: Bearer eyJ...
```
