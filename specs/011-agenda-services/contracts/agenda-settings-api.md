# Contract: Agenda Settings REST API

**Spec**: 011-agenda-services
**Base**: `https://{slug}.omnicare.ia.br/api`
**Auth**: JWT Bearer. Role: **`tenant_admin`** only.

Singleton settings row per tenant. `GET` always returns a row (defaults inserted by migration).

---

## `GET /api/agenda-settings` — read settings

### Response — `200 OK`

```json
{
  "success": true,
  "data": {
    "late_cancel_window_hours": 24,
    "late_cancel_text": "Cancelamentos com menos de 24h poderão ser cobrados.",
    "cancellation_policy_text": "",
    "updated_at": "2026-05-12T14:30:01Z"
  }
}
```

### Errors

| Code | Error code |
|---|---|
| 401 | `UNAUTHENTICATED` |
| 403 | `FORBIDDEN` |

---

## `PUT /api/agenda-settings` — update settings

**Authorization**: `AgendaSettings.Manage` (TenantAdmin).

### Request body

```json
{
  "late_cancel_window_hours": 12,
  "late_cancel_text": "Cancelamentos com menos de 12 horas estão sujeitos à taxa de R$ 50,00.",
  "cancellation_policy_text": "Política de cancelamento da Clínica XYZ: ..."
}
```

All three fields required (PUT semantics). To clear a text, send empty string `""`.

### Response — `200 OK`

```json
{ "success": true, "data": { /* full settings shape */ } }
```

### Errors

| Code | Error code | Notes |
|---|---|---|
| 401 | `UNAUTHENTICATED` | |
| 403 | `FORBIDDEN` | Not TenantAdmin. |
| 422 | `LATE_CANCEL_WINDOW_INVALID` | `late_cancel_window_hours <= 0`. |
| 422 | `VALIDATION_FAILED` | Texts exceed 2000 chars. |

### Side effects

- Persisted via UPSERT on singleton row (`id = 1`).
- Affects subsequent WhatsApp cancellation responses immediately (no cache).

---

## Implementation notes

- Mapped in `Features/Agenda/Settings/AgendaSettingsEndpoints.cs`.
- Validator `Features/Agenda/Validators/AgendaSettingsValidator.cs`.
- Repository `Infrastructure/Agenda/AgendaSettingsRepository.cs`.
- Singleton enforced at DB layer (`CHECK (id = 1)`); repository simply does `UPDATE`.
