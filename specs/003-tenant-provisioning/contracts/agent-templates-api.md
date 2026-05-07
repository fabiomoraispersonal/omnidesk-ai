# API Contract: Agent Templates Admin

**Feature**: Tenants (Provisionamento)
**Auth**: Todos os endpoints exigem `Authorization: Bearer <jwt>` com `role: saas_admin`
**Base path**: `/api/admin/agent-templates`

---

## `GET /api/admin/agent-templates`

Lista todos os templates de agentes (incluindo inativos, excluindo soft-deleted).

**Query params**:
- `active_only` (opcional, boolean): retorna apenas templates com `is_active = true`

**Response 200**:
```json
[
  {
    "id": "uuid",
    "name": "Agente Principal",
    "type": "orchestrator",
    "description": "Ponto de entrada. Faz saudação, qualifica o cliente e decide qual agente acionar.",
    "prompt": null,
    "is_active": true,
    "used_in_provisioning_count": 3,
    "created_at": "2026-05-06T00:00:00Z",
    "updated_at": "2026-05-06T00:00:00Z"
  },
  {
    "id": "uuid",
    "name": "Recepção",
    "type": "sub_agent",
    "description": "Responsável por informações gerais, localização, horários de funcionamento e primeiro contato.",
    "prompt": null,
    "is_active": true,
    "used_in_provisioning_count": 3,
    "created_at": "2026-05-06T00:00:00Z",
    "updated_at": "2026-05-06T00:00:00Z"
  }
]
```

---

## `POST /api/admin/agent-templates`

Cria um novo template de agente.

**Request body**:
```json
{
  "name": "Agendamento",
  "type": "sub_agent",
  "description": "Responsável por agendar consultas e procedimentos.",
  "prompt": "Você é o agente de agendamento da clínica. Seu objetivo é..."
}
```

**Validações**:
- `name`: obrigatório, não-vazio, máximo 255 chars
- `type`: obrigatório; `orchestrator` ou `sub_agent`
- `description`: obrigatório, não-vazio
- `prompt`: opcional

**Response 201**:
```json
{
  "id": "uuid",
  "name": "Agendamento",
  "type": "sub_agent",
  "description": "Responsável por agendar consultas e procedimentos.",
  "prompt": "Você é o agente de agendamento...",
  "is_active": true,
  "used_in_provisioning_count": 0,
  "created_at": "2026-05-06T16:00:00Z",
  "updated_at": "2026-05-06T16:00:00Z"
}
```

**Erros**:
- `400` — validação falhou

---

## `PUT /api/admin/agent-templates/{id}`

Atualiza um template existente. Campos omitidos não são alterados.

**Request body**:
```json
{
  "name": "Agendamento Online",
  "description": "Responsável por agendar e reagendar consultas e procedimentos.",
  "prompt": "Prompt atualizado...",
  "is_active": false
}
```

**Observação**: Alterar `is_active` para `false` desativa o template — não será mais copiado para novos tenants. Tenants já provisionados não são afetados.

**Response 200**: template atualizado (mesmo shape do `POST`)

**Erros**:
- `404` — template não encontrado
- `400` — validação falhou

---

## `DELETE /api/admin/agent-templates/{id}`

Desativa o template (soft delete). Se o template nunca foi utilizado em um provisionamento, pode ser excluído fisicamente. Caso contrário, apenas `deleted_at` é preenchido e `is_active` é setado para `false`.

**Request body**: nenhum

**Response 204** (No Content)

**Erros**:
- `404` — template não encontrado
- `409` — template já foi excluído (soft deleted)

**Comportamento**:
- `used_in_provisioning_count == 0`: exclusão física da linha
- `used_in_provisioning_count > 0`: soft delete (`deleted_at = now()`, `is_active = false`)
