# Contract: Token de Impersonation

Define o formato, claims, ciclo de vida e regras de uso do token usado pelo `saas_admin` para acessar temporariamente o CRM de um tenant.

---

## Endpoint emissor

```
POST /admin/tenants/{slug}/impersonation
```

- **Autenticação**: requer access token regular do `saas_admin` (Painel Admin).
- **Authorization**: policy `PainelAdmin.Access`.
- **Body**: vazio.
- **Response 200**:

```json
{
  "token": "eyJhbGciOiJSUzI1NiIs...",
  "tokenType": "Bearer",
  "expiresIn": 300,
  "tenantSlug": "clinica-x",
  "redirectUrl": "https://clinica-x.omnicare.ia.br/?impersonation=1"
}
```

- **Response 404**: tenant não encontrado.
- **Response 423**: tenant bloqueado (Spec 02 governa) — impersonation segue permitida mesmo em tenant bloqueado para fins de suporte (decisão a confirmar com Spec 02; default seguro: permitir).

> Endpoint pertence formalmente à Spec 02 (Painel Admin). Esta spec define apenas o formato do token gerado.

---

## Formato do JWT

| Header | Valor |
|---|---|
| `alg` | `RS256` (mesma chave RSA do access token regular) |
| `typ` | `JWT` |
| `kid` | mesmo `kid` do access token regular |

| Claim | Valor | Notas |
|---|---|---|
| `iss` | `omnidesk-saas` | Distingue o emissor (painel admin) |
| `aud` | `omnidesk-crm` | Validado pelo middleware do CRM |
| `sub` | `saas_admin` | Identifica o operador SaaS |
| `role` | `saas_admin` | Permite ao backend reconhecer o portador |
| `tenant_slug` | `<slug>` | Tenant alvo da impersonation |
| `impersonating` | `true` | Flag única — gatilho do banner e do enricher de auditoria |
| `impersonated_by` | `saas_admin` | Sempre fixo em V1 (única conta saas_admin) |
| `iat` | unix timestamp | Emissão |
| `exp` | `iat + IMPERSONATION_JWT_TTL_SECONDS` | TTL controlado por env (default 300, máx 600) |
| `jti` | UUID v4 | Identificador único do token (logado) |

**Não emitidos**: `email`, `name`, `permissions`, `dept_ids` — token é minimalista.

---

## Validação no backend do CRM

Quando uma requisição chega ao CRM com este token, o middleware (já existente da Spec 002) valida `iss`, `aud`, assinatura, `exp` e popula o `ClaimsPrincipal`. Em seguida, esta spec adiciona:

1. **`ImpersonationContextHandler`** detecta `impersonating: true` e armazena o estado em `ICurrentUser.IsImpersonating = true`.
2. **`ImpersonationAuditEnricher`** (Serilog) injeta `impersonated_by: "saas_admin"` em **todos** os logs estruturados emitidos durante essa requisição.
3. **`TenantResolverMiddleware`** (Spec 003) confirma que o subdomínio do request bate com `tenant_slug` do token; caso contrário, 401.

---

## Comportamento de autorização durante impersonation

- O portador é tratado como **`tenant_admin` do tenant alvo** para fins de policies (`role >= tenant_admin`). Implementação: o `RoleRequirement` reconhece a combinação `(role=saas_admin, impersonating=true)` e a equipara a `tenant_admin` da hierarquia.
- Exceção: ações **destrutivas/altamente sensíveis** podem ser explicitamente bloqueadas durante impersonation. Lista inicial:
  - Convidar novos usuários (`Auth.InviteUser`, `Auth.InviteSupervisor`).
  - Desativar usuários (`Auth.DeactivateUser`).
  - Editar/visualizar Access Token do WhatsApp (`Whatsapp.ViewAccessToken`, `Whatsapp.EditConfig`).

  Implementação: handler customizado `ForbidsDuringImpersonationRequirement` aplicado a essas policies. Mensagem PT-BR clara: "Esta ação não é permitida em modo impersonation."

> A lista de ações bloqueadas durante impersonation é deliberadamente conservadora em V1. Pode ser ampliada via update desta spec sem refatoração estrutural.

---

## Frontend: banner permanente

Componente Angular `<omni-impersonation-banner>` no shell do CRM:

- Renderizado quando `claims.impersonating === true`.
- Texto: "Modo impersonation — você está acessando o CRM de **{tenant_slug}** em nome do operador SaaS. Tempo restante: **MM:SS**."
- Cor de destaque (vermelho/laranja) diferente do tema padrão.
- Não removível pelo usuário; expiração do token remove naturalmente (logout automático).
- Botão "Encerrar agora" — chama `POST /me/logout` (endpoint da Spec 002), volta ao painel admin.

---

## Auditoria (FR-031)

Estrutura mínima do log emitido para cada ação:

```json
{
  "@timestamp": "2026-05-06T13:42:11.123Z",
  "level": "Information",
  "message": "Action {Action} executed by {UserId}",
  "Action": "Departments.Create",
  "UserId": "saas_admin",
  "Role": "saas_admin",
  "TenantSlug": "clinica-x",
  "Impersonating": true,
  "ImpersonatedBy": "saas_admin",
  "Jti": "{jti-do-token}",
  "RequestId": "{trace-id}"
}
```

Sink: MongoDB (Serilog sink já existente, Spec 003). Spec 11 (Auditoria) consome esses logs para a UI do `tenant_admin` que vai listá-los.

---

## Regras invariantes

- `IMPERSONATION_JWT_TTL_SECONDS` lido em startup; valor > 600 ⇒ aplicação falha a inicializar com mensagem clara (refletindo a constituição: ≤ 15 min, e nesta spec ≤ 5 min recomendado).
- Não há refresh: tentar renovar via `POST /auth/refresh` com o token de impersonation retorna 400 — endpoint deve detectar `impersonating: true` e rejeitar.
- `jti` é gravado em log para correlação. Não é necessário lista de revogação — TTL natural cobre.

---

## Testes (Testcontainers)

- Emissão de token com claims corretos.
- Token expirado é rejeitado (limpa `Authorization` e responde 401).
- Token válido + subdomínio errado ⇒ 401 (TenantResolver bloqueia).
- Tentativa de refresh ⇒ 400.
- Ação durante impersonation gera log com `impersonated_by`.
- Ações da lista bloqueada (convite, desativação, ViewAccessToken) ⇒ 403 com mensagem PT-BR específica.
