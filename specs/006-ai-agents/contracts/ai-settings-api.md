# Contract — `/api/ai-settings` (CRM)

**Auth**: `tenant_admin` apenas.

---

## GET /api/ai-settings

Retorna configurações de IA do tenant + chave OpenAI mascarada + allowlist global.

**200**:

```json
{
  "success": true,
  "data": {
    "context_window_messages": 20,
    "available_models": [],
    "global_allowlist": ["gpt-4o", "gpt-4o-mini", "gpt-4-turbo"],
    "openai_credentials": {
      "key_set": true,
      "key_preview": "sk-...x4Q2",
      "organization": "org_abc",
      "project": "proj_xyz"
    }
  }
}
```

> `key_preview` mostra os últimos 4 caracteres apenas. Nunca retornar chave completa.

---

## PUT /api/ai-settings

Atualiza `context_window_messages` e/ou `available_models`.

**Body** (opcionais):

```json
{
  "context_window_messages": 30,
  "available_models": ["gpt-4o"]
}
```

**400** `VALIDATION_FAILED`:
- `context_window_messages` fora de [5, 100].
- `available_models` contém modelo fora da `global_allowlist`.

**200** dados atualizados.

---

## PUT /api/ai-settings/openai-credentials

Define chave OpenAI própria do tenant (criptografada via `IDataProtectionProvider`).

**Body**:

```json
{
  "api_key": "sk-...",
  "organization": "org_abc",
  "project": "proj_xyz"
}
```

**Validação síncrona**: backend faz `GET https://api.openai.com/v1/models` com a chave fornecida; se 401/403 → **400** `OPENAI_KEY_INVALID`.

**200** `{ success: true, data: { key_set: true, key_preview: "sk-...x4Q2" } }`.

---

## DELETE /api/ai-settings/openai-credentials

Remove chave própria — sistema cai para chave global no próximo run.

**200** `{ success: true, data: { key_set: false } }`.

---

## Códigos de erro

| Code | HTTP | Causa |
|---|---|---|
| `VALIDATION_FAILED` | 400 | payload inválido |
| `OPENAI_KEY_INVALID` | 400 | chave rejeitada pela OpenAI |
| `MODEL_NOT_IN_GLOBAL_ALLOWLIST` | 400 | modelo fora da allowlist |
