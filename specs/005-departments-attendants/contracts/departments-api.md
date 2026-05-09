# Contract: Departments API

Base path: `/api/departments` — todos os endpoints exigem autenticação no contexto do tenant.

| Endpoint | Method | Policy (Spec 004) | Descrição |
|---|---|---|---|
| `/api/departments` | GET | `Policies.CanListDepartments` | Lista departamentos do tenant |
| `/api/departments/{id}` | GET | `Policies.CanListDepartments` | Detalhe de um departamento |
| `/api/departments` | POST | `Policies.CanCreateDepartment` | Cria departamento |
| `/api/departments/{id}` | PUT | `Policies.CanEditDepartment` | Edita departamento (full) |
| `/api/departments/{id}` | DELETE | `Policies.CanEditDepartment` | Soft delete (`is_active=false`) |
| `/api/departments/{id}/attendants` | GET | `Policies.CanListDepartments` | Atendentes vinculados ao departamento |

## Listagem

```
GET /api/departments?include_inactive=false&page=1&per_page=50
```

**Response 200**:

```json
{
  "success": true,
  "data": [
    {
      "id": "dept-uuid",
      "name": "Comercial",
      "description": "Time comercial e vendas",
      "business_hours": {
        "start": "08:00",
        "end": "18:00",
        "days": [1, 2, 3, 4, 5]
      },
      "sla": {
        "first_response_minutes": 30,
        "resolution_minutes": 240
      },
      "is_active": true,
      "attendant_count": 4,
      "active_ticket_count": 12,
      "created_at": "2026-05-07T10:00:00Z",
      "updated_at": "2026-05-07T10:00:00Z"
    }
  ],
  "meta": { "page": 1, "per_page": 50, "total": 8 }
}
```

## Criar

```
POST /api/departments
Content-Type: application/json

{
  "name": "Comercial",
  "description": "Vendas e novos clientes",
  "business_hours": {
    "start": "08:00",
    "end": "18:00",
    "days": [1, 2, 3, 4, 5]
  },
  "sla": {
    "first_response_minutes": 30,
    "resolution_minutes": 240
  }
}
```

**Validações** (FluentValidation):

- `name`: obrigatório, 2–100 caracteres
- `business_hours`: tudo nulo OU `start < end` AND `days[] não vazio`
- `sla.*_minutes`: opcional, > 0
- Nome único por tenant (case-insensitive)

**Response 201**:

```json
{
  "success": true,
  "data": { "id": "dept-uuid", "name": "Comercial", ... }
}
```

**Erros**:

- `422 DEPARTMENT_NAME_DUPLICATE` — nome já existe no tenant
- `422 BUSINESS_HOURS_INCOMPLETE` — start/end/days mistura nulos com valores

## Editar

```
PUT /api/departments/{id}
```

Mesmo payload de POST. Mudança de horário comercial **não** retroage no cálculo de SLA de tickets já abertos (cada ticket lê o horário vigente do departamento na hora de renderizar).

## Deletar (soft)

```
DELETE /api/departments/{id}
```

**Comportamento**:

- Marca `is_active=false`.
- **Bloqueia** se houver atendentes vinculados (sugere desvincular antes).
- **Bloqueia** exclusão física (não há hard delete na API).

**Response 422 quando há tickets ativos**:

```json
{
  "success": false,
  "error": {
    "code": "DEPARTMENT_HAS_ACTIVE_TICKETS",
    "message": "Departamento possui tickets ativos. Resolva ou transfira antes de desativar.",
    "details": { "active_ticket_count": 7 }
  }
}
```

## Atendentes do departamento

```
GET /api/departments/{id}/attendants?status=online
```

**Query params**:

- `status`: filtro opcional (`online`/`away`/`offline`)
- `include_at_capacity`: bool, default `false` (filtra os que estão no `max_simultaneous_chats`)

**Response 200**:

```json
{
  "success": true,
  "data": [
    {
      "attendant_id": "uuid",
      "name": "Maria",
      "avatar_url": "https://.../avatars/.../256x256.jpg?...",
      "status": "online",
      "active_ticket_count": 3,
      "max_simultaneous_chats": 5,
      "is_primary_department": true
    }
  ]
}
```
