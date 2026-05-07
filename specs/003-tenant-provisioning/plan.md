# Implementation Plan: Tenants (Provisionamento)

**Branch**: `003-tenant-provisioning` | **Date**: 2026-05-06 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/003-tenant-provisioning/spec.md`

## Summary

O módulo de Tenants permite ao operador SaaS provisionar empresas clientes no sistema, criando automaticamente recursos isolados por tenant (schema Postgres com migrations aplicadas, bucket MinIO, database MongoDB, prefixo Redis), gerenciar o ciclo de vida dos tenants (bloqueio/desbloqueio, redefinição de senha do Super Admin) e acessar temporariamente o CRM de qualquer tenant para suporte (impersonation JWT de 15 min, não renovável). O provisionamento é executado de forma assíncrona via Hangfire. Métricas de saúde de todos os tenants são coletadas a cada 5 minutos por job recorrente e exibidas no dashboard admin via cache Redis — sem queries diretas ao banco em tempo de exibição.

## Technical Context

**Language/Version**: .NET 10 (API), Angular 21 (admin SPA)
**Primary Dependencies**: Entity Framework Core 9 + Npgsql, Hangfire + Redis, MinIO .NET SDK (`Minio`), MongoDB .NET Driver, FluentValidation, SendGrid, PrimeNG 21
**Storage**: PostgreSQL (`public.*` para sistema; schemas `tenant_{slug}` por tenant via EF Core migrations dinâmicas), Redis (cache `saas:metrics:{slug}`, sessões `{slug}:session:*`), MongoDB (databases `tenant_{slug}`), MinIO (buckets `tenant-{slug}`)
**Testing**: Testcontainers (integração com PostgreSQL, Redis, MongoDB e MinIO reais — sem mock de banco), Angular `.spec.ts` co-localizados (unit)
**Target Platform**: Linux ARM64 (Oracle Cloud, Docker `linux/arm64`), Cloudflare Pages + Workers (frontend admin)
**Project Type**: Web service (API Minimal .NET 10) + Web application (Angular 21 SPA admin)
**Performance Goals**: Provisionamento completo < 3 min (SC-001); invalidação de sessões no bloqueio < 5 s (SC-002)
**Constraints**: ARM64 Docker obrigatório; HTTPS only; zero queries diretas ao banco durante exibição do dashboard (SC-007); OpenAI API Key nunca em texto plano em nenhuma resposta

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio | Status | Observação |
|---|---|---|
| I. Multi-Tenant Isolation | ✅ PASS | `public.tenants`, `public.tenant_contacts`, `public.agent_templates` são tabelas de sistema no schema `public` — permitido pela constituição. Recursos por tenant seguem padrões: `tenant_{slug}` (Postgres schema + MongoDB), `tenant-{slug}` (MinIO), `{slug}:*` (Redis). |
| II. AI-First | ✅ PASS | Templates incluem `orchestrator` + `sub_agent`; copiados para cada tenant no provisionamento. |
| III. Channel Agnosticism | ✅ N/A | Módulo de admin; sem canais de comunicação com clientes. |
| IV. Security e LGPD | ✅ PASS | OpenAI Key: AES-256-GCM em repouso, nunca exposta em resposta. Token de impersonation: JWT RS256, expira em 15 min ≤ limite constitucional, sem refresh. Dados em infraestrutura nacional (Oracle Cloud Brasil). |
| V. Simplicity | ✅ PASS | MinIO SDK e MongoDB Driver são dependências necessárias — não especulativas. Hangfire já no stack. Sem padrões não-óbvios introduzidos. |
| VI. Observability | ✅ PASS | Log de erro de provisionamento persistido e acessível no admin. Ações de impersonation rastreáveis via log de auditoria. Métricas sistematicamente coletadas e cacheadas. |
| VII. Test Discipline | ✅ PASS | Testes de integração do provisionamento usarão Testcontainers com Postgres, MinIO e MongoDB reais. Sem mock de banco. |

**Constitution Check pós-design: APROVADO — todos os artefatos respeitam os princípios.**

## Project Structure

### Documentation (this feature)

```text
specs/003-tenant-provisioning/
├── plan.md              # Este arquivo
├── research.md          # Phase 0 — decisões técnicas
├── data-model.md        # Phase 1 — entidades e tipos
├── quickstart.md        # Phase 1 — cenários de verificação
├── contracts/
│   ├── tenants-api.md          # Phase 1 — contratos REST de tenants
│   └── agent-templates-api.md  # Phase 1 — contratos REST de templates
└── tasks.md             # Phase 2 — gerado por /speckit-tasks
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Domain/
│   │   ├── Tenants/
│   │   │   ├── Tenant.cs
│   │   │   ├── TenantContact.cs
│   │   │   ├── TenantStatus.cs           # enum: Provisioning, Active, Blocked, Error
│   │   │   └── ContactType.cs            # enum: Financial, Technical
│   │   └── AgentTemplates/
│   │       ├── AgentTemplate.cs
│   │       └── AgentType.cs              # enum: Orchestrator, SubAgent
│   ├── Application/
│   │   └── Admin/
│   │       ├── Tenants/
│   │       │   ├── CreateTenantCommand.cs
│   │       │   ├── UpdateTenantCommand.cs
│   │       │   ├── BlockTenantCommand.cs
│   │       │   ├── UnblockTenantCommand.cs
│   │       │   ├── ImpersonateTenantCommand.cs
│   │       │   ├── ResetSuperAdminPasswordCommand.cs
│   │       │   ├── RetryProvisioningCommand.cs
│   │       │   ├── GetTenantsQuery.cs
│   │       │   ├── GetTenantDetailQuery.cs
│   │       │   └── GetTenantMetricsQuery.cs
│   │       └── AgentTemplates/
│   │           ├── CreateAgentTemplateCommand.cs
│   │           ├── UpdateAgentTemplateCommand.cs
│   │           └── DeactivateAgentTemplateCommand.cs
│   ├── Infrastructure/
│   │   ├── Provisioning/
│   │   │   ├── TenantProvisioningJob.cs       # Hangfire fire-and-forget
│   │   │   ├── TenantSchemaProvisioner.cs     # EF Core migrations dinâmicas
│   │   │   ├── MinioProvisioner.cs
│   │   │   └── MongoProvisioner.cs
│   │   ├── Jobs/
│   │   │   └── TenantMetricsCollectorJob.cs   # Hangfire recorrente (*/5 * * * *)
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs                # public.* tables
│   │   │   ├── TenantDbContext.cs             # tenant_{slug} schemas (runtime)
│   │   │   └── Configurations/
│   │   │       ├── TenantConfiguration.cs
│   │   │       ├── TenantContactConfiguration.cs
│   │   │       └── AgentTemplateConfiguration.cs
│   │   ├── Security/
│   │   │   ├── AesEncryptionService.cs        # reutilizado da Spec 002
│   │   │   └── SessionInvalidationService.cs  # Redis SCAN + bulk DEL
│   │   └── Validators/
│   │       └── CnpjValidator.cs
│   └── Api/
│       └── Admin/
│           ├── TenantsEndpoints.cs
│           └── AgentTemplatesEndpoints.cs
└── tests/
    ├── integration/
    │   └── Admin/
    │       ├── TenantsEndpointsTests.cs
    │       └── TenantProvisioningJobTests.cs
    └── contract/
        └── Admin/
            └── TenantsContractTests.cs

frontend/admin/src/
└── app/
    └── features/
        ├── tenants/
        │   ├── tenant-list/
        │   │   ├── tenant-list.component.ts
        │   │   └── tenant-list.component.spec.ts
        │   ├── tenant-detail/
        │   │   ├── tenant-detail.component.ts
        │   │   └── tenant-detail.component.spec.ts
        │   ├── tenant-create/
        │   │   ├── tenant-create.component.ts
        │   │   └── tenant-create.component.spec.ts
        │   ├── tenant-health-dashboard/
        │   │   ├── tenant-health-dashboard.component.ts
        │   │   └── tenant-health-dashboard.component.spec.ts
        │   ├── models/
        │   │   └── tenant.models.ts
        │   ├── services/
        │   │   └── tenant.service.ts
        │   └── tenants.routes.ts
        └── agent-templates/
            ├── agent-template-list/
            │   ├── agent-template-list.component.ts
            │   └── agent-template-list.component.spec.ts
            ├── agent-template-form/
            │   ├── agent-template-form.component.ts
            │   └── agent-template-form.component.spec.ts
            ├── services/
            │   └── agent-template.service.ts
            └── agent-templates.routes.ts
```

**Structure Decision**: Web application (backend API + Angular SPA admin). Estrutura espelha o padrão Domain → Application → Infrastructure → Api estabelecido na Spec 002. Nenhum projeto novo criado — esta feature expande os projetos existentes.

## Complexity Tracking

> Sem violações da constituição identificadas — tabela de complexidade não aplicável.
