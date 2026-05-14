# Contract: /api/api-keys

**Feature**: `012-audit-observabilidade`
**Date**: 2026-05-13

---

## Autenticação (todos os endpoints)

```
Authorization: Bearer {access_token}
```
Role requerida: `tenant_admin`

---

## GET /api/api-keys — Listar API Keys

### Response — 200 OK

```json
{
  "success": true,
  "data": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "name": "Metabase Auditoria",
      "scopes": ["audit_logs:read"],
      "last_used_at": "2026-06-03T14:32:00Z",
      "expires_at": null,
      "revoked": false,
      "created_at": "2026-05-15T10:00:00Z"
    }
  ],
  "meta": {
    "page": 1,
    "per_page": 20,
    "total": 2
  }
}
```

> **Importante**: `key_hash` nunca é incluído na response. A chave bruta é exibida apenas no `POST`.

---

## POST /api/api-keys — Criar API Key

### Request

```json
{
  "name": "Metabase Auditoria"
}
```

| Campo | Tipo | Obrigatório | Validação |
|---|---|---|---|
| `name` | string | sim | 1–100 caracteres |

### Response — 201 Created

```json
{
  "success": true,
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "name": "Metabase Auditoria",
    "key": "omni_aB3xZ9mNpQ7rS5tU2vW4xY6zA8bC0dE1fG3hI",
    "scopes": ["audit_logs:read"],
    "expires_at": null,
    "created_at": "2026-06-03T15:00:00Z"
  }
}
```

> **`key` é exibido APENAS nesta response.** O campo não existe em nenhum outro endpoint. O usuário deve copiar o valor imediatamente.

### Response — 422 Unprocessable Entity (limite atingido)

```json
{
  "success": false,
  "error": {
    "code": "API_KEY_LIMIT_REACHED",
    "message": "Limite de 5 API Keys ativas atingido. Revogue uma existente para criar uma nova.",
    "details": []
  }
}
```

### Response — 400 Bad Request (validação)

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Dados inválidos.",
    "details": [
      { "field": "name", "message": "O nome é obrigatório." }
    ]
  }
}
```

---

## DELETE /api/api-keys/{id} — Revogar API Key

Revogação é permanente — a chave não pode ser reativada.

### Path Parameters

| Parâmetro | Tipo | Descrição |
|---|---|---|
| `id` | uuid | ID da API Key a revogar |

### Response — 204 No Content

Sem body. Revogação bem-sucedida.

### Response — 404 Not Found

```json
{
  "success": false,
  "error": {
    "code": "API_KEY_NOT_FOUND",
    "message": "API Key não encontrada.",
    "details": []
  }
}
```

> **Nota**: Tentar revogar uma chave já revogada retorna 404 (chave não existe como recurso ativo).

---

## Fluxo completo de uso (integração Metabase)

```
1. POST /api/api-keys { "name": "Metabase Auditoria" }
   → Copiar o campo "key" da response

2. Configurar no Metabase:
   Header: X-Api-Key: omni_aB3xZ9...
   URL: https://{slug}.omnicare.ia.br/api/audit-logs

3. Metabase faz GET /api/audit-logs?per_page=100&from=...
   → Retorna logs paginados

4. Para revogar: DELETE /api/api-keys/{id}
```
