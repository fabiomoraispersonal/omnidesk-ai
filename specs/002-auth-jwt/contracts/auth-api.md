# Contratos de API: Autenticação

**Branch**: `002-auth-jwt` | **Data**: 2026-05-05

> Contratos definem o formato de request/response de cada endpoint. Implementação usa .NET 10 Minimal API.
> Todos os endpoints retornam `application/json; charset=utf-8`.
> Erros seguem o formato `ProblemDetails` (RFC 7807).

---

## Autenticação Base

### `POST /api/auth/login`

Login com e-mail + senha. Requer token Turnstile validado.

**Request**:
```json
{
  "email": "maria@clinica.com",
  "password": "minhasenha123",
  "remember_me": false,
  "turnstile_token": "<token-do-widget>"
}
```

**Response 200 — login sem 2FA**:
```json
{
  "access_token": "<jwt>",
  "user": {
    "id": "uuid",
    "name": "Maria Silva",
    "role": "attendant",
    "tenant_slug": "clinica-abc"
  }
}
```
Cookie: `refresh_token=<uuid>; HttpOnly; Secure; SameSite=Strict; Path=/api/auth; Max-Age=604800`

**Response 200 — login com 2FA ativo**:
```json
{
  "requires_totp": true,
  "totp_session_token": "<jwt-curto-5min>"
}
```
*(sem cookie de refresh — sessão não iniciada ainda)*

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 400 | `validation_error` | Campos inválidos ou ausentes |
| 401 | `invalid_credentials` | E-mail ou senha incorretos |
| 401 | `account_inactive` | Usuário inativo |
| 401 | `email_not_verified` | Convite não aceito |
| 403 | `turnstile_failed` | Verificação anti-bot falhou |
| 429 | `rate_limit_exceeded` | 5+ tentativas em 10 min |

---

### `POST /api/auth/refresh`

Renova o access token usando o cookie `refresh_token`.

**Request**: sem body — lê cookie `refresh_token` automaticamente.

**Response 200**:
```json
{
  "access_token": "<novo-jwt>"
}
```
Cookie: novo `refresh_token` (token anterior revogado)

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 401 | `token_missing` | Cookie ausente |
| 401 | `token_expired` | Token expirado |
| 401 | `token_revoked` | Token revogado (inclui reuse detection) |

---

### `POST /api/auth/logout`

Encerra a sessão atual.

**Request**: sem body — lê cookie `refresh_token`.

**Response 204**: sem body.
Cookie: `refresh_token` removido (`Max-Age=0`).

---

## TOTP (2FA)

### `POST /api/auth/totp/verify`

Verifica o código TOTP após login com 2FA ativo.

**Request**:
```json
{
  "totp_session_token": "<jwt-recebido-no-login>",
  "code": "123456"
}
```

**Response 200** — igual ao login sem 2FA (access token + cookie refresh).

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 400 | `invalid_totp_session` | Token ausente, inválido ou expirado |
| 401 | `invalid_totp_code` | Código TOTP ou recovery code inválido |

---

### `POST /api/auth/totp/setup`

Inicia configuração do 2FA. Requer autenticação.

**Request**: sem body.

**Response 200**:
```json
{
  "qr_code_uri": "otpauth://totp/OmniDesk:maria@clinica.com?secret=BASE32SECRET&issuer=OmniDesk",
  "secret": "BASE32SECRET"
}
```
*(o secret em Base32 é exibido como fallback para entrada manual)*

---

### `POST /api/auth/totp/confirm`

Confirma código válido e ativa o 2FA. Requer autenticação.

**Request**:
```json
{
  "code": "123456"
}
```

**Response 200**:
```json
{
  "recovery_codes": [
    "A3BX-9K2M",
    "F7YQ-4NW1",
    "R2PC-8LT5",
    "H6ZD-3MV9",
    "J4KS-7QE2",
    "N9XB-5RU6",
    "W1FG-2PJ8",
    "C8TH-6NY4"
  ]
}
```

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 400 | `invalid_totp_code` | Código TOTP inválido |
| 409 | `totp_already_enabled` | 2FA já estava ativo |

---

### `DELETE /api/auth/totp`

Desativa o 2FA. Requer autenticação + senha atual.

**Request**:
```json
{
  "password": "minhasenha123"
}
```

**Response 204**: sem body.

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 401 | `invalid_password` | Senha incorreta |
| 409 | `totp_not_enabled` | 2FA não estava ativo |

---

## Convites

### `POST /api/auth/invite`

Envia convite para novo usuário. Requer autenticação (tenant_admin ou supervisor).

**Request**:
```json
{
  "email": "novo@clinica.com",
  "role": "attendant"
}
```

**Response 201**:
```json
{
  "id": "uuid-do-convite",
  "email": "novo@clinica.com",
  "role": "attendant",
  "expires_at": "2026-05-08T12:00:00Z"
}
```

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 400 | `invalid_role` | Role não permitida para convite |
| 403 | `forbidden` | Role do solicitante não pode convidar |
| 409 | `user_already_exists` | E-mail já cadastrado |

---

### `POST /api/auth/accept-invite`

Aceita o convite e cria o usuário.

**Request**:
```json
{
  "token": "<token-do-email>",
  "name": "João da Silva",
  "password": "senhasegura123"
}
```

**Response 200** — igual ao login sem 2FA (access token + cookie refresh).

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 400 | `invalid_token` | Token inválido, expirado ou já aceito |
| 400 | `password_too_short` | Senha menor que 8 caracteres |

---

## Recuperação de Senha

### `POST /api/auth/forgot-password`

Solicita link de recuperação. Requer token Turnstile.

**Request**:
```json
{
  "email": "maria@clinica.com",
  "turnstile_token": "<token-do-widget>"
}
```

**Response 200**:
```json
{
  "message": "Se o e-mail estiver cadastrado, você receberá um link em breve."
}
```
*(mesma resposta independente de o e-mail existir)*

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 403 | `turnstile_failed` | Verificação anti-bot falhou |
| 429 | `rate_limit_exceeded` | Rate limit atingido |

---

### `POST /api/auth/reset-password`

Redefine a senha usando o token do e-mail.

**Request**:
```json
{
  "token": "<token-do-email>",
  "new_password": "novasenha456"
}
```

**Response 204**: sem body. Todas as sessões do usuário são revogadas.

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 400 | `invalid_token` | Token inválido, expirado ou já utilizado |
| 400 | `password_too_short` | Senha menor que 8 caracteres |

---

## Sessões

### `GET /api/auth/sessions`

Lista sessões ativas do usuário autenticado.

**Response 200**:
```json
{
  "sessions": [
    {
      "id": "uuid",
      "user_agent": "Mozilla/5.0 (Macintosh...)",
      "ip_address": "179.33.44.55",
      "created_at": "2026-05-01T10:00:00Z",
      "is_current": true
    }
  ]
}
```

---

### `DELETE /api/auth/sessions/{id}`

Encerra sessão específica. Requer autenticação.

**Response 204**: sem body.

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 404 | `session_not_found` | Sessão não encontrada ou não pertence ao usuário |

---

### `DELETE /api/auth/sessions`

Encerra todas as sessões exceto a atual. Requer autenticação.

**Response 204**: sem body.

---

## Perfil

### `GET /api/me`

Retorna dados do usuário autenticado.

**Response 200**:
```json
{
  "id": "uuid",
  "email": "maria@clinica.com",
  "name": "Maria Silva",
  "role": "attendant",
  "tenant_slug": "clinica-abc",
  "totp_enabled": false,
  "last_login_at": "2026-05-05T09:30:00Z",
  "created_at": "2026-01-15T00:00:00Z"
}
```

---

### `PUT /api/me`

Atualiza nome do usuário autenticado.

**Request**:
```json
{
  "name": "Maria Oliveira Silva"
}
```

**Response 200** — mesma estrutura do `GET /api/me`.

---

### `PUT /api/me/password`

Troca a senha. Requer a senha atual.

**Request**:
```json
{
  "current_password": "senhaatual123",
  "new_password": "novasenha456"
}
```

**Response 204**: sem body.

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 401 | `invalid_password` | Senha atual incorreta |
| 400 | `password_too_short` | Nova senha menor que 8 caracteres |

---

## Admin SaaS

### `POST /api/admin/tenants/{slug}/impersonate`

Gera token de impersonation. Exclusivo para `saas_admin`.

**Request**: sem body.

**Response 200**:
```json
{
  "impersonation_token": "<jwt-5min>",
  "expires_at": "2026-05-05T14:05:00Z",
  "redirect_url": "https://clinica-abc.omnideskcrm.com.br/impersonate?token=<jwt-5min>"
}
```

**Erros**:
| Status | Código | Condição |
|--------|--------|----------|
| 403 | `forbidden` | Usuário não é `saas_admin` |
| 404 | `tenant_not_found` | Slug não existe |

---

## Padrões Gerais

### ProblemDetails (erros)

```json
{
  "type": "https://omnideskcrm.com.br/errors/invalid_credentials",
  "title": "Credenciais inválidas",
  "status": 401,
  "detail": "E-mail ou senha incorretos.",
  "traceId": "00-abc123..."
}
```

### Autenticação de endpoints protegidos

Bearer token no header:
```
Authorization: Bearer <access_token>
```

### Roles por endpoint

| Endpoint | Roles permitidas |
|---|---|
| `POST /api/auth/login` | qualquer (sem auth) |
| `POST /api/auth/refresh` | qualquer (via cookie) |
| `POST /api/auth/logout` | qualquer (via cookie) |
| `POST /api/auth/invite` | `tenant_admin`, `supervisor` |
| `POST /api/auth/accept-invite` | qualquer (sem auth) |
| `POST /api/auth/forgot-password` | qualquer (sem auth) |
| `POST /api/auth/reset-password` | qualquer (sem auth) |
| `GET/DELETE /api/auth/sessions` | autenticado |
| `GET/PUT /api/me` | autenticado |
| `PUT /api/me/password` | autenticado |
| `POST /api/auth/totp/*` | autenticado (exceto verify) |
| `POST /api/admin/tenants/{slug}/impersonate` | `saas_admin` |
