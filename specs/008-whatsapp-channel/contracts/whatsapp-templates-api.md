# Contract — WhatsApp Templates API (CRM)

**Audience**: CRM Angular → API. JWT Bearer (Spec 002).

**RBAC**:

| Endpoint | tenant_admin | supervisor | tenant_attendant |
|---|---|---|---|
| GET `/api/whatsapp/templates` | ✅ | ✅ | ✅ (apenas para envio fora da janela; UI esconde gestão) |
| GET `/api/whatsapp/templates/{id}` | ✅ | ✅ | ✅ (apenas approved) |
| POST `/api/whatsapp/templates` | ✅ | ✅ | ❌ |
| PUT `/api/whatsapp/templates/{id}` | ✅ | ✅ | ❌ |
| POST `/api/whatsapp/templates/{id}/submit` | ✅ | ✅ | ❌ |
| DELETE `/api/whatsapp/templates/{id}` | ✅ | ✅ | ❌ |

---

## 1. GET `/api/whatsapp/templates`

### Query params

| Param | Default | Notas |
|---|---|---|
| `status` | (todos) | Filtra por status. `tenant_attendant` força `status=approved`. |
| `type` | (todos) | Filtra por tipo. |
| `page` | 1 | Paginação. |
| `per_page` | 20 | Max 100. |

### Response 200

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "type": "appointment_reminder",
      "name": "lembrete_consulta_clinicaabc",
      "category": "utility",
      "language": "pt_BR",
      "status": "approved",
      "body_template": "Olá, {{1}}! Lembramos que você tem...",
      "variable_labels": ["nome do cliente", "data da consulta", "horário"],
      "variable_count": 3,
      "rejection_reason": null,
      "submitted_at": "2026-05-09T18:22:00Z",
      "approved_at": "2026-05-10T09:14:00Z",
      "rejected_at": null,
      "meta_template_id": "1234567890",
      "created_at": "2026-05-09T17:00:00Z",
      "updated_at": "2026-05-10T09:14:00Z"
    }
  ],
  "meta": { "page": 1, "per_page": 20, "total": 12 }
}
```

---

## 2. POST `/api/whatsapp/templates` (tenant_admin, supervisor)

### Request — tipo pré-definido

```json
{
  "type": "appointment_reminder",
  "body_template": "Olá, {{1}}! Sua consulta na Clínica ABC é dia {{2}} às {{3}}. Confirme com SIM ou cancele com NÃO.",
  "variable_labels": ["nome do cliente", "data da consulta", "horário"]
}
```

### Request — custom

```json
{
  "type": "custom",
  "name_suffix": "primeira_consulta",
  "body_template": "Bem-vindo à Clínica ABC, {{1}}!",
  "variable_labels": ["nome do cliente"]
}
```

### Validação (`CreateTemplateValidator`)

| Regra | Aplicação |
|---|---|
| `type` ∈ enum | sempre |
| Para tipos pré-definidos: count(`{{N}}`) === `PredefinedTemplates[type].VariableCount` | sempre |
| Para tipos pré-definidos: `variable_labels.Length === VariableCount` | sempre |
| Para `custom`: `name_suffix` obrigatório, snake_case, 1–40 chars | apenas custom |
| `body_template` ≤ 1024 chars (limite Meta) | sempre |
| Placeholders `{{N}}` numerados sequencialmente de 1 | sempre |
| `variable_labels[i]` ≤ 60 chars cada | sempre |
| `name` final único por tenant (gerado automaticamente — `lembrete_consulta_{slug}`, `custom_{name_suffix}_{slug}`) | sempre |

### Response 201

Mesmo shape do GET, com `status: "draft"`, `submitted_at: null`.

### Response 400

```json
{
  "success": false,
  "error": {
    "code": "TEMPLATE_VARIABLE_MISMATCH",
    "message": "Tipo appointment_reminder exige exatamente 3 variáveis; recebido 2."
  }
}
```

```json
{
  "success": false,
  "error": {
    "code": "TEMPLATE_NAME_CONFLICT",
    "message": "Já existe um template com nome lembrete_consulta_clinicaabc."
  }
}
```

---

## 3. PUT `/api/whatsapp/templates/{id}` (apenas status `draft`)

Mesmo shape de POST.

### Response 409 (status incompatível)

```json
{
  "success": false,
  "error": {
    "code": "TEMPLATE_NOT_EDITABLE",
    "message": "Templates em status pending_meta ou approved não podem ser editados."
  }
}
```

---

## 4. POST `/api/whatsapp/templates/{id}/submit` (apenas status `draft`)

### Request

Body vazio.

### Side effects

1. Carrega `whatsapp_config` do tenant — se `access_token_ciphertext` null → 422 `WHATSAPP_NOT_CONFIGURED`.
2. Decifra access_token e chama Meta:
    ```
    POST https://graph.facebook.com/v19.0/{waba_id}/message_templates
    Content-Type: application/json
    Authorization: Bearer {access_token}

    {
      "name": "lembrete_consulta_clinicaabc",
      "category": "UTILITY",
      "language": "pt_BR",
      "components": [
        { "type": "BODY", "text": "Olá, {{1}}! ...", "example": { "body_text": [["João", "10/06/2026", "14:00"]] } }
      ]
    }
    ```
3. Em sucesso, persiste `meta_template_id` (do response Meta) e seta `status='pending_meta'`, `submitted_at=now()`.
4. Em falha 4xx, persiste `rejection_reason` com `error.message` da Meta e seta `status='rejected'`, `rejected_at=now()` (sem ir por `pending_meta`).

### Response 200

Mesmo shape do GET.

### Response 422

- `WHATSAPP_NOT_CONFIGURED` — credenciais ausentes.
- `META_REJECTED` — Meta retornou erro síncrono (template duplicado, body inválido).

---

## 5. DELETE `/api/whatsapp/templates/{id}`

Apenas `draft` ou `rejected`. Soft delete.

### Response 204

Sem body.

### Response 409

```json
{
  "success": false,
  "error": {
    "code": "TEMPLATE_NOT_DELETABLE",
    "message": "Templates em status pending_meta ou approved não podem ser excluídos."
  }
}
```

---

## 6. Pseudo Wireframe — Template Editor (referência UI-PHASE)

```
┌── Novo Template ──────────────────────────────────────────────────────┐
│                                                                        │
│  Tipo:                                                                 │
│  (•) Lembrete de Consulta      ( ) Confirmação de Agendamento         │
│  ( ) Cancelamento              ( ) Follow-up                          │
│  ( ) Custom                                                            │
│                                                                        │
│  Nome do template (gerado automaticamente):                            │
│  [ lembrete_consulta_clinicaabc                ]   (read-only)         │
│                                                                        │
│  Corpo (use {{1}}, {{2}}, {{3}} nas posições marcadas):                │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │ Olá, {{1}}! Lembramos que você tem uma consulta agendada para  │  │
│  │ {{2}} às {{3}}. Confirme com SIM ou cancele com NÃO.            │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                        │
│  Variáveis (descrição para a Meta — read-only para tipos fixos):       │
│  {{1}}  [ nome do cliente              ]                              │
│  {{2}}  [ data da consulta             ]                              │
│  {{3}}  [ horário                       ]                              │
│                                                                        │
│  ☑️  Salvar como rascunho                                              │
│  [ Submeter para aprovação Meta ]                                      │
└────────────────────────────────────────────────────────────────────────┘

# Para tipo `custom`: variáveis editáveis livres (add/remove); name_suffix obrigatório.
```
