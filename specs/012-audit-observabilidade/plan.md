# Implementation Plan: Auditoria e Observabilidade

**Branch**: `012-audit-observabilidade` | **Date**: 2026-05-13 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/012-audit-observabilidade/spec.md`

---

## Summary

Implementar um sistema de auditoria leve para o OmniDesk que registra automaticamente 29 eventos críticos em MongoDB por tenant, expõe esses logs via API REST autenticada por API Key (para ferramentas externas como Metabase) e por JWT (para a interface CRM), e fornece uma UI mínima de "Atividade Recente" para `tenant_admin`. Inclui gestão de API Keys e job mensal de retenção (12 meses) via Hangfire.

---

## Technical Context

**Language/Version**: C# .NET 10 (API) + TypeScript/Angular 21 (CRM frontend)
**Primary Dependencies**: MongoDB.Driver 3.x, Hangfire + Redis, EF Core 10, PrimeNG 21, xUnit + Testcontainers
**Storage**: PostgreSQL `tenant_{slug}` schema (entidade `api_keys`) + MongoDB collection `{tenant_slug}_audit_logs`
**Testing**: xUnit + Testcontainers MongoDB/PostgreSQL (backend) + Karma/Jasmine .spec.ts co-localizados (frontend)
**Target Platform**: Linux ARM64 (Oracle Cloud) + Cloudflare Pages (frontend CRM)
**Project Type**: Módulo de API (Minimal API) + Feature de CRM Angular
**Performance Goals**: Queries filtradasde audit logs < 1s para janela de 30 dias; página CRM "Atividade Recente" carrega < 2s para até 10.000 eventos
**Constraints**: Logs imutáveis (sem UPDATE/DELETE via API); API Key bruta nunca recuperável após criação; isolamento total por tenant; máximo 5 API Keys ativas por tenant
**Scale/Scope**: Uma collection MongoDB por tenant; 29 tipos de evento auditados; estimativa de centenas a poucos milhares de eventos/dia por tenant

---

## Constitution Check

*GATE: Obrigatório antes de iniciar Phase 0. Re-verificado após Phase 1.*

| Princípio | Status | Evidência |
|---|---|---|
| I. Multi-Tenant Isolation (NON-NEG) | ✅ PASS | `audit_logs` em `{tenant_slug}_audit_logs` (MongoDB); `api_keys` no schema `tenant_{slug}` (PostgreSQL); todo query filtra por `tenant_slug` |
| II. AI-First | ✅ N/A | Feature interna de observabilidade — sem conversa com cliente |
| III. Channel Agnosticism | ✅ N/A | Feature não toca pipeline de mensagens |
| IV. Security/LGPD (NON-NEG) | ✅ PASS | API Key armazenada como SHA-256 hash (raw key nunca persiste); `impersonated_by` obrigatório para rastreabilidade LGPD; endpoint requer autenticação; nenhum dado sensível logado |
| V. Simplicity | ✅ PASS | Scope mínimo: sem export, sem gráficos, sem real-time; UI é apenas lista paginada; sem padrões não-padronizados |
| VI. Observability | ✅ PASS | Este módulo é a implementação direta do Princípio VI |
| VII. Test Discipline | ✅ PASS | Testcontainers para MongoDB e PostgreSQL; `.spec.ts` co-localizados para Angular; contract tests antes de integration tests |

**Resultado: GATE APROVADO — implementação pode prosseguir.**

---

## Project Structure

### Documentation (this feature)

```text
specs/012-audit-observabilidade/
├── plan.md              ← este arquivo
├── research.md          ← decisões de arquitetura e padrões
├── data-model.md        ← entidades AuditLog e ApiKey
├── quickstart.md        ← verificação local do sistema
├── contracts/
│   ├── audit-logs.md    ← GET /api/audit-logs
│   └── api-keys.md      ← CRUD /api/api-keys
└── tasks.md             ← gerado por /speckit-tasks
```

### Source Code (repository root)

```text
src/omniDesk.Api/
├── Domain/
│   └── Audit/
│       ├── AuditLog.cs              ← MongoDB document model
│       ├── AuditEventNames.cs       ← constantes dos 29 eventos (sem magic strings)
│       └── ApiKey.cs                ← EF Core entity (tenant schema)
├── Features/
│   ├── Audit/
│   │   ├── AuditEndpoints.cs        ← GET /api/audit-logs (JWT ou API Key)
│   │   ├── GetAuditLogsHandler.cs
│   │   ├── AuditLogDto.cs
│   │   └── AuditLogFilters.cs       ← record com parâmetros de query
│   └── ApiKeys/
│       ├── ApiKeyEndpoints.cs       ← CRUD /api/api-keys (JWT tenant_admin)
│       ├── CreateApiKeyHandler.cs
│       ├── ListApiKeysHandler.cs
│       ├── RevokeApiKeyHandler.cs
│       └── ApiKeyDtos.cs
├── Infrastructure/
│   └── Audit/
│       ├── IAuditService.cs         ← interface para injeção nos handlers existentes
│       ├── AuditService.cs          ← implementação (fire-and-forget async)
│       ├── AuditMongoRepository.cs  ← queries MongoDB (filter, paginate)
│       ├── ApiKeyRepository.cs      ← EF Core repository
│       └── AuditRetentionJob.cs     ← Hangfire monthly job (12 meses)
└── Middleware/
    └── ApiKeyAuthenticationHandler.cs ← valida X-Api-Key header

src/omniDesk.Crm/src/app/features/
├── audit/
│   ├── audit.routes.ts
│   ├── audit-activity/
│   │   ├── audit-activity.component.ts     ← lazy-loaded, guard: tenant_admin
│   │   ├── audit-activity.component.html
│   │   ├── audit-activity.component.scss
│   │   └── audit-activity.component.spec.ts
│   └── services/
│       ├── audit.service.ts
│       └── audit.service.spec.ts
└── settings/
    └── api-keys/
        ├── api-keys.component.ts
        ├── api-keys.component.html
        ├── api-keys.component.scss
        ├── api-keys.component.spec.ts
        └── create-api-key-dialog/
            ├── create-api-key-dialog.component.ts
            ├── create-api-key-dialog.component.html
            └── create-api-key-dialog.component.spec.ts

src/omniDesk.Api/tests/omniDesk.Api.Tests/
├── Features/
│   ├── Audit/
│   │   └── AuditLogsEndpointTests.cs
│   └── ApiKeys/
│       └── ApiKeysEndpointTests.cs
└── Infrastructure/
    └── Audit/
        ├── AuditMongoRepositoryTests.cs
        └── ApiKeyRepositoryTests.cs
```

**Structure Decision**: Feature de API segue o padrão existente `Features/{Module}/` com handlers separados por operação. Infrastructure de MongoDB segue o padrão do módulo Notifications (que já usa MongoDB para logs raw). Frontend segue o padrão de feature modules existentes no CRM — lazy-loaded com guard de role.

---

## Complexity Tracking

> Nenhuma violação de Constitution — seção não aplicável.
