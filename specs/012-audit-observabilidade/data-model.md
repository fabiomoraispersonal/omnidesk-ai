# Data Model: Auditoria e Observabilidade

**Feature**: `012-audit-observabilidade`
**Date**: 2026-05-13

---

## Entidade 1: AuditLog (MongoDB)

**Collection**: `{tenant_slug}_audit_logs`
**Operações permitidas**: Insert only — sem Update, sem Delete via API.

### Campos

| Campo | Tipo MongoDB | Obrigatório | Descrição |
|---|---|---|---|
| `_id` | ObjectId | sim | Gerado automaticamente pelo MongoDB |
| `tenant_slug` | string | sim | Slug do tenant — redundante com a collection, mas facilita queries ad-hoc |
| `tenant_id` | BsonBinaryData (UUID) | sim | UUID do tenant |
| `event` | string | sim | Nome do evento. Ver `AuditEventNames` para os 29 valores válidos |
| `actor.user_id` | BsonBinaryData (UUID) | condicional | UUID do usuário executor. Null apenas para eventos de sistema (ex: job de retenção) |
| `actor.name` | string | não | Nome do usuário no momento do evento |
| `actor.role` | string | sim | Role no momento do evento: `saas_admin`, `tenant_admin`, `tenant_attendant`, `system` |
| `actor.impersonated_by` | string | não | Preenchido com `"saas_admin"` quando ação é executada via impersonation |
| `target.entity_type` | string | não | Tipo da entidade-alvo: `ticket`, `appointment`, `user`, `ai_agent`, `tenant` |
| `target.entity_id` | BsonBinaryData (UUID) | não | UUID da entidade-alvo |
| `target.label` | string | não | Rótulo legível da entidade (ex: `TK-20260503-00042`) |
| `metadata` | BsonDocument | não | Dados contextuais livres (ex: `{ from: "in_progress", to: "resolved" }`) |
| `ip_address` | string | não | IP do request. Null para eventos de background job |
| `user_agent` | string | não | User-Agent do request. Null para eventos de background job |
| `timestamp` | BsonDateTime (UTC) | sim | Momento do evento em UTC |

### Índices

```javascript
// Índice principal: listagem e range queries
db.audit_logs.createIndex(
  { "tenant_slug": 1, "timestamp": -1 },
  { name: "idx_tenant_timestamp" }
)

// Filtro por tipo de evento
db.audit_logs.createIndex(
  { "tenant_slug": 1, "event": 1, "timestamp": -1 },
  { name: "idx_tenant_event_timestamp" }
)

// Filtro por ator
db.audit_logs.createIndex(
  { "tenant_slug": 1, "actor.user_id": 1, "timestamp": -1 },
  { name: "idx_tenant_actor_timestamp" }
)
```

### C# Model

```csharp
[BsonCollection("{tenant_slug}_audit_logs")]  // nome resolvido em runtime
public class AuditLog
{
    [BsonId] public ObjectId Id { get; set; }
    public string TenantSlug { get; set; } = null!;
    public Guid TenantId { get; set; }
    public string Event { get; set; } = null!;         // AuditEventNames.*
    public AuditActor Actor { get; set; } = null!;
    public AuditTarget? Target { get; set; }
    public BsonDocument? Metadata { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AuditActor
{
    public Guid? UserId { get; set; }
    public string? Name { get; set; }
    public string Role { get; set; } = null!;
    public string? ImpersonatedBy { get; set; }
}

public class AuditTarget
{
    public string EntityType { get; set; } = null!;
    public Guid EntityId { get; set; }
    public string? Label { get; set; }
}
```

### Constantes de Evento (AuditEventNames)

```csharp
public static class AuditEventNames
{
    // Auth
    public const string AuthLoginSuccess       = "auth.login_success";
    public const string AuthLoginFailed        = "auth.login_failed";
    public const string AuthLogout             = "auth.logout";
    public const string AuthPasswordChanged    = "auth.password_changed";
    public const string AuthPasswordReset      = "auth.password_reset";
    public const string AuthTotpEnabled        = "auth.totp_enabled";
    public const string AuthTotpDisabled       = "auth.totp_disabled";
    public const string AuthImpersonationStarted = "auth.impersonation_started";
    public const string AuthImpersonationEnded   = "auth.impersonation_ended";

    // Users
    public const string UserInvited           = "user.invited";
    public const string UserInviteAccepted    = "user.invite_accepted";
    public const string UserDeactivated       = "user.deactivated";
    public const string UserReactivated       = "user.reactivated";
    public const string UserRoleChanged       = "user.role_changed";

    // Tickets
    public const string TicketCreated         = "ticket.created";
    public const string TicketAssigned        = "ticket.assigned";
    public const string TicketTransferred     = "ticket.transferred";
    public const string TicketStatusChanged   = "ticket.status_changed";
    public const string TicketCancelled       = "ticket.cancelled";

    // Appointments
    public const string AppointmentCreated    = "appointment.created";
    public const string AppointmentConfirmed  = "appointment.confirmed";
    public const string AppointmentCancelled  = "appointment.cancelled";
    public const string AppointmentNoShow     = "appointment.no_show";

    // Tenant Config
    public const string TenantWhatsappConfigured = "tenant.whatsapp_configured";
    public const string TenantOpenAiKeyChanged   = "tenant.openai_key_changed";
    public const string TenantPlanChanged        = "tenant.plan_changed";
    public const string AiAgentCreated           = "ai_agent.created";
    public const string AiAgentUpdated           = "ai_agent.updated";
    public const string AiAgentDeleted           = "ai_agent.deleted";
}
```

---

## Entidade 2: ApiKey (PostgreSQL)

**Schema**: `tenant_{slug}` (TenantDbContext)
**Tabela**: `api_keys`

### Campos

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | uuid | sim | PK, gerado na criação |
| `tenant_id` | uuid | sim | FK → `public.tenants.id` |
| `name` | varchar(100) | sim | Nome descritivo dado pelo `tenant_admin` |
| `key_hash` | text | sim | SHA-256 hex da chave bruta. Raw key nunca armazenada |
| `scopes` | text[] | sim | V1: sempre `["audit_logs:read"]` |
| `last_used_at` | timestamptz | não | Atualizado em cada autenticação bem-sucedida (async) |
| `expires_at` | timestamptz | não | Null = sem expiração (V1 padrão) |
| `revoked` | boolean | sim | Default `false`. Revogação é permanente |
| `created_at` | timestamptz | sim | Gerado na inserção |

### Regras de negócio

- Máximo 5 registros com `revoked = false` por `tenant_id`
- `key_hash` é único (constraint UNIQUE)
- Geração da chave: `RandomNumberGenerator.GetBytes(32)` → Base64Url → prefixo `omni_`
- Hash: `SHA256(Encoding.UTF8.GetBytes(rawKey))` → ToHexString lowercase

### C# Entity

```csharp
public class ApiKey
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
    public string KeyHash { get; set; } = null!;
    public string[] Scopes { get; set; } = [];
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool Revoked { get; set; } = false;
    public DateTime CreatedAt { get; set; }
}
```

### EF Core Configuration

```csharp
public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.KeyHash).IsRequired();
        builder.HasIndex(x => x.KeyHash).IsUnique();
        builder.Property(x => x.Scopes).HasColumnType("text[]");
        builder.HasIndex(x => new { x.TenantId, x.Revoked });
    }
}
```

---

## Relacionamentos

```
public.tenants (1) ──< tenant_{slug}.api_keys (N)
{tenant_slug}_audit_logs ← sem FK relacional (MongoDB, referencia tenant_slug como string)
```

---

## Migration

Nome: `{timestamp}_AddApiKeys` (ex: `20260513000000_AddApiKeys`)
Contexto: `TenantDbContext`
Tabela criada: `api_keys` no schema corrente do contexto.
