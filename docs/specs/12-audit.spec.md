# Spec 12 — Auditoria e Observabilidade
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

---

## 1. Visão Geral

A auditoria do OmniDesk é **leve e orientada a dados brutos** — sem UI complexa no CRM. O sistema registra eventos críticos em MongoDB (já na stack) e os expõe via API para consumo por ferramentas externas como **Metabase**, sem processamento adicional no produto. A única interface nativa é uma listagem simples de "Atividade Recente" para o `tenant_admin`.

---

## 2. Estratégia

| Aspecto | Decisão |
|---|---|
| Armazenamento | MongoDB (coleção `audit_logs` — já na stack) |
| UI no CRM | Mínima: listagem simples de atividade recente (`tenant_admin`) |
| Análise avançada | Ferramenta externa (Metabase, Retool etc.) consumindo a API |
| Retenção | 12 meses (documentos mais antigos removidos por job Hangfire) |
| Exposição | API REST paginada + filtros básicos |

---

## 3. Eventos Auditados

### 3.1 Autenticação

| Evento | Quando |
|---|---|
| `auth.login_success` | Login bem-sucedido |
| `auth.login_failed` | Tentativa de login com credenciais inválidas |
| `auth.logout` | Logout explícito |
| `auth.password_changed` | Senha alterada |
| `auth.password_reset` | Redefinição de senha via link |
| `auth.totp_enabled` | 2FA ativado |
| `auth.totp_disabled` | 2FA desativado |
| `auth.impersonation_started` | saas_admin entrou em impersonation de tenant |
| `auth.impersonation_ended` | Token de impersonation expirou |

### 3.2 Gestão de Usuários

| Evento | Quando |
|---|---|
| `user.invited` | Convite enviado |
| `user.invite_accepted` | Convite aceito — usuário criado |
| `user.deactivated` | Usuário desativado |
| `user.reactivated` | Usuário reativado |
| `user.role_changed` | Role alterada |

### 3.3 Tickets

| Evento | Quando |
|---|---|
| `ticket.created` | Ticket criado |
| `ticket.assigned` | Ticket atribuído |
| `ticket.transferred` | Ticket transferido |
| `ticket.status_changed` | Status mudou (ex: `in_progress → resolved`) |
| `ticket.cancelled` | Ticket cancelado |

### 3.4 Agendamentos

| Evento | Quando |
|---|---|
| `appointment.created` | Agendamento criado |
| `appointment.confirmed` | Confirmado |
| `appointment.cancelled` | Cancelado (com `cancelled_by`) |
| `appointment.no_show` | Marcado como no-show |

### 3.5 Configurações Críticas do Tenant

| Evento | Quando |
|---|---|
| `tenant.whatsapp_configured` | Credenciais de WhatsApp salvas/alteradas |
| `tenant.openai_key_changed` | Chave OpenAI personalizada alterada |
| `tenant.plan_changed` | Plano alterado (V2) |
| `ai_agent.created` | Agente de IA criado |
| `ai_agent.updated` | Agente de IA editado |
| `ai_agent.deleted` | Agente de IA deletado |

---

## 4. Estrutura do Documento de Log

```json
{
  "_id": "ObjectId",
  "tenant_slug": "clinica-abc",
  "tenant_id": "uuid",
  "event": "ticket.status_changed",
  "actor": {
    "user_id": "uuid",
    "name": "Maria Silva",
    "role": "attendant",
    "impersonated_by": null
  },
  "target": {
    "entity_type": "ticket",
    "entity_id": "uuid",
    "label": "TK-20260503-00042"
  },
  "metadata": {
    "from": "in_progress",
    "to": "resolved"
  },
  "ip_address": "189.x.x.x",
  "user_agent": "Mozilla/5.0...",
  "timestamp": "2026-06-03T14:32:00Z"
}
```

**Campos obrigatórios em todo evento:** `tenant_slug`, `tenant_id`, `event`, `actor.user_id`, `actor.role`, `timestamp`.

> Quando `actor.impersonated_by` está preenchido, a ação foi executada pelo `saas_admin` em impersonation — campo fundamental para LGPD e compliance.

---

## 5. Interface no CRM (Mínima)

Acessível em: **CRM → Configurações → Atividade Recente** — visível apenas para `tenant_admin`.

- Lista simples, paginada (20/página), ordenada por `timestamp` decrescente
- Filtros: tipo de evento (dropdown), usuário (select), período (date range)
- Cada linha: ícone do evento, descrição legível, ator, timestamp relativo
- Sem gráficos ou análises — para isso, usar Metabase
- Sem exportação no CRM — dados acessíveis via API

---

## 6. API de Consulta (para Metabase / ferramentas externas)

```
GET /api/audit-logs
  ?event=ticket.status_changed
  &actor_id=uuid
  &from=2026-06-01
  &to=2026-06-31
  &page=1
  &per_page=100
```

**Autenticação:** Token de API dedicado (não o JWT do usuário). Gerado em CRM → Configurações → Integrações → API Key. Somente `tenant_admin` pode gerar/revogar.

**Resposta:**
```json
{
  "data": [...],
  "meta": { "total": 1024, "page": 1, "per_page": 100 }
}
```

> O Metabase conecta diretamente no endpoint REST ou na collection MongoDB (via MongoDB connector). Para a maioria dos cases, o REST é suficiente.

---

## 7. API Key para Integrações

### 7.1 Entidade: `api_keys`

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `tenant_id` | UUID | sim | FK → tenants |
| `name` | varchar(100) | sim | Nome descritivo. Ex: "Metabase Auditoria" |
| `key_hash` | text | sim | Hash SHA-256 da chave. A chave bruta é exibida apenas no momento da criação. |
| `scopes` | text[] | sim | Permissões. V1: apenas `["audit_logs:read"]` |
| `last_used_at` | timestamptz | não | Última vez que foi usada. |
| `expires_at` | timestamptz | não | `null` = sem expiração. |
| `revoked` | boolean | sim | Default: `false`. |
| `created_at` | timestamptz | sim | — |

---

## 8. Regras de Negócio

- Logs são **apenas de escrita** — nenhum log pode ser editado ou deletado via API
- Retenção: 12 meses. Job Hangfire mensal remove documentos com `timestamp < now() - 12 meses`
- Impersonation: toda ação do `saas_admin` em tenant é registrada com `impersonated_by = "saas_admin"` — obrigatório para LGPD
- A API Key é exibida em texto plano **apenas no momento da criação** — impossível recuperar depois
- Um tenant pode ter no máximo 5 API Keys ativas simultaneamente
- Falhas de autenticação (`auth.login_failed`) são registradas **mesmo sem um usuário existir** (tentativa com e-mail inválido registra o e-mail tentado no metadata)

---

## 9. Endpoints

```
# Audit logs (CRM)
GET    /api/audit-logs                    → listar logs (tenant_admin)

# API Keys (CRM)
GET    /api/api-keys                      → listar API keys do tenant
POST   /api/api-keys                      → criar API key (retorna chave bruta 1x)
DELETE /api/api-keys/{id}                 → revogar API key
```

---

## 10. Critérios de Aceite

- [ ] Todos os eventos da seção 3 geram documento no MongoDB com estrutura correta
- [ ] `impersonated_by` preenchido em toda ação de impersonation
- [ ] Logs não podem ser editados nem deletados via API
- [ ] Job de limpeza remove logs com mais de 12 meses mensalmente
- [ ] Interface "Atividade Recente" visível apenas para `tenant_admin`
- [ ] API REST paginada retorna logs com filtros de evento, ator e período
- [ ] API Key exibida apenas no momento da criação
- [ ] API Key autenticada via header `X-Api-Key`
- [ ] Máximo de 5 API Keys ativas por tenant
- [ ] Tentativas de login com e-mail inválido também registradas em `auth.login_failed`
