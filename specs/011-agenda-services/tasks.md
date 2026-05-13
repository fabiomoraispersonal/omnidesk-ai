---
description: "Task breakdown for Spec 011 — Agenda e Catálogo de Serviços"
---

# Tasks: Agenda e Catálogo de Serviços (Spec 011)

**Input**: Design documents from `specs/011-agenda-services/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: ✅ INCLUSOS. Constituição §VII (Test Discipline) torna testes obrigatórios para backend (xUnit + Testcontainers) e frontend (Karma + Jasmine `.spec.ts` co-localizados). Sem mock de DB.

**Organization**: tarefas agrupadas pelas 6 user stories da spec — cada US é independentemente testável e entregável. Backend e frontend de cada US ficam juntos para permitir entrega vertical incremental.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência).
- **[Story]**: `US1` Catálogo · `US2` Profissionais · `US3` Agendamentos Manuais · `US4` IA Tools · `US5` Cancelamento WhatsApp · `US6` Settings.
- Caminhos absolutos a partir da raiz do repo.

## Path Conventions

- Backend: `src/omniDesk.Api/`
- Backend tests: `src/omniDesk.Api/tests/omniDesk.Api.Tests/`
- Frontend CRM: `src/omniDesk.Crm/src/app/`
- Migrations: `src/omniDesk.Api/Infrastructure/Persistence/Migrations/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: scaffolding inicial — diretórios, registros DI, error codes, permissions.

- [X] T001 Criar estrutura de diretórios `Features/Agenda/{Services,Professionals,Availability,Appointments,Cancellation,Settings,Tools,Validators}` e `Infrastructure/Agenda/` em `src/omniDesk.Api/`
- [X] T002 [P] Criar `src/omniDesk.Api/Features/Agenda/README.md` descrevendo o módulo (sub-domínios, fluxos principais, links para spec/plan)
- [X] T003 [P] Adicionar `Services.Manage`, `Professionals.Manage`, `Appointments.View`, `Appointments.Manage`, `AgendaSettings.Manage` em `src/omniDesk.Api/Domain/Authorization/Permissions.cs`
- [X] T004 [P] Adicionar códigos de erro semânticos em `src/omniDesk.Api/Domain/Errors/AgendaErrors.cs` (`SERVICE_NOT_FOUND`, `SERVICE_DURATION_INVALID`, `PROFESSIONAL_NOT_FOUND`, `PROFESSIONAL_ATTENDANT_ALREADY_LINKED`, `WEEKLY_SCHEDULE_INVALID_RANGE`, `WEEKLY_SCHEDULE_OVERLAP`, `WEEKLY_SCHEDULE_INVALID_DAY`, `BLOCK_NOT_FOUND`, `BLOCK_RANGE_INVALID`, `BLOCK_OVERLAPS_APPOINTMENTS`, `APPOINTMENT_NOT_FOUND`, `APPOINTMENT_OUTSIDE_AVAILABILITY`, `APPOINTMENT_SLOT_CONFLICT`, `APPOINTMENT_INVALID_STATUS_TRANSITION`, `PROFESSIONAL_DOES_NOT_OFFER_SERVICE`, `CONTACT_HAS_NO_PHONE`, `WHATSAPP_CHANNEL_INACTIVE`, `LATE_CANCEL_WINDOW_INVALID`)
- [X] T005 [P] Estender `src/omniDesk.Api/Infrastructure/Authorization/RedisKeys.cs` com método `AppointmentSlotLock(string tenantSlug, Guid professionalId, DateTimeOffset startAt)` retornando `"{slug}:appointment_slot_lock:{prof:N}:{startAt:O}"`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: entidades de domínio compartilhadas, migrations e config EF Core. Sem essa fase, **nenhuma** user story compila.

**⚠️ CRITICAL**: bloqueia todas as US.

- [X] T006 [P] Criar `src/omniDesk.Api/Domain/Agenda/AppointmentStatus.cs` (static class com `PendingConfirmation`, `Confirmed`, `Cancelled`, `NoShow` + sets `All` e `ActiveForSlot`)
- [X] T007 [P] Criar `src/omniDesk.Api/Domain/Agenda/ClientType.cs` (static class `NewClient`, `ReturningClient`)
- [X] T008 [P] Criar `src/omniDesk.Api/Domain/Agenda/CreatedBy.cs` (static class `Ai`, `Attendant`)
- [X] T009 [P] Criar `src/omniDesk.Api/Domain/Agenda/CancelledBy.cs` (static class `Client`, `Attendant`, `System`)
- [X] T010 [P] Criar entidade `src/omniDesk.Api/Domain/Agenda/Service.cs` conforme data-model.md §Service (props: `Id`, `Name`, `Description?`, `Category?`, `DurationMinutes`, `Price?`, `RequiresConfirmation`, `IsActive`, `CreatedAt`, `UpdatedAt`)
- [X] T011 [P] Criar entidade `src/omniDesk.Api/Domain/Agenda/Professional.cs` (props: `Id`, `Name`, `Specialty?`, `DepartmentId?`, `AttendantId?`, `IsActive`, `CreatedAt`, `UpdatedAt`; navegação para `Department` e `Attendant`)
- [X] T012 [P] Criar entidade `src/omniDesk.Api/Domain/Agenda/ProfessionalService.cs` (props: `Id`, `ProfessionalId`, `ServiceId`)
- [X] T013 [P] Criar entidade `src/omniDesk.Api/Domain/Agenda/WeeklySchedule.cs` (props: `Id`, `ProfessionalId`, `DayOfWeek` smallint, `StartTime` TimeOnly, `EndTime` TimeOnly)
- [X] T014 [P] Criar entidade `src/omniDesk.Api/Domain/Agenda/ScheduleBlock.cs` (props: `Id`, `ProfessionalId`, `StartAt`, `EndAt`, `Reason?`, `CreatedAt`)
- [X] T015 [P] Criar entidade `src/omniDesk.Api/Domain/Agenda/Appointment.cs` conforme data-model.md §Appointment (props completos: relacionais + temporais + estado + timestamps)
- [X] T016 [P] Criar entidade `src/omniDesk.Api/Domain/Agenda/AgendaSettings.cs` (props: `Id` smallint, `LateCancelWindowHours`, `LateCancelText`, `CancellationPolicyText`, `UpdatedAt`)
- [X] T017 Criar migration `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Agenda_ServicesAndProfessionals.sql` com `services`, `professionals`, `professional_services`, FKs, índices, unique parcial `(attendant_id)` (depende de T010–T012)
- [X] T018 Criar migration `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Agenda_SchedulesAndBlocks.sql` com `CREATE EXTENSION IF NOT EXISTS btree_gist`, `weekly_schedules` (CHECK day 0–6 e start<end), `schedule_blocks` (GIST index sobre `tstzrange`) (depende de T013–T014, T017)
- [X] T019 Criar migration `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Agenda_Appointments.sql` com `appointments`, FKs para `professionals`/`services`/`contacts`/`tickets`/`conversations`, CHECKs de status/client_type/created_by/cancelled_by, todos os índices (incluindo o UNIQUE parcial `(professional_id, start_at) WHERE status IN ('pending_confirmation','confirmed')`) (depende de T015, T018)
- [X] T020 Criar migration `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Agenda_Settings.sql` com `agenda_settings` singleton (`CHECK (id = 1)`, defaults, `INSERT ... ON CONFLICT DO NOTHING`) (depende de T016, T017)
- [X] T021 Criar `src/omniDesk.Api/Infrastructure/Agenda/AgendaModelConfiguration.cs` (Fluent API EF Core para Service, Professional, ProfessionalService, WeeklySchedule, ScheduleBlock, Appointment, AgendaSettings — `ToTable` com tenant schema, `HasKey`, `HasIndex`, conversions de status/client_type/created_by para string) (depende de T010–T016)
- [X] T022 Registrar `AgendaModelConfiguration` no `TenantDbContext.OnModelCreating` em `src/omniDesk.Api/Infrastructure/Persistence/TenantDbContext.cs` (depende de T021)
- [X] T023 Estender `src/omniDesk.Api/Infrastructure/Persistence/TenantMigrationsRunner.cs` para aplicar as 4 novas migrations em ordem (depende de T017–T020)
- [X] T024 [P] Criar `src/omniDesk.Api/Infrastructure/Agenda/AppointmentSlotLockService.cs` com método `AcquireAsync(tenantSlug, professionalId, startAt, ttlSeconds=10, ct)` usando Redis `SETNX` + TTL (depende de T005)
- [X] T025 [P] Criar `src/omniDesk.Api/Infrastructure/Agenda/AppointmentEventStore.cs` (implementa `IAppointmentEventStore.AppendAsync`) escrevendo em `{slug}_appointment_events` via `IActivityLogStore` (reaproveita store da Spec 006)

**Checkpoint**: 7 tabelas existem em `tenant_*`, entidades C# compiláveis, EF mapping registrado. User stories podem começar.

---

## Phase 3: User Story 1 — Catálogo de Serviços (Priority: P1) 🎯 MVP

**Goal**: `tenant_admin` cria/edita/desativa serviços; tela do CRM acessível em **Configurações → Serviços**; serviço inativo some de novos agendamentos e seletores; agendamentos existentes preservados.

**Independent Test**: criar 3 serviços com durações distintas, editar um, desativar outro; confirmar via REST `GET /api/services?include_inactive=true` que existe 1 inativo; tentar criar com `duration_minutes = 0` retorna `SERVICE_DURATION_INVALID`; `tenant_attendant` recebe 403 em endpoints de escrita.

### Tests for User Story 1 ⚠️ (escrever ANTES da implementação)

- [X] T026 [P] [US1] Contract test `ServicesEndpointContractTests` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Agenda/Services/ServicesEndpointContractTests.cs` validando shape de request/response contra `contracts/services-api.md` (GET list, POST create, PUT update, PATCH toggle, error codes)
- [X] T027 [P] [US1] Integration test `ServicesEndpointTests` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Agenda/Services/ServicesEndpointTests.cs` cobrindo: criar serviço, listar com filtro `include_inactive`, editar, desativar, role enforcement (attendant recebe 403), validação `duration_minutes <= 0`, soft delete preserva agendamentos existentes

### Implementation for User Story 1

- [X] T028 [P] [US1] Criar `src/omniDesk.Api/Features/Agenda/Validators/CreateServiceValidator.cs` e `UpdateServiceValidator.cs` (FluentValidation: name 1–100, duration > 0, price ≥ 0 se presente, category ≤ 100, description ≤ 2000)
- [X] T029 [US1] Criar `src/omniDesk.Api/Infrastructure/Agenda/ServiceRepository.cs` (CRUD + soft toggle; usa `TenantDbContext`)
- [X] T030 [P] [US1] Criar `src/omniDesk.Api/Features/Agenda/Services/Queries/ListServicesQuery.cs` (paginado, filtro `include_inactive`, sort)
- [X] T031 [P] [US1] Criar `src/omniDesk.Api/Features/Agenda/Services/Commands/CreateServiceCommand.cs`
- [X] T032 [P] [US1] Criar `src/omniDesk.Api/Features/Agenda/Services/Commands/UpdateServiceCommand.cs`
- [X] T033 [P] [US1] Criar `src/omniDesk.Api/Features/Agenda/Services/Commands/ToggleServiceCommand.cs`
- [X] T034 [US1] Criar `src/omniDesk.Api/Features/Agenda/Services/ServicesEndpoints.cs` mapeando GET/POST/PUT/PATCH em `app.MapGroup("/api/services").MapServicesEndpoints().RequireAuthorization()` (depende de T028–T033)
- [X] T035 [US1] Registrar `ServicesEndpoints`, `ServiceRepository`, validators, commands no DI em `src/omniDesk.Api/Program.cs`
- [X] T036 [P] [US1] Criar `src/omniDesk.Crm/src/app/features/services-catalog/services.service.ts` (HTTP client tipado para `/api/services`)
- [X] T037 [P] [US1] Criar `src/omniDesk.Crm/src/app/features/services-catalog/services.service.spec.ts` (mock HttpClient, testa GET/POST/PUT/PATCH)
- [X] T038 [P] [US1] Criar `src/omniDesk.Crm/src/app/features/services-catalog/services-list.component.{ts,html,scss}` (PrimeNG Table com filtro ativos/inativos, sort, action buttons)
- [X] T039 [P] [US1] Criar `src/omniDesk.Crm/src/app/features/services-catalog/services-list.component.spec.ts`
- [X] T040 [P] [US1] Criar `src/omniDesk.Crm/src/app/features/services-catalog/service-form.component.{ts,html,scss}` (Reactive Form: name, description textarea, category, duration_minutes number, price ngx-mask, requires_confirmation toggle; PrimeNG components)
- [X] T041 [P] [US1] Criar `src/omniDesk.Crm/src/app/features/services-catalog/service-form.component.spec.ts`
- [X] T042 [US1] Criar `src/omniDesk.Crm/src/app/features/services-catalog/services-catalog.routes.ts` (lazy load com guard `canManageAgenda`)
- [X] T043 [US1] Adicionar rota `/configuracoes/servicos` em `src/omniDesk.Crm/src/app/app.routes.ts` e item de menu condicional (`*ngIf="canManageAgenda$"`) na sidebar

**Checkpoint**: US1 funcional. Catálogo persistido; CRM `tenant_admin` consegue gerenciar; suite `ServicesEndpointTests` verde.

---

## Phase 4: User Story 2 — Profissionais, Serviços Vinculados e Disponibilidade (Priority: P1) 🎯 MVP

**Goal**: `tenant_admin` cadastra profissionais (com vínculo opcional a atendente e departamento), associa serviços do catálogo (`professional_services`), define disponibilidade semanal multi-turno e bloqueios pontuais. Profissional inativo e serviço inativo desaparecem de seletores de novos agendamentos.

**Independent Test**: criar "Dra. Ana Lima" sem atendente vinculado; associar 2 serviços; cadastrar Seg-Sex 08:00–12:00 + 14:00–18:00; criar bloqueio "Férias 10/06–17/06"; tentar criar bloqueio sobrepondo agendamento existente retorna `BLOCK_OVERLAPS_APPOINTMENTS` com IDs no `details`.

### Tests for User Story 2 ⚠️

- [X] T044 [P] [US2] Contract test `ProfessionalsEndpointContractTests` em `tests/Features/Agenda/Professionals/ProfessionalsEndpointContractTests.cs` (GET list, POST create, PUT update, PATCH toggle, sub-rotas `/services` `/schedule` `/blocks`)
- [X] T045 [P] [US2] Integration test `ProfessionalsEndpointTests` em `tests/Features/Agenda/Professionals/ProfessionalsEndpointTests.cs` (CRUD, vínculo opcional com atendente, unique partial `attendant_id`, filtros `department_id`/`service_id`)
- [X] T046 [P] [US2] Integration test `ProfessionalServicesEndpointTests` em `tests/Features/Agenda/Professionals/ProfessionalServicesEndpointTests.cs` (replace-all diff, erro se service_id inválido)
- [X] T047 [P] [US2] Integration test `WeeklyScheduleEndpointTests` em `tests/Features/Agenda/Professionals/WeeklyScheduleEndpointTests.cs` (replace-all transacional, erro `INVALID_RANGE`, erro `OVERLAP`, erro `INVALID_DAY`)
- [X] T048 [P] [US2] Integration test `ScheduleBlocksEndpointTests` em `tests/Features/Agenda/Professionals/ScheduleBlocksEndpointTests.cs` (criar, listar `from`, deletar, erro `BLOCK_RANGE_INVALID`, erro `BLOCK_OVERLAPS_APPOINTMENTS` com lista de IDs)

### Implementation for User Story 2

- [X] T049 [P] [US2] Criar `src/omniDesk.Api/Features/Agenda/Validators/CreateProfessionalValidator.cs` e `UpdateProfessionalValidator.cs`
- [X] T050 [P] [US2] Criar `src/omniDesk.Api/Features/Agenda/Validators/WeeklyScheduleValidator.cs` (turnos: day 0–6, start<end, sem overlap entre turnos do mesmo dia)
- [X] T051 [P] [US2] Criar `src/omniDesk.Api/Features/Agenda/Validators/ScheduleBlockValidator.cs` (start<end)
- [X] T052 [US2] Criar `src/omniDesk.Api/Infrastructure/Agenda/ProfessionalRepository.cs` (CRUD + filtros + unique attendant check)
- [X] T053 [US2] Criar `src/omniDesk.Api/Infrastructure/Agenda/WeeklyScheduleRepository.cs` (replace-all em transação)
- [X] T054 [US2] Criar `src/omniDesk.Api/Infrastructure/Agenda/ScheduleBlockRepository.cs` (CRUD + query GIST de overlap contra appointments para a validação de criação)
- [X] T055 [P] [US2] Criar queries `ListProfessionalsQuery.cs`, `GetProfessionalServicesQuery.cs`, `GetWeeklyScheduleQuery.cs`, `ListBlocksQuery.cs` em `Features/Agenda/Professionals/Queries/`
- [X] T056 [P] [US2] Criar commands `CreateProfessionalCommand.cs`, `UpdateProfessionalCommand.cs`, `ToggleProfessionalCommand.cs` em `Features/Agenda/Professionals/Commands/`
- [X] T057 [P] [US2] Criar `Features/Agenda/Professionals/Commands/UpdateProfessionalServicesCommand.cs` (diff atomic em transação)
- [X] T058 [P] [US2] Criar `Features/Agenda/Professionals/Commands/UpdateWeeklyScheduleCommand.cs` (replace-all em transação, valida overlap)
- [X] T059 [P] [US2] Criar `Features/Agenda/Professionals/Commands/CreateBlockCommand.cs` (valida overlap contra appointments, retorna `BLOCK_OVERLAPS_APPOINTMENTS` com IDs)
- [X] T060 [P] [US2] Criar `Features/Agenda/Professionals/Commands/DeleteBlockCommand.cs`
- [X] T061 [US2] Criar `src/omniDesk.Api/Features/Agenda/Professionals/ProfessionalsEndpoints.cs` mapeando todas as rotas (`/api/professionals`, sub-rotas `/{id}/services`, `/{id}/schedule`, `/{id}/blocks`, `/{id}/blocks/{blockId}`) (depende de T049–T060)
- [X] T062 [US2] Registrar `ProfessionalsEndpoints` e repositórios no DI em `Program.cs`
- [X] T063 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/professionals/professionals.service.{ts,spec.ts}` (HTTP client com sub-rotas)
- [X] T064 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/professionals/professionals-list.component.{ts,html,scss,spec.ts}`
- [X] T065 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/professionals/professional-form.component.{ts,html,scss,spec.ts}` (campos: name, specialty, department dropdown, attendant dropdown)
- [X] T066 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/professionals/professional-services.component.{ts,html,scss,spec.ts}` (multi-select dos serviços do catálogo, replace-all no save)
- [X] T067 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/professionals/weekly-schedule.component.{ts,html,scss,spec.ts}` (matriz 7 dias × N turnos, add/remove turnos por dia)
- [X] T068 [P] [US2] Criar `src/omniDesk.Crm/src/app/features/professionals/schedule-blocks.component.{ts,html,scss,spec.ts}` (lista + form criar bloqueio)
- [X] T069 [US2] Criar `src/omniDesk.Crm/src/app/features/professionals/professionals.routes.ts` com guard `canManageAgenda`
- [X] T070 [US2] Adicionar rota `/configuracoes/profissionais` em `app.routes.ts` + item de menu

**Checkpoint**: US2 funcional. Profissionais cadastrados com serviços e disponibilidade. US1 + US2 já permitem configurar a agenda (sem agendamentos ainda).

---

## Phase 5: User Story 3 — Atendente cria e gerencia agendamentos manualmente (Priority: P1) 🎯 MVP

**Goal**: atendente cria agendamentos pela UI da Agenda (grade semanal + lista + aba Pendentes). Sistema calcula `end_at`, determina `client_type` autoritativamente, aplica regra de status (`pending_confirmation` vs `confirmed`), dispara `appointment_confirmation` WhatsApp ao virar confirmed. Atendente confirma/cancela/marca no-show/reenvia lembrete. Race-condition protegida em 3 camadas (Redis + UNIQUE + FOR UPDATE).

**Independent Test**: criar agendamento para "João Silva" novo cliente → status `pending_confirmation`, aparece em Pendentes; confirmar → status `confirmed`, WhatsApp enfileirado; criar 2º agendamento para mesmo João → `client_type=returning_client`, status `confirmed` direto; tentar criar 2 simultâneos no mesmo slot → exatamente 1 sucesso e 1 `APPOINTMENT_SLOT_CONFLICT`; marcar `no_show` no passado funciona; visibility policy: attendant de outro departamento não vê.

### Tests for User Story 3 ⚠️

- [X] T071 [P] [US3] Contract test `AppointmentsEndpointContractTests` em `tests/Features/Agenda/Appointments/AppointmentsEndpointContractTests.cs` contra `contracts/appointments-api.md`
- [X] T072 [P] [US3] Contract test `AvailabilityEndpointContractTests` em `tests/Features/Agenda/Availability/AvailabilityEndpointContractTests.cs` contra `contracts/availability-api.md`
- [X] T073 [P] [US3] Integration test `AvailabilityCalculatorTests` em `tests/Features/Agenda/Availability/AvailabilityCalculatorTests.cs` (slots dentro de turnos, subtração de bloqueios, subtração de appointments, profissional/serviço inativos retornam lista vazia, sem `professional_services` retorna vazio, slot no passado filtrado)
- [X] T074 [P] [US3] Integration test `CreateAppointmentCommandTests` em `tests/Features/Agenda/Appointments/CreateAppointmentCommandTests.cs` (cliente novo→pending, cliente retorno→confirmed, `requires_confirmation=true` override, end_at calculado, `client_type` autoritativo descarta input)
- [X] T075 [P] [US3] Integration test `AppointmentLifecycleTests` em `tests/Features/Agenda/Appointments/AppointmentLifecycleTests.cs` (confirm, cancel, no-show, resend-reminder, transições inválidas retornam `APPOINTMENT_INVALID_STATUS_TRANSITION`, no-show só após `start_at`)
- [X] T076 [P] [US3] Integration test `ConcurrentAppointmentCreationTests` em `tests/Features/Agenda/Appointments/ConcurrentAppointmentCreationTests.cs` usando `Task.WhenAll` com 2+ tentativas no mesmo slot → exatamente 1 sucesso, demais retornam 409 (cobre SC-005)
- [X] T077 [P] [US3] Integration test `AppointmentVisibilityPolicyTests` em `tests/Features/Agenda/Appointments/AppointmentVisibilityPolicyTests.cs` (TenantAdmin vê tudo; Supervisor por departamento; Attendant por departamento OR por `professional.attendant_id`)
- [X] T078 [P] [US3] Integration test `AppointmentSlotLockServiceTests` em `tests/Infrastructure/Agenda/AppointmentSlotLockServiceTests.cs` (SETNX adquire; segunda tentativa falha; TTL expira; release manual)
- [X] T079 [P] [US3] Integration test `AppointmentEventStoreTests` em `tests/Infrastructure/Agenda/AppointmentEventStoreTests.cs` (append imutável; query cronológica por appointment_id)

### Implementation for User Story 3

- [X] T080 [P] [US3] Criar `src/omniDesk.Api/Features/Agenda/Availability/Slot.cs` (value object `record struct Slot(DateTimeOffset StartAt, DateTimeOffset EndAt)`)
- [X] T081 [US3] Criar `src/omniDesk.Api/Features/Agenda/Availability/AvailabilityCalculator.cs` implementando algoritmo de research §R1 (carrega service, weekly_schedules, schedule_blocks, appointments; merge intervals; gera slots) (depende de T080, T021)
- [X] T082 [P] [US3] Criar `src/omniDesk.Api/Features/Agenda/Availability/AvailabilityEndpoint.cs` mapeando `GET /api/availability` (depende de T081)
- [X] T083 [P] [US3] Criar `src/omniDesk.Api/Features/Agenda/Appointments/ClientTypeResolver.cs` (`ResolveAsync(contactId, ct)`; query autoritativa em `appointments` por status `confirmed`/`no_show`)
- [X] T084 [P] [US3] Criar `src/omniDesk.Api/Features/Agenda/Appointments/IAppointmentVisibilityPolicy.cs` e `AppointmentVisibilityPolicy.cs` (regras de research §R8: TenantAdmin/Supervisor/Attendant)
- [X] T085 [US3] Criar `src/omniDesk.Api/Infrastructure/Agenda/AppointmentRepository.cs` (queries com filtro de visibility, eager-load de professional/service/contact/ticket; query autoritativa de `client_type`)
- [X] T086 [P] [US3] Criar `src/omniDesk.Api/Features/Agenda/Validators/CreateAppointmentValidator.cs` (campos obrigatórios, start_at no futuro, professional+service+contact existem)
- [X] T087 [P] [US3] Criar `src/omniDesk.Api/Features/Agenda/Validators/CancelAppointmentValidator.cs` (cancellation_reason ≤ 255)
- [X] T088 [US3] Criar `src/omniDesk.Api/Features/Agenda/Appointments/Commands/CreateAppointmentCommand.cs` implementando: (1) lock Redis via `AppointmentSlotLockService`; (2) BEGIN TX; (3) revalida disponibilidade incluindo `FOR UPDATE`; (4) resolve `client_type` autoritativo; (5) calcula `end_at`; (6) INSERT; (7) catch `unique_violation` → 409; (8) COMMIT; (9) `appointment.changed` action=created via `AppointmentEventPublisher`; (10) se `confirmed`, chama `INotificationService.NotifyAppointmentConfirmedAsync` (depende de T024, T081, T083, T085)
- [X] T089 [P] [US3] Criar `Features/Agenda/Appointments/Commands/UpdateAppointmentCommand.cs` (recalcula end_at, revalida disponibilidade excluindo o próprio appointment, action=rescheduled se start_at/service_id mudaram, NÃO re-dispara confirmation per research §R7)
- [X] T090 [P] [US3] Criar `Features/Agenda/Appointments/Commands/ConfirmAppointmentCommand.cs` (transição pending→confirmed, dispara `INotificationService.NotifyAppointmentConfirmedAsync`, event action=confirmed)
- [X] T091 [P] [US3] Criar `Features/Agenda/Appointments/Commands/CancelAppointmentCommand.cs` (transições pending→cancelled, confirmed→cancelled; persiste `cancelled_by=attendant`, `cancelled_at`, `cancellation_reason`; event action=cancelled channel=crm)
- [X] T092 [P] [US3] Criar `Features/Agenda/Appointments/Commands/MarkNoShowCommand.cs` (validar status=confirmed E start_at <= now())
- [X] T093 [P] [US3] Criar `Features/Agenda/Appointments/Commands/ResendReminderCommand.cs` (validar status=confirmed E contato tem phone; atualiza `reminder_sent_at=now()`; enfileira template via `OutgoingMessagePublisher` Spec 008; event action=reminder_resent; erro `WHATSAPP_CHANNEL_INACTIVE` se canal inativo)
- [X] T094 [P] [US3] Criar `Features/Agenda/Appointments/Queries/ListAppointmentsQuery.cs` (paginado + filtros profissional/serviço/status/from/to; aplica visibility policy)
- [X] T095 [P] [US3] Criar `Features/Agenda/Appointments/Queries/GetAppointmentQuery.cs` (carrega appointment + history do MongoDB `{slug}_appointment_events`; aplica visibility policy)
- [X] T096 [US3] Criar `src/omniDesk.Api/Hubs/Events/AppointmentEvents.cs` (constants `Type = "appointment.changed"`, `Action.Created/Confirmed/Cancelled/NoShow/Rescheduled/ReminderSent/ReminderResent`)
- [X] T097 [US3] Criar `src/omniDesk.Api/Infrastructure/WebSockets/AppointmentEventPublisher.cs` (`IAppointmentEventPublisher.PublishAsync(appointment, action, actor, ct)` publica em `RedisKeys.WsCrmDept(slug, deptId)` e `RedisKeys.WsAttendant(slug, attendantId)`) (depende de T096)
- [X] T098 [US3] Criar `src/omniDesk.Api/Features/Agenda/Appointments/AppointmentsEndpoints.cs` mapeando todas as rotas: GET list/detail, POST, PUT, PATCH `/confirm` `/cancel` `/no-show`, POST `/resend-reminder`. Cada handler chama o command correspondente (depende de T088–T095, T097)
- [X] T099 [US3] Registrar `AppointmentsEndpoints`, `AvailabilityEndpoint`, `AvailabilityCalculator`, `ClientTypeResolver`, `IAppointmentVisibilityPolicy`, `AppointmentRepository`, `AppointmentEventPublisher` no DI em `Program.cs`
- [X] T100 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/appointments.service.{ts,spec.ts}` (HTTP client + assinar WS `appointment.changed` via `notification-stream.service.ts` existente)
- [X] T101 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/availability.service.{ts,spec.ts}` (HTTP client para `GET /api/availability`)
- [X] T102 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/appointment-card.component.{ts,html,scss,spec.ts}` (badge Novo/Retorno, serviço+preço, horário, status com cor, profissional)
- [X] T103 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/weekly-grid.component.{ts,html,scss,spec.ts}` (grade semanal por profissional usando PrimeNG `p-fullCalendar` ou implementação custom; slots coloridos por status; clique vazio abre form; clique em card abre detalhe)
- [X] T104 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/appointments-list.component.{ts,html,scss,spec.ts}` (PrimeNG Table com filtros profissional/serviço/status/período)
- [X] T105 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/pending-appointments.component.{ts,html,scss,spec.ts}` (lista pendentes com botões Confirmar/Editar/Cancelar inline)
- [X] T106 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/appointment-form.component.{ts,html,scss,spec.ts}` (Reactive Form: profissional, serviço filtrado por professional_services, autocomplete contato, datepicker, slots disponíveis via `availability.service`)
- [X] T107 [P] [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/appointment-detail.component.{ts,html,scss,spec.ts}` (dados editáveis, history, links para ticket/conversa/contato, ações Confirmar/Cancelar/No-show/Reenviar)
- [X] T108 [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/agenda-page.component.{ts,html,scss,spec.ts}` (container com 3 abas: Grade Semanal · Lista · Pendentes)
- [X] T109 [US3] Criar `src/omniDesk.Crm/src/app/features/agenda/agenda.routes.ts` (rotas lazy; permissão `Appointments.View`)
- [X] T110 [US3] Adicionar rota `/agenda` em `app.routes.ts` + item de menu visível a todos os atendentes

**Checkpoint**: US3 funcional. MVP completo (US1+US2+US3): clínica consegue operar manualmente sem IA. SC-002, SC-005, SC-008, SC-010 verificáveis.

---

## Phase 6: User Story 4 — IA consulta disponibilidade e cria agendamento via chat (Priority: P2)

**Goal**: orquestrador da Spec 006 ganha duas tools (`check_availability`, `create_appointment`); IA agenda durante conversa Live Chat/WhatsApp; `client_type` informado é descartado pelo backend; conflito de slot dispara re-consulta.

**Independent Test**: tenant com `agenda_enabled=true`; conversar via widget "Quero marcar Sessão de Fisioterapia com a Dra. Ana para sexta"; IA chama tools; agendamento criado com `created_by=ai`, `conversation_id` preenchido; segundo cliente que pede o mesmo slot recebe slot alternativo.

### Tests for User Story 4 ⚠️

- [ ] T111 [P] [US4] Integration test `CheckAvailabilityToolTests` em `tests/Features/Agenda/Tools/CheckAvailabilityToolTests.cs` (paridade com endpoint REST — mesma resposta para os mesmos args)
- [ ] T112 [P] [US4] Integration test `CreateAppointmentToolTests` em `tests/Features/Agenda/Tools/CreateAppointmentToolTests.cs` (cria contato se phone novo, descarta client_type da IA, conversation_id setado, conflito retorna erro)
- [ ] T113 [P] [US4] Integration test `ToolRegistryAgendaTests` em `tests/Infrastructure/AgentRuntime/ToolRegistryAgendaTests.cs` (tools registradas só se `agenda_enabled=true` no `ai_settings`)

### Implementation for User Story 4

- [ ] T114 [P] [US4] Criar `src/omniDesk.Api/Features/Agenda/Tools/CheckAvailabilityTool.cs` implementando `ITool` (Spec 006); parsea args, chama `IAvailabilityCalculator`, serializa resposta JSON
- [ ] T115 [P] [US4] Criar `src/omniDesk.Api/Features/Agenda/Tools/CreateAppointmentTool.cs` implementando `ITool`; usa `IContactRepository.FindOrCreateByPhoneAsync`; chama `CreateAppointmentCommand` com `created_by=ai`, `conversation_id` do `IAgentContext`; descarta `client_type` do payload
- [ ] T116 [US4] Estender `src/omniDesk.Api/Infrastructure/AgentRuntime/ToolRegistry.cs` para registrar `CheckAvailabilityTool` e `CreateAppointmentTool` quando `settings.AgendaEnabled == true` (depende de T114, T115)
- [ ] T117 [US4] Adicionar campo `AgendaEnabled` em `Domain/AiSettings/AiSettings.cs` (default `false` para tenants legacy) — migration `Add_AgendaEnabled_To_AiSettings.sql` se ainda não existir
- [ ] T118 [US4] Atualizar Spec 006 system prompt template para mencionar as tools (ver `Features/AgentRuntime/Prompts/system-prompt.md` ou equivalente) — descrição curta orientando "consulte disponibilidade antes de propor horários" e "NUNCA invente IDs"
- [ ] T119 [US4] Quickstart manual: validar fluxo end-to-end via Live Chat widget (script em `quickstart.md` §6 será expandido após implementação)

**Checkpoint**: US4 funcional. SC-003 verificável.

---

## Phase 7: User Story 5 — Cliente cancela agendamento via "NÃO" no WhatsApp (Priority: P2)

**Goal**: cliente responde "NÃO" ao lembrete; sistema cancela o agendamento elegível mais cedo na janela de 26h, responde com texto de política + (se aplicável) aviso de cancelamento tardio; notifica atendente in-app.

**Independent Test**: agendamento confirmed amanhã, `reminder_sent_at` há 2h; webhook WhatsApp inbound "NÃO" → status=cancelled, cancelled_by=client, resposta WA enfileirada com texto de política, notificação in-app criada. Variações "Não"/"NAO"/"não" funcionam. Fora da janela (>26h ou reminder_sent_at null) → mensagem processada como normal pela IA.

### Tests for User Story 5 ⚠️

- [ ] T120 [P] [US5] Unit test `ReminderResponseInterpreterTests` em `tests/Features/Agenda/Cancellation/ReminderResponseInterpreterTests.cs` (normalização "NÃO"/"Não"/"nao"/"NAO"; lookup correto pelo `conversation_id`; janela 26h; múltiplos elegíveis cancela o mais cedo; sem elegível retorna `NotApplicable`)
- [ ] T121 [P] [US5] Integration test `CancelAppointmentByClientCommandTests` em `tests/Features/Agenda/Cancellation/CancelAppointmentByClientCommandTests.cs` (cancela appointment, late cancel inclui aviso, fora da janela não inclui, resposta WhatsApp enfileirada, notificação in-app via Spec 010)
- [ ] T122 [P] [US5] Integration test `WaWebhookProcessorJobReminderResponseTests` em `tests/Features/WhatsApp/Webhook/WaWebhookProcessorJobReminderResponseTests.cs` (webhook "NÃO" elegível pula IA; webhook "NÃO" não elegível segue para IA; outro texto sempre segue para IA)

### Implementation for User Story 5

- [ ] T123 [P] [US5] Criar `src/omniDesk.Api/Features/Agenda/Cancellation/ReminderResponseInterpreter.cs` (`Outcome` enum: `NotApplicable | OutsideWindow | Cancelled(appointment)`); função pura de normalização + query SQL
- [ ] T124 [US5] Estender `src/omniDesk.Api/Features/Notifications/INotificationService.cs` adicionando `NotifyAppointmentCancelledByClientAsync(Guid? ticketId, Guid appointmentId, string contactName, DateTimeOffset startAt, CancellationToken ct)` (assinatura conforme `contracts/whatsapp-cancellation.md`)
- [ ] T125 [US5] Estender `src/omniDesk.Api/Features/Notifications/NotificationService.cs` implementando o novo método (cria notification + WS + push, mesmo padrão dos outros eventos)
- [ ] T126 [US5] Criar `src/omniDesk.Api/Features/Agenda/Cancellation/CancelAppointmentByClientCommand.cs` (cancela appointment; carrega `agenda_settings`; renderiza resposta WA; enfileira via `OutgoingMessagePublisher` com `sender_type=system`, `message_type=text`; emite event action=cancelled channel=whatsapp; chama `NotifyAppointmentCancelledByClientAsync`; publica WS `appointment.changed`) (depende de T123, T124, T125)
- [ ] T127 [US5] Estender `src/omniDesk.Api/Features/WhatsApp/Webhook/WaWebhookProcessorJob.cs` (`ProcessAsync`): após resolver conversation+message, antes do despacho à IA, chamar `ReminderResponseInterpreter.TryInterpretAsync(...)`. Se `Cancelled` → executar `CancelAppointmentByClientCommand` e retornar (early-return). Senão → fluxo normal. (depende de T123, T126)
- [ ] T128 [P] [US5] Estender `src/omniDesk.Crm/src/app/features/notifications/notification-item.component.html` (e/ou map de tipos no `notifications.service.ts`) para renderizar evento `appointment.cancelled_by_client` com link `/agenda/{appointmentId}` — apenas se este evento já não estiver coberto pelo render genérico da Spec 010

**Checkpoint**: US5 funcional. SC-006, SC-009 verificáveis.

---

## Phase 8: User Story 6 — Tenant configura política de cancelamento tardio (Priority: P3)

**Goal**: `tenant_admin` ajusta janela (horas) e texto via **CRM → Configurações → Agenda**.

**Independent Test**: alterar janela para 12h e texto custom; cancelar agendamento via WhatsApp com `start_at - now() = 8h` → resposta inclui texto custom; cancelar com `start_at - now() = 24h` → não inclui.

### Tests for User Story 6 ⚠️

- [ ] T129 [P] [US6] Contract test `AgendaSettingsEndpointContractTests` em `tests/Features/Agenda/Settings/AgendaSettingsEndpointContractTests.cs`
- [ ] T130 [P] [US6] Integration test `AgendaSettingsEndpointTests` em `tests/Features/Agenda/Settings/AgendaSettingsEndpointTests.cs` (GET retorna defaults, PUT valida `late_cancel_window_hours > 0`, role enforcement)

### Implementation for User Story 6

- [ ] T131 [P] [US6] Criar `src/omniDesk.Api/Features/Agenda/Validators/AgendaSettingsValidator.cs` (`late_cancel_window_hours > 0`, textos ≤ 2000)
- [ ] T132 [US6] Criar `src/omniDesk.Api/Infrastructure/Agenda/AgendaSettingsRepository.cs` (GET/UPDATE singleton)
- [ ] T133 [P] [US6] Criar `src/omniDesk.Api/Features/Agenda/Settings/Commands/UpdateAgendaSettingsCommand.cs`
- [ ] T134 [US6] Criar `src/omniDesk.Api/Features/Agenda/Settings/AgendaSettingsEndpoints.cs` mapeando GET/PUT `/api/agenda-settings` com `RequirePermission(AgendaSettings.Manage)` (depende de T131–T133)
- [ ] T135 [US6] Registrar `AgendaSettingsEndpoints` no DI em `Program.cs`
- [ ] T136 [P] [US6] Criar `src/omniDesk.Crm/src/app/features/agenda-settings/agenda-settings.service.{ts,spec.ts}`
- [ ] T137 [P] [US6] Criar `src/omniDesk.Crm/src/app/features/agenda-settings/settings-page.component.{ts,html,scss,spec.ts}` (3 controles: window number input, late_cancel_text textarea, cancellation_policy_text textarea)
- [ ] T138 [US6] Criar `src/omniDesk.Crm/src/app/features/agenda-settings/agenda-settings.routes.ts` (lazy, guard `canManageAgenda`)
- [ ] T139 [US6] Adicionar rota `/configuracoes/agenda` em `app.routes.ts` + item de menu

**Checkpoint**: US6 funcional. Toda a Spec 011 entregue end-to-end.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: métricas, documentação, validação operacional, dívidas técnicas.

- [ ] T140 [P] Adicionar contadores Prometheus em pontos relevantes via `System.Diagnostics.Metrics` (padrão Spec 010): `appointments_created_total{tenant,source,status_inicial}` em `CreateAppointmentCommand`, `appointment_cancellations_total{tenant,by,channel}` em `CancelAppointmentCommand` e `CancelAppointmentByClientCommand`, `appointment_no_show_total{tenant}` em `MarkNoShowCommand`, `availability_query_duration_seconds{tenant}` em `AvailabilityCalculator`, `reminder_response_no_total{tenant,outcome}` em `ReminderResponseInterpreter`, `appointment_slot_conflict_total{tenant,layer}` em `CreateAppointmentCommand` (research §R12)
- [ ] T141 [P] Atualizar `src/omniDesk.Api/Features/Agenda/README.md` com diagrama de fluxo (criação manual + IA + cancelamento via "NÃO") e referências aos contracts
- [ ] T142 [P] Revisar performance dos índices PG após carga de teste — confirmar `idx_ap_prof_start`, `idx_ap_contact_status_start`, `idx_ap_reminder_pending`, `idx_ap_conv_confirmed`, `idx_ap_slot_unique`, `idx_sb_overlap` (GIST) todos sendo usados (EXPLAIN ANALYZE nos paths críticos)
- [ ] T143 Remover o `LogWarning` de "tabela appointments não existe" em `src/omniDesk.Api/Infrastructure/Appointments/AppointmentReadRepository.cs` (Spec 010 — graceful empty agora desnecessário; tabela existe)
- [ ] T144 [P] Atualizar `docs/specs/11-agenda.spec.md` se houve drift entre a spec original e a implementação (ex.: códigos de erro descobertos durante implementação) — analogous a T184 da Spec 009
- [ ] T145 Atualizar `CLAUDE.md` (entre marcadores `<!-- SPECKIT START -->` / `<!-- SPECKIT END -->`) marcando Spec 011 como ✅ IMPLEMENTADO com sumário final (n/N tasks, mudanças relevantes, dívidas se houver)
- [ ] T146 Rodar `quickstart.md` ponta-a-ponta em ambiente local e escrever `specs/011-agenda-services/quickstart-evidences.md` com saídas reais dos 8 smoke tests REST + WS + race condition + métricas (padrão Spec 009/010)
- [ ] T147 [P] Adicionar entry em `docs/DEPENDENCIES.md` confirmando que Spec 010 deixa de ter graceful-empty (Spec 011 satisfaz dep)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Fase 1)**: sem deps — pode começar imediatamente.
- **Foundational (Fase 2)**: depende de Fase 1. **BLOQUEIA** todas as US.
- **US1 Catálogo (Fase 3)**: depende de Fase 2.
- **US2 Profissionais (Fase 4)**: depende de Fase 2. Pode rodar em paralelo com US1 (arquivos distintos).
- **US3 Agendamentos (Fase 5)**: depende de Fase 2 + US1 + US2 (precisa de serviços e profissionais cadastrados para criar agendamentos). **MVP completa aqui.**
- **US4 IA Tools (Fase 6)**: depende de US3 (reusa `CreateAppointmentCommand`).
- **US5 Cancelamento WhatsApp (Fase 7)**: depende de US3 (reusa entidade Appointment e WS event) + Spec 008 (WhatsApp) + Spec 010 (`INotificationService`). Pode rodar em paralelo com US4 e US6.
- **US6 Settings (Fase 8)**: depende de Fase 2 (precisa de `agenda_settings` table). **Pode rodar em paralelo com US3+, mas US5 consome a config — entregar US6 antes ou junto.**
- **Polish (Fase 9)**: depende de todas as US.

### Within Each User Story

- Tests escritos PRIMEIRO (TDD soft: testes devem falhar antes da implementação).
- Validators e value objects antes de commands.
- Commands antes de endpoints.
- Backend antes do frontend (frontend chama a API real, sem stub).
- Cada US deve ter checkpoint válido independente.

### Parallel Opportunities

- **Fase 1**: T002, T003, T004, T005 todos `[P]` — 4 dev-streams.
- **Fase 2**: T006–T016 (entidades + constants) todos `[P]` — 11 dev-streams.
- **Fase 3 (US1)**: tests T026 + T027 em paralelo; commands T031–T033 em paralelo; frontend T036–T041 em paralelo.
- **Fase 4 (US2)**: tests T044–T048 em paralelo; commands T056–T060 em paralelo; frontend T063–T068 em paralelo.
- **Fase 5 (US3)**: tests T071–T079 em paralelo; commands T089–T095 em paralelo (T088 não pode ser paralelo — depende de muitos serviços); frontend T100–T107 em paralelo.
- **Fase 6 (US4)**: tests T111–T113 em paralelo; tools T114–T115 em paralelo.
- **Fase 7 (US5)**: tests T120–T122 em paralelo; impl T123 (Interpreter) e T124/T125 (INotificationService) em paralelo.
- **Fase 8 (US6)**: tests T129–T130 em paralelo; impl frontend T136–T137 em paralelo.

---

## Parallel Example: User Story 3 (US3 — Agendamentos Manuais)

```bash
# Todos os testes da US3 podem rodar juntos:
Task: "Contract test AppointmentsEndpoint em tests/Features/Agenda/Appointments/AppointmentsEndpointContractTests.cs"
Task: "Contract test AvailabilityEndpoint em tests/Features/Agenda/Availability/AvailabilityEndpointContractTests.cs"
Task: "Integration test AvailabilityCalculator em tests/Features/Agenda/Availability/AvailabilityCalculatorTests.cs"
Task: "Integration test CreateAppointmentCommand em tests/Features/Agenda/Appointments/CreateAppointmentCommandTests.cs"
Task: "Integration test AppointmentLifecycle em tests/Features/Agenda/Appointments/AppointmentLifecycleTests.cs"
Task: "Integration test ConcurrentAppointmentCreation em tests/Features/Agenda/Appointments/ConcurrentAppointmentCreationTests.cs"
Task: "Integration test AppointmentVisibilityPolicy em tests/Features/Agenda/Appointments/AppointmentVisibilityPolicyTests.cs"
Task: "Integration test AppointmentSlotLockService em tests/Infrastructure/Agenda/AppointmentSlotLockServiceTests.cs"
Task: "Integration test AppointmentEventStore em tests/Infrastructure/Agenda/AppointmentEventStoreTests.cs"

# Frontend components em paralelo após T100/T101 (services):
Task: "Criar appointment-card.component em src/omniDesk.Crm/src/app/features/agenda/"
Task: "Criar weekly-grid.component em src/omniDesk.Crm/src/app/features/agenda/"
Task: "Criar appointments-list.component em src/omniDesk.Crm/src/app/features/agenda/"
Task: "Criar pending-appointments.component em src/omniDesk.Crm/src/app/features/agenda/"
Task: "Criar appointment-form.component em src/omniDesk.Crm/src/app/features/agenda/"
Task: "Criar appointment-detail.component em src/omniDesk.Crm/src/app/features/agenda/"
```

---

## Implementation Strategy

### MVP First (US1 + US2 + US3)

1. Completar Fase 1 (Setup).
2. Completar Fase 2 (Foundational) — **gate crítico**.
3. Completar Fase 3 (US1 Catálogo) e Fase 4 (US2 Profissionais) — podem ser entregues por devs distintos em paralelo.
4. Completar Fase 5 (US3 Agendamentos Manuais).
5. **STOP & VALIDATE**: clínica consegue cadastrar serviços + profissionais + criar/gerenciar agendamentos manualmente. Rodar quickstart §6.1–§6.7.
6. Deploy/demo MVP.

### Incremental Delivery (recomendado)

1. MVP (US1+US2+US3) → deploy/demo.
2. Adicionar US4 (IA Tools) → demo: IA agendando autonomamente.
3. Adicionar US5 (Cancelamento WhatsApp) → demo: cliente cancela respondendo "NÃO".
4. Adicionar US6 (Settings) → demo: tenant ajusta política.
5. Polish (Fase 9) → métricas + docs + quickstart evidences → release.

### Parallel Team Strategy

Com 3 devs no MVP:

1. Todos completam Fase 1 + Fase 2 juntos.
2. Dev A: US1 (Catálogo, ~15 tasks).
3. Dev B: US2 (Profissionais, ~28 tasks).
4. Dev C: começa preparando testes da US3 (T071–T079).
5. Quando US1 e US2 entregam, todos convergem para US3.

Pós-MVP, paralelizar US4/US5/US6 entre 3 devs.

---

## Notes

- `[P]` = arquivos diferentes, sem dependência incompleta.
- `[US{n}]` mapeia ao escopo da user story na spec.md.
- Backend tests SEMPRE com Testcontainers (Postgres + Redis + Mongo) — sem mock de DB (constituição §VII).
- Frontend `.spec.ts` SEMPRE co-localizado.
- Constants em static classes (`AppointmentStatus.*`, `RedisKeys.*`, `ErrorCodes.*`) — sem magic strings.
- Commits scoped a 1 task (ou grupo lógico pequeno) — facilita rollback e review.
- Cada checkpoint = oportunidade de demo / deploy.
- Em conflito entre spec.md e contracts/, os contracts mandam (mais específicos).
