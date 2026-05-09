# Contract: Canned Responses API

Base path: `/api/canned-responses`.

| Endpoint | Method | Policy | Descrição |
|---|---|---|---|
| `/api/canned-responses` | GET | autenticado | Lista respostas (filtrável por dept) |
| `/api/canned-responses/{id}` | GET | autenticado | Detalhe |
| `/api/canned-responses` | POST | autenticado (qualquer atendente) | Cria |
| `/api/canned-responses/{id}` | PUT | autor ou tenant_admin | Edita |
| `/api/canned-responses/{id}` | DELETE | autor ou tenant_admin | Exclui |
| `/api/canned-responses/render` | POST | autenticado | Substitui variáveis em um template (preview) |

## Listar

```
GET /api/canned-responses?department_id=dept-uuid&q=saudacao
```

**Comportamento**:

- Sem `department_id`: retorna globais + as do(s) departamento(s) do atendente solicitante.
- Com `department_id`: globais + dept específico.
- `q`: busca trigram em `title` (índice GIN); fallback ILIKE em `content`.

**Response 200**:

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "title": "Saudação inicial",
      "content": "Olá {{client_name}}, sou {{attendant_name}} do {{department_name}}. Como posso ajudar?",
      "department_id": null,
      "scope": "global",
      "created_by": { "id": "uuid", "name": "Carlos" },
      "created_at": "...",
      "updated_at": "..."
    }
  ]
}
```

## Criar

```
POST /api/canned-responses
{
  "title": "Saudação inicial",
  "content": "Olá {{client_name}}, ...",
  "department_id": null
}
```

**Validações**:

- `title`: 2–100 caracteres, único por escopo (global ou dept)
- `content`: 1–4000 caracteres
- `department_id`: opcional, deve existir e estar ativo

**Erros**:

- `422 TITLE_DUPLICATE_IN_SCOPE`

## Variáveis suportadas

| Variável | Substituição | Fallback (ausente) |
|---|---|---|
| `{{client_name}}` | Nome do cliente da conversa | `cliente` |
| `{{attendant_name}}` | Nome do atendente atual | `atendente` |
| `{{ticket_number}}` | Número humanizado do ticket | `—` |
| `{{department_name}}` | Nome do departamento atual | `atendimento` |

Variáveis **desconhecidas** (não na tabela acima) são preservadas literalmente e logadas como `Warning` (FR-034 inverso — sinal de canned mal cadastrada).

## Render (preview do conteúdo final)

```
POST /api/canned-responses/render
{
  "template_id": "uuid",
  "context": {
    "conversation_id": "uuid"
  }
}
```

**Comportamento**: backend resolve todas as variáveis a partir do `conversation_id` (busca client_name, attendant_name, ticket_number, department_name) e devolve o texto final.

**Response 200**:

```json
{
  "success": true,
  "data": {
    "rendered": "Olá Maria, sou Carlos do Comercial. Como posso ajudar?",
    "missing_variables": []
  }
}
```

> Útil para preview no chat antes do `Enter`. O envio efetivo da mensagem é feito pelo endpoint de mensagens (Spec 008).

## Editar / Excluir

- Autor original ou tenant_admin podem editar/excluir.
- Outras roles: 403 `FORBIDDEN_NOT_OWNER`.
