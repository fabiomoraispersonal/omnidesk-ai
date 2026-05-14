# Research: Auditoria e Observabilidade

**Feature**: `012-audit-observabilidade`
**Date**: 2026-05-13

---

## Decisão 1: Estratégia de indexação no MongoDB para audit logs

**Decisão**: Três índices compostos por tenant + TTL index removido em favor de job Hangfire.

**Índices criados na collection `{tenant_slug}_audit_logs`:**
```
{ tenant_slug: 1, timestamp: -1 }           -- principal (listagem e range queries)
{ tenant_slug: 1, event: 1, timestamp: -1 } -- filtro por tipo de evento
{ tenant_slug: 1, "actor.user_id": 1, timestamp: -1 } -- filtro por ator
```

**Rationale**: O padrão de acesso é sempre `tenant_slug + timestamp range`. Filtros de `event` e `actor_id` são secundários mas frequentes. O prefixo `tenant_slug` em todos os índices garante que nenhuma query escaneia dados de outros tenants.

**Por que Hangfire em vez de TTL index**: O TTL index do MongoDB roda a cada 60 segundos em background e é impreciso (pode atrasar horas sob carga). O spec exige controle explícito ("job mensal"), e o Hangfire já está na stack. O job garante execução confirmada e log de resultado.

**Alternativas consideradas**:
- TTL index nativo: descartado pela imprecisão e falta de log de execução
- Cron externo: descartado — Hangfire já disponível e preferido pela constituição

---

## Decisão 2: Estratégia de geração e armazenamento de API Keys

**Decisão**: `RandomNumberGenerator.GetBytes(32)` → Base64Url → prefixo `omni_` → SHA-256 hex-encoded para storage.

**Formato da chave bruta**: `omni_<43-chars-base64url>` (ex: `omni_aB3xZ9...`)
**Storage**: `key_hash = SHA256(raw_key).ToHexString()` — raw key descartada após exibição.

**Rationale**: 32 bytes = 256 bits de entropia — impossível de bruteforce. Base64url evita caracteres especiais problemáticos em headers HTTP. Prefixo `omni_` permite identificação visual imediata. SHA-256 sem salt é adequado para API Keys de alta entropia (ao contrário de senhas de baixa entropia que precisam de bcrypt).

**Autenticação via X-Api-Key**: `ApiKeyAuthenticationHandler` (IAuthenticationHandler) busca a key pelo hash, verifica `revoked = false` e `expires_at` (se preenchido). Atualiza `last_used_at` de forma assíncrona não-bloqueante (fire-and-forget) para não impactar latência do request.

**Alternativas consideradas**:
- bcrypt para hash: descartado — desnecessário para chaves de alta entropia, adiciona latência
- HMAC: descartado — sem benefício adicional para este caso de uso

---

## Decisão 3: Integração do IAuditService como cross-cutting concern

**Decisão**: Interface `IAuditService` injetada explicitamente nos handlers que geram eventos auditáveis. Eventos de autenticação capturados em `AuthEndpoints.cs` e `TenantMiddleware`. Fire-and-forget para não bloquear o response.

```csharp
// Padrão de uso em qualquer handler:
await _auditService.LogAsync(new AuditEvent
{
    Event = AuditEventNames.TicketStatusChanged,
    TenantSlug = tenant.Slug,
    TenantId = tenant.Id,
    Actor = AuditActor.FromHttpContext(httpContext),
    Target = AuditTarget.FromTicket(ticket),
    Metadata = new { from = oldStatus, to = newStatus }
});
```

**Rationale**: Minimal API sem MediatR — pipeline behavior não disponível. Chamada explícita é direta, testável e sem indireção. Fire-and-forget via `Task.Run` + logging de falhas garante que falha no audit não quebra a operação principal.

**AuditActor.FromHttpContext**: extrai `user_id`, `name`, `role` e `impersonated_by` das claims do JWT. Para eventos de background (ex: `appointment.no_show`), usa `AuditActor.System()` sem `ip_address`/`user_agent`.

**Alternativas consideradas**:
- Middleware global: descartado — captura muito noise, difícil filtrar os 29 eventos específicos
- Domain Events + Handler: descartado — over-engineering para Minimal API sem MediatR
- Hangfire background para cada log: descartado — latência desnecessária, MongoDB write é sub-ms

---

## Decisão 4: Dual authentication no endpoint GET /api/audit-logs

**Decisão**: O endpoint aceita JWT Bearer (CRM UI, role `tenant_admin`) OU header `X-Api-Key` (ferramentas externas). Implementado como dois authentication schemes com policy combinada.

```csharp
// Program.cs
builder.Services.AddAuthentication()
    .AddJwtBearer("jwt", ...)
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthenticationHandler>("apikey", ...);

// Endpoint
group.MapGet("/", GetAuditLogs)
    .RequireAuthorization(policy => policy
        .AddAuthenticationSchemes("jwt", "apikey")
        .RequireAuthenticatedUser());
```

**Rationale**: Reutilizar um endpoint para dois consumers reduz duplicação. O context da autenticação (JWT vs API Key) determina o tenant para isolamento — ambos os schemes populam um `TenantId` claim no HttpContext. Sem overhead de endpoint duplicado.

**Alternativas consideradas**:
- Endpoint separado `/api/external/audit-logs`: descartado — duplicação desnecessária
- Passar API Key como query param: descartado — expõe chave em logs de servidor e histórico do browser

---

## Decisão 5: Estrutura MongoDB — collection por tenant vs campo discriminador

**Decisão**: Collection separada por tenant: `{tenant_slug}_audit_logs`.

**Rationale**: Consistente com o padrão já estabelecido na constituição (Princípio I) e com o padrão de `{tenant_slug}_events` e `{tenant_slug}_messages_raw` já existentes. Permite índices otimizados por collection (sem `tenant_slug` no índice = menos espaço). Facilita eventual migração de tenant para instância dedicada.

**Alternativas consideradas**:
- Collection única com campo `tenant_slug`: descartado — incompatível com a constituição e com padrões existentes

---

## Decisão 6: Isolamento da migration EF Core para api_keys

**Decisão**: Migration com timestamp no nome: `{timestamp}_AddApiKeys`. Entidade `ApiKey` pertence ao `TenantDbContext` (schema dinâmico `tenant_{slug}`).

**Rationale**: Todos os dados de API Keys são por-tenant. O schema `public` é reservado para dados de sistema. Consistente com o padrão de migration do projeto (timestamp evita conflitos entre branches paralelas).
