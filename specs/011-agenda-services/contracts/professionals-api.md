# Contract: Professionals REST API

**Spec**: 011-agenda-services
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer. Role: **`tenant_admin`** for write; `tenant_attendant`+ for read.

---

## `GET /api/professionals` — list

### Query params

| Param | Type | Default | Notes |
|---|---|---|---|
| `page` | int ≥ 1 | 1 | |
| `per_page` | int 1..100 | 50 | |
| `include_inactive` | bool | false | |
| `department_id` | uuid | (any) | Filter by department. |
| `service_id` | uuid | (any) | Filter to those who offer the service. |

### Response — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "p1...",
      "name": "Dra. Ana Lima",
      "specialty": "Fisioterapeuta",
      "department_id": "dep-comercial",
      "attendant_id": null,
      "is_active": true,
      "created_at": "2026-05-12T14:30:01Z",
      "updated_at": "2026-05-12T14:30:01Z"
    }
  ],
  "meta": { "page": 1, "per_page": 50, "total": 8 }
}
```

---

## `POST /api/professionals` — create

**Authorization**: `Professionals.Manage`.

### Request body

```json
{
  "name": "Dra. Ana Lima",
  "specialty": "Fisioterapeuta",
  "department_id": "dep-comercial",
  "attendant_id": null
}
```

`department_id` and `attendant_id` are **optional**. `attendant_id` MUST belong to the same tenant.

### Response — `201 Created`

```json
{ "success": true, "data": { /* full professional shape */ } }
```

### Errors

| Code | Error code | Notes |
|---|---|---|
| 400 | `PROFESSIONAL_ATTENDANT_ALREADY_LINKED` | The attendant is already linked to another professional (unique partial index). |
| 401 | `UNAUTHENTICATED` | |
| 403 | `FORBIDDEN` | |
| 404 | `DEPARTMENT_NOT_FOUND` / `ATTENDANT_NOT_FOUND` | |
| 422 | `VALIDATION_FAILED` | |

---

## `PUT /api/professionals/{id}` — update

Same body as `POST`. Replacing `attendant_id` from non-null to null is allowed; vice versa subject to unique partial index check.

### Response — `200 OK`

Same shape.

---

## `PATCH /api/professionals/{id}/toggle` — activate/deactivate

```json
{ "is_active": false }
```

**Side effects**:

- `is_active = false` → professional disappears from `GET /api/availability` and from new appointment selectors. Existing appointments retained.

---

## `GET /api/professionals/{id}/services` — list linked services

### Response — `200 OK`

```json
{
  "success": true,
  "data": [
    { "service_id": "s1...", "service_name": "Consulta de Avaliação", "duration_minutes": 45 },
    { "service_id": "s2...", "service_name": "Sessão de Fisioterapia", "duration_minutes": 60 }
  ]
}
```

---

## `PUT /api/professionals/{id}/services` — replace linked services

**Authorization**: `Professionals.Manage`.

Atomic diff (delete missing + insert new) within a transaction.

### Request body

```json
{ "service_ids": ["s1...", "s2...", "s5..."] }
```

### Response — `200 OK`

```json
{ "success": true, "data": { "linked_count": 3 } }
```

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |
| 403 | `FORBIDDEN` |
| 404 | `PROFESSIONAL_NOT_FOUND` |
| 422 | `SERVICE_NOT_FOUND` (one of the IDs invalid) |

---

## `GET /api/professionals/{id}/schedule` — weekly schedule

### Response — `200 OK`

```json
{
  "success": true,
  "data": [
    { "day_of_week": 1, "start_time": "08:00", "end_time": "12:00" },
    { "day_of_week": 1, "start_time": "14:00", "end_time": "18:00" },
    { "day_of_week": 2, "start_time": "08:00", "end_time": "12:00" }
  ]
}
```

`day_of_week`: 0 = Sunday … 6 = Saturday.

---

## `PUT /api/professionals/{id}/schedule` — replace weekly schedule

**Authorization**: `Professionals.Manage`.

Replace-all within a transaction.

### Request body

```json
{
  "shifts": [
    { "day_of_week": 1, "start_time": "08:00", "end_time": "12:00" },
    { "day_of_week": 1, "start_time": "14:00", "end_time": "18:00" }
  ]
}
```

### Response — `200 OK`

```json
{ "success": true, "data": { "shifts_count": 2 } }
```

### Errors

| Code | Error code | Notes |
|---|---|---|
| 422 | `WEEKLY_SCHEDULE_INVALID_RANGE` | `start_time >= end_time` |
| 422 | `WEEKLY_SCHEDULE_OVERLAP` | Two shifts on same day overlap. |
| 422 | `WEEKLY_SCHEDULE_INVALID_DAY` | `day_of_week` outside 0..6. |

---

## `GET /api/professionals/{id}/blocks` — list future blocks

### Query params

| Param | Type | Default | Notes |
|---|---|---|---|
| `from` | ISO date | today | List blocks with `end_at >= from`. |

### Response — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "b1...",
      "start_at": "2026-06-10T00:00:00-03:00",
      "end_at": "2026-06-17T23:59:59-03:00",
      "reason": "Férias",
      "created_at": "2026-05-12T10:00:00Z"
    }
  ]
}
```

---

## `POST /api/professionals/{id}/blocks` — create block

**Authorization**: `Professionals.Manage`.

### Request body

```json
{
  "start_at": "2026-06-10T00:00:00-03:00",
  "end_at": "2026-06-17T23:59:59-03:00",
  "reason": "Férias"
}
```

### Response — `201 Created`

```json
{ "success": true, "data": { /* full block shape */ } }
```

### Errors

| Code | Error code | Notes |
|---|---|---|
| 422 | `BLOCK_RANGE_INVALID` | `start_at >= end_at` |
| 422 | `BLOCK_OVERLAPS_APPOINTMENTS` | Lists conflicting appointment IDs in `details.appointments[]`. |

Error response shape for `BLOCK_OVERLAPS_APPOINTMENTS`:

```json
{
  "success": false,
  "error": {
    "code": "BLOCK_OVERLAPS_APPOINTMENTS",
    "message": "Existem 2 agendamentos no intervalo solicitado. Cancele-os antes de criar o bloqueio.",
    "details": {
      "appointments": ["a1...", "a2..."]
    }
  }
}
```

---

## `DELETE /api/professionals/{id}/blocks/{blockId}` — delete block

**Authorization**: `Professionals.Manage`.

### Response — `204 No Content`

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |
| 403 | `FORBIDDEN` |
| 404 | `BLOCK_NOT_FOUND` |

---

## Implementation notes

- All endpoints under `app.MapGroup("/api/professionals")` — `Features/Agenda/Professionals/ProfessionalsEndpoints.cs`.
- Sub-resources (`/services`, `/schedule`, `/blocks`) handled in same file via nested route mapping.
- Schedule update uses `UpdateWeeklyScheduleCommand` which executes within `IDbTransaction`.
- Block overlap check: query `appointments` with status `IN ('pending_confirmation','confirmed')` and `tstzrange(start_at, end_at) && tstzrange(@block_start, @block_end)`.
