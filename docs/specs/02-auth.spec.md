# Spec 02 — Autenticação
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

> **Nota de ordem de implementação:** Esta spec define o sistema de autenticação que sustenta todos os outros módulos. Na prática, é o primeiro módulo a ser implementado — os demais dependem dela.

---

## 1. Visão Geral

O OmniDesk possui dois contextos de autenticação independentes:

| Contexto | URL | Usuários |
|---|---|---|
| **Painel Admin SaaS** | `admin.omnicare.ia.br` | Apenas `saas_admin` |
| **CRM do Tenant** | `{slug}.omnicare.ia.br` | `tenant_admin`, `supervisor`, `attendant` |

Autenticação baseada em **JWT** (access token de curta duração) + **Refresh Token** (rotativo, armazenado em cookie `HttpOnly`). Não há sessão server-side — o backend é stateless.

---

## 2. Entidades

### 2.1 Usuário (`users`)

Tabela global (fora do schema de tenant). Representa qualquer usuário autenticável do sistema.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `email` | varchar(255) | sim | E-mail único no sistema. Usado como login. |
| `password_hash` | text | sim | Hash Argon2id da senha. |
| `role` | enum | sim | `saas_admin`, `tenant_admin`, `supervisor`, `attendant`. |
| `tenant_id` | UUID | não | FK → tenants. `null` para `saas_admin`. |
| `is_active` | boolean | sim | Default: `true`. Usuários inativos não podem logar. |
| `email_verified` | boolean | sim | Default: `false`. Verificado ao aceitar o convite. |
| `totp_secret` | text | não | Secret do TOTP (2FA). `null` = 2FA não ativado. Criptografado em repouso. |
| `totp_enabled` | boolean | sim | Default: `false`. |
| `last_login_at` | timestamptz | não | Última autenticação bem-sucedida. |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

### 2.2 Refresh Token (`refresh_tokens`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `user_id` | UUID | sim | FK → users |
| `token_hash` | text | sim | Hash SHA-256 do refresh token. Nunca armazenado em texto plano. |
| `expires_at` | timestamptz | sim | Expiração do refresh token. |
| `revoked` | boolean | sim | Default: `false`. Setado `true` ao revogar (logout ou rotação). |
| `created_at` | timestamptz | sim | — |
| `user_agent` | varchar(255) | não | Browser/dispositivo. Para identificação na lista de sessões ativas. |
| `ip_address` | varchar(45) | não | IP de origem do login. |

### 2.3 Token de Convite (`invite_tokens`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `email` | varchar(255) | sim | E-mail do convidado. |
| `role` | enum | sim | Role que será atribuída ao aceitar. |
| `tenant_id` | UUID | não | FK → tenants. |
| `token_hash` | text | sim | Hash do token enviado por e-mail. |
| `expires_at` | timestamptz | sim | Validade: 72 horas após criação. |
| `accepted_at` | timestamptz | não | Preenchido ao aceitar. |
| `created_by` | UUID | sim | FK → users. Quem enviou o convite. |
| `created_at` | timestamptz | sim | — |

### 2.4 Token de Recuperação de Senha (`password_reset_tokens`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `user_id` | UUID | sim | FK → users |
| `token_hash` | text | sim | Hash do token enviado por e-mail. |
| `expires_at` | timestamptz | sim | Validade: 1 hora após criação. |
| `used_at` | timestamptz | não | Preenchido ao usar. |
| `created_at` | timestamptz | sim | — |

---

## 3. Tokens JWT

### 3.1 Access Token

| Propriedade | Valor |
|---|---|
| Algoritmo | RS256 (par de chaves RSA) |
| Duração | 15 minutos |
| Armazenamento (client) | Memória (não em localStorage — evita XSS) |
| Renovação | Via Refresh Token antes de expirar |

**Payload:**
```json
{
  "sub": "uuid-do-user",
  "role": "attendant",
  "tenant_id": "uuid-do-tenant",
  "tenant_slug": "clinica-abc",
  "email": "maria@clinica.com",
  "iat": 1234567890,
  "exp": 1234568790
}
```

### 3.2 Refresh Token

| Propriedade | Valor |
|---|---|
| Formato | UUID v4 opaco (não JWT) |
| Duração | 7 dias ("remember me" desativado) / 30 dias ("remember me" ativado) |
| Armazenamento (client) | Cookie `HttpOnly; Secure; SameSite=Strict` |
| Rotação | A cada renovação — token antigo é revogado, novo emitido |
| Detecção de reutilização | Se token revogado é usado → revogar TODAS as sessões do usuário |

---

## 4. Fluxos de Autenticação

### 4.1 Login

```
POST /api/auth/login
  { email, password, remember_me, recaptcha_token }
  ↓
Backend valida reCAPTCHA v3 server-side (score ≥ 0.5 — invisível para o usuário)
Se score < 0.5 → rejeita com 403
  ↓
Valida email + password (Argon2id)
  ↓
Se totp_enabled = true → retorna { requires_totp: true, totp_session_token }
  ↓ (ou sem 2FA)
Gera access token (JWT RS256, 15min)
Gera refresh token (opaco, 7 ou 30 dias)
Salva hash do refresh token em refresh_tokens
  ↓
Resposta:
  - Body: { access_token, user: { id, name, role, tenant_slug } }
  - Cookie: refresh_token (HttpOnly)
```

**Proteção contra força-bruta (camadas complementares):**
- **reCAPTCHA v3 (invisível):** token gerado pelo frontend, validado server-side via `https://www.google.com/recaptcha/api/siteverify`. Configurado via `RECAPTCHA_SECRET_KEY` (variável de ambiente). Score mínimo: `0.5`.
- **Rate limiting:** máximo de 5 tentativas falhas em 10 minutos por IP + e-mail → bloqueio de 15 minutos.


### 4.2 Renovação do Access Token (Token Refresh)

```
POST /api/auth/refresh
  (sem body — usa cookie refresh_token)
  ↓
Valida refresh token (busca por hash, verifica expiração e revocação)
  ↓
Se token revogado → revogar todas as sessões + retornar 401
  ↓
Revoga token antigo, emite novo refresh token + novo access token
  ↓
Resposta: { access_token } + novo cookie refresh_token
```

### 4.3 Logout

```
POST /api/auth/logout
  (usa cookie refresh_token)
  ↓
Marca refresh token como revogado (revoked = true)
  ↓
Apaga cookie refresh_token
```

### 4.4 Autenticação de 2 Fatores (TOTP)

```
POST /api/auth/totp/verify
  { totp_session_token, code }
  ↓
Valida code TOTP (Google Authenticator / Authy compatível — RFC 6238)
  ↓
Emite access token + refresh token normalmente
```

**Ativação do 2FA (pelo usuário em Perfil):**
1. Backend gera `totp_secret` + QR Code URI
2. Usuário escaneia no app autenticador
3. Usuário confirma digitando um código válido
4. Sistema seta `totp_enabled = true` e salva `totp_secret` (criptografado)
5. Sistema exibe 8 **códigos de recuperação** (one-time, hash armazenado) para o usuário salvar

### 4.5 Convite de Novo Usuário

```
tenant_admin / supervisor envia convite (POST /api/auth/invite)
  ↓
Sistema cria invite_token (72h de validade)
  ↓
E-mail enviado via SendGrid com link:
  {slug}.omnicare.ia.br/aceitar-convite?token=xxx
  ↓
Usuário acessa o link, define nome e senha
  ↓
Sistema valida token, cria user com email_verified = true
  ↓
Login automático
```

### 4.6 Recuperação de Senha

```
POST /api/auth/forgot-password  { email }
  ↓
Se e-mail existir → gera password_reset_token (1h)
  ↓
E-mail via SendGrid com link de redefinição
  ↓
POST /api/auth/reset-password  { token, new_password }
  ↓
Valida token, atualiza password_hash, revoga TODOS os refresh_tokens do usuário
```

### 4.7 Impersonation (saas_admin)

```
saas_admin solicita: POST /api/admin/tenants/{slug}/impersonate
  ↓
Sistema gera access token de curta duração (5 min, não renovável)
  com payload: { role: "tenant_admin", tenant_id, impersonated_by: "saas_admin", impersonation: true }
  ↓
saas_admin é redirecionado para {slug}.omnicare.ia.br com o token
  ↓
Toda ação durante a impersonation é registrada no audit log com flag impersonated_by
  ↓
Token expira em 5 min — sem refresh token emitido para impersonation
```

---

## 5. Regras de Segurança

- Senhas: mínimo 8 caracteres
- Hash: Argon2id (memory ≥ 64MB, iterations ≥ 3, parallelism = 1)
- Refresh token armazenado **apenas como hash SHA-256**
- Cookie do refresh token: `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`
- Access token em memória no frontend (nunca `localStorage`)
- HTTPS obrigatório em produção (Cloudflare Tunnel)
- **reCAPTCHA v3 (invísível)** no endpoint de login: token validado server-side contra a API do Google. `RECAPTCHA_SECRET_KEY` configurado como variável de ambiente global. Score mínimo aceitável: `0.5` (configurável por variável de ambiente).
- Rate limiting em `/api/auth/login` e `/api/auth/forgot-password`
- Token de recuperação de senha invalida todas as sessões ativas ao ser usado
- Reutilização de refresh token revogado → invalida todas as sessões do usuário
- Impersonation tokens duram 5 min e não podem ser renovados
- **2FA TOTP é opcional** para todas as roles na V1. Qualquer usuário pode ativar em Perfil → Segurança. Não há role com 2FA obrigatório na V1.

---

## 6. Endpoints

```
POST   /api/auth/login                    → login com e-mail + senha
POST   /api/auth/refresh                  → renovar access token via cookie
POST   /api/auth/logout                   → encerrar sessão
POST   /api/auth/totp/verify              → verificar código 2FA após login
POST   /api/auth/totp/setup               → iniciar configuração do 2FA
POST   /api/auth/totp/confirm             → confirmar código e ativar 2FA
DELETE /api/auth/totp                     → desativar 2FA (requer senha)

POST   /api/auth/invite                   → enviar convite para novo usuário
POST   /api/auth/accept-invite            → aceitar convite e definir senha
POST   /api/auth/forgot-password          → solicitar redefinição de senha
POST   /api/auth/reset-password           → redefinir senha com token

GET    /api/auth/sessions                 → listar sessões ativas do usuário
DELETE /api/auth/sessions/{id}            → encerrar sessão específica
DELETE /api/auth/sessions                 → encerrar todas as outras sessões

# Perfil do usuário autenticado
GET    /api/me                            → dados do usuário logado
PUT    /api/me                            → atualizar nome / avatar
PUT    /api/me/password                   → trocar senha (requer senha atual)

# Admin SaaS — impersonation
POST   /api/admin/tenants/{slug}/impersonate  → gerar token de impersonation
```

---

## 7. Critérios de Aceite

- [ ] Login com e-mail + senha retorna access token (JWT RS256, 15min) + cookie refresh token
- [ ] Access token em memória no frontend — nunca em localStorage
- [ ] reCAPTCHA v3 validado server-side em cada tentativa de login; score < 0.5 rejeita com 403
- [ ] Rate limiting: 5 tentativas falhas em 10 min por IP+e-mail → bloqueio de 15 min
- [ ] Refresh token rotativo: token antigo revogado a cada renovação
- [ ] Refresh token reutilizado após revogação → todas as sessões do usuário invalidadas
- [ ] 2FA TOTP ativado manualmente pelo próprio usuário (opcional para todas as roles na V1)
- [ ] Ativação do 2FA exige confirmação com código válido antes de ativar
- [ ] Usuário sem e-mail verificado não pode logar — deve aceitar o convite primeiro
- [ ] Convite de usuário expira em 72h
- [ ] Link de recuperação de senha expira em 1h e invalida todas as sessões ao ser usado
- [ ] Impersonation token: 5 min, sem renovação, ações auditadas com `impersonated_by`
- [ ] Usuário inativo não pode logar
- [ ] Senhas armazenadas apenas como hash Argon2id
- [ ] Cookie refresh token: HttpOnly, Secure, SameSite=Strict

---

## 8. Decisões Registradas

| # | Decisão | Registrado em |
|---|---|---|
| P1 | 2FA TOTP opcional para todas as roles na V1 — sem obrigatoriedade. Complexidade baixa com libs .NET (OtpNet); implementado como feature de Perfil → Segurança | v1.1 |
| P2 | Login requer e-mail verificado (convite aceito). reCAPTCHA v3 invísível adicionado ao endpoint de login como camada complementar ao rate limiting | v1.1 |
