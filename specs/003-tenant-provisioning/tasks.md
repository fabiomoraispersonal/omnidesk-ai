# Tasks: Tenants (Provisionamento)

**Input**: Design documents from `specs/003-tenant-provisioning/`
**Stack**: .NET 10 Minimal API + Angular 21 | PostgreSQL + Redis + MongoDB + MinIO | Hangfire

**Organization**: Tarefas agrupadas por user story para permitir entrega incremental e teste independente.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Pode executar em paralelo (arquivos diferentes, sem dependências de tarefas incompletas)
- **[Story]**: User story à qual a tarefa pertence (US1–US6)
- Caminhos relativos à raiz do repositório

---

## Phase 1: Setup (Infraestrutura Compartilhada)

**Purpose**: Dependências e estrutura de diretórios necessárias antes de qualquer código de domínio.

- [X] T001 Adicionar pacotes NuGet ao backend.csproj: `Minio` (v6.x) e `MongoDB.Driver` (v3.x)
- [X] T002 [P] Configurar MinioClient como singleton no DI em `backend/src/Program.cs` lendo variáveis de ambiente `MINIO_ENDPOINT`, `MINIO_ACCESS_KEY`, `MINIO_SECRET_KEY`
- [X] T003 [P] Configurar IMongoClient como singleton no DI em `backend/src/Program.cs` lendo variável de ambiente `MONGODB_CONNECTION_STRING`
- [X] T004 [P] Criar estrutura de diretórios no backend: `Domain/Tenants/`, `Domain/AgentTemplates/`, `Application/Admin/Tenants/`, `Application/Admin/AgentTemplates/`, `Infrastructure/Provisioning/`, `Infrastructure/Jobs/`, `Infrastructure/Validators/`
- [X] T005 [P] Criar estrutura de diretórios no frontend admin: `features/tenants/{tenant-list,tenant-detail,tenant-create,tenant-health-dashboard,models,services}/` e `features/agent-templates/{agent-template-list,agent-template-form,services}/`

**Checkpoint**: Dependências instaladas, estrutura de diretórios pronta.

---

## Phase 2: Foundational (Pré-requisitos Bloqueantes)

**Purpose**: Domínio, persistência e serviços de infraestrutura que TODAS as user stories dependem.

**⚠️ CRÍTICO**: Nenhuma user story pode começar até esta fase estar completa.

- [X] T006 Criar migration EF Core `CreateTenantsTables` em `backend/src/Infrastructure/Persistence/Migrations/`: cria enums PostgreSQL (`tenant_status`, `contact_type`, `agent_type`) e tabelas `public.tenants`, `public.tenant_contacts`, `public.agent_templates` com todos os campos e índices definidos em data-model.md
- [X] T007 [P] Criar entidade `Tenant` em `backend/src/Domain/Tenants/Tenant.cs` com todas as propriedades mapeadas (incluindo `OpenAiApiKeyEnc`, `ProvisioningErrorLog`, `BlockedAt`)
- [X] T008 [P] Criar entidade `TenantContact` em `backend/src/Domain/Tenants/TenantContact.cs`
- [X] T009 [P] Criar enums `TenantStatus` em `backend/src/Domain/Tenants/TenantStatus.cs` e `ContactType` em `backend/src/Domain/Tenants/ContactType.cs`
- [X] T010 [P] Criar entidade `AgentTemplate` em `backend/src/Domain/AgentTemplates/AgentTemplate.cs` e enum `AgentType` em `backend/src/Domain/AgentTemplates/AgentType.cs`
- [X] T011 Criar configurações EF Core em `backend/src/Infrastructure/Persistence/Configurations/`: `TenantConfiguration.cs` (UNIQUE idx em slug/cnpj, coluna `openai_api_key_enc`), `TenantContactConfiguration.cs` (UNIQUE constraint em `tenant_id+type`), `AgentTemplateConfiguration.cs`
- [X] T012 Registrar entidades `Tenant`, `TenantContact` e `AgentTemplate` em `AppDbContext` em `backend/src/Infrastructure/Persistence/AppDbContext.cs` e aplicar as configurações do T011
- [X] T013 [P] Criar `TenantDbContext` em `backend/src/Infrastructure/Persistence/TenantDbContext.cs`: aceita schema name no construtor, aplica via `HasDefaultSchema()`, configura `MigrationsHistoryTable` no schema do tenant para migrations dinâmicas por tenant
- [X] T014 [P] Criar `CnpjValidator` em `backend/src/Infrastructure/Validators/CnpjValidator.cs`: método estático `IsValidCnpj(string cnpj)` implementando validação de formato e cálculo dos dois dígitos verificadores (algoritmo módulo 11)
- [X] T015 [P] Criar `SessionInvalidationService` em `backend/src/Infrastructure/Security/SessionInvalidationService.cs`: método `InvalidateAllTenantSessionsAsync(string slug)` usando `IServer.Keys(pattern: "{slug}:session:*")` e bulk `KeyDeleteAsync`
- [X] T016 [P] Verificar se `AesEncryptionService` existe (Spec 002); se não existir, criar em `backend/src/Infrastructure/Security/AesEncryptionService.cs`: AES-256-GCM com nonce de 12 bytes, chave de `AES_ENCRYPTION_KEY` env var, formato `<nonce_hex>:<ciphertext_hex>`
- [X] T017 [P] Criar modelos TypeScript em `frontend/admin/src/app/features/tenants/models/tenant.models.ts`: interfaces `TenantSummary`, `TenantDetail`, `TenantContact`, `TenantMetricsSummary`, `TenantMetricsDetail`, `CreateTenantRequest`, `ImpersonateResponse`, `AgentTemplate` conforme data-model.md
- [X] T018 Criar `DatabaseSeeder` em `backend/src/Infrastructure/Persistence/DatabaseSeeder.cs`: insere os 5 templates padrão (Agente Principal/orchestrator, Recepção/sub_agent, Vendas/sub_agent, Pós-Vendas/sub_agent, Suporte/sub_agent) se a tabela estiver vazia; registrar invocação no startup em `backend/src/Program.cs`
- [X] T019 [P] Registrar Hangfire recurring job `TenantMetricsCollectorJob` com cron `"*/5 * * * *"` no bloco de configuração Hangfire em `backend/src/Program.cs` (classe criada em Phase 8)
- [X] T020 [P] Criar stub de `TenantService` em `frontend/admin/src/app/features/tenants/services/tenant.service.ts`: injetar `HttpClient`, definir métodos vazios `getTenants()`, `getTenantDetail()`, `createTenant()`, `updateTenant()` para implementação nas fases seguintes

**Checkpoint**: Banco de dados migrado, domínio mapeado, infraestrutura pronta — user stories podem começar.

---

## Phase 3: User Story 1 — Provisionamento de Novo Tenant (Priority: P1) 🎯 MVP

**Goal**: Operador cria um tenant via formulário, o sistema provisiona todos os recursos isolados de forma assíncrona e envia e-mail de boas-vindas ao responsável técnico com credenciais de acesso.

**Independent Test**: Preencher o formulário de novo tenant → aguardar status `active` → verificar schema Postgres, bucket MinIO, database MongoDB, e-mail recebido, login com credenciais funcionando.

- [X] T021 [P] [US1] Criar `MinioProvisioner` em `backend/src/Infrastructure/Provisioning/MinioProvisioner.cs`: método `CreateBucketAsync(string slug, CancellationToken ct)` com `BucketExistsAsync` antes de `MakeBucketAsync` para garantir idempotência no retry
- [X] T022 [P] [US1] Criar `MongoProvisioner` em `backend/src/Infrastructure/Provisioning/MongoProvisioner.cs`: método `InitializeDatabaseAsync(string slug, CancellationToken ct)` que obtém database `tenant_{slug.Replace('-','_')}` e insere documento em coleção `__metadata` com `tenant_slug` e `provisioned_at`
- [X] T023 [US1] Criar `TenantSchemaProvisioner` em `backend/src/Infrastructure/Provisioning/TenantSchemaProvisioner.cs`: `ProvisionSchemaAsync(string slug, CancellationToken ct)` executa `CREATE SCHEMA IF NOT EXISTS "tenant_{slug}"` via raw SQL e em seguida chama `tenantContext.Database.MigrateAsync()` usando `TenantDbContext` configurado para o schema do tenant
- [X] T024 [US1] Criar `TenantProvisioningJob` em `backend/src/Infrastructure/Provisioning/TenantProvisioningJob.cs`: orquestra T023 → T021 → T022 → criação do Super Admin (gera senha de 12 chars com letras maiúsculas/minúsculas/números/símbolos, chama Spec 002 user service) → envio do e-mail de boas-vindas via SendGrid → atualiza `tenant.Status = Active`; em qualquer exceção: salva log em `tenant.ProvisioningErrorLog` e atualiza `Status = Error`; todas as etapas são idempotentes (verificar antes de criar)
- [X] T025 [US1] Registrar `TenantProvisioningJob`, `TenantSchemaProvisioner`, `MinioProvisioner`, `MongoProvisioner` no DI em `backend/src/Program.cs`
- [X] T026 [P] [US1] Criar `CreateTenantCommand` + handler em `backend/src/Application/Admin/Tenants/CreateTenantCommand.cs`: validar com FluentValidation (slug `[a-z0-9-]` 3-50, CNPJ via `CnpjValidator`, unicidade de slug/CNPJ/email técnico); criar `Tenant` + 2 `TenantContact`; enfileirar `TenantProvisioningJob` via `BackgroundJobClient.Enqueue`; retornar 202 com `{id, slug, status: "provisioning"}`
- [X] T027 [P] [US1] Criar `GetTenantsQuery` + handler em `backend/src/Application/Admin/Tenants/GetTenantsQuery.cs`: lista todos os tenants com `TenantSummaryResponse`; lê métricas do Redis cache `saas:metrics:{slug}` via `IDatabase.StringGetAsync`; `has_openai_key` calculado sem expor `openai_api_key_enc`
- [X] T028 [P] [US1] Criar `GetTenantDetailQuery` + handler em `backend/src/Application/Admin/Tenants/GetTenantDetailQuery.cs`: retorna `TenantDetail` completo com contatos e métricas detalhadas do cache; `openai_api_key_enc` nunca incluso no response
- [X] T029 [P] [US1] Criar `UpdateTenantCommand` + handler em `backend/src/Application/Admin/Tenants/UpdateTenantCommand.cs`: atualiza `razao_social`, `nome_fantasia`, `timezone`, contatos, campos OpenAI; se `openai_api_key` enviada, criptografar via `AesEncryptionService` antes de salvar; slug é imutável (ignorar se enviado)
- [X] T030 [P] [US1] Criar `RetryProvisioningCommand` + handler em `backend/src/Application/Admin/Tenants/RetryProvisioningCommand.cs`: rejeita com 409 se tenant não estiver em status `error`; atualiza status para `provisioning`; limpa `ProvisioningErrorLog`; reenfileira `TenantProvisioningJob`
- [X] T031 [US1] Criar `TenantsEndpoints.cs` em `backend/src/Api/Admin/TenantsEndpoints.cs`: registrar `POST /api/admin/tenants` (T026), `GET /api/admin/tenants` (T027), `GET /api/admin/tenants/{id}` (T028), `PUT /api/admin/tenants/{id}` (T029), `POST /api/admin/tenants/{id}/retry-provisioning` (T030); todos com `RequireAuthorization(Roles.SaasAdmin)`
- [X] T032 [US1] Implementar métodos `createTenant()`, `getTenants()`, `getTenantDetail()`, `updateTenant()`, `retryProvisioning()` em `frontend/admin/src/app/features/tenants/services/tenant.service.ts`
- [X] T033 [P] [US1] Criar `tenant-create.component` em `frontend/admin/src/app/features/tenants/tenant-create/tenant-create.component.ts`: formulário reativo com `slug`, `razao_social`, `nome_fantasia`, `cnpj`, `timezone` (dropdown com opções V1), `financial_contact` (name/email/phone), `technical_contact` (name/email/phone), seção opcional OpenAI (api_key/organization/project); submit chama `TenantService.createTenant()`; redireciona para list após 202
- [X] T034 [P] [US1] Criar `tenant-list.component` básico em `frontend/admin/src/app/features/tenants/tenant-list/tenant-list.component.ts`: tabela PrimeNG com colunas Nome Fantasia + slug, Status (badge colorido por status), CNPJ, Created At, e coluna de Ações com botões placeholder; dados via `TenantService.getTenants()`
- [X] T035 [US1] Criar `tenants.routes.ts` em `frontend/admin/src/app/features/tenants/tenants.routes.ts` com rotas lazy-loaded para list e create; registrar no router principal do admin app; criar `tenant-detail.component` básico em `frontend/admin/src/app/features/tenants/tenant-detail/tenant-detail.component.ts` com todos os dados cadastrais e contatos (sem métricas)

**Checkpoint**: Operador pode criar, listar e detalhar tenants; provisionamento completo funcionando de ponta a ponta; e-mail enviado após sucesso.

---

## Phase 4: User Story 2 — Bloqueio e Desbloqueio (Priority: P2)

**Goal**: Operador bloqueia ou desbloqueia qualquer tenant com um clique; bloqueio invalida sessões imediatamente; CRM exibe tela de acesso suspenso.

**Independent Test**: Ter sessão ativa no CRM → operador bloqueia → sessão é invalidada → CRM exibe "Acesso suspenso" → operador desbloqueia → acesso restaurado.

- [X] T036 [P] [US2] Criar `BlockTenantCommand` + handler em `backend/src/Application/Admin/Tenants/BlockTenantCommand.cs`: rejeitar com 409 se já `blocked`; atualizar `Status = Blocked` e `BlockedAt = DateTime.UtcNow`; chamar `SessionInvalidationService.InvalidateAllTenantSessionsAsync(slug)`
- [X] T037 [P] [US2] Criar `UnblockTenantCommand` + handler em `backend/src/Application/Admin/Tenants/UnblockTenantCommand.cs`: rejeitar com 409 se não estiver `blocked`; atualizar `Status = Active` e `BlockedAt = null`
- [X] T038 [US2] Adicionar endpoints `POST /api/admin/tenants/{id}/block` (T036) e `POST /api/admin/tenants/{id}/unblock` (T037) ao `TenantsEndpoints.cs` em `backend/src/Api/Admin/TenantsEndpoints.cs`
- [X] T039 [P] [US2] Adicionar métodos `blockTenant()` e `unblockTenant()` ao `TenantService` em `frontend/admin/src/app/features/tenants/services/tenant.service.ts`
- [X] T040 [US2] Adicionar botões "Bloquear" / "Desbloquear" com diálogo de confirmação ao `tenant-list.component` e `tenant-detail.component`; atualizar badge de status após ação
- [X] T041 [US2] Adicionar middleware/interceptor no CRM Angular em `frontend/crm/src/app/core/tenant-access.interceptor.ts`: interceptar respostas 403 de qualquer rota e redirecionar para página `/acesso-suspenso` com mensagem "Acesso suspenso. Entre em contato com o suporte."

**Checkpoint**: Bloqueio invalida sessões Redis imediatamente; CRM exibe página de suspensão; desbloqueio restaura acesso.

---

## Phase 5: User Story 3 — Impersonation (Priority: P2)

**Goal**: Operador acessa CRM de qualquer tenant sem senha, com token JWT de 15 min não renovável; CRM exibe barra de aviso visível.

**Independent Test**: Operador clica "Acessar ambiente" → redirect para CRM com token → barra de aviso aparece → após 15 min qualquer request retorna 401 sem opção de renovação.

- [X] T042 [US3] Criar `ImpersonateTenantCommand` + handler em `backend/src/Application/Admin/Tenants/ImpersonateTenantCommand.cs`: rejeitar com 422 se tenant não for `active`; gerar JWT RS256 com claims `{sub: saasAdminId, role: "tenant_admin", tenant_id, tenant_slug, impersonating: true, impersonated_by: saasAdminId, exp: now+900}`; sem refresh token emitido; retornar `ImpersonateResponse {impersonation_token, redirect_url: "https://{slug}.omnideskcrm.com.br/impersonate?token={jwt}", expires_at}`
- [X] T043 [US3] Adicionar endpoint `POST /api/admin/tenants/{id}/impersonate` (T042) ao `TenantsEndpoints.cs` em `backend/src/Api/Admin/TenantsEndpoints.cs`
- [X] T044 [US3] Adicionar método `impersonateTenant()` ao `TenantService`; no componente admin, ao receber a resposta abrir `redirect_url` em nova aba em `frontend/admin/src/app/features/tenants/services/tenant.service.ts`
- [X] T045 [US3] Criar `ImpersonationHandlerComponent` em `frontend/crm/src/app/core/impersonation/impersonation-handler.component.ts`: na rota `/impersonate`, ler `?token=` do query param, armazenar access token em memória via `AuthService`, chamar `history.replaceState` para limpar o token do URL, redirecionar para `/`
- [X] T046 [P] [US3] Criar `ImpersonationBannerComponent` em `frontend/crm/src/app/core/impersonation/impersonation-banner.component.ts`: exibido no topo do layout quando `AuthService.currentUser.isImpersonation === true`; texto "Você está acessando como operador SaaS"; não ocultável pelo usuário
- [X] T047 [US3] Adicionar botão "Acessar ambiente" ao `tenant-list.component` e `tenant-detail.component` no admin frontend; chamar `TenantService.impersonateTenant()` e abrir `redirect_url`

**Checkpoint**: Impersonation funciona end-to-end; barra de aviso visível no CRM; token expira em 15 min sem renovação possível.

---

## Phase 6: User Story 4 — Redefinição de Senha do Super Admin (Priority: P2)

**Goal**: Operador redefine senha do Super Admin sem conhecer a senha atual; nova senha enviada por e-mail; sessões ativas invalidadas.

**Independent Test**: Super Admin com sessão ativa → operador redefine senha → sessão é invalidada → e-mail com nova senha recebido → login com nova senha funciona.

- [X] T048 [US4] Criar `ResetSuperAdminPasswordCommand` + handler em `backend/src/Application/Admin/Tenants/ResetSuperAdminPasswordCommand.cs`: localizar Super Admin do tenant (usuário com `role = TenantAdmin` vinculado ao tenant); gerar senha forte de 12 chars (upper + lower + digit + symbol via `RandomNumberGenerator`); atualizar `password_hash` via Argon2id; chamar `SessionInvalidationService.InvalidateAllTenantSessionsAsync(slug)` filtrando apenas sessões do Super Admin; enviar e-mail ao contato técnico com a nova senha via SendGrid
- [X] T049 [US4] Adicionar endpoint `POST /api/admin/tenants/{id}/reset-password` (T048) ao `TenantsEndpoints.cs` em `backend/src/Api/Admin/TenantsEndpoints.cs`; retornar 204
- [X] T050 [US4] Adicionar método `resetSuperAdminPassword()` ao `TenantService` em `frontend/admin/src/app/features/tenants/services/tenant.service.ts`
- [X] T051 [US4] Adicionar botão "Redefinir senha" com diálogo de confirmação ao `tenant-list.component` e `tenant-detail.component` no admin frontend; exibir toast de confirmação após sucesso

**Checkpoint**: Nova senha gerada, enviada por e-mail e sessões invalidadas; login com nova senha funciona.

---

## Phase 7: User Story 5 — Gestão de Templates de Agentes (Priority: P3)

**Goal**: Operador gerencia catálogo global de templates; templates ativos são copiados para novos tenants no provisionamento; alterações globais não afetam tenants já provisionados.

**Independent Test**: Criar template → provisionar tenant → verificar agente copiado → editar template global → verificar agente do tenant inalterado → desativar template → provisionar novo tenant → verificar que template não foi copiado.

- [X] T052 [P] [US5] Criar `CreateAgentTemplateCommand` + handler em `backend/src/Application/Admin/AgentTemplates/CreateAgentTemplateCommand.cs`: validar `name` (obrigatório), `type` (orchestrator|sub_agent), `description` (obrigatório); `prompt` opcional; criar `AgentTemplate` com `is_active = true`, `used_in_provisioning_count = 0`; retornar 201
- [X] T053 [P] [US5] Criar `UpdateAgentTemplateCommand` + handler em `backend/src/Application/Admin/AgentTemplates/UpdateAgentTemplateCommand.cs`: atualizar name, description, prompt, is_active; rejeitar com 404 se não encontrado ou soft-deleted
- [X] T054 [P] [US5] Criar `DeactivateAgentTemplateCommand` + handler em `backend/src/Application/Admin/AgentTemplates/DeactivateAgentTemplateCommand.cs`: se `used_in_provisioning_count == 0` → exclusão física; se `> 0` → soft delete (`deleted_at = now()`, `is_active = false`); rejeitar com 409 se já excluído
- [X] T055 [P] [US5] Criar `GetAgentTemplatesQuery` + handler em `backend/src/Application/Admin/AgentTemplates/GetAgentTemplatesQuery.cs`: retorna templates não soft-deleted; suporte a filtro `active_only`
- [X] T056 [US5] Criar `AgentTemplatesEndpoints.cs` em `backend/src/Api/Admin/AgentTemplatesEndpoints.cs`: registrar `GET /api/admin/agent-templates` (T055), `POST /api/admin/agent-templates` (T052), `PUT /api/admin/agent-templates/{id}` (T053), `DELETE /api/admin/agent-templates/{id}` (T054); todos com `RequireAuthorization(Roles.SaasAdmin)`
- [X] T057 [US5] Atualizar `TenantProvisioningJob` em `backend/src/Infrastructure/Provisioning/TenantProvisioningJob.cs`: após criar o schema Postgres, copiar todos os templates com `is_active = true` para a tabela `{tenant_slug}.agents` no schema do tenant (novo UUID por agente, `template_id` armazenado como referência histórica sem FK); incrementar `used_in_provisioning_count` de cada template copiado
- [X] T058 [P] [US5] Criar `AgentTemplateService` em `frontend/admin/src/app/features/agent-templates/services/agent-template.service.ts`: métodos `getTemplates()`, `createTemplate()`, `updateTemplate()`, `deleteTemplate()`
- [X] T059 [P] [US5] Criar `agent-template-list.component` em `frontend/admin/src/app/features/agent-templates/agent-template-list/agent-template-list.component.ts`: tabela com nome, tipo (badge), ativo/inativo, usos, ações (editar/desativar); filtro por ativo/inativo
- [X] T060 [P] [US5] Criar `agent-template-form.component` em `frontend/admin/src/app/features/agent-templates/agent-template-form/agent-template-form.component.ts`: formulário reativo para criar e editar templates (nome, tipo dropdown, descrição, prompt textarea, is_active toggle)
- [X] T061 [US5] Criar `agent-templates.routes.ts` em `frontend/admin/src/app/features/agent-templates/agent-templates.routes.ts` com rotas lazy-loaded; registrar no router principal do admin app

**Checkpoint**: CRUD de templates funcional; provisioning copia templates ativos; edições globais não afetam tenants existentes.

---

## Phase 8: User Story 6 — Dashboard de Saúde (Priority: P3)

**Goal**: Dashboard exibe status de saúde de todos os tenants com métricas em tempo real (cache); polling de 60s atualiza automaticamente; zero queries diretas ao banco durante exibição.

**Independent Test**: Acessar dashboard → verificar que dados vêm do cache Redis `saas:metrics:{slug}` → aguardar 60s → verificar atualização automática → verificar que `saas:metrics:{slug}` existe no Redis com TTL 5 min.

- [X] T062 [US6] Criar `TenantMetricsCollectorJob` em `backend/src/Infrastructure/Jobs/TenantMetricsCollectorJob.cs`: para cada tenant `active`: coletar tamanho do schema Postgres (`pg_total_relation_size`), keys/memória Redis por prefixo `{slug}:*`, tamanho do database MongoDB, tamanho/contagem de objetos MinIO, métricas de negócio (conversas 30d, tickets abertos, usuários ativos) via queries nos schemas dos tenants; serializar como JSON e salvar em `saas:metrics:{slug}` com `TTL 300s`; registrar erros de conectividade sem interromper o job
- [X] T063 [P] [US6] Criar `GetTenantMetricsQuery` + handler em `backend/src/Application/Admin/Tenants/GetTenantMetricsQuery.cs`: ler de `saas:metrics:{slug}` via Redis; se chave não existir retornar 503 com `{error: "metrics_unavailable"}`
- [X] T064 [US6] Adicionar endpoint `GET /api/admin/tenants/{id}/metrics` (T063) ao `TenantsEndpoints.cs` em `backend/src/Api/Admin/TenantsEndpoints.cs`
- [X] T065 [US6] Adicionar método `getTenantMetrics()` ao `TenantService`; adicionar polling com `rxjs interval(60000)` ao componente de dashboard; cancelar subscription no `ngOnDestroy`
- [X] T066 [P] [US6] Estender `tenant-list.component` em `frontend/admin/src/app/features/tenants/tenant-list/tenant-list.component.ts`: adicionar colunas de métricas (Postgres ✅/❌, Redis ✅/❌, MongoDB ✅/❌, Chats, Tickets, Usuários, OpenAI ✅/⚙️); dados das métricas já vêm no response do `GET /api/admin/tenants` (cache)
- [X] T067 [P] [US6] Criar `tenant-health-dashboard.component` em `frontend/admin/src/app/features/tenants/tenant-health-dashboard/tenant-health-dashboard.component.ts`: visão individual detalhada com métricas por recurso (cards de Postgres/Redis/MongoDB/MinIO), métricas de negócio (conversas por canal, tickets por status, agendamentos), e log dos últimos eventos de provisionamento/bloqueio
- [X] T068 [US6] Registrar rota do dashboard em `tenants.routes.ts`; adicionar botão "Ver saúde detalhada" no `tenant-list.component` e `tenant-detail.component` que navega para o dashboard individual

**Checkpoint**: Dashboard exibe métricas em cache com polling de 60s; zero queries diretas ao banco durante exibição.

---

## Phase 9: Polish & Qualidade

**Purpose**: Testes (constitucionalmente obrigatórios: contract tests antes de integration tests) e hardening de segurança.

- [X] T069 [P] Criar contract tests para endpoints de tenants em `backend/tests/contract/Admin/TenantsContractTests.cs`: validar shapes de request/response de todos os endpoints conforme `contracts/tenants-api.md`; devem passar antes dos integration tests rodarem
- [X] T070 [P] Criar contract tests para endpoints de agent-templates em `backend/tests/contract/Admin/AgentTemplatesContractTests.cs`: validar shapes conforme `contracts/agent-templates-api.md`
- [X] T071 [P] Criar integration tests para `TenantProvisioningJob` em `backend/tests/integration/Admin/TenantProvisioningJobTests.cs`: usar Testcontainers com PostgreSQL + MinIO + MongoDB reais; verificar criação de schema, bucket, database e Super Admin; sem mock de banco
- [X] T072 [P] Criar integration tests para endpoints de tenants em `backend/tests/integration/Admin/TenantsEndpointsTests.cs`: usar Testcontainers; cobrir cenários de criação com validação, slug/CNPJ duplicado, bloqueio/desbloqueio, impersonation
- [ ] T073 Executar checklist de verificação em `quickstart.md` manualmente: todos os 18 itens devem passar; corrigir falhas encontradas — **pendente: requer ambiente rodando (Postgres + Redis + Mongo + MinIO + SendGrid)**
- [ ] T074 [P] Revisão de segurança: confirmar que `openai_api_key_enc` não aparece em nenhum response (grep nos handlers); verificar claims do token de impersonation; verificar que `SessionInvalidationService` deleta todas as sessões no bloqueio; verificar que retry é idempotente (rodar duas vezes sem efeitos colaterais) — **pendente: requer execução manual após T073**

**Checkpoint**: Todos os contract tests passam → integration tests passam → quickstart checklist verde → revisão de segurança limpa.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Nenhuma dependência — pode iniciar imediatamente
- **Phase 2 (Foundational)**: Depende de Phase 1 — **BLOQUEIA todas as user stories**
- **Phases 3-8 (User Stories)**: Todas dependem da conclusão de Phase 2
  - Podem ser executadas em sequência (P1 → P2 → P3) ou em paralelo se houver múltiplos devs
- **Phase 9 (Polish)**: Depende de todas as user stories desejadas

### User Story Dependencies

- **US1 (P1)**: Inicia após Phase 2 — sem dependências em outras user stories
- **US2 (P2)**: Inicia após Phase 2 — integra com US1 (reutiliza TenantsEndpoints.cs e TenantService)
- **US3 (P2)**: Inicia após Phase 2 — integra com US1; requer que CRM angular exista
- **US4 (P2)**: Inicia após Phase 2 — integra com US1; depende do modelo de usuário da Spec 002
- **US5 (P3)**: Inicia após Phase 2 — modifica TenantProvisioningJob (US1) para copiar templates
- **US6 (P3)**: Inicia após Phase 2 — estende tenant-list.component (US1) com colunas de métricas

### Within Each User Story

- Provisioners (T021-T022) → Job (T024) → Endpoints (T031) → Frontend (T032-T035)
- Modelos e queries paralelas → Endpoints → Frontend

### Parallel Opportunities

- T002, T003, T004, T005 — podem rodar em paralelo (Phase 1)
- T007–T017 — podem rodar em paralelo dentro de Phase 2
- T021, T022 (MinioProvisioner, MongoProvisioner) — paralelos entre si dentro de US1
- T026, T027, T028, T029, T030 (Commands/Queries) — paralelos entre si dentro de US1
- T033, T034 (Components) — paralelos entre si dentro de US1
- T036, T037 (Block/Unblock commands) — paralelos entre si dentro de US2
- T052, T053, T054, T055 (Template commands) — paralelos entre si dentro de US5
- T069, T070, T071, T072, T074 (Tests) — paralelos entre si em Phase 9

---

## Parallel Example: User Story 1

```
# Após Phase 2 completa, lançar em paralelo:
Task T021: MinioProvisioner em Infrastructure/Provisioning/MinioProvisioner.cs
Task T022: MongoProvisioner em Infrastructure/Provisioning/MongoProvisioner.cs

# Em paralelo (commands são independentes):
Task T026: CreateTenantCommand em Application/Admin/Tenants/CreateTenantCommand.cs
Task T027: GetTenantsQuery em Application/Admin/Tenants/GetTenantsQuery.cs
Task T028: GetTenantDetailQuery em Application/Admin/Tenants/GetTenantDetailQuery.cs
Task T029: UpdateTenantCommand em Application/Admin/Tenants/UpdateTenantCommand.cs
Task T030: RetryProvisioningCommand em Application/Admin/Tenants/RetryProvisioningCommand.cs

# Em paralelo (componentes Angular são independentes):
Task T033: tenant-create.component
Task T034: tenant-list.component
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Completar Phase 1: Setup
2. Completar Phase 2: Foundational (**crítico — bloqueia tudo**)
3. Completar Phase 3: User Story 1
4. **PARAR E VALIDAR**: testar provisionamento completo de ponta a ponta via `quickstart.md` Cenário 1
5. Demo/validação com Fabio antes de continuar

### Incremental Delivery

1. Setup + Foundational → base pronta
2. US1 → provisionamento funcional (MVP) → validar
3. US2 → bloqueio/desbloqueio → validar
4. US3 → impersonation → validar
5. US4 → redefinição de senha → validar
6. US5 → templates de agentes → validar
7. US6 → dashboard de saúde → validar
8. Phase 9 → testes + hardening → PR

### Parallel Team Strategy

Com múltiplos devs (após Phase 2):
- Dev A: US1 (provisionamento — caminho crítico)
- Dev B: US2 + US3 (bloqueio + impersonation — podem rodar em paralelo após US1 estar estável)
- Dev C: US5 (templates — independente)
- Dev D: US6 (dashboard — independente)

---

## Notes

- [P] = arquivos diferentes, sem dependências entre si — podem ser implementados em paralelo
- [USN] = user story à qual a tarefa pertence — rastreabilidade e escopo claro
- Cada user story deve ser testável independentemente antes de avançar para a próxima
- Contract tests (T069-T070) devem passar **antes** de rodar integration tests (T071-T072) — constitucionalmente obrigatório
- Idempotência é requisito em todas as etapas do provisionamento — retry não deve criar recursos duplicados
- `openai_api_key_enc` nunca deve aparecer em nenhum response DTO — verificar em cada handler criado
