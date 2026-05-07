# API Contract: Tenants Admin

**Feature**: Tenants (Provisionamento)
**Auth**: Todos os endpoints exigem `Authorization: Bearer <jwt>` com `role: saas_admin`
**Base path**: `/api/admin/tenants`

---

## `GET /api/admin/tenants`

Lista todos os tenants com métricas resumidas (lidas do cache Redis).

**Query params**:
- `status` (opcional): filtro por status (`provisioning`, `active`, `blocked`, `error`)

**Response 200**:
```json
[
  {
    "id": "uuid",
    "slug": "clinica-abc",
    "razao_social": "Clínica ABC Ltda",
    "nome_fantasia": "Clínica ABC",
    "cnpj": "11.222.333/0001-44",
    "status": "active",
    "has_openai_key": true,
    "created_at": "2026-05-06T12:00:00Z",
    "blocked_at": null,
    "metrics": {
      "postgres": { "connected": true, "error": null },
      "redis": { "connected": true, "error": null },
      "mongodb": { "connected": true, "error": null },
      "conversations_last_30d": 892,
      "open_tickets": 14,
      "active_users": 7
    }
  }
]
```

**Observação**: `metrics` é `null` se o cache ainda não estiver populado (primeiro ciclo do job ainda não executou).

---

## `GET /api/admin/tenants/{id}`

Retorna dados completos de um tenant.

**Response 200**:
```json
{
  "id": "uuid",
  "slug": "clinica-abc",
  "razao_social": "Clínica ABC Ltda",
  "nome_fantasia": "Clínica ABC",
  "cnpj": "11.222.333/0001-44",
  "status": "active",
  "has_openai_key": true,
  "openai_organization": "org-xyz",
  "openai_project": "proj-abc",
  "timezone": "America/Sao_Paulo",
  "locale": "pt-BR",
  "currency": "BRL",
  "date_format": "dd/MM/yyyy",
  "provisioning_error_log": null,
  "created_at": "2026-05-06T12:00:00Z",
  "blocked_at": null,
  "contacts": [
    {
      "id": "uuid",
      "type": "financial",
      "name": "Maria Financeiro",
      "email": "financeiro@clinicaabc.com.br",
      "phone": "(11) 98765-4321"
    },
    {
      "id": "uuid",
      "type": "technical",
      "name": "João Técnico",
      "email": "joao@clinicaabc.com.br",
      "phone": "(11) 91234-5678"
    }
  ],
  "metrics": { /* TenantMetricsDetail — ver estrutura no data-model.md */ }
}
```

**Erros**:
- `404` — tenant não encontrado

---

## `POST /api/admin/tenants`

Cria um novo tenant e inicia o provisionamento assíncrono.

**Request body**:
```json
{
  "slug": "clinica-abc",
  "razao_social": "Clínica ABC Ltda",
  "nome_fantasia": "Clínica ABC",
  "cnpj": "11.222.333/0001-44",
  "timezone": "America/Sao_Paulo",
  "financial_contact": {
    "name": "Maria Financeiro",
    "email": "financeiro@clinicaabc.com.br",
    "phone": "(11) 98765-4321"
  },
  "technical_contact": {
    "name": "João Técnico",
    "email": "joao@clinicaabc.com.br",
    "phone": "(11) 91234-5678"
  },
  "openai_api_key": "sk-...",
  "openai_organization": "org-xyz",
  "openai_project": "proj-abc"
}
```

**Validações**:
- `slug`: `[a-z0-9-]`, 3–50 chars, único
- `cnpj`: formato válido + dígitos verificadores + único
- `technical_contact.email`: único no sistema (não pode existir em `public.users`)
- `timezone`: um dos valores IANA permitidos na V1
- `openai_api_key`, `openai_organization`, `openai_project`: opcionais

**Response 202** (Accepted — provisionamento iniciado de forma assíncrona):
```json
{
  "id": "uuid",
  "slug": "clinica-abc",
  "status": "provisioning"
}
```

**Erros**:
- `400` — validação falhou (slug inválido, CNPJ inválido, etc.)
- `409` — slug ou CNPJ já existem

---

## `PUT /api/admin/tenants/{id}`

Atualiza dados cadastrais do tenant. **Slug não pode ser alterado.**

**Request body** (campos opcionais — apenas os enviados são atualizados):
```json
{
  "razao_social": "Clínica ABC Ltda Atualizada",
  "nome_fantasia": "ABC Saúde",
  "timezone": "America/Manaus",
  "openai_api_key": "sk-nova...",
  "openai_organization": "org-nova",
  "openai_project": "proj-novo",
  "financial_contact": {
    "name": "Maria Nova",
    "email": "nova.financeiro@abc.com.br",
    "phone": "(21) 99999-0000"
  },
  "technical_contact": {
    "name": "João Novo",
    "email": "joao.novo@abc.com.br",
    "phone": "(21) 88888-0000"
  }
}
```

**Response 200**: dados atualizados do tenant (mesmo shape do `GET /api/admin/tenants/{id}`)

**Erros**:
- `404` — tenant não encontrado
- `400` — validação falhou

---

## `POST /api/admin/tenants/{id}/block`

Bloqueia o tenant. Invalida todas as sessões ativas dos usuários do tenant.

**Request body**: nenhum

**Response 200**:
```json
{
  "id": "uuid",
  "status": "blocked",
  "blocked_at": "2026-05-06T15:00:00Z"
}
```

**Erros**:
- `404` — tenant não encontrado
- `409` — tenant já está bloqueado

---

## `POST /api/admin/tenants/{id}/unblock`

Desbloqueia o tenant.

**Request body**: nenhum

**Response 200**:
```json
{
  "id": "uuid",
  "status": "active",
  "blocked_at": null
}
```

**Erros**:
- `404` — tenant não encontrado
- `409` — tenant não está bloqueado

---

## `POST /api/admin/tenants/{id}/reset-password`

Gera nova senha para o Super Admin do tenant, invalida sessões ativas e envia por e-mail.

**Request body**: nenhum

**Response 204** (No Content — operação assíncrona de e-mail; sem dados sensíveis na resposta)

**Erros**:
- `404` — tenant não encontrado
- `422` — tenant sem status `active` (não faz sentido redefinir senha de tenant bloqueado/em provisionamento)

---

## `POST /api/admin/tenants/{id}/impersonate`

Gera token JWT de impersonation (15 min, não renovável) e retorna URL de redirect para o CRM do tenant.

**Request body**: nenhum

**Response 200**:
```json
{
  "impersonation_token": "<jwt>",
  "redirect_url": "https://clinica-abc.omnideskcrm.com.br/impersonate?token=<jwt>",
  "expires_at": "2026-05-06T15:15:00Z"
}
```

**Claims do JWT gerado**:
```json
{
  "sub": "<saas_admin_user_id>",
  "role": "tenant_admin",
  "tenant_id": "<uuid>",
  "tenant_slug": "clinica-abc",
  "impersonating": true,
  "impersonated_by": "<saas_admin_user_id>",
  "iat": 1234567890,
  "exp": 1234567890
}
```

**Erros**:
- `404` — tenant não encontrado
- `422` — tenant não está `active` (não é possível impersonar tenant bloqueado ou em provisionamento)

---

## `GET /api/admin/tenants/{id}/metrics`

Retorna métricas detalhadas do tenant (lidas do cache Redis).

**Response 200**: estrutura `TenantMetricsDetail` conforme data-model.md

**Response 503** (caso o cache esteja vazio):
```json
{
  "error": "metrics_unavailable",
  "message": "Métricas ainda não coletadas. Aguarde até 5 minutos."
}
```

---

## `POST /api/admin/tenants/{id}/retry-provisioning`

Retenta o provisionamento de um tenant com status `error`.

**Request body**: nenhum

**Response 202**:
```json
{
  "id": "uuid",
  "status": "provisioning"
}
```

**Erros**:
- `404` — tenant não encontrado
- `409` — tenant não está em status `error`
