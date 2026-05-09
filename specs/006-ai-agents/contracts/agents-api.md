# Contract — `/api/agents` (CRM)

**Auth**: `RequireAuthorization()` — papéis `tenant_admin` e `supervisor` (Spec 004 FR-016 — supervisor pode gerenciar agentes; apenas configurações avançadas em `/api/ai-settings` exigem `tenant_admin`).
**Tenant scope**: resolvido via `TenantResolverMiddleware`.
**Envelope**: padrão da Spec 002 — `{success, data, meta?}` ou `{success: false, error: {code, message, details}}`.

---

## GET /api/agents

Lista todos os agentes do tenant — orchestrator + sub-agentes.

**Query**:
- `include_inactive` (bool, default `false`) — incluir desativados.
- `type` (`orchestrator` | `sub_agent`, opcional) — filtro.

**200**:

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "type": "orchestrator",
      "name": "Assistente OmniDesk",
      "short_description": "",
      "model": "gpt-4o",
      "department_id": null,
      "department_name": null,
      "is_active": true,
      "openai_assistant_id_present": true,
      "created_at": "2026-05-08T12:00:00Z",
      "updated_at": "2026-05-08T12:00:00Z"
    },
    {
      "id": "uuid",
      "type": "sub_agent",
      "name": "Agente Comercial",
      "short_description": "Cuida de vendas, planos e preços.",
      "model": "gpt-4o",
      "department_id": "uuid",
      "department_name": "Comercial",
      "is_active": true,
      "openai_assistant_id_present": true,
      "created_at": "...",
      "updated_at": "..."
    }
  ],
  "meta": { "total": 2 }
}
```

> **Não retorna o prompt completo** — apenas resumo. `GET /api/agents/{id}` retorna o prompt.
> `openai_assistant_id_present` (boolean) substitui o id real para evitar vazar artefato OpenAI ao frontend.

---

## GET /api/agents/{id}

Detalhe completo do agente.

**200**:

```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "type": "sub_agent",
    "name": "Agente Comercial",
    "short_description": "Cuida de vendas, planos e preços.",
    "prompt": "Você é o Agente Comercial da {{company_name}}…",
    "model": "gpt-4o",
    "available_models_for_tenant": ["gpt-4o", "gpt-4o-mini"],
    "department_id": "uuid",
    "department_name": "Comercial",
    "is_active": true,
    "created_at": "...",
    "updated_at": "...",
    "deleted_at": null
  }
}
```

**404** `AGENT_NOT_FOUND`.

---

## POST /api/agents

Cria sub-agente. **Não aceita** criar orchestrator (FR-001).

**Body**:

```json
{
  "name": "Agente Suporte",
  "short_description": "Atende dúvidas técnicas e problemas.",
  "prompt": "Você é o Agente Suporte da {{company_name}}…",
  "model": "gpt-4o",
  "department_id": "uuid"
}
```

**201**:

```json
{ "success": true, "data": { "id": "uuid", "...": "..." } }
```

**Side effect**: cria Assistant na OpenAI e persiste `openai_assistant_id`. Em caso de falha da OpenAI, o agente é criado com `openai_assistant_id = null` e o Assistant é criado lazy no primeiro uso (`EnsureAssistantAsync`). Resposta inclui `openai_assistant_id_present: false` quando lazy.

**400** `VALIDATION_FAILED` — payload inválido.
**409** `ORCHESTRATOR_CANNOT_BE_CREATED` — se `type` no body for `orchestrator`.
**409** `DEPARTMENT_NOT_ACTIVE` — depto inativo/inexistente.

---

## PUT /api/agents/{id}

Edita agente. Para Orchestrator, aceita apenas `name`, `prompt`, `model`. Para sub-agente, aceita todos os campos exceto `type`.

**Body** (todos opcionais):

```json
{
  "name": "Aria | IA",
  "short_description": "...",
  "prompt": "...",
  "model": "gpt-4o-mini",
  "department_id": "uuid",
  "is_active": true
}
```

**Side effect**: Se `prompt` ou `model` mudou, agenda atualização do Assistant na OpenAI (`assistants.update`). Em caso de falha, registra warning e tenta novamente no próximo uso.

**200** dados atualizados.
**404** `AGENT_NOT_FOUND`.
**400** `VALIDATION_FAILED`.
**409** `CANNOT_CHANGE_TYPE` — tentativa de mudar `type`.
**409** `CANNOT_DEACTIVATE_ORCHESTRATOR` — `is_active=false` em orchestrator.

---

## DELETE /api/agents/{id}

Soft delete de sub-agente.

- Orchestrator não pode ser deletado → **409** `CANNOT_DELETE_ORCHESTRATOR`.
- Sub-agente sem nenhum registro em `ai_threads.current_agent_id` E nenhum doc em `agent_activity_logs` → permite **delete físico**.
- Sub-agente com qualquer histórico → **soft delete** (`is_active=false`, `deleted_at=now()`). Retorna **200** `{success, data: {soft_deleted: true}}`.

---

## PATCH /api/agents/{id}/toggle

Ativa / desativa sub-agente. Atalho de UI.

**Body**: `{ "is_active": true }` ou `{ "is_active": false }`.

**200** `{ "success": true, "data": { "id": "uuid", "is_active": false } }`.
**409** `CANNOT_DEACTIVATE_ORCHESTRATOR`.

---

## Códigos de erro

| Code | HTTP | Causa |
|---|---|---|
| `VALIDATION_FAILED` | 400 | FluentValidation rejeitou |
| `AGENT_NOT_FOUND` | 404 | id inexistente / soft-deleted |
| `DEPARTMENT_NOT_ACTIVE` | 409 | depto inativo/excluído |
| `ORCHESTRATOR_CANNOT_BE_CREATED` | 409 | tentativa de criar tipo orchestrator |
| `CANNOT_DELETE_ORCHESTRATOR` | 409 | DELETE em orchestrator |
| `CANNOT_DEACTIVATE_ORCHESTRATOR` | 409 | toggle off em orchestrator |
| `CANNOT_CHANGE_TYPE` | 409 | mudança de `type` em PUT |
| `MODEL_NOT_ALLOWED` | 409 | modelo fora da allowlist |

---

## Testes de contrato

- `Features/AiAgents/AiAgentsEndpointsContractTests.cs` — verifica shape de cada endpoint contra esta tabela.
- `AuthorizationFixture` da Spec 002 — só `tenant_admin` autoriza.
