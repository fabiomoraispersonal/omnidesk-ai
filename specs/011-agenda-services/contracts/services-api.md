# Contract: Services Catalog REST API

**Spec**: 011-agenda-services
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer. Role: **`tenant_admin`** for write operations; `tenant_attendant`+ for read.
**Tenant scope**: derived from JWT `tenant_slug` claim.

---

## `GET /api/services` — list services

Lists services in the tenant's catalog.

### Query params

| Param | Type | Default | Notes |
|---|---|---|---|
| `page` | int ≥ 1 | 1 | Pagination. |
| `per_page` | int 1..100 | 50 | Page size. |
| `include_inactive` | bool | false | If true, returns soft-deleted too. |
| `sort` | string | `name` | One of `name`, `created_at`. |
| `order` | string | `asc` | `asc` or `desc`. |

### Response — `200 OK`

```json
{
  "success": true,
  "data": [
    {
      "id": "9a8...",
      "name": "Consulta de Avaliação",
      "description": "Primeira consulta com anamnese completa",
      "category": "Consulta",
      "duration_minutes": 45,
      "price": 200.00,
      "requires_confirmation": false,
      "is_active": true,
      "created_at": "2026-05-12T14:30:01Z",
      "updated_at": "2026-05-12T14:30:01Z"
    }
  ],
  "meta": { "page": 1, "per_page": 50, "total": 12 }
}
```

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |
| 422 | `INVALID_PAGINATION` |

---

## `POST /api/services` — create service

**Authorization**: `Services.Manage` (TenantAdmin).

### Request body

```json
{
  "name": "Sessão de Fisioterapia",
  "description": "Sessão individual de 60 minutos",
  "category": "Procedimento",
  "duration_minutes": 60,
  "price": 150.00,
  "requires_confirmation": false
}
```

### Response — `201 Created`

```json
{ "success": true, "data": { /* full service shape */ } }
```

### Errors

| Code | Error code | Notes |
|---|---|---|
| 400 | `SERVICE_DURATION_INVALID` | `duration_minutes <= 0` |
| 401 | `UNAUTHENTICATED` | |
| 403 | `FORBIDDEN` | Not TenantAdmin. |
| 422 | `VALIDATION_FAILED` | Field-level errors in `details[]`. |

---

## `PUT /api/services/{id}` — update service

**Authorization**: `Services.Manage`.

### Request body

Same shape as `POST`. All fields required.

### Response — `200 OK`

```json
{ "success": true, "data": { /* updated service */ } }
```

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |
| 403 | `FORBIDDEN` |
| 404 | `SERVICE_NOT_FOUND` |
| 422 | `VALIDATION_FAILED` |

---

## `PATCH /api/services/{id}/toggle` — activate/deactivate

Toggles `is_active`. **Soft delete** — agendamentos vinculados preservados.

**Authorization**: `Services.Manage`.

### Request body

```json
{ "is_active": false }
```

### Response — `200 OK`

```json
{ "success": true, "data": { "id": "9a8...", "is_active": false } }
```

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |
| 403 | `FORBIDDEN` |
| 404 | `SERVICE_NOT_FOUND` |

### Side effects

- `is_active = false` → serviço some de `GET /api/availability` e do seletor de novos agendamentos. Não afeta `appointments` existentes.

---

## Implementation notes

- Endpoints mapeados em `Features/Agenda/Services/ServicesEndpoints.cs` via `app.MapGroup("/api/services").MapServicesEndpoints().RequireAuthorization()`.
- Validação FluentValidation em `Features/Agenda/Validators/CreateServiceValidator.cs` (e `UpdateServiceValidator.cs`).
- Repositório em `Infrastructure/Agenda/ServiceRepository.cs`.
- Erros semânticos em `Domain/Errors/AgendaErrors.cs` (`SERVICE_NOT_FOUND`, `SERVICE_DURATION_INVALID`).
