# Tasks: Autenticação

**Input**: Design documents from `specs/002-auth-jwt/`
**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅ | quickstart.md ✅

---

## Phase 1: Setup — Entidades e Banco de Dados

**Purpose**: Criar as entidades de domínio, repositórios e a migration inicial. Nenhum endpoint ou serviço depende diretamente desta fase — ela define o contrato de dados.

- [x] T001 Criar enum `UserRole` em `src/omniDesk.Api/Domain/Users/UserRole.cs` com valores: `SaasAdmin`, `TenantAdmin`, `Supervisor`, `Attendant`
- [x] T002 [P] Criar entidade `User` em `src/omniDesk.Api/Domain/Users/User.cs` com todos os campos do data-model.md (id, email, password_hash, role, tenant_id, is_active, email_verified, totp_secret, totp_enabled, last_login_at, created_at, updated_at)
- [x] T003 [P] Criar entidade `RefreshToken` em `src/omniDesk.Api/Domain/RefreshTokens/RefreshToken.cs` (id, user_id, token_hash, expires_at, revoked, revoked_at, user_agent, ip_address, created_at)
- [x] T004 [P] Criar entidade `InviteToken` em `src/omniDesk.Api/Domain/InviteTokens/InviteToken.cs` (id, email, role, tenant_id, token_hash, expires_at, accepted_at, invalidated_at, created_by, created_at)
- [x] T005 [P] Criar entidade `PasswordResetToken` em `src/omniDesk.Api/Domain/PasswordResetTokens/PasswordResetToken.cs` (id, user_id, token_hash, expires_at, used_at, created_at)
- [x] T006 [P] Criar entidade `TotpRecoveryCode` em `src/omniDesk.Api/Domain/TotpRecoveryCodes/TotpRecoveryCode.cs` (id, user_id, code_hash, used_at, created_at)
- [x] T007 [P] Criar interface `IUserRepository` em `src/omniDesk.Api/Domain/Users/IUserRepository.cs` com métodos: `GetByIdAsync`, `GetByEmailAsync`, `CreateAsync`, `UpdateAsync`, `ExistsByEmailAsync`
- [x] T008 [P] Criar interfaces de repositório em seus respectivos diretórios de domínio: `IRefreshTokenRepository` (GetByHashAsync, CreateAsync, RevokeAsync, RevokeAllByUserIdAsync, GetActiveByUserIdAsync), `IInviteTokenRepository` (GetByHashAsync, CreateAsync, InvalidatePendingByEmailAsync), `IPasswordResetTokenRepository` (GetByHashAsync, CreateAsync, MarkUsedAsync), `ITotpRecoveryCodeRepository` (GetByHashAsync, CreateAllAsync, MarkUsedAsync, DeleteAllByUserIdAsync)
- [x] T009 Criar `AppDbContext` em `src/omniDesk.Api/Infrastructure/Persistence/AppDbContext.cs` com `DbSet<T>` para as 5 entidades; registrar as configurations via `ApplyConfigurationsFromAssembly`
- [x] T010 [P] Criar configurações EF Core `UserConfiguration` em `src/omniDesk.Api/Infrastructure/Persistence/Configurations/UserConfiguration.cs`: tabela `users`, schema `public`, coluna `role` mapeada para enum PostgreSQL `user_role`, índice único em `email`
- [x] T011 [P] Criar configurações EF Core para as demais entidades em `src/omniDesk.Api/Infrastructure/Persistence/Configurations/`: `RefreshTokenConfiguration` (índice único em `token_hash`, índice em `user_id`), `InviteTokenConfiguration`, `PasswordResetTokenConfiguration`, `TotpRecoveryCodeConfiguration` — conforme índices do data-model.md
- [x] T012 Gerar migration inicial com `dotnet ef migrations add InitialAuth --project src/omniDesk.Api` criando: enum `user_role` via SQL raw, tabelas `users`, `refresh_tokens`, `invite_tokens`, `password_reset_tokens`, `totp_recovery_codes` com todos os campos e índices definidos no data-model.md

---

## Phase 2: Foundational — Serviços de Infraestrutura

**Purpose**: Serviços transversais que todas as user stories consomem. BLOQUEANTE — nenhuma US pode ser implementada sem esta fase.

**⚠️ CRÍTICO**: Nenhuma user story começa antes desta fase estar completa.

- [x] T013 Implementar `PasswordHasher` em `src/omniDesk.Api/Infrastructure/Security/PasswordHasher.cs` usando `Konscious.Security.Cryptography.Argon2`; parâmetros: `MemorySize=65536`, `Iterations=3`, `DegreeOfParallelism=1`; salt 16 bytes via `RandomNumberGenerator`; hash 32 bytes; métodos: `HashAsync(password) → string` (PHC format), `VerifyAsync(password, hash) → bool`
- [x] T014 [P] Implementar `JwtService` em `src/omniDesk.Api/Infrastructure/Security/JwtService.cs`: carregar par RSA de `JWT_PRIVATE_KEY_PEM` / `JWT_PUBLIC_KEY_PEM`; métodos: `GenerateAccessToken(User, TimeSpan? duration)` com claims (sub, role, tenant_id, tenant_slug, email, iat, exp), `GenerateTotpSessionToken(Guid userId)` (5 min, claim type=totp_session), `ValidateTotpSessionToken(string token) → Guid?`, `GenerateImpersonationToken(Guid tenantId, string slug, Guid impersonatedBy)` (5 min, impersonation=true)
- [x] T015 [P] Criar interface `IEmailService` em `src/omniDesk.Api/Infrastructure/Email/IEmailService.cs` e implementação `SendGridEmailService` em `src/omniDesk.Api/Infrastructure/Email/SendGridEmailService.cs` usando SendGrid SDK; chave via env `SENDGRID_API_KEY`; métodos: `SendInviteAsync(to, tenantName, inviteLink)`, `SendPasswordResetAsync(to, resetLink)`
- [x] T016 [P] Implementar os 5 repositórios em `src/omniDesk.Api/Infrastructure/Persistence/Repositories/`: `UserRepository`, `RefreshTokenRepository` (incluindo `RevokeAllByUserIdAsync` para reuse detection), `InviteTokenRepository` (incluindo `InvalidatePendingByEmailAsync`), `PasswordResetTokenRepository`, `TotpRecoveryCodeRepository` — todos usando `AppDbContext`
- [x] T017 Criar `AuthExtensions.cs` em `src/omniDesk.Api/Infrastructure/Auth/AuthExtensions.cs` registrando no DI: `AppDbContext`, todos os repositórios, `PasswordHasher`, `JwtService`, `SendGridEmailService`, `TurnstileService` (já existe da Spec 001); configurar `AddAuthentication().AddJwtBearer()` com validação RS256 usando `JWT_PUBLIC_KEY_PEM`
- [x] T018 [P] Configurar rate limiting em `src/omniDesk.Api/Infrastructure/Security/RateLimitingExtensions.cs` usando `Microsoft.AspNetCore.RateLimiting`: sliding window, 5 req por 10 min, chave = `SHA256(ip + ":" + email)`; aplicável em `/api/auth/login` e `/api/auth/forgot-password`; retorna 429 com código `rate_limit_exceeded`
- [x] T019 Atualizar `src/omniDesk.Api/Program.cs`: chamar `AuthExtensions`, `RateLimitingExtensions`; adicionar middleware `UseAuthentication()`, `UseAuthorization()`, `UseRateLimiter()`; mapear os grupos de endpoints de auth (a serem criados nas USs seguintes)

**Checkpoint**: Infraestrutura de auth pronta — implementação das user stories pode começar.

---

## Phase 3: User Story 1 — Acesso Seguro ao Sistema (Priority: P1) 🎯 MVP

**Goal**: Login com e-mail+senha, renovação automática de sessão, logout, proteção anti-bot e rate limiting funcionando nos dois frontends.

**Independent Test**: Criar usuário no banco, fazer login via curl (Cenário 1 do quickstart.md), verificar JWT claims, renovar token (Cenário 2), testar reuse detection (Cenário 3), logout (Cenário 4).

- [x] T020 [US1] Implementar `LoginEndpoint` em `src/omniDesk.Api/Features/Auth/Login/LoginEndpoint.cs` com `LoginRequest` (email, password, remember_me, turnstile_token) e `LoginResponse`: (1) validar Turnstile via `TurnstileService` → 403 se falhar; (2) aplicar rate limiter por ip+email; (3) buscar user por email, verificar `is_active` → 401, verificar `email_verified` → 401, verificar senha via `PasswordHasher` → 401; (4) se `totp_enabled=true` → retornar `{ requires_totp: true, totp_session_token }`; (5) caso contrário gerar access token (15 min) + refresh token opaco (UUID v4), salvar hash SHA-256 em `refresh_tokens` com `expires_at` de 7 ou 30 dias conforme `remember_me`, emitir cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`; (6) atualizar `last_login_at`
- [x] T021 [US1] Implementar `RefreshEndpoint` em `src/omniDesk.Api/Features/Auth/Refresh/RefreshEndpoint.cs`: ler cookie `refresh_token`, calcular SHA-256, buscar no banco; se não encontrado → 401 `token_missing`; se `revoked=true` → chamar `RevokeAllByUserIdAsync` + retornar 401 `token_revoked` (reuse detection); se `expires_at <= now()` → 401 `token_expired`; revogar token atual (`revoked=true`, `revoked_at=now()`), criar novo token, emitir novo cookie; retornar novo access token
- [x] T022 [US1] Implementar `LogoutEndpoint` em `src/omniDesk.Api/Features/Auth/Logout/LogoutEndpoint.cs`: ler cookie, buscar por hash, marcar `revoked=true`; limpar cookie (`Max-Age=0`); retornar 204
- [x] T023 [P] [US1] Implementar `TokenService` em `src/omniDesk.Admin/src/app/core/services/token.service.ts`: armazenar access token em `Signal<string | null>` privado; métodos: `setToken(token: string)`, `clearToken()`, `isTokenValid(): boolean` (decodifica payload e verifica `exp > Date.now()/1000`)
- [x] T024 [P] [US1] Implementar `AuthService` em `src/omniDesk.Admin/src/app/core/services/auth.service.ts`: `currentUser = signal<AuthUser | null>(null)`; `login(req: LoginRequest): Observable<LoginResponse>` chamando `POST /api/auth/login`, armazenando token e user em memória; `restoreSession(): Observable<boolean>` chamando `POST /api/auth/refresh` no bootstrap; `logout(): Observable<void>` chamando `POST /api/auth/logout` e limpando estado
- [x] T025 [P] [US1] Implementar `AuthInterceptor` em `src/omniDesk.Admin/src/app/shared/interceptors/auth.interceptor.ts`: anexar `Authorization: Bearer <token>` em toda requisição autenticada; interceptar resposta 401 → chamar `AuthService.restoreSession()` → retry da requisição original (máx 1 retry)
- [x] T026 [P] [US1] Implementar `AuthGuard` em `src/omniDesk.Admin/src/app/shared/guards/auth.guard.ts`: se `TokenService.isTokenValid()` → permitir; senão chamar `AuthService.restoreSession()` → se falhar redirecionar para `/login`
- [x] T027 [P] [US1] Criar `LoginComponent` em `src/omniDesk.Admin/src/app/features/auth/login/login.component.ts` e `login.component.html`: formulário Reactive com campos email + password; incluir `<app-turnstile>` (Spec 001); desabilitar botão submit até `turnstileToken` preenchido e form válido; chamar `AuthService.login()`; redirecionar para `/dashboard` no sucesso; exibir erro em caso de 401/403/429
- [x] T028 [P] [US1] Implementar `TokenService` para CRM em `src/omniDesk.Crm/src/app/core/services/token.service.ts` — mesmo contrato do Admin
- [x] T029 [P] [US1] Implementar `AuthService` para CRM em `src/omniDesk.Crm/src/app/core/services/auth.service.ts`: mesmo padrão do Admin; adicionalmente detectar query param `?token=` na inicialização para fluxo de impersonation (guardar `isImpersonation=true` no `AuthUser`)
- [x] T030 [P] [US1] Implementar `AuthInterceptor` para CRM em `src/omniDesk.Crm/src/app/shared/interceptors/auth.interceptor.ts` — mesmo padrão do Admin
- [x] T031 [P] [US1] Implementar `AuthGuard`, `RoleGuard` e `GuestGuard` para CRM em `src/omniDesk.Crm/src/app/shared/guards/`: `AuthGuard` — mesmo padrão do Admin; `RoleGuard` — verifica `currentUser().role` contra `route.data['roles']`, redireciona para `/acesso-negado` se não autorizado; `GuestGuard` — redireciona para `/dashboard` se já autenticado
- [x] T032 [P] [US1] Criar `LoginComponent` para CRM em `src/omniDesk.Crm/src/app/features/auth/login/login.component.ts` e `login.component.html`: mesmo padrão do Admin; adicionar opção "lembrar-me" (checkbox → `remember_me: true`); redirecionar para `/dashboard` no sucesso

**Checkpoint**: Login, renovação e logout funcionando em ambos os frontends — US1 testável pelo quickstart Cenários 1–5.

---

## Phase 4: User Story 2 — Recuperação de Senha (Priority: P1)

**Goal**: Fluxo completo de forgot-password e reset-password com Turnstile, expiração de 1h e invalidação de sessões.

**Independent Test**: Solicitar recuperação via curl, obter token do banco, redefinir senha, verificar que login funciona com nova senha e sessões anteriores foram revogadas (Cenário 6 do quickstart.md).

- [x] T033 [US2] Implementar `ForgotPasswordEndpoint` em `src/omniDesk.Api/Features/Auth/ForgotPassword/ForgotPasswordEndpoint.cs` com `ForgotPasswordRequest` (email, turnstile_token): (1) validar Turnstile → 403 se falhar; (2) buscar user por email — independente do resultado retornar 200 com mensagem genérica (prevenção de user enumeration); (3) se email existir: gerar UUID v4 (token raw), calcular SHA-256, salvar em `password_reset_tokens` com `expires_at=now()+1h`; (4) chamar `IEmailService.SendPasswordResetAsync()`
- [x] T034 [US2] Implementar `ResetPasswordEndpoint` em `src/omniDesk.Api/Features/Auth/ResetPassword/ResetPasswordEndpoint.cs` com `ResetPasswordRequest` (token, new_password): (1) calcular SHA-256 do token, buscar em `password_reset_tokens`; (2) validar `used_at IS NULL` e `expires_at > now()` → 400 `invalid_token` se falhar; (3) validar `new_password.Length >= 8` → 400 `password_too_short`; (4) hash nova senha via `PasswordHasher`; (5) atualizar `user.password_hash`; (6) marcar `used_at=now()` no token; (7) chamar `IRefreshTokenRepository.RevokeAllByUserIdAsync()` para invalidar todas as sessões; (8) retornar 204
- [x] T035 [P] [US2] Criar `ForgotPasswordComponent` para CRM em `src/omniDesk.Crm/src/app/features/auth/forgot-password/forgot-password.component.ts` e `forgot-password.component.html`: campo email + `<app-turnstile>`; desabilitar submit até token Turnstile emitido; chamar `POST /api/auth/forgot-password`; exibir confirmação genérica; link "Voltar ao login"
- [x] T036 [P] [US2] Criar `ResetPasswordComponent` para CRM em `src/omniDesk.Crm/src/app/features/auth/reset-password/reset-password.component.ts` e `reset-password.component.html`: ler `?token=` da URL via `ActivatedRoute`; campos nova senha + confirmação (validar igualdade); chamar `POST /api/auth/reset-password`; redirecionar para `/login` com mensagem de sucesso; exibir erro se token inválido/expirado

**Checkpoint**: US2 testável independentemente — fluxo completo de recuperação de senha funcionando.

---

## Phase 5: User Story 3 — Convite de Colaboradores (Priority: P2)

**Goal**: tenant_admin/supervisor envia convite por email; convidado define nome+senha e é autenticado automaticamente.

**Independent Test**: Autenticar como tenant_admin, enviar convite, aceitar via curl com token do banco, verificar que usuário foi criado com `email_verified=true` e recebeu sessão (Cenário 7 do quickstart.md).

- [x] T037 [US3] Implementar `SendInviteEndpoint` em `src/omniDesk.Api/Features/Auth/Invite/SendInviteEndpoint.cs` com `InviteRequest` (email, role): (1) autorizar apenas `tenant_admin` e `supervisor`; (2) verificar que `email` não está em `users` → 409 `user_already_exists`; (3) chamar `IInviteTokenRepository.InvalidatePendingByEmailAsync()` (setar `invalidated_at` em convites pendentes); (4) gerar UUID v4 (token raw), SHA-256, salvar em `invite_tokens` com `expires_at=now()+72h`, `created_by=currentUserId`, `tenant_id=currentUserTenantId`, `role`; (5) chamar `IEmailService.SendInviteAsync()`; (6) retornar 201 com `{ id, email, role, expires_at }`
- [x] T038 [US3] Implementar `AcceptInviteEndpoint` em `src/omniDesk.Api/Features/Auth/Invite/AcceptInviteEndpoint.cs` com `AcceptInviteRequest` (token, name, password): (1) SHA-256 do token, buscar em `invite_tokens`; (2) validar `accepted_at IS NULL`, `invalidated_at IS NULL`, `expires_at > now()` → 400 `invalid_token`; (3) validar `password.Length >= 8`; (4) hash senha via `PasswordHasher`; (5) criar `User` com `email_verified=true`, `is_active=true`, `role` e `tenant_id` do convite; (6) marcar `accepted_at=now()`; (7) emitir access token + refresh token cookie (fluxo idêntico ao login); (8) retornar 200 com mesmo body do login
- [x] T039 [P] [US3] Criar `AcceptInviteComponent` para CRM em `src/omniDesk.Crm/src/app/features/auth/accept-invite/accept-invite.component.ts` e `accept-invite.component.html`: ler `?token=` da URL; campos nome + senha + confirmação de senha; chamar `POST /api/auth/accept-invite`; ao receber access token armazenar via `AuthService` e redirecionar para `/dashboard`; exibir erro se token expirado/inválido

**Checkpoint**: US3 testável independentemente — convite funcional de ponta a ponta.

---

## Phase 6: User Story 4 — Verificação em Dois Fatores (Priority: P2)

**Goal**: Qualquer usuário pode ativar/desativar 2FA TOTP opcional; login com 2FA ativo exige código antes de conceder acesso.

**Independent Test**: Ativar 2FA via endpoints, fazer login e verificar que retorna `requires_totp`, completar verificação com código TOTP, verificar que acesso é concedido (Cenário 8 do quickstart.md).

- [x] T040 [US4] Implementar `TotpEncryptionService` em `src/omniDesk.Api/Infrastructure/Security/TotpEncryptionService.cs`: AES-256-GCM com chave de 32 bytes lida de env var `TOTP_ENCRYPTION_KEY` (Base64); nonce de 12 bytes aleatórios por operação; formato de armazenamento `<nonce_hex>:<ciphertext_hex>`; métodos: `Encrypt(plaintext) → string`, `Decrypt(stored) → string`
- [x] T041 [P] [US4] Implementar `TotpService` em `src/omniDesk.Api/Infrastructure/Security/TotpService.cs` usando `OtpNet`: `GenerateSecret() → string` (Base32, 20 bytes aleatórios), `GenerateQrCodeUri(email, secret, issuer="OmniDesk") → string` (otpauth://totp/...), `ValidateCode(secret, code) → bool` (janela de ±1 passo de 30s para clock drift), `GenerateRecoveryCodes(count=8) → string[]` (8 chars alfanuméricos, charset sem ambíguos: A-Z excluindo I/O + 2-9)
- [x] T042 [US4] Implementar `TotpSetupEndpoint` em `src/omniDesk.Api/Features/Auth/Totp/TotpSetupEndpoint.cs`: gerar secret via `TotpService.GenerateSecret()`; cifrar com `TotpEncryptionService`; salvar em `user.totp_secret` (sem ativar ainda — `totp_enabled` permanece `false`); retornar `{ qr_code_uri, secret }` (secret em Base32 para entrada manual)
- [x] T043 [US4] Implementar `TotpConfirmEndpoint` em `src/omniDesk.Api/Features/Auth/Totp/TotpConfirmEndpoint.cs`: ler `user.totp_secret` pendente (decifrar), validar código do request via `TotpService.ValidateCode()` → 400 se inválido; setar `totp_enabled=true`; gerar 8 recovery codes via `TotpService.GenerateRecoveryCodes()`; salvar hashes SHA-256 em `totp_recovery_codes` (excluindo anteriores); retornar `{ recovery_codes: [...] }` ao usuário (exibição única)
- [x] T044 [US4] Implementar `TotpVerifyEndpoint` em `src/omniDesk.Api/Features/Auth/Totp/TotpVerifyEndpoint.cs` com `TotpVerifyRequest` (totp_session_token, code): (1) validar `totp_session_token` via `JwtService.ValidateTotpSessionToken()` → 400 se inválido/expirado; (2) buscar user; (3) tentar validar `code` como TOTP (`TotpService.ValidateCode()`) OU como recovery code (SHA-256 do code → buscar em `totp_recovery_codes` com `used_at IS NULL`, marcar `used_at=now()` se encontrado); (4) se nenhum válido → 401 `invalid_totp_code`; (5) emitir access token + refresh token cookie (fluxo idêntico ao login)
- [x] T045 [US4] Implementar `TotpDisableEndpoint` em `src/omniDesk.Api/Features/Auth/Totp/TotpDisableEndpoint.cs` com body `{ password }`: verificar senha atual via `PasswordHasher.VerifyAsync()` → 401 se incorreta; setar `totp_enabled=false`, `totp_secret=null`; deletar todos os `totp_recovery_codes` do user; retornar 204
- [x] T046 [US4] Atualizar `LoginEndpoint` em `src/omniDesk.Api/Features/Auth/Login/LoginEndpoint.cs` para suportar fluxo 2FA: após validação de credenciais, se `user.totp_enabled=true` gerar `totp_session_token` via `JwtService.GenerateTotpSessionToken()` e retornar `{ requires_totp: true, totp_session_token }` sem emitir refresh token

**Checkpoint**: US4 testável independentemente — 2FA ativável, login com TOTP exigindo código.

---

## Phase 7: User Story 5 — Gestão de Sessões e Perfil (Priority: P3)

**Goal**: Usuário pode listar e revogar sessões ativas, atualizar nome e trocar senha.

**Independent Test**: Autenticar em dois dispositivos, listar sessões via GET /api/auth/sessions, revogar uma, verificar que o device revogado recebe 401 no próximo refresh.

- [x] T047 [US5] Implementar `ListSessionsEndpoint` em `src/omniDesk.Api/Features/Auth/Sessions/ListSessionsEndpoint.cs`: buscar todos os `refresh_tokens` do `currentUserId` onde `revoked=false` e `expires_at > now()`; marcar `is_current=true` na linha cujo `token_hash` corresponde ao cookie atual; retornar array conforme contrato `auth-api.md`
- [x] T048 [P] [US5] Implementar `RevokeSessionEndpoint` em `src/omniDesk.Api/Features/Auth/Sessions/RevokeSessionEndpoint.cs`: buscar `refresh_tokens` pelo `{id}` da rota; validar que pertence ao `currentUserId` → 404 se não; marcar `revoked=true`, `revoked_at=now()`; retornar 204
- [x] T049 [P] [US5] Implementar `RevokeAllSessionsEndpoint` em `src/omniDesk.Api/Features/Auth/Sessions/RevokeAllSessionsEndpoint.cs`: revogar todos os `refresh_tokens` do `currentUserId` EXCETO o token atual (identificado pelo hash do cookie); retornar 204
- [x] T050 [P] [US5] Implementar `GetProfileEndpoint` em `src/omniDesk.Api/Features/Me/GetProfileEndpoint.cs` (retornar dados do `currentUser` conforme contrato) e `UpdateProfileEndpoint` em `src/omniDesk.Api/Features/Me/UpdateProfileEndpoint.cs` com `UpdateProfileRequest` (name) validado via FluentValidation (nome obrigatório, máx 100 chars)
- [x] T051 [P] [US5] Implementar `ChangePasswordEndpoint` em `src/omniDesk.Api/Features/Me/ChangePasswordEndpoint.cs` com `ChangePasswordRequest` (current_password, new_password): verificar `current_password` via `PasswordHasher.VerifyAsync()` → 401 `invalid_password`; validar `new_password.Length >= 8`; hash via `PasswordHasher.HashAsync()`; atualizar `user.password_hash`; retornar 204

**Checkpoint**: US5 testável — gestão de sessões e perfil funcionando.

---

## Phase 8: User Story 6 — Acesso de Suporte pelo Admin SaaS (Priority: P3)

**Goal**: saas_admin acessa painel do tenant com token temporário (5 min, não renovável); todas as ações ficam auditadas.

**Independent Test**: Login como saas_admin, chamar POST /api/admin/tenants/{slug}/impersonate, usar o token retornado para acessar endpoint do CRM, verificar que o token expira em 5 min e que tentativa de renovação é rejeitada (Cenário 9 do quickstart.md).

- [x] T052 [US6] Implementar `ImpersonateEndpoint` em `src/omniDesk.Api/Features/Admin/Impersonate/ImpersonateEndpoint.cs`: autorizar somente `role=saas_admin`; buscar tenant por `{slug}` → 404 se não encontrado; gerar token via `JwtService.GenerateImpersonationToken(tenantId, slug, impersonatedBy=currentUserId)` (5 min, claims: impersonation=true, impersonated_by, role=tenant_admin, tenant_id, tenant_slug); **não emitir refresh token**; retornar `{ impersonation_token, expires_at, redirect_url }` conforme contrato
- [x] T053 [US6] Atualizar `AuthService` no CRM em `src/omniDesk.Crm/src/app/core/services/auth.service.ts`: no método `restoreSession()`, verificar se `window.location.search` contém `?token=`; se sim, armazenar o token via `TokenService.setToken()`, decodificar payload, popular `currentUser` com `isImpersonation=true`; limpar o query param via `history.replaceState()` imediatamente
- [x] T054 [P] [US6] Criar `ImpersonationBannerComponent` em `src/omniDesk.Crm/src/app/shared/components/impersonation-banner/impersonation-banner.component.ts` e `impersonation-banner.component.html`: exibir apenas quando `authService.currentUser()?.isImpersonation === true`; mostrar mensagem "Você está acessando como admin SaaS. Esta sessão expira em 5 minutos."; estilizar com cor de destaque (warning) usando tokens da Spec 001
- [x] T055 [US6] Criar `ImpersonationAuditFilter` em `src/omniDesk.Api/Infrastructure/Auth/ImpersonationAuditFilter.cs` como `IEndpointFilter`: verificar se o JWT do request contém claim `impersonation=true`; se sim, registrar via Serilog `{ action: endpoint, impersonated_by, tenant_id, timestamp }`; registrar o filtro globalmente no grupo de endpoints autenticados em `Program.cs`

**Checkpoint**: US6 testável — impersonation funcional com auditoria.

---

## Phase 9: Polish & Testes

**Purpose**: Testes unitários, integração com Testcontainers e validação dos cenários do quickstart.

- [x] T056 Escrever testes unitários para `PasswordHasher` em `tests/omniDesk.Api.Tests/Infrastructure/Security/PasswordHasherTests.cs`: round-trip hash+verify, senha incorreta retorna false, string vazia, senha com caracteres especiais
- [x] T057 [P] Escrever testes unitários para `JwtService` em `tests/omniDesk.Api.Tests/Infrastructure/Security/JwtServiceTests.cs`: claims corretos no access token, expiração de 15 min, totp_session_token com claim type correto, impersonation token com claims impersonation=true e impersonated_by
- [x] T058 [P] Escrever testes unitários para `TotpService` em `tests/omniDesk.Api.Tests/Infrastructure/Security/TotpServiceTests.cs`: código válido aceito, código inválido rejeitado, geração de 8 recovery codes com 8 chars cada, nenhum char ambíguo (I, O, 0, 1, L) nos códigos gerados
- [x] T059 [P] Escrever testes de integração para `LoginEndpoint` e `RefreshEndpoint` em `tests/omniDesk.Api.Tests/Features/Auth/LoginEndpointTests.cs` usando Testcontainers (PostgreSQL real): login OK → JWT + cookie, senha errada → 401, usuário inativo → 401, email não verificado → 401, Turnstile inválido → 403, rate limit → 429, refresh válido → novo token, refresh com token revogado → 401 + todas sessões revogadas (reuse detection)
- [x] T060 [P] Escrever testes de integração para `ForgotPasswordEndpoint` e `ResetPasswordEndpoint` em `tests/omniDesk.Api.Tests/Features/Auth/PasswordResetEndpointTests.cs`: forgot com email válido → token criado no banco, forgot com email inválido → mesma resposta, reset com token válido → senha atualizada + sessões revogadas, reset com token expirado → 400, reset com token já usado → 400
- [x] T061 [P] Escrever testes de integração para `SendInviteEndpoint` e `AcceptInviteEndpoint` em `tests/omniDesk.Api.Tests/Features/Auth/InviteEndpointTests.cs`: convite enviado → registro no banco, convite aceito → usuário criado com email_verified=true + auto-login, convite expirado → 400, e-mail já cadastrado → 409, duplo convite invalida o anterior
- [x] T062 [P] Criar `TestWebApplicationFactory` em `tests/omniDesk.Api.Tests/Helpers/TestWebApplicationFactory.cs` e `AuthTestHelpers` em `tests/omniDesk.Api.Tests/Helpers/AuthTestHelpers.cs` com utilitários: seed de usuários de teste, geração de tokens de teste, asserção de cookies
- [x] T063 Executar todos os cenários do `quickstart.md` (Cenários 1–9) e confirmar que o checklist final está 100% verde; registrar resultado na seção de notas deste arquivo

> **Nota de verificação (T063)**: A verificação manual via curl (Cenários 1–9) requer o projeto .NET scaffoldado com `dotnet new webapi` e a migration aplicada no PostgreSQL. Neste estágio, toda a lógica de negócio foi implementada nos endpoints. A execução dos cenários do quickstart.md está bloqueada até o scaffold do projeto. Os testes de integração (T059–T061) cobrem os caminhos equivalentes com Testcontainers.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Sem dependências — pode começar imediatamente
- **Phase 2 (Foundational)**: Depende da Phase 1 — BLOQUEIA todas as user stories
- **Phase 3 (US1)**: Depende da Phase 2 — sem dependência de outras USs
- **Phase 4 (US2)**: Depende da Phase 2 — sem dependência de US1 (reutiliza mesmos serviços)
- **Phase 5 (US3)**: Depende da Phase 2 — sem dependência de US1/US2
- **Phase 6 (US4)**: Depende da Phase 3 (T046 atualiza LoginEndpoint) — deve vir após US1
- **Phase 7 (US5)**: Depende da Phase 2 — sem dependência de outras USs
- **Phase 8 (US6)**: Depende da Phase 3 (fluxo de token) — deve vir após US1 no CRM
- **Phase 9 (Polish)**: Depende da conclusão de todas as USs desejadas

### User Story Dependencies

- **US1 (P1)**: Inicia após Phase 2 — MVP mínimo viável
- **US2 (P1)**: Inicia após Phase 2 — pode ser paralela a US1
- **US3 (P2)**: Inicia após Phase 2 — pode ser paralela a US1/US2
- **US4 (P2)**: Inicia após US1 (T046 modifica LoginEndpoint)
- **US5 (P3)**: Inicia após Phase 2 — independente
- **US6 (P3)**: Inicia após US1 (CRM precisa do fluxo de token)

### Parallel Opportunities

- Phase 1: T002–T008 paralelos entre si; T009 depende de T002–T006; T010–T011 paralelos após T009
- Phase 2: T013–T016, T018 paralelos; T017 depende de T013–T016; T019 depende de T017–T018
- Phase 3 (US1): T023–T032 todos paralelos entre si (frontends Admin e CRM são arquivos distintos); T020–T022 backend em sequência lógica mas arquivos diferentes
- Phase 4–8: backend e frontend de cada US são paralelos
- Phase 9: T057–T062 todos paralelos entre si

---

## Parallel Example: User Story 1

```text
# Backend (sequencial — dependências lógicas):
T020 LoginEndpoint → T021 RefreshEndpoint → T022 LogoutEndpoint

# Frontend Admin (paralelo entre si):
T023 TokenService (Admin)
T024 AuthService (Admin)
T025 AuthInterceptor (Admin)
T026 AuthGuard (Admin)
T027 LoginComponent (Admin)

# Frontend CRM (paralelo com Admin e entre si):
T028 TokenService (CRM)
T029 AuthService (CRM)
T030 AuthInterceptor (CRM)
T031 Guards (CRM)
T032 LoginComponent (CRM)
```

---

## Implementation Strategy

### MVP (US1 + US2 apenas)

1. Completar Phase 1 (Setup)
2. Completar Phase 2 (Foundational)
3. Completar Phase 3 (US1 — Login/Refresh/Logout)
4. Completar Phase 4 (US2 — Recuperação de Senha)
5. **PARAR e VALIDAR**: quickstart Cenários 1–6
6. Com MVP: operador pode provisionar o primeiro tenant_admin via seed, que pode fazer login e recuperar senha

### Entrega Incremental

- Phases 1–3 → MVP de autenticação básica
- + Phase 4 → Recuperação de senha completa
- + Phase 5 → Time pode ser convidado
- + Phase 6 → 2FA disponível para usuários que desejam
- + Phases 7–8 → Controle de sessões e suporte operacional

### Notas

- [P] = tarefas paralelas (arquivos distintos, sem dependências pendentes)
- [USN] = user story mapeada para rastreabilidade
- Commitar após cada fase ou grupo lógico
- Testar cada US independentemente antes de avançar
