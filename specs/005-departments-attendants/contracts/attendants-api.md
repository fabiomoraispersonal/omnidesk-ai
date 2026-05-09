# Contract: Attendants API

Base path: `/api/attendants`.

| Endpoint | Method | Policy | Descrição |
|---|---|---|---|
| `/api/attendants` | GET | `Policies.CanListDepartments` | Lista atendentes do tenant |
| `/api/attendants/{id}` | GET | `Policies.CanListDepartments` | Detalhe |
| `/api/attendants` | POST | `Policies.CanCreateAttendant` | Cria atendente (a partir de `user_id` existente) |
| `/api/attendants/{id}` | PUT | `Policies.CanEditAttendant` | Edita atendente |
| `/api/attendants/{id}` | DELETE | `Policies.CanDeactivateAttendant` | Soft delete |
| `/api/attendants/{id}/avatar` | POST | `Policies.CanEditAttendant` | Upload de avatar (multipart) |
| `/api/attendants/{id}/status` | PATCH | self ou supervisor | Atualiza status |
| `/api/attendants/{id}/heartbeat` | PATCH | self | Renova heartbeat (60 s) |
| `/api/attendants/{id}/tickets` | GET | self ou `Policies.CanViewAnyAttendantTickets` | Tickets ativos do atendente |
| `/api/attendants/{id}/departments` | PUT | `Policies.CanEditAttendant` | Atualiza vínculos N:N |

## Criar

```
POST /api/attendants
{
  "user_id": "uuid-from-spec-002",
  "name": "Maria Silva",
  "max_simultaneous_chats": 5,
  "department_ids": ["dept-uuid-1", "dept-uuid-2"],
  "primary_department_id": "dept-uuid-1"
}
```

**Validações**:

- `user_id`: existe em `public.users`, não tem registro em `attendants` ainda
- `max_simultaneous_chats`: 1–100, default 5
- `department_ids`: ≥ 1 quando `primary_department_id` é fornecido
- `primary_department_id` ∈ `department_ids`

**Response 201**: payload completo do atendente.

**Erros**:

- `422 USER_ALREADY_ATTENDANT`
- `422 PRIMARY_NOT_IN_DEPARTMENTS`
- `404 USER_NOT_FOUND`

## Atualizar status

```
PATCH /api/attendants/{id}/status
{ "status": "away" }
```

**Regras**:

- O atendente só pode mudar **o próprio** status (FR-007). Supervisor/tenant_admin pode mudar status de qualquer um para suporte operacional, marcado `changed_by="manual"` mesmo assim.
- Resposta 200 com o status atual + `changed_at` + `changed_by`.
- Side-effects:
  1. Atualiza Redis `{slug}:attendant_status:{id}` (TTL 5 min).
  2. Atualiza Postgres `attendant_status`.
  3. Grava log no Mongo.
  4. Publica evento `attendant.status_changed` (R4).

## Heartbeat

```
PATCH /api/attendants/{id}/heartbeat
```

- Sem body. Renova TTL e atualiza `last_heartbeat_at`. Não dispara evento WebSocket.
- Disparado pelo CRM a cada 60 s enquanto o atendente está com aba ativa **e** houve interação (mouse/keyboard nos últimos 60 s).

## Tickets ativos

```
GET /api/attendants/{id}/tickets
```

**Response 200**:

```json
{
  "success": true,
  "data": [
    {
      "ticket_id": "uuid",
      "ticket_number": 1234,
      "subject": "Dúvida sobre plano",
      "department_id": "uuid",
      "department_name": "Comercial",
      "started_at": "2026-05-07T13:45:00Z",
      "sla": {
        "first_response_minutes": 30,
        "resolution_minutes": 240,
        "first_response_elapsed_minutes": 12,
        "resolution_elapsed_minutes": 22,
        "first_response_status": "ok",
        "resolution_status": "ok"
      }
    }
  ]
}
```

> SLA `*_status` ∈ `ok | warning | overdue` (R5: amarelo ≥ 80 %, vermelho ≥ 100 %).

## Atualizar vínculos com departamentos

```
PUT /api/attendants/{id}/departments
{
  "department_ids": ["dept-1", "dept-2", "dept-3"],
  "primary_department_id": "dept-2"
}
```

**Comportamento**: substitui o conjunto inteiro (idempotente). Transação:

1. Remove vínculos não presentes em `department_ids`.
2. Insere vínculos novos.
3. Zera `is_primary` em todos e marca o `primary_department_id` como `true`.

**Validação**: `primary_department_id` deve estar em `department_ids`.

## Avatar

```
POST /api/attendants/{id}/avatar
Content-Type: multipart/form-data

file: <imagem JPG/PNG/WebP, ≤ 2 MB>
```

**Response 200**:

```json
{
  "success": true,
  "data": {
    "avatar_url": "https://.../avatars/attendants/{id}/256x256.jpg?signed=..."
  }
}
```

Backend redimensiona para 256×256 antes de persistir; URL retornada é assinada com 7 dias (R9).
