# Implementation Plan: Autenticação

**Branch**: `002-auth-jwt` | **Data**: 2026-05-05 | **Spec**: [spec.md](spec.md)

## Summary

Sistema completo de autenticação stateless para o OmniDesk. JWT RS256 (15 min) + Refresh Token rotativo opaco em cookie HttpOnly (7/30 dias). Cobre login, logout, renovação automática, recuperação de senha, convite de colaboradores, 2FA TOTP opcional, gestão de sessões, perfil e impersonation do saas_admin. Backend .NET 10 Minimal API; frontend Angular 21 com token exclusivamente em memória.

## Technical Context

**Backend**: C# .NET 10 — Minimal API + Endpoint Groups
**Frontend**: TypeScript — Angular 21 Standalone Components + Signals
**ORM**: Entity Framework Core 9 + Migrations (PostgreSQL)
**Storage**: PostgreSQL `public` schema (5 tabelas globais de auth) + Redis (rate limiting in-memory V1)
**Testing**: xUnit + Testcontainers (backend) / Angular TestBed (frontend)
**Target Platform**: Linux ARM64 (Oracle Cloud) + Cloudflare Pages (frontends)
**Project Type**: Web service (API) + dois SPAs (Admin e CRM)

**Dependências backend adicionais**:
| Pacote | Propósito |
|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Middleware de validação JWT |
| `System.IdentityModel.Tokens.Jwt` | Geração de JWT RS256 |
| `Konscious.Security.Cryptography.Argon2` | Hash de senha Argon2id |
| `OtpNet` | TOTP RFC 6238 (2FA) |
| `QRCoder` | Geração de QR Code para setup do 2FA |
| `SendGrid` | Envio de e-mails transacionais |
| `Microsoft.AspNetCore.RateLimiting` | Rate limiting nativo .NET 8+ |
| `FluentValidation.AspNetCore` | Validação de requests (já na constituição) |

**Dependências frontend** (Angular 21 built-ins — sem libs extras):
- `HttpClient` + `HttpInterceptor`
- `@angular/router` Guards
- `Signals` para estado reativo

**Variáveis de ambiente obrigatórias**:
| Variável | Descrição |
|---|---|
| `JWT_PRIVATE_KEY_PEM` | Chave RSA privada em formato PEM |
| `JWT_PUBLIC_KEY_PEM` | Chave RSA pública em formato PEM |
| `TOTP_ENCRYPTION_KEY` | 32 bytes em Base64 para AES-256-GCM |
| `TURNSTILE_SECRET_KEY` | Chave secreta do Cloudflare Turnstile (já existe da Spec 001) |
| `SENDGRID_API_KEY` | Chave da API SendGrid |
| `DATABASE_URL` | Connection string PostgreSQL |
| `REDIS_URL` | Connection string Redis |

**Performance Goals**: Login < 5 s (SC-001); renovação de token transparente (SC-002)
**Constraints**: JWT ≤ 15 min, refresh ≤ 30 dias, LGPD, dados em território brasileiro

## Constitution Check

| Princípio | Status | Observação |
|---|---|---|
| I. Multi-Tenant Isolation | ⚠️ JUSTIFICADO | Tabelas de auth no schema `public` — ver Complexity Tracking |
| II. AI-First, Human-Assisted | ✅ N/A | Auth não envolve IA |
| III. Channel Agnosticism | ✅ N/A | Auth é infraestrutura, não canal |
| IV. Security / LGPD | ✅ PASS | JWT 15 min, httpOnly cookie, Turnstile, Argon2id, sem localStorage |
| V. Simplicity | ✅ PASS | Sem padrões desnecessários; deps justificadas no research |
| VI. Observability | ✅ PASS | Audit log em impersonation (FR-039); Serilog para erros |
| VII. Test Discipline | ✅ PASS | Testcontainers + `.spec.ts` obrigatórios |

**Resultado do gate**: ✅ APROVADO (com desvio justificado do Princípio I)

## Project Structure

### Documentação (esta feature)

```text
specs/002-auth-jwt/
├── plan.md              ← este arquivo
├── research.md          ← decisões técnicas (10 seções)
├── data-model.md        ← 5 entidades + tipos TypeScript/C#
├── quickstart.md        ← 9 cenários de verificação
├── contracts/
│   ├── auth-api.md      ← contratos REST (18 endpoints)
│   └── auth-frontend.ts ← interfaces Angular (IAuthService, ITokenService, Guards)
└── tasks.md             ← gerado pelo /speckit-tasks
```

### Código-fonte (backend)

```text
src/omniDesk.Api/
├── Features/
│   ├── Auth/
│   │   ├── Login/
│   │   │   ├── LoginEndpoint.cs
│   │   │   ├── LoginRequest.cs
│   │   │   └── LoginResponse.cs
│   │   ├── Refresh/
│   │   │   └── RefreshEndpoint.cs
│   │   ├── Logout/
│   │   │   └── LogoutEndpoint.cs
│   │   ├── Totp/
│   │   │   ├── TotpVerifyEndpoint.cs
│   │   │   ├── TotpSetupEndpoint.cs
│   │   │   ├── TotpConfirmEndpoint.cs
│   │   │   ├── TotpDisableEndpoint.cs
│   │   │   └── TotpVerifyRequest.cs
│   │   ├── Invite/
│   │   │   ├── SendInviteEndpoint.cs
│   │   │   ├── AcceptInviteEndpoint.cs
│   │   │   ├── InviteRequest.cs
│   │   │   └── AcceptInviteRequest.cs
│   │   ├── ForgotPassword/
│   │   │   ├── ForgotPasswordEndpoint.cs
│   │   │   └── ForgotPasswordRequest.cs
│   │   ├── ResetPassword/
│   │   │   ├── ResetPasswordEndpoint.cs
│   │   │   └── ResetPasswordRequest.cs
│   │   └── Sessions/
│   │       ├── ListSessionsEndpoint.cs
│   │       ├── RevokeSessionEndpoint.cs
│   │       └── RevokeAllSessionsEndpoint.cs
│   ├── Me/
│   │   ├── GetProfileEndpoint.cs
│   │   ├── UpdateProfileEndpoint.cs
│   │   ├── UpdateProfileRequest.cs
│   │   ├── ChangePasswordEndpoint.cs
│   │   └── ChangePasswordRequest.cs
│   └── Admin/
│       └── Impersonate/
│           ├── ImpersonateEndpoint.cs
│           └── ImpersonateResponse.cs
├── Domain/
│   ├── Users/
│   │   ├── User.cs
│   │   ├── UserRole.cs
│   │   └── IUserRepository.cs
│   ├── RefreshTokens/
│   │   ├── RefreshToken.cs
│   │   └── IRefreshTokenRepository.cs
│   ├── InviteTokens/
│   │   ├── InviteToken.cs
│   │   └── IInviteTokenRepository.cs
│   ├── PasswordResetTokens/
│   │   ├── PasswordResetToken.cs
│   │   └── IPasswordResetTokenRepository.cs
│   └── TotpRecoveryCodes/
│       ├── TotpRecoveryCode.cs
│       └── ITotpRecoveryCodeRepository.cs
├── Infrastructure/
│   ├── Security/
│   │   ├── TurnstileService.cs          ← já existe (Spec 001)
│   │   ├── JwtService.cs                ← geração e validação RS256
│   │   ├── PasswordHasher.cs            ← Argon2id
│   │   ├── TotpService.cs               ← OtpNet + QRCoder
│   │   ├── TotpEncryptionService.cs     ← AES-256-GCM para totp_secret
│   │   └── RateLimitingExtensions.cs    ← configuração do built-in rate limiter
│   ├── Email/
│   │   ├── IEmailService.cs
│   │   └── SendGridEmailService.cs
│   ├── Persistence/
│   │   ├── AppDbContext.cs
│   │   ├── Migrations/                  ← EF Core migrations
│   │   ├── Configurations/              ← IEntityTypeConfiguration<T>
│   │   │   ├── UserConfiguration.cs
│   │   │   ├── RefreshTokenConfiguration.cs
│   │   │   ├── InviteTokenConfiguration.cs
│   │   │   ├── PasswordResetTokenConfiguration.cs
│   │   │   └── TotpRecoveryCodeConfiguration.cs
│   │   └── Repositories/
│   │       ├── UserRepository.cs
│   │       ├── RefreshTokenRepository.cs
│   │       ├── InviteTokenRepository.cs
│   │       ├── PasswordResetTokenRepository.cs
│   │       └── TotpRecoveryCodeRepository.cs
│   └── Auth/
│       └── AuthExtensions.cs            ← registro DI de todos os serviços de auth
└── Program.cs                           ← mapeamento de endpoints + middleware
```

```text
tests/omniDesk.Api.Tests/
├── Features/
│   └── Auth/
│       ├── LoginEndpointTests.cs
│       ├── RefreshEndpointTests.cs
│       ├── LogoutEndpointTests.cs
│       ├── TotpEndpointTests.cs
│       ├── InviteEndpointTests.cs
│       ├── ForgotPasswordEndpointTests.cs
│       ├── ResetPasswordEndpointTests.cs
│       ├── SessionsEndpointTests.cs
│       └── ImpersonateEndpointTests.cs
├── Infrastructure/
│   └── Security/
│       ├── TurnstileServiceTests.cs     ← já existe (Spec 001)
│       ├── JwtServiceTests.cs
│       ├── PasswordHasherTests.cs
│       └── TotpServiceTests.cs
└── Helpers/
    ├── TestWebApplicationFactory.cs
    └── AuthTestHelpers.cs
```

### Código-fonte (frontend Admin)

```text
src/omniDesk.Admin/src/app/
├── features/
│   └── auth/
│       └── login/
│           ├── login.component.ts
│           ├── login.component.html
│           └── login.component.spec.ts
├── core/
│   └── services/
│       ├── auth.service.ts
│       ├── auth.service.spec.ts
│       ├── token.service.ts
│       └── token.service.spec.ts
└── shared/
    ├── guards/
    │   ├── auth.guard.ts
    │   └── auth.guard.spec.ts
    └── interceptors/
        ├── auth.interceptor.ts
        └── auth.interceptor.spec.ts
```

### Código-fonte (frontend CRM)

```text
src/omniDesk.Crm/src/app/
├── features/
│   └── auth/
│       ├── login/
│       │   ├── login.component.ts
│       │   ├── login.component.html
│       │   └── login.component.spec.ts
│       ├── forgot-password/
│       │   ├── forgot-password.component.ts
│       │   ├── forgot-password.component.html
│       │   └── forgot-password.component.spec.ts
│       ├── reset-password/
│       │   ├── reset-password.component.ts
│       │   ├── reset-password.component.html
│       │   └── reset-password.component.spec.ts
│       └── accept-invite/
│           ├── accept-invite.component.ts
│           ├── accept-invite.component.html
│           └── accept-invite.component.spec.ts
├── core/
│   └── services/
│       ├── auth.service.ts
│       ├── auth.service.spec.ts
│       ├── token.service.ts
│       └── token.service.spec.ts
└── shared/
    ├── guards/
    │   ├── auth.guard.ts
    │   ├── auth.guard.spec.ts
    │   ├── role.guard.ts
    │   ├── role.guard.spec.ts
    │   ├── guest.guard.ts
    │   └── guest.guard.spec.ts
    └── interceptors/
        ├── auth.interceptor.ts
        └── auth.interceptor.spec.ts
```

## Complexity Tracking

| Desvio | Por Que Necessário | Alternativa Rejeitada |
|---|---|---|
| Tabelas de auth no schema `public` | `users` é cross-tenant por design: `saas_admin` não pertence a nenhum tenant; autenticação precede a resolução do tenant | Colocar `users` em cada schema de tenant impossibilita login central do Admin SaaS e impersonation cross-tenant |
| JWT RS256 (assimétrico) | Impersonation exige que o CRM do Tenant valide um token emitido pelo Admin SaaS — RS256 permite distribuir apenas a chave pública | HS256 (simétrico) exigiria compartilhar o mesmo secret entre os dois domínios, comprometendo o isolamento |
| Tabela `totp_recovery_codes` (não no spec original) | FR-029 exige 8 códigos one-time armazenados de forma segura — requer entidade separada com `used_at` por linha | Armazenar como array JSON em `users` impede invalidação individual eficiente |
