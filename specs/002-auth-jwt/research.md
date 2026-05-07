# Research: Autenticação

**Branch**: `002-auth-jwt` | **Data**: 2026-05-05

---

## 1. JWT RS256 — Geração e Gerenciamento de Chaves em .NET 10

**Decisão**: Par de chaves RSA 2048-bit gerado fora do código e armazenado como variáveis de ambiente (`JWT_PRIVATE_KEY_PEM`, `JWT_PUBLIC_KEY_PEM`). Carregado na inicialização via `RSA.ImportFromPem()`.

**Rationale**: RS256 (assimétrico) é obrigatório para suportar impersonation (o token gerado no Admin SaaS é validado no CRM do Tenant, que pode conhecer apenas a chave pública). HS256 (simétrico) não permite esse modelo. A chave privada fica exclusivamente no backend; a pública pode ser exposta via JWKS endpoint se necessário no futuro.

**Alternativas consideradas**:
- HS256: rejeitado — chave simétrica impede modelo de impersonation cross-domain seguro.
- Certificado X.509 em arquivo: rejeitado — mais complexo para CI/CD sem ganho real em ambientes Docker com secrets.

**Implementação**:
```csharp
// Program.cs
var rsa = RSA.Create();
rsa.ImportFromPem(Environment.GetEnvironmentVariable("JWT_PRIVATE_KEY_PEM"));
var signingKey = new RsaSecurityKey(rsa);
// Configurar JwtBearerOptions com signingKey
```

**Rotação de chave**: Como access tokens duram 15 min, rotação de chave RSA pode ser feita com downtime mínimo (substituir env var + reiniciar container). JWKS rotation não é necessária na V1.

---

## 2. Argon2id — Parâmetros e Implementação

**Decisão**: Biblioteca `Konscious.Security.Cryptography.Argon2` (NuGet). Parâmetros: memória 65.536 KB (64 MB), iterações 3, paralelismo 1. Salt: 16 bytes aleatórios via `RandomNumberGenerator`. Hash output: 32 bytes.

**Rationale**: Parâmetros atendem o mínimo exigido pelo spec (memory ≥ 64MB, iterations ≥ 3). Paralelismo 1 é adequado para ambiente ARM64 single-threaded por operação — evita contenção sob carga.

**Alternativas consideradas**:
- `BCrypt.Net-Next`: rejeitado — BCrypt tem limite de 72 bytes de senha e não é recomendado para novos sistemas.
- `Microsoft.AspNetCore.Identity` PasswordHasher: rejeitado — usa PBKDF2, inferior ao Argon2id para proteção offline.

**Formato de armazenamento** (PHC string):
```
$argon2id$v=19$m=65536,t=3,p=1$<salt_base64>$<hash_base64>
```

---

## 3. Refresh Token — Rotação e Detecção de Reutilização

**Decisão**: Token opaco (UUID v4) gerado via `Guid.NewGuid()`. Armazenado no cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`. No banco: hash SHA-256 do token em `refresh_tokens.token_hash`.

**Fluxo de rotação**:
1. Recebe cookie com refresh token opaco
2. Calcula SHA-256 → busca no banco
3. Se `revoked=true`: revoga TODAS as sessões do `user_id` + retorna 401 (reuse detection)
4. Se expirado: retorna 401 sem revogar
5. Se válido: marca linha como `revoked=true`, cria nova linha, emite novo cookie + novo access token

**Rationale**: SHA-256 para hash do token é suficiente — UUID v4 tem 122 bits de entropia, tornando força-bruta computacionalmente inviável. Armazenar o raw token apenas no cookie (nunca no banco) elimina a exposição em caso de vazamento do banco.

**Alternativas consideradas**:
- Token JWT como refresh token: rejeitado — JWT refresh tokens não podem ser revogados sem lista de revogação (negaria o modelo stateless da renovação).

---

## 4. TOTP Session Token (intermediário pós-login com 2FA)

**Decisão**: JWT de curta duração (5 minutos) assinado com a mesma chave RSA privada, com claims extras `{ "type": "totp_session", "sub": "<user_id>" }`. Stateless — sem armazenamento em Redis.

**Rationale**: Evita estado adicional no servidor. 5 minutos é tempo suficiente para o usuário inserir o código TOTP. O endpoint `POST /api/auth/totp/verify` valida a assinatura e o claim `type=totp_session` antes de emitir os tokens definitivos.

**Segurança**: O token não concede acesso a nenhum recurso além do endpoint `/api/auth/totp/verify` — o endpoint verifica explicitamente o claim `type`.

**Alternativas consideradas**:
- Redis com chave temporária: rejeitado — adiciona dependência desnecessária para um estado muito simples.

---

## 5. TOTP Secret — Criptografia em Repouso

**Decisão**: AES-256-GCM com chave derivada de variável de ambiente `TOTP_ENCRYPTION_KEY` (32 bytes em Base64). Cada secret é cifrado com nonce único (12 bytes aleatórios) e armazenado como `<nonce_hex>:<ciphertext_hex>` em `users.totp_secret`.

**Rationale**: AES-256-GCM é autenticado (detecta adulteração), determinístico para descriptografia e portável — não depende de APIs específicas do .NET (`DataProtection` vincula ao ambiente de execução, dificultando rotação de chave manual).

**Alternativas consideradas**:
- `Microsoft.AspNetCore.DataProtection`: rejeitado — chaves são geradas automaticamente e vinculadas ao ambiente, tornando rotação explícita e migração entre containers complexas.
- Armazenamento em texto plano com backup seguro: rejeitado — viola FR-030 e Princípio IV da constituição.

---

## 6. Rate Limiting — Login e Forgot-Password

**Decisão**: `Microsoft.AspNetCore.RateLimiting` (built-in .NET 8+) com `SlidingWindowRateLimiter`. Chave composta: `SHA256(ip + ":" + email)` para evitar armazenar PII no estado do limiter. Janela: 10 minutos, limite: 5 requisições.

**Rationale**: Built-in elimina dependência externa. Sliding window é mais justo que fixed window para o comportamento de "5 tentativas em 10 minutos". Hashing da chave protege PII em logs e dumps de memória.

**Estado distribuído**: Em V1, instância única → in-memory é suficiente. Redis BackedRateLimiter pode ser adicionado em V2 se múltiplas instâncias forem necessárias.

**Alternativas consideradas**:
- Polly `RateLimiter`: rejeitado — sobreposto com `Microsoft.AspNetCore.RateLimiting`.
- Nginx rate limiting via Cloudflare: rejeitado — não permite granularidade por e-mail (só por IP).

---

## 7. Recovery Codes para 2FA

**Decisão**: 8 códigos de 8 caracteres alfanuméricos (A-Z, 2-9, excluindo caracteres ambíguos 0/O, 1/I/L). Gerados via `RandomNumberGenerator`. Cada código armazenado como hash SHA-256 na tabela `public.totp_recovery_codes`. Exibidos ao usuário uma única vez.

**Rationale**: Formato legível e digitável (sem confusão de caracteres). SHA-256 suficiente — o código tem 37 bits de entropia (8 chars × log2(32)), que protege adequadamente contra força-bruta (cada tentativa exige um request HTTP).

**Alternativas consideradas**:
- Códigos numéricos longos (16 dígitos): menos amigáveis para digitação manual.
- Bcrypt para hash dos códigos: desnecessário — o valor de rate limiting do endpoint protege contra força-bruta.

---

## 8. Angular — Auth State e Token em Memória

**Decisão**: `AuthService` com `BehaviorSubject<string | null>` para o access token. `HttpInterceptor` global para anexar `Authorization: Bearer <token>`. `AuthGuard` e `RoleGuard` para proteção de rotas. Na inicialização da app, chamar `POST /api/auth/refresh` para restaurar sessão a partir do cookie.

**Rationale**: Token exclusivamente em memória (nunca `localStorage`/`sessionStorage`) — conform Princípio IV e SC-004. O cookie `HttpOnly` é invisível ao JavaScript, portanto o refresh inicial via endpoint é o único mecanismo de restauração de sessão.

**Fluxo de inicialização**:
1. App bootstrap → `AuthService.restoreSession()` → `POST /api/auth/refresh`
2. Sucesso → armazena access token em memória → permite navegação
3. Falha (401) → redireciona para `/login`

**Interceptor de renovação automática**: O interceptor captura respostas `401` de rotas protegidas e tenta `POST /api/auth/refresh` automaticamente antes de repetir a requisição original (max 1 retry).

**Alternativas consideradas**:
- `sessionStorage`: rejeitado — persiste em tab duplicada, viola "apenas memória".
- Cookies para access token: rejeitado — requer `SameSite=None` para requests cross-origin e complica CSRF.

---

## 9. SendGrid — Emails Transacionais

**Decisão**: Pacote `SendGrid` (NuGet oficial). Interface `IEmailService` com método `SendAsync(to, subject, htmlBody)`. Implementação `SendGridEmailService`. `SENDGRID_API_KEY` via variável de ambiente. Templates inline HTML (sem Dynamic Templates do SendGrid em V1).

**Rationale**: Simplicidade na V1. Templates inline evitam dependência do painel SendGrid para mudanças de conteúdo.

**Emails necessários**:
- Convite de usuário: assunto "Você foi convidado para o OmniDesk", corpo com link de aceite
- Recuperação de senha: assunto "Redefinir sua senha OmniDesk", corpo com link de reset

---

## 10. Impersonation — Abordagem JWT

**Decisão**: Access token especial com claims `{ "impersonation": true, "impersonated_by": "<saas_admin_id>", "role": "tenant_admin", "tenant_id": "...", "tenant_slug": "..." }` e expiração de 5 minutos. **Sem refresh token emitido**. O token é retornado no body (não em cookie) para que o frontend Admin possa passá-lo para o CRM via redirect com query param seguro.

**Fluxo**:
1. saas_admin chama `POST /api/admin/tenants/{slug}/impersonate`
2. API retorna `{ "impersonation_token": "<jwt>" }`
3. Admin frontend redireciona para `{slug}.omnicare.ia.br/impersonate?token=<jwt>`
4. CRM valida o token (mesma chave pública RSA), detecta `impersonation: true`, armazena em memória
5. Middleware de auditoria adiciona `impersonated_by` em todos os logs enquanto `impersonation: true` estiver no token

**Rationale**: Passar o token via query param é aceitável porque: (1) Cloudflare Pages usa HTTPS, (2) o token expira em 5 min, (3) não há alternativa limpa para cross-domain token handoff sem risco de CORS ou redirect loops.

**Risco mitigado**: Query params aparecem em logs de servidor. Mitigação: Cloudflare Workers pode remover o param do log após validação, ou o CRM pode limpar o URL imediatamente via `history.replaceState()`.

**Alternativas consideradas**:
- Cookie cross-domain: impossível com `SameSite=Strict`.
- Troca via endpoint intermediário (token exchange): mais complexo sem ganho real em V1.
