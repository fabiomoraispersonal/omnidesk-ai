# Data Model: Tenants (Provisionamento)

**Branch**: `003-tenant-provisioning` | **Data**: 2026-05-06

> Todas as tabelas deste módulo vivem no schema `public` — são tabelas de sistema cross-tenant, conforme permitido pelo Princípio I da constituição.

---

## Entidades

### `public.tenants`

Registro central de cada empresa cliente provisionada no sistema.

| Campo | Tipo | Nulo | Default | Restrições |
|---|---|---|---|---|
| `id` | `uuid` | não | `gen_random_uuid()` | PK |
| `slug` | `varchar(50)` | não | — | UNIQUE; `[a-z0-9-]`; 3–50 chars; imutável após criação |
| `razao_social` | `varchar(255)` | não | — | |
| `nome_fantasia` | `varchar(255)` | sim | `null` | |
| `cnpj` | `varchar(18)` | não | — | UNIQUE; formato `XX.XXX.XXX/XXXX-XX`; dígitos verificadores válidos |
| `status` | `tenant_status` (enum) | não | `'provisioning'` | |
| `openai_api_key_enc` | `text` | sim | `null` | AES-256-GCM: `<nonce_hex>:<ciphertext_hex>`; nunca retornado em texto plano |
| `openai_organization` | `varchar(255)` | sim | `null` | |
| `openai_project` | `varchar(255)` | sim | `null` | |
| `timezone` | `varchar(50)` | não | `'America/Sao_Paulo'` | Formato IANA; valores V1 pré-definidos |
| `locale` | `varchar(10)` | não | `'pt-BR'` | BCP 47; V1: fixo `pt-BR` |
| `currency` | `varchar(3)` | não | `'BRL'` | ISO 4217; V1: fixo `BRL` |
| `date_format` | `varchar(20)` | não | `'dd/MM/yyyy'` | V1: fixo |
| `provisioning_error_log` | `text` | sim | `null` | Log de erro da última tentativa de provisionamento |
| `created_at` | `timestamptz` | não | `now()` | |
| `updated_at` | `timestamptz` | não | `now()` | Atualizado via trigger ou aplicação |
| `blocked_at` | `timestamptz` | sim | `null` | Preenchido ao bloquear; `null` quando ativo |

**Enum `tenant_status`**:
```sql
CREATE TYPE tenant_status AS ENUM ('provisioning', 'active', 'blocked', 'error');
```

**Índices**:
- `idx_tenants_slug` — UNIQUE em `slug`
- `idx_tenants_cnpj` — UNIQUE em `cnpj`
- `idx_tenants_status` — em `status` (filtros de listagem no dashboard)

**Regras de negócio**:
- `slug` é imutável após a criação — nunca expor campo de edição na UI
- `openai_api_key_enc`: coluna armazena o valor criptografado; coluna não é retornada em nenhum endpoint — apenas `has_openai_key: bool` é exposto
- `provisioning_error_log`: substituído a cada tentativa de provisionamento (retry sobrescreve)

**Recursos derivados do slug** (criados no provisionamento, não armazenados aqui):
```
Schema Postgres:  tenant_{slug}          (hífens → underscore)
Bucket MinIO:     tenant-{slug}
Database MongoDB: tenant_{slug}          (hífens → underscore)
Prefixo Redis:    {slug}:
Subdomínio:       {slug}.omnideskcrm.com.br
```

---

### `public.tenant_contacts`

Contatos obrigatórios do tenant. Exatamente dois por tenant: financeiro e responsável técnico.

| Campo | Tipo | Nulo | Default | Restrições |
|---|---|---|---|---|
| `id` | `uuid` | não | `gen_random_uuid()` | PK |
| `tenant_id` | `uuid` | não | — | FK → `public.tenants(id)` ON DELETE CASCADE |
| `type` | `contact_type` (enum) | não | — | |
| `name` | `varchar(255)` | não | — | Nome completo |
| `email` | `varchar(255)` | não | — | Lowercase; e-mail do responsável técnico é usado como login do Super Admin |
| `phone` | `varchar(20)` | não | — | Com DDD |

**Enum `contact_type`**:
```sql
CREATE TYPE contact_type AS ENUM ('financial', 'technical');
```

**Índices**:
- `idx_tenant_contacts_tenant_id` — em `tenant_id`
- `ux_tenant_contacts_tenant_type` — UNIQUE em `(tenant_id, type)` — garante exatamente um contato de cada tipo por tenant

**Regras de negócio**:
- O e-mail do contato `technical` é o e-mail do Super Admin criado no provisionamento
- Constraint UNIQUE `(tenant_id, type)` garante no banco que não existem dois contatos do mesmo tipo para o mesmo tenant

---

### `public.agent_templates`

Templates globais de agentes de IA gerenciados pelo operador SaaS. Copiados para o schema de cada novo tenant no provisionamento.

| Campo | Tipo | Nulo | Default | Restrições |
|---|---|---|---|---|
| `id` | `uuid` | não | `gen_random_uuid()` | PK |
| `name` | `varchar(255)` | não | — | Nome do template |
| `type` | `agent_type` (enum) | não | — | |
| `description` | `text` | não | — | Descrição para uso pelo Orchestrator no roteamento |
| `prompt` | `text` | sim | `null` | Prompt inicial; pode ser `null` e configurado pelo tenant |
| `is_active` | `boolean` | não | `true` | `false` = desativado; não aplicado em novos provisionamentos |
| `used_in_provisioning_count` | `integer` | não | `0` | Contador de provisionamentos onde foi utilizado; > 0 impede exclusão física |
| `deleted_at` | `timestamptz` | sim | `null` | Soft delete: preenchido ao "excluir"; `null` = visível |
| `created_at` | `timestamptz` | não | `now()` | |
| `updated_at` | `timestamptz` | não | `now()` | |

**Enum `agent_type`**:
```sql
CREATE TYPE agent_type AS ENUM ('orchestrator', 'sub_agent');
```

**Índices**:
- `idx_agent_templates_is_active` — em `is_active` (filtro de templates para provisionamento)
- `idx_agent_templates_deleted_at` — em `deleted_at` (soft delete filter)

**Regras de negócio**:
- Template com `is_active = false` não é copiado para novos tenants
- Template com `used_in_provisioning_count > 0` não pode ser excluído fisicamente — apenas desativado
- A exclusão via `DELETE /api/admin/agent-templates/{id}` faz soft delete (`deleted_at = now()`) e `is_active = false`

---

### `{tenant_slug}.agents` *(no schema do tenant — gerado no provisionamento)*

Cópia independente dos templates, criada no schema do tenant durante o provisionamento. O tenant pode editar livremente esses registros sem afetar os templates globais.

| Campo | Tipo | Nulo | Observação |
|---|---|---|---|
| `id` | `uuid` | não | PK; novo UUID gerado no momento da cópia |
| `template_id` | `uuid` | sim | ID do template de origem (apenas referência histórica; sem FK para `public`) |
| `name` | `varchar(255)` | não | Copiado do template; editável pelo tenant |
| `type` | `agent_type` | não | Copiado; editável |
| `description` | `text` | não | Copiado; editável |
| `prompt` | `text` | sim | Copiado; editável |
| `is_active` | `boolean` | não | `true` por padrão |
| `created_at` | `timestamptz` | não | Timestamp do provisionamento |
| `updated_at` | `timestamptz` | não | |

> **Nota de design**: `template_id` é armazenado como informação histórica apenas — não há FK para `public.agent_templates`. Isso é intencional: o tenant é dono completo de seus agentes; alterações ou exclusões nos templates globais não afetam os agentes do tenant.

---

## Cache Redis — Métricas do Tenant

Chave: `saas:metrics:{tenant_slug}` | TTL: 300 segundos (5 minutos)

Estrutura (serializada como JSON):

```json
{
  "tenant_slug": "clinica-abc",
  "collected_at": "2026-05-06T14:30:00Z",
  "postgres": {
    "connected": true,
    "schema_size_mb": 12.4,
    "error": null
  },
  "redis": {
    "connected": true,
    "key_count": 247,
    "memory_bytes": 102400,
    "error": null
  },
  "mongodb": {
    "connected": true,
    "db_size_mb": 8.1,
    "document_count": 3420,
    "error": null
  },
  "minio": {
    "connected": true,
    "object_count": 156,
    "total_size_mb": 340.2,
    "error": null
  },
  "business": {
    "conversations_last_30d": 892,
    "open_tickets": 14,
    "active_users": 7,
    "appointments_last_30d": 203
  },
  "openai": {
    "has_own_key": true
  }
}
```

**Convenção Redis**: Chave `saas:metrics:{tenant_slug}` está no prefixo `saas:` — reservado para dados do operador, fora do namespace `{slug}:*` dos tenants.

---

## Tipos TypeScript (Frontend Admin)

```typescript
// features/tenants/models/tenant.models.ts

export type TenantStatus = 'provisioning' | 'active' | 'blocked' | 'error';
export type ContactType = 'financial' | 'technical';
export type AgentType = 'orchestrator' | 'sub_agent';

export interface TenantContact {
  id: string;
  type: ContactType;
  name: string;
  email: string;
  phone: string;
}

export interface TenantSummary {
  id: string;
  slug: string;
  razao_social: string;
  nome_fantasia: string | null;
  cnpj: string;
  status: TenantStatus;
  has_openai_key: boolean;
  created_at: string;
  blocked_at: string | null;
  metrics?: TenantMetricsSummary;
}

export interface TenantDetail extends TenantSummary {
  contacts: TenantContact[];
  openai_organization: string | null;
  openai_project: string | null;
  timezone: string;
  locale: string;
  currency: string;
  date_format: string;
  provisioning_error_log: string | null;
  metrics?: TenantMetricsDetail;
}

export interface TenantMetricsSummary {
  postgres: ResourceStatus;
  redis: ResourceStatus;
  mongodb: ResourceStatus;
  conversations_last_30d: number;
  open_tickets: number;
  active_users: number;
}

export interface TenantMetricsDetail extends TenantMetricsSummary {
  minio: ResourceStatus;
  appointments_last_30d: number;
  collected_at: string;
}

export interface ResourceStatus {
  connected: boolean;
  error: string | null;
}

export interface CreateTenantRequest {
  slug: string;
  razao_social: string;
  nome_fantasia?: string;
  cnpj: string;
  timezone: string;
  financial_contact: Omit<TenantContact, 'id' | 'type'>;
  technical_contact: Omit<TenantContact, 'id' | 'type'>;
  openai_api_key?: string;
  openai_organization?: string;
  openai_project?: string;
}

export interface ImpersonateResponse {
  impersonation_token: string;
  redirect_url: string;
  expires_at: string;
}

export interface AgentTemplate {
  id: string;
  name: string;
  type: AgentType;
  description: string;
  prompt: string | null;
  is_active: boolean;
  used_in_provisioning_count: number;
  created_at: string;
  updated_at: string;
}
```

## Value Objects C# (Backend)

```csharp
// Domain/Tenants/TenantStatus.cs
public enum TenantStatus { Provisioning, Active, Blocked, Error }

// Domain/Tenants/ContactType.cs
public enum ContactType { Financial, Technical }

// Domain/AgentTemplates/AgentType.cs
public enum AgentType { Orchestrator, SubAgent }

// Application/Admin/Tenants/Responses/TenantSummaryResponse.cs
public record TenantSummaryResponse(
    Guid Id,
    string Slug,
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    TenantStatus Status,
    bool HasOpenAiKey,
    DateTime CreatedAt,
    DateTime? BlockedAt,
    TenantMetricsSummaryResponse? Metrics);

// Application/Admin/Tenants/Responses/ImpersonateResponse.cs
public record ImpersonateResponse(
    string ImpersonationToken,
    string RedirectUrl,
    DateTime ExpiresAt);
```

---

## Relações

```
public.tenants           (1) ──< (2)   public.tenant_contacts
public.tenants           (1) ──< (0..N) public.users [tenant_id]  ← Spec 002
public.agent_templates   (N) ──> (cópia) {tenant_slug}.agents
```

---

## EF Core — Mapeamento

Configurações via `IEntityTypeConfiguration<T>`:

```csharp
// Infrastructure/Persistence/Configurations/TenantConfiguration.cs
public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", "public");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Slug).HasMaxLength(50).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.HasIndex(t => t.Cnpj).IsUnique();
        builder.Property(t => t.Status)
               .HasConversion<string>()
               .IsRequired();
        // openai_api_key_enc: mapeado mas nunca retornado via DTO
        builder.Property(t => t.OpenAiApiKeyEnc).HasColumnName("openai_api_key_enc");
    }
}
```

**Enum com conversão string**: `.HasConversion<string>()` no EF Core garante que os valores do enum são armazenados como strings no PostgreSQL (independente do enum SQL). Alternativa: usar o enum PostgreSQL via `HasPostgresEnum()` — escolha a decidir na implementação; ambas são válidas.
