# Data Model: Autenticação

**Branch**: `002-auth-jwt` | **Data**: 2026-05-05

> Todas as tabelas vivem no schema `public` — auth é infraestrutura cross-tenant (ver Complexity Tracking no plan.md).

---

## Entidades

### `public.users`

Representa qualquer pessoa autenticável no sistema. Tabela global — não pertence a nenhum schema de tenant.

| Campo | Tipo | Nulo | Default | Restrições |
|---|---|---|---|---|
| `id` | `uuid` | não | `gen_random_uuid()` | PK |
| `email` | `varchar(255)` | não | — | UNIQUE, lowercase |
| `password_hash` | `text` | não | — | Formato PHC Argon2id |
| `role` | `user_role` (enum) | não | — | `saas_admin`, `tenant_admin`, `supervisor`, `attendant` |
| `tenant_id` | `uuid` | sim | `null` | FK → `public.tenants(id)`; null para `saas_admin` |
| `is_active` | `boolean` | não | `true` | |
| `email_verified` | `boolean` | não | `false` | Setado `true` ao aceitar convite |
| `totp_secret` | `text` | sim | `null` | Cifrado AES-256-GCM: `<nonce_hex>:<ciphertext_hex>` |
| `totp_enabled` | `boolean` | não | `false` | |
| `last_login_at` | `timestamptz` | sim | `null` | Atualizado após login bem-sucedido |
| `created_at` | `timestamptz` | não | `now()` | |
| `updated_at` | `timestamptz` | não | `now()` | Trigger ou aplicação |

**Índices**:
- `idx_users_email` — UNIQUE em `email`
- `idx_users_tenant_id` — em `tenant_id` (consultas de usuários por tenant)

**Enum `user_role`**:
```sql
CREATE TYPE user_role AS ENUM ('saas_admin', 'tenant_admin', 'supervisor', 'attendant');
```

**Regras de negócio**:
- `tenant_id` MUST ser `null` se `role = 'saas_admin'`
- `tenant_id` MUST ser preenchido se `role != 'saas_admin'`
- `email` sempre armazenado em minúsculas (normalizar na camada de aplicação)
- `totp_enabled` só pode ser `true` se `totp_secret` não for `null`

---

### `public.refresh_tokens`

Sessões ativas dos usuários. Cada linha representa um dispositivo/sessão.

| Campo | Tipo | Nulo | Default | Restrições |
|---|---|---|---|---|
| `id` | `uuid` | não | `gen_random_uuid()` | PK |
| `user_id` | `uuid` | não | — | FK → `public.users(id)` ON DELETE CASCADE |
| `token_hash` | `text` | não | — | SHA-256 hex do token opaco; UNIQUE |
| `expires_at` | `timestamptz` | não | — | 7 ou 30 dias após criação |
| `revoked` | `boolean` | não | `false` | `true` = sessão encerrada |
| `revoked_at` | `timestamptz` | sim | `null` | Timestamp da revogação |
| `user_agent` | `varchar(512)` | sim | `null` | Header `User-Agent` do request de login |
| `ip_address` | `varchar(45)` | sim | `null` | IPv4 ou IPv6 |
| `created_at` | `timestamptz` | não | `now()` | |

**Índices**:
- `idx_refresh_tokens_token_hash` — UNIQUE em `token_hash`
- `idx_refresh_tokens_user_id` — em `user_id` (listar sessões do usuário)
- `idx_refresh_tokens_expires_at` — para limpeza de tokens expirados (job periódico)

**Estados possíveis**:
```
ATIVO:    revoked=false, expires_at > now()
EXPIRADO: revoked=false, expires_at <= now()  → tratado como inválido
REVOGADO: revoked=true                        → nunca reutilizável
```

**Regra de reuse detection**: Se `revoked=true` e o token é apresentado → revogar TODOS os `refresh_tokens` do `user_id`.

---

### `public.invite_tokens`

Convites pendentes para novos usuários.

| Campo | Tipo | Nulo | Default | Restrições |
|---|---|---|---|---|
| `id` | `uuid` | não | `gen_random_uuid()` | PK |
| `email` | `varchar(255)` | não | — | Email do convidado (lowercase) |
| `role` | `user_role` | não | — | Role que será atribuída ao aceitar |
| `tenant_id` | `uuid` | sim | `null` | FK → `public.tenants(id)`; null para convites `saas_admin` |
| `token_hash` | `text` | não | — | SHA-256 hex do token enviado por e-mail; UNIQUE |
| `expires_at` | `timestamptz` | não | — | `created_at + 72h` |
| `accepted_at` | `timestamptz` | sim | `null` | Preenchido ao aceitar; null = pendente |
| `invalidated_at` | `timestamptz` | sim | `null` | Preenchido quando substituído por novo convite |
| `created_by` | `uuid` | não | — | FK → `public.users(id)` |
| `created_at` | `timestamptz` | não | `now()` | |

**Índices**:
- `idx_invite_tokens_token_hash` — UNIQUE em `token_hash`
- `idx_invite_tokens_email` — para verificar convite ativo por e-mail

**Estados possíveis**:
```
PENDENTE:    accepted_at=null, invalidated_at=null, expires_at > now()
ACEITO:      accepted_at IS NOT NULL
EXPIRADO:    accepted_at=null, expires_at <= now()
INVALIDADO:  invalidated_at IS NOT NULL
```

**Regra**: Ao criar novo convite para o mesmo `email`, setar `invalidated_at` em todos os convites pendentes do mesmo email.

---

### `public.password_reset_tokens`

Tokens de recuperação de senha. Uso único.

| Campo | Tipo | Nulo | Default | Restrições |
|---|---|---|---|---|
| `id` | `uuid` | não | `gen_random_uuid()` | PK |
| `user_id` | `uuid` | não | — | FK → `public.users(id)` ON DELETE CASCADE |
| `token_hash` | `text` | não | — | SHA-256 hex do token enviado por e-mail; UNIQUE |
| `expires_at` | `timestamptz` | não | — | `created_at + 1h` |
| `used_at` | `timestamptz` | sim | `null` | Preenchido ao consumir; null = não utilizado |
| `created_at` | `timestamptz` | não | `now()` | |

**Índices**:
- `idx_password_reset_tokens_token_hash` — UNIQUE em `token_hash`
- `idx_password_reset_tokens_user_id` — em `user_id`

**Regra**: Token é válido somente se `used_at IS NULL AND expires_at > now()`.

---

### `public.totp_recovery_codes`

Códigos de recuperação one-time para usuários com 2FA ativo.

| Campo | Tipo | Nulo | Default | Restrições |
|---|---|---|---|---|
| `id` | `uuid` | não | `gen_random_uuid()` | PK |
| `user_id` | `uuid` | não | — | FK → `public.users(id)` ON DELETE CASCADE |
| `code_hash` | `text` | não | — | SHA-256 hex do código de recuperação; UNIQUE |
| `used_at` | `timestamptz` | sim | `null` | Preenchido ao usar; null = disponível |
| `created_at` | `timestamptz` | não | `now()` | |

**Índices**:
- `idx_totp_recovery_codes_user_id` — em `user_id`
- `idx_totp_recovery_codes_code_hash` — UNIQUE em `code_hash`

**Regras**:
- 8 linhas criadas por usuário ao ativar 2FA (substituem todas as anteriores)
- Código válido somente se `used_at IS NULL`
- Ao desativar 2FA: DELETE todos os códigos do `user_id`

---

## Tipos TypeScript (Frontend)

```typescript
// Payload do Access Token (decodificado em memória)
export interface AccessTokenPayload {
  sub: string;           // user UUID
  role: UserRole;
  tenant_id: string | null;
  tenant_slug: string | null;
  email: string;
  iat: number;
  exp: number;
  impersonation?: boolean;
  impersonated_by?: string;
}

export type UserRole = 'saas_admin' | 'tenant_admin' | 'supervisor' | 'attendant';

// Estado de autenticação em memória
export interface AuthState {
  accessToken: string | null;
  user: AuthUser | null;
}

export interface AuthUser {
  id: string;
  email: string;
  name: string;
  role: UserRole;
  tenantSlug: string | null;
  isImpersonation: boolean;
}

// Respostas de login
export interface LoginResponse {
  access_token: string;
  user: {
    id: string;
    name: string;
    role: UserRole;
    tenant_slug: string | null;
  };
}

export interface LoginRequiresTotp {
  requires_totp: true;
  totp_session_token: string;
}

// Sessão ativa (para listagem)
export interface ActiveSession {
  id: string;
  user_agent: string | null;
  ip_address: string | null;
  created_at: string;
  is_current: boolean;
}
```

## Value Objects C# (Backend)

```csharp
// Domain/Users/UserRole.cs
public enum UserRole { SaasAdmin, TenantAdmin, Supervisor, Attendant }

// Domain/Auth/TokenResult.cs
public record TokenResult(string AccessToken, bool RequiresTotp, string? TotpSessionToken);

// Domain/Auth/ImpersonationTokenResult.cs
public record ImpersonationTokenResult(string ImpersonationToken, DateTime ExpiresAt);

// Infrastructure/Security/HashResult.cs
public record HashResult(string Hash, bool IsValid);
```

---

## Relações

```
public.tenants (1) ──< (0..N) public.users
public.users   (1) ──< (0..N) public.refresh_tokens
public.users   (1) ──< (0..N) public.password_reset_tokens
public.users   (1) ──< (0..8) public.totp_recovery_codes
public.users   (1) ──< (0..N) public.invite_tokens [created_by]
public.tenants (1) ──< (0..N) public.invite_tokens [tenant_id]
```

---

## EF Core — Mapeamento

As entidades acima serão configuradas via `IEntityTypeConfiguration<T>` no `AppDbContext`. O schema `public` é o default do PostgreSQL — não é necessário especificar explicitamente. Migrations via `dotnet ef migrations add`.

**Tabela não mapeada pelo EF** (criada diretamente na migration inicial):
- Enum `user_role` — criado via SQL raw na migration.
