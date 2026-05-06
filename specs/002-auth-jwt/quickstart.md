# Quickstart: Verificação de Autenticação

**Branch**: `002-auth-jwt` | **Data**: 2026-05-05

> Cenários de verificação para validar que a implementação satisfaz os critérios de aceite.
> Execute cada cenário após o `/speckit-implement` antes de abrir o PR.

---

## Pré-requisitos

- API rodando em `http://localhost:5000`
- PostgreSQL com migrations aplicadas (`dotnet ef database update`)
- Usuário de teste criado diretamente no banco (ver seed abaixo)
- `TURNSTILE_SECRET_KEY` configurada com a chave de **teste** do Cloudflare (sempre aprova)
- `JWT_PRIVATE_KEY_PEM` e `JWT_PUBLIC_KEY_PEM` configuradas

**Seed de teste** (inserir diretamente no banco):
```sql
-- senha: Test@12345 (hash Argon2id gerado pela própria API no seed)
INSERT INTO public.users (id, email, password_hash, role, is_active, email_verified)
VALUES (
  'a0000000-0000-0000-0000-000000000001',
  'teste@omnidesk.dev',
  '<hash-gerado>',
  'tenant_admin',
  true,
  true
);
```

---

## Cenário 1 — Login bem-sucedido (sem 2FA)

```bash
curl -c cookies.txt -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "teste@omnidesk.dev",
    "password": "Test@12345",
    "remember_me": false,
    "turnstile_token": "XXXX.DUMMY.TOKEN.XXXX"
  }'
```

**Esperado**:
- Status `200`
- Body: `{ "access_token": "<jwt>", "user": { "role": "tenant_admin", ... } }`
- Cookie `refresh_token` definido na resposta (HttpOnly)

**Verificar JWT** (decodificar payload):
```bash
echo "<jwt>" | cut -d. -f2 | base64 -d 2>/dev/null | python3 -m json.tool
```
Deve conter: `sub`, `role`, `tenant_id`, `email`, `exp` (now + 900 segundos).

---

## Cenário 2 — Renovação automática de token

```bash
# Usando o cookie salvo no Cenário 1
curl -b cookies.txt -c cookies.txt -X POST http://localhost:5000/api/auth/refresh
```

**Esperado**:
- Status `200`
- Body: `{ "access_token": "<novo-jwt>" }`
- Cookie `refresh_token` atualizado (novo valor)
- O token anterior foi revogado no banco: `SELECT revoked FROM refresh_tokens ORDER BY created_at DESC LIMIT 2;`

---

## Cenário 3 — Reuse Detection (segurança crítica)

```bash
# 1. Capturar o valor do refresh token antes da renovação
OLD_TOKEN=$(grep refresh_token cookies.txt | awk '{print $NF}')

# 2. Renovar uma vez (invalida o token antigo)
curl -b cookies.txt -c cookies.txt -X POST http://localhost:5000/api/auth/refresh

# 3. Tentar usar o token antigo manualmente
curl -b "refresh_token=$OLD_TOKEN" -X POST http://localhost:5000/api/auth/refresh
```

**Esperado no passo 3**:
- Status `401`, código `token_revoked`
- TODAS as sessões do usuário devem estar revogadas:
  ```sql
  SELECT COUNT(*) FROM refresh_tokens
  WHERE user_id = 'a0000000-...' AND revoked = false;
  -- deve retornar 0
  ```

---

## Cenário 4 — Logout

```bash
curl -b cookies.txt -X POST http://localhost:5000/api/auth/logout
```

**Esperado**:
- Status `204`
- Cookie `refresh_token` removido na resposta (`Max-Age=0`)
- Após logout, tentar refresh deve retornar `401`

---

## Cenário 5 — Rate Limiting

```bash
# Executar 6 tentativas com senha errada
for i in {1..6}; do
  curl -s -o /dev/null -w "Tentativa $i: %{http_code}\n" \
    -X POST http://localhost:5000/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"email":"teste@omnidesk.dev","password":"errada","remember_me":false,"turnstile_token":"XXXX.DUMMY.TOKEN.XXXX"}'
done
```

**Esperado**:
- Tentativas 1–5: `401`
- Tentativa 6: `429` (rate limit atingido)

---

## Cenário 6 — Recuperação de Senha

```bash
# 1. Solicitar reset
curl -X POST http://localhost:5000/api/auth/forgot-password \
  -H "Content-Type: application/json" \
  -d '{"email":"teste@omnidesk.dev","turnstile_token":"XXXX.DUMMY.TOKEN.XXXX"}'
```

**Esperado**: Status `200`, mesma mensagem genérica.

```bash
# 2. Buscar token no banco (simulando e-mail)
TOKEN_HASH=$(psql $DATABASE_URL -t -c "
  SELECT encode(decode(token_hash, 'hex'), 'hex')
  FROM password_reset_tokens
  WHERE user_id = 'a0000000-...'
  ORDER BY created_at DESC LIMIT 1;" | xargs)
# O token real está no e-mail — em testes, buscar o raw token via log ou test double
```

```bash
# 3. Redefinir senha
curl -X POST http://localhost:5000/api/auth/reset-password \
  -H "Content-Type: application/json" \
  -d '{"token":"<raw-token-do-email>","new_password":"NovaSenha@789"}'
```

**Esperado**:
- Status `204`
- Sessões revogadas: `SELECT COUNT(*) FROM refresh_tokens WHERE user_id = '...' AND revoked = false;` → `0`
- Login com nova senha funciona, com senha antiga retorna `401`

---

## Cenário 7 — Convite de Usuário

```bash
# 1. Autenticar como tenant_admin
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"teste@omnidesk.dev","password":"Test@12345","remember_me":false,"turnstile_token":"XXXX.DUMMY.TOKEN.XXXX"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['access_token'])")

# 2. Enviar convite
curl -X POST http://localhost:5000/api/auth/invite \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"email":"novo@clinica.com","role":"attendant"}'
```

**Esperado**: Status `201`, body com `id`, `email`, `expires_at`.

```bash
# 3. Aceitar convite (token do e-mail)
curl -c cookies2.txt -X POST http://localhost:5000/api/auth/accept-invite \
  -H "Content-Type: application/json" \
  -d '{"token":"<token-do-email>","name":"Novo Atendente","password":"Senha@123"}'
```

**Esperado**: Status `200`, access token + cookie refresh. Verificar no banco:
```sql
SELECT email_verified FROM users WHERE email = 'novo@clinica.com';
-- deve ser true
```

---

## Cenário 8 — Ativação e Uso do 2FA

```bash
# 1. Iniciar setup (usuário autenticado)
curl -X POST http://localhost:5000/api/auth/totp/setup \
  -H "Authorization: Bearer $TOKEN"
# Resposta: { "qr_code_uri": "otpauth://...", "secret": "BASE32SECRET" }

# 2. Gerar código TOTP a partir do secret (via oathtool ou app)
CODE=$(oathtool --totp --base32 "BASE32SECRET")

# 3. Confirmar ativação
curl -X POST http://localhost:5000/api/auth/totp/confirm \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"code\":\"$CODE\"}"
# Resposta: { "recovery_codes": [...8 códigos...] }

# 4. Fazer login — deve exigir TOTP
RESP=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"teste@omnidesk.dev","password":"Test@12345","remember_me":false,"turnstile_token":"XXXX.DUMMY.TOKEN.XXXX"}')
echo $RESP | python3 -m json.tool
# Esperado: { "requires_totp": true, "totp_session_token": "..." }

TOTP_SESSION=$(echo $RESP | python3 -c "import sys,json; print(json.load(sys.stdin)['totp_session_token'])")
CODE2=$(oathtool --totp --base32 "BASE32SECRET")

# 5. Verificar código e completar login
curl -c cookies.txt -X POST http://localhost:5000/api/auth/totp/verify \
  -H "Content-Type: application/json" \
  -d "{\"totp_session_token\":\"$TOTP_SESSION\",\"code\":\"$CODE2\"}"
# Esperado: access token + cookie refresh
```

---

## Cenário 9 — Impersonation (saas_admin)

```bash
# 1. Login como saas_admin (usuário seed separado com role=saas_admin)
ADMIN_TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@omnidesk.dev","password":"Admin@12345","remember_me":false,"turnstile_token":"XXXX.DUMMY.TOKEN.XXXX"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['access_token'])")

# 2. Solicitar impersonation
curl -X POST http://localhost:5000/api/admin/tenants/clinica-abc/impersonate \
  -H "Authorization: Bearer $ADMIN_TOKEN"
# Esperado: { "impersonation_token": "<jwt-5min>", "redirect_url": "..." }

# 3. Decodificar o token e verificar claims
echo "<impersonation_token>" | cut -d. -f2 | base64 -d 2>/dev/null | python3 -m json.tool
# Deve conter: "impersonation": true, "impersonated_by": "<saas_admin_id>", exp = now+300
```

---

## Checklist de Verificação Final

- [ ] Login retorna JWT com claims corretos (sub, role, tenant_slug, exp)
- [ ] Cookie refresh_token presente e HttpOnly (verificar em DevTools → Application → Cookies)
- [ ] Access token NÃO aparece em localStorage nem sessionStorage
- [ ] Renovação automática funciona e revoga token anterior
- [ ] Reuse detection invalida todas as sessões do usuário
- [ ] Rate limiting bloqueia na 6ª tentativa
- [ ] Turnstile: request sem token retorna 403
- [ ] Recuperação de senha invalida todas as sessões ao ser usada
- [ ] Convite expira após 72h (verificar `expires_at` no banco)
- [ ] 2FA: login exige código TOTP após ativação
- [ ] 2FA: recovery code funciona e é invalidado após uso
- [ ] Impersonation: token expira em 5 min, sem refresh emitido
- [ ] Usuário inativo retorna 401 no login
- [ ] Usuário com email_verified=false retorna 401 no login
