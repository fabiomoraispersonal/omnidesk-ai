# Implementation Plan: Agenda e Catálogo de Serviços (Spec 011)

**Branch**: `011-agenda-services` | **Data**: 2026-05-12 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/011-agenda-services/spec.md`

## Summary

Spec **Agenda e Catálogo de Serviços** entrega dois cadastros interdependentes mantidos juntos: o catálogo de serviços oferecidos (consultas, procedimentos, exames, avaliações com nome, duração, preço opcional, flag `requires_confirmation` e soft delete) e a agenda propriamente dita (profissionais, vínculos serviço↔profissional, disponibilidade semanal recorrente, bloqueios pontuais, agendamentos com ciclo `pending_confirmation` → `confirmed` → `cancelled` / `no_show`). V1 é estritamente **1 profissional × 1 cliente × 1 serviço por agendamento** — agendamentos de grupo, salas com capacidade > 1 e integrações com Google/Outlook ficam para V2+.

A spec materializa o que Spec 010 (Notifications) já estabeleceu como contrato:

- **`IAppointmentReadRepository`** (Spec 010 / `Infrastructure/Appointments/`) hoje lê via SQL bruto com fallback "tabela não existe → lista vazia". Esta spec entrega a tabela `tenant_{slug}.appointments` real, mas **mantém o repositório de leitura no formato atual** (mesma interface, mesmo DTO `AppointmentReminderDto`) para o `AppointmentReminderJob` da Spec 010 funcionar sem alterações. Possível upgrade incremental do repositório fica como cleanup.
- **`INotificationService`** (Spec 010 / `Features/Notifications/`) ganha **um** método novo: `NotifyAppointmentCancelledByClientAsync(...)` para o caso US5 (cancelamento via WhatsApp). Não duplicamos a stack de notificações; reutilizamos persist + WS + push existentes.
- **`WaWebhookProcessorJob`** (Spec 008 / `Features/WhatsApp/Webhook/`) recebe um novo passo antes do despacho à IA: o `ReminderResponseInterpreter` examina mensagens textuais de inbound, e — se for `"NÃO"` normalizado dentro da janela de 26h — pula a IA, executa o cancelamento e responde via `OutgoingMessagePublisher`. Não altera o pipeline existente; é um early-return condicional.

Esta spec é a primeira que cria entidades de **Agenda** no banco. O backend novo entrega:

- **6 tabelas tenant-scoped novas**:
  - `services` (catálogo)
  - `professionals` (com FK opcional `attendant_id` → `attendants` e FK opcional `department_id` → `departments`)
  - `professional_services` (junção)
  - `weekly_schedules` (turnos semanais recorrentes)
  - `schedule_blocks` (bloqueios pontuais)
  - `appointments` (agendamentos)
- **1 tabela tenant-scoped nova**: `agenda_settings` (configurações de cancelamento — singleton por tenant, padrão similar a `tenant_notification_settings` mas vivendo no schema do tenant porque é configuração operacional do CRM, não metadado do tenant).
- **~32 endpoints REST** organizados em 5 grupos (`/api/services`, `/api/professionals`, `/api/availability`, `/api/appointments`, `/api/agenda-settings`).
- **2 tool calls OpenAI** (`check_availability`, `create_appointment`) registradas no orquestrador (Spec 006), com a mesma lógica de disponibilidade exposta pelo endpoint REST — fonte única de verdade no serviço `AvailabilityCalculator`.
- **1 interpretador de webhook**: `ReminderResponseInterpreter` — normaliza texto, verifica conversa↔agendamento elegível, cancela, responde, notifica.
- **Sem novos jobs Hangfire**: o `AppointmentReminderJob` da Spec 010 já cobre o envio diário; esta spec só popula `reminder_sent_at` no fluxo já existente.

Frontend CRM (Angular 21) entrega 3 novas features e estende 0:

- **`features/services-catalog/`** — CRM → Configurações → Serviços (listagem com filtro ativos/inativos, criar/editar/desativar).
- **`features/professionals/`** — CRM → Configurações → Profissionais (listagem, criar/editar/desativar, sub-páginas: serviços vinculados, disponibilidade semanal, bloqueios).
- **`features/agenda/`** — CRM → Agenda (três views: grade semanal por profissional, lista cronológica com filtros, aba "Pendentes"). Detalhe de agendamento com ações.
- **`features/agenda-settings/`** — CRM → Configurações → Agenda (janela + texto de cancelamento tardio + texto de política).

Implementação faseada respeitando dependências internas e o princípio de "User Stories independentemente testáveis":

1. **Fase A (Foundation — Catálogo)** → entrega US1: migration `services`, domain entity, EF config, endpoints `/api/services`, frontend `services-catalog`. P1.
2. **Fase B (Foundation — Profissionais)** → entrega US2: migrations `professionals` + `professional_services` + `weekly_schedules` + `schedule_blocks`, domain entities, endpoints `/api/professionals`, frontend `professionals` (3 sub-páginas). P1.
3. **Fase C (Core — Agendamentos manuais + Disponibilidade)** → entrega US3 + a parte REST de US4: migration `appointments`, `AvailabilityCalculator`, endpoint `/api/availability`, endpoints `/api/appointments/*`, frontend `agenda` (grade + lista + pendentes + detalhe). P1.
4. **Fase D (IA — Tool calls)** → entrega US4 completo: registra `check_availability` e `create_appointment` no orquestrador de Spec 006; valida fluxo end-to-end via Live Chat e WhatsApp. P2.
5. **Fase E (Cancelamento via WhatsApp)** → entrega US5: migration `agenda_settings`, `ReminderResponseInterpreter`, integração no `WaWebhookProcessorJob`, novo método em `INotificationService`, resposta automática. P2.
6. **Fase F (Configurações de cancelamento tardio)** → entrega US6: endpoints `/api/agenda-settings`, frontend `agenda-settings`. P3.
7. **Fase G (Polish)** — métricas (`appointments_created_total{source}`, `appointment_cancellations_total{by}`, `availability_query_duration_seconds`), índices PostgreSQL extras se a Fase C revelar slowness, documentação operacional.

---

## Technical Context

**Backend**: C# .NET 10 — Minimal API + Endpoint Groups (continuação dos padrões 002–010).
**Frontend**: TypeScript — Angular 21 Standalone Components + Signals (CRM em `src/omniDesk.Crm/`). PrimeNG Calendar/Schedule para a grade semanal.
**ORM**: Entity Framework Core 10 + Migrations SQL tenant-scoped (padrão `Add_*` em `Infrastructure/Persistence/Migrations/`).

**Storage**:

- PostgreSQL tenant-scoped (todas em `tenant_{slug}.*`):
  - `services` — catálogo. Index `(is_active, name)` para listagem.
  - `professionals` — FK opcional `department_id`, `attendant_id`. Index `(is_active, name)`; unique parcial `(attendant_id) WHERE attendant_id IS NOT NULL` (FR-008: um atendente vinculado a no máximo um profissional).
  - `professional_services` — junção com unique `(professional_id, service_id)`. Index inverso `(service_id)` para "quais profissionais oferecem X?".
  - `weekly_schedules` — recorrente. Index `(professional_id, day_of_week)`.
  - `schedule_blocks` — pontual. Index `(professional_id, start_at, end_at)` via GIST sobre tstzrange (research §R3) para detecção de overlap eficiente.
  - `appointments` — central. Index `(professional_id, start_at)` (cálculo de disponibilidade), index `(contact_id, status, start_at)` (histórico de cliente para `client_type`), index parcial `(status, reminder_sent_at) WHERE status = 'confirmed' AND reminder_sent_at IS NULL` (job de lembrete), index parcial `(conversation_id, status) WHERE status = 'confirmed'` (lookup de cancelamento via "NÃO").
  - `agenda_settings` — singleton (PK fixa `id = 1` ou unique constraint sem PK). Defaults: `late_cancel_window_hours = 24`, `late_cancel_text = 'Cancelamentos com menos de 24h poderão ser cobrados.'`, `cancellation_policy_text = ''`.
- Redis:
  - `{slug}:appointment_slot_lock:{professional_id}:{start_at_iso}` — `SETNX` com TTL 10s usado por `CreateAppointmentCommand` como **lock de criação** (research §R2). Garante FR-023 (proteção contra race condition) sem precisar de `SELECT FOR UPDATE`. Backup: constraint UNIQUE em `(professional_id, start_at)` para `status IN ('pending_confirmation', 'confirmed')` via index parcial.
- MongoDB:
  - `{slug}_appointment_events` (nova collection) — recebe entrada imutável a cada transição de status (`created`, `confirmed`, `cancelled`, `no_show`, `reminder_sent`, `reminder_resent`). Reaproveita o `IActivityLogStore` da Spec 006 — apenas nova collection.

**Race-condition protection (FR-023, SC-005)** — solução em camadas:

1. **Camada 1 — Redis SETNX lock**: antes de inserir, `SETNX {slug}:appointment_slot_lock:{prof}:{start}` com TTL 10s. Se falhou, retorna 409 `APPOINTMENT_SLOT_CONFLICT` imediatamente.
2. **Camada 2 — index parcial UNIQUE no Postgres**: `CREATE UNIQUE INDEX ... ON appointments (professional_id, start_at) WHERE status IN ('pending_confirmation', 'confirmed')`. Se duas instâncias passarem o lock Redis simultaneamente (improvável, mas possível), o INSERT mais lento bate em `unique_violation` (23505) → tratado e retorna 409.
3. **Camada 3 — disponibilidade revalidada no transaction**: a transação que faz `INSERT INTO appointments` também roda a query de disponibilidade dentro do mesmo BEGIN; se o slot foi tomado entre o `check_availability` e o `create_appointment` por outra fonte (job de importação, etc.), aborta.

**Background jobs**:

- Spec 011 NÃO cria jobs novos. O `AppointmentReminderJob` da Spec 010 (cron diário per-tenant) já lê `appointments` via `IAppointmentReadRepository` e popula `reminder_sent_at` ao enviar o template `appointment_reminder` com sucesso. Esta spec apenas garante que a tabela existe e o campo é atualizado pela mesma escrita do job.

**WebSocket**: Spec 011 emite 1 evento novo no canal CRM (`{slug}:ws:crm:dept:{id}` e `{slug}:ws:attendant:{id}` quando aplicável):

- `appointment.changed` — `{ id, status, professional_id, service_id, start_at, end_at, action: 'created'|'confirmed'|'cancelled'|'no_show'|'rescheduled' }`. Frontend usa para atualizar a grade semanal e a aba "Pendentes" em tempo real. Sem novo endpoint WS; reutiliza `/ws/crm` da Spec 007.

**Integração com IA (Spec 006)**:

- Tools registradas via `IToolRegistry` (já existente em `src/omniDesk.Api/Features/AgentRuntime/`). Implementação dos dois tools em `Features/Agenda/Tools/`:
  - `CheckAvailabilityTool` → chama `AvailabilityCalculator.GetSlotsAsync(...)` (mesmo serviço usado pelo endpoint REST — fonte única).
  - `CreateAppointmentTool` → chama `CreateAppointmentCommand` (mesmo command que o CRM usa). `created_by = ai` setado no chamador.
- O `client_type` informado pela IA é **descartado** pelo backend (FR-020) — recalculado a partir do histórico do contato.
- Tools são registradas **por tenant** com a configuração da Spec 006 — apenas tenants com Spec 011 ativa têm acesso.

**Integração com WhatsApp (Spec 008)**:

- `WaWebhookProcessorJob.ProcessAsync` recebe extensão: antes de chamar `AgentOrchestrator`, consulta `ReminderResponseInterpreter.TryInterpretAsync(tenantSlug, conversationId, messageText)`. Se retorna `Cancelled`, pula o orquestrador, executa `CancelAppointmentByClientCommand`, enfileira resposta de confirmação via `OutgoingMessagePublisher` e retorna. Caso contrário, fluxo normal.
- `appointment_confirmation` e `appointment_reminder` continuam sendo enviados via `INotificationService` (Spec 010) — esta spec não toca no pipeline outgoing WhatsApp.

**Authorization (FR-005/011/041/047)**:

- Reutiliza `Roles.TenantAdmin`, `Roles.Supervisor`, `Roles.Attendant`. Define novas Permissions em `Domain/Authorization/Permissions.cs`:
  - `Services.Manage` — TenantAdmin only.
  - `Professionals.Manage` — TenantAdmin only.
  - `Appointments.View` — Attendant+ (visibilidade restrita por departamento via `IAppointmentVisibilityPolicy`).
  - `Appointments.Manage` — Attendant+ no escopo de visibilidade.
  - `AgendaSettings.Manage` — TenantAdmin only.
- `IAppointmentVisibilityPolicy` (novo) — recebe `(currentUser, appointment)` e retorna `bool`. Lógica: TenantAdmin → true; Supervisor → true para appointments cujo departamento (do profissional OR do ticket vinculado) ∈ depts do supervisor; Attendant → true se `professional.attendant_id == currentUser.id` OR `appointment.ticket.department_id` ∈ depts do attendant.

**Testing**:

- Backend: xUnit + Testcontainers (Postgres + Redis + Mongo). Testes principais:
  - `ServicesEndpointTests` — CRUD, soft delete, role enforcement.
  - `ProfessionalsEndpointTests` — CRUD, vínculo opcional com atendente, single-tenant guard.
  - `ProfessionalServicesEndpointTests` — diff de vínculos.
  - `WeeklyScheduleEndpointTests` — overlap, range inválido.
  - `ScheduleBlocksEndpointTests` — overlap com agendamentos rejeitado.
  - `AvailabilityCalculatorTests` — turnos – bloqueios – ocupados (combinações).
  - `CreateAppointmentCommandTests` — client_type novo/retorno, status pending/confirmed, requires_confirmation override, Redis lock + UNIQUE fallback, ai vs attendant.
  - `ConcurrentAppointmentCreationTests` — 2 threads disputando o mesmo slot, exatamente 1 sucesso.
  - `ReminderResponseInterpreterTests` — variações de "NÃO" (case/acento), janela de 26h, múltiplos elegíveis (cancela o mais cedo), sem elegível ignora.
  - `CancelAppointmentByClientCommandTests` — late-cancel inclui aviso, fora da janela não inclui, mensagem WhatsApp enfileirada, notificação in-app via Spec 010.
  - `AgendaSettingsEndpointTests` — defaults, validação, role enforcement.
  - `CheckAvailabilityToolTests` — paridade com o endpoint REST.
- Frontend: Karma + Jasmine `.spec.ts` co-localizados. Mocks de `HttpClient` e Signal-based stores. PrimeNG Calendar testado via DOM em jsdom.
- Contratos: testes em `tests/Contracts/` validam request/response shape de cada endpoint contra os arquivos `contracts/*.md` (mesma estratégia da Spec 009).

**Target Platform**: Linux ARM64 (API); Cloudflare Pages (CRM).

**Project Type**: Web service (API .NET 10) + 1 SPA Angular (CRM). Sem novo projeto.

**Performance Goals**:

- p95 `GET /api/availability` (tenant com 50 profissionais, 1000 appointments futuros, query de dia único): **< 500ms** (SC-008).
- p95 `POST /api/appointments` (criação manual via CRM): **< 800ms** incluindo cálculo de `client_type` e disparo de `appointment_confirmation`.
- p95 webhook "NÃO" → resposta WhatsApp enfileirada: **< 5s** (SC-006).

**Constraints**:

- Sem novas dependências externas (sem NuGet novo). PostgreSQL precisa do extension `btree_gist` para o índice de detecção de overlap de bloqueios — habilitado via migration (research §R3).
- IA tools dependem de Spec 006 (Agentes IA) — já entregue. Mesma assinatura/registro.
- `RecurringJob` de Spec 010 referencia `IAppointmentReadRepository`; manter assinatura **estritamente compatível** ao popular a tabela real (apenas remover o warning de "tabela não existe").

**Scale/Scope**:

- ~5–20 profissionais por tenant em V1; até ~50 serviços; até ~500 agendamentos/dia no pico operacional.
- Disponibilidade típica: 5 dias × 2 turnos = 10 entradas em `weekly_schedules` por profissional.
- 1–3 bloqueios futuros por profissional em qualquer momento.
- Janela típica de consulta de disponibilidade: 1 dia (UI) ou 7 dias (planejamento).

---

## Constitution Check

*Gate: passou pré-Fase 0; revalidado pós-Fase 1 (ver seção final).*

| Princípio | Compliance | Notas |
|---|---|---|
| **I. Multi-Tenant Isolation (NN)** | ✅ | Todas as 7 tabelas (`services`, `professionals`, `professional_services`, `weekly_schedules`, `schedule_blocks`, `appointments`, `agenda_settings`) residem em `tenant_{slug}.*`. Sem nada em `public.*`. Redis lock keys via novo `RedisKeys.AppointmentSlotLock(slug, professionalId, startAt)`. MongoDB `{slug}_appointment_events` segue o padrão `{slug}_*`. `agenda_settings` é singleton tenant-scoped (configuração operacional do CRM, não metadado do tenant — por isso fica no schema do tenant, ao contrário de `tenant_notification_settings` que é cfg sobre o tenant). |
| **II. AI-First, Human-Assisted** | ✅ | Tools `check_availability` e `create_appointment` ampliam o repertório do orquestrador. `client_type` informado pela IA é descartado pelo backend (autoritativo) — protege contra alucinação (FR-020). Cancelamento via WhatsApp não exige handoff humano (é ação direta do cliente) — não viola "AI primeiro" porque o cliente já passou pelo lembrete; é resposta operacional. Notificação in-app ao atendente preserva visibilidade humana. |
| **III. Channel Agnosticism** | ✅ | `appointment_confirmation` e `appointment_reminder` usam `OutgoingMessagePublisher` (canal-agnóstico). Cancelamento via WhatsApp é específico do canal `whatsapp`, mas o **interpretador** vive em `Features/Agenda/Cancellation/` (não em `Features/WhatsApp/`) — o webhook chama um serviço de domínio; a separação adapter↔domínio é preservada. Se um dia o Live Chat ganhar "responder 'NÃO' ao lembrete", o mesmo interpretador é chamado pelo adapter do Live Chat. |
| **IV. Security / LGPD (NN)** | ✅ | `cancellation_reason` e `notes` armazenados no schema do tenant (dados no Brasil). `appointments.client_name` (snapshot quando contato cria via IA) limitado a 255 chars; PII fica no contato vinculado (`contact_id`). Endpoints exigem JWT + role checks. Endpoint de cancelamento via WhatsApp valida que a mensagem vem de número conhecido na conversa (sem injeção). |
| **V. Simplicity / YAGNI** | ✅ | 0 libs novas. Sem CQRS framework adicional — segue o padrão `Commands/Queries/Handlers` já em uso. Sem cache de disponibilidade (consulta direta no DB com índices apropriados — performance comprovada em Spec 010 com cargas similares). Sem reagendamento direto (`POST /reschedule`) — atendente cancela e cria novo (assumption explícita). Sem capacidade > 1, sem agendamento de grupo, sem multi-recurso. |
| **VI. Observability / Auditability** | ✅ | Toda transição de status materializa entry em `{slug}_appointment_events`. Logs Serilog para Redis lock fail, UNIQUE violation fallback, IA tool calls. Métricas Prometheus planejadas: `appointments_created_total{source}`, `appointment_cancellations_total{by,channel}`, `availability_query_duration_seconds{tenant}`, `reminder_response_no_total{outcome}`. |
| **VII. Test Discipline** | ✅ | Backend tests com Testcontainers (Postgres + Redis + Mongo). Sem mock DB. Constants centralizadas (`AppointmentStatus`, `ClientType`, `CreatedBy`, `ErrorCodes.Appointments.*`). Frontend `.spec.ts` co-localizados. Concorrência testada com Tasks.WhenAll em `ConcurrentAppointmentCreationTests`. |

**Veredicto pré-Fase 0**: ✅ APROVADO. Sem violações; nenhuma entrada na Complexity Tracking necessária.

---

## Project Structure

### Documentation (this feature)

```text
specs/011-agenda-services/
├── plan.md              # Este arquivo
├── research.md          # Fase 0 — decisões sobre slot-lock, overlap detection, tool registration
├── data-model.md        # Fase 1 — entidades + DDL
├── contracts/           # Fase 1 — REST + WS + tool calls + WhatsApp flow
│   ├── services-api.md
│   ├── professionals-api.md
│   ├── availability-api.md
│   ├── appointments-api.md
│   ├── agenda-settings-api.md
│   ├── agenda-websocket-events.md
│   ├── ai-tool-calls.md
│   └── whatsapp-cancellation.md
├── quickstart.md        # Fase 1 — passo a passo dev (setup, seeds, smoke tests)
├── checklists/
│   └── requirements.md  # gerado por /speckit-specify (já existe)
└── tasks.md             # Fase 2 — saída do /speckit-tasks (NÃO criado aqui)
```

### Source Code (repository root)

```text
src/omniDesk.Api/
├── Domain/
│   ├── Agenda/
│   │   ├── Service.cs                              # NOVO — entidade catálogo
│   │   ├── Professional.cs                         # NOVO
│   │   ├── ProfessionalService.cs                  # NOVO — junção
│   │   ├── WeeklySchedule.cs                       # NOVO
│   │   ├── ScheduleBlock.cs                        # NOVO
│   │   ├── Appointment.cs                          # NOVO — entidade central
│   │   ├── AppointmentStatus.cs                    # NOVO — static class de constantes
│   │   ├── ClientType.cs                           # NOVO — static class
│   │   ├── CreatedBy.cs                            # NOVO — static class
│   │   ├── CancelledBy.cs                          # NOVO — static class
│   │   └── AgendaSettings.cs                       # NOVO — singleton tenant
│   └── Authorization/
│       └── Permissions.cs                          # ESTENDIDO — Services.*, Professionals.*, Appointments.*, AgendaSettings.*
├── Features/
│   ├── Agenda/
│   │   ├── Services/
│   │   │   ├── ServicesEndpoints.cs                # NOVO — GET/POST/PUT, PATCH toggle
│   │   │   ├── Queries/ListServicesQuery.cs        # NOVO
│   │   │   ├── Commands/CreateServiceCommand.cs    # NOVO
│   │   │   ├── Commands/UpdateServiceCommand.cs    # NOVO
│   │   │   └── Commands/ToggleServiceCommand.cs    # NOVO
│   │   ├── Professionals/
│   │   │   ├── ProfessionalsEndpoints.cs           # NOVO — GET/POST/PUT, PATCH toggle, sub-rotas
│   │   │   ├── Queries/ListProfessionalsQuery.cs   # NOVO
│   │   │   ├── Queries/GetProfessionalServicesQuery.cs # NOVO
│   │   │   ├── Queries/GetWeeklyScheduleQuery.cs   # NOVO
│   │   │   ├── Queries/ListBlocksQuery.cs          # NOVO
│   │   │   ├── Commands/CreateProfessionalCommand.cs    # NOVO
│   │   │   ├── Commands/UpdateProfessionalCommand.cs    # NOVO
│   │   │   ├── Commands/ToggleProfessionalCommand.cs    # NOVO
│   │   │   ├── Commands/UpdateProfessionalServicesCommand.cs  # NOVO — diff
│   │   │   ├── Commands/UpdateWeeklyScheduleCommand.cs        # NOVO — replace all
│   │   │   ├── Commands/CreateBlockCommand.cs                  # NOVO
│   │   │   └── Commands/DeleteBlockCommand.cs                  # NOVO
│   │   ├── Availability/
│   │   │   ├── AvailabilityEndpoint.cs             # NOVO — GET /api/availability
│   │   │   ├── AvailabilityCalculator.cs           # NOVO — fonte única (REST + Tool)
│   │   │   └── Slot.cs                             # NOVO — value object
│   │   ├── Appointments/
│   │   │   ├── AppointmentsEndpoints.cs            # NOVO — CRUD + confirm/cancel/no-show/resend
│   │   │   ├── IAppointmentVisibilityPolicy.cs     # NOVO
│   │   │   ├── AppointmentVisibilityPolicy.cs      # NOVO — TenantAdmin/Supervisor/Attendant
│   │   │   ├── ClientTypeResolver.cs               # NOVO — autoritativo
│   │   │   ├── Queries/ListAppointmentsQuery.cs    # NOVO
│   │   │   ├── Queries/GetAppointmentQuery.cs      # NOVO
│   │   │   ├── Commands/CreateAppointmentCommand.cs       # NOVO — usado por CRM + IA
│   │   │   ├── Commands/UpdateAppointmentCommand.cs       # NOVO
│   │   │   ├── Commands/ConfirmAppointmentCommand.cs      # NOVO
│   │   │   ├── Commands/CancelAppointmentCommand.cs       # NOVO — by attendant
│   │   │   ├── Commands/MarkNoShowCommand.cs              # NOVO
│   │   │   └── Commands/ResendReminderCommand.cs          # NOVO
│   │   ├── Cancellation/
│   │   │   ├── ReminderResponseInterpreter.cs              # NOVO — "NÃO" + janela
│   │   │   └── CancelAppointmentByClientCommand.cs         # NOVO — by WhatsApp
│   │   ├── Settings/
│   │   │   ├── AgendaSettingsEndpoints.cs          # NOVO — GET/PUT
│   │   │   └── Commands/UpdateAgendaSettingsCommand.cs  # NOVO
│   │   ├── Tools/
│   │   │   ├── CheckAvailabilityTool.cs            # NOVO — OpenAI tool (Spec 006)
│   │   │   └── CreateAppointmentTool.cs            # NOVO — OpenAI tool (Spec 006)
│   │   ├── Validators/
│   │   │   ├── CreateServiceValidator.cs           # NOVO — FluentValidation
│   │   │   ├── CreateProfessionalValidator.cs      # NOVO
│   │   │   ├── WeeklyScheduleValidator.cs          # NOVO
│   │   │   ├── ScheduleBlockValidator.cs           # NOVO
│   │   │   ├── CreateAppointmentValidator.cs       # NOVO
│   │   │   ├── CancelAppointmentValidator.cs       # NOVO
│   │   │   └── AgendaSettingsValidator.cs          # NOVO
│   │   └── README.md                               # NOVO — visão do módulo
│   ├── Notifications/
│   │   └── INotificationService.cs                 # ESTENDIDO — NotifyAppointmentCancelledByClientAsync
│   └── WhatsApp/
│       └── Webhook/
│           └── WaWebhookProcessorJob.cs            # ESTENDIDO — chama ReminderResponseInterpreter antes da IA
├── Infrastructure/
│   ├── Agenda/
│   │   ├── ServiceRepository.cs                    # NOVO
│   │   ├── ProfessionalRepository.cs               # NOVO
│   │   ├── WeeklyScheduleRepository.cs             # NOVO
│   │   ├── ScheduleBlockRepository.cs              # NOVO
│   │   ├── AppointmentRepository.cs                # NOVO
│   │   ├── AgendaSettingsRepository.cs             # NOVO
│   │   ├── AppointmentEventStore.cs                # NOVO — escreve em {slug}_appointment_events
│   │   ├── AppointmentSlotLockService.cs           # NOVO — Redis SETNX
│   │   └── AgendaModelConfiguration.cs             # NOVO — EF Core fluent config
│   ├── Appointments/
│   │   ├── AppointmentReadRepository.cs            # MANTIDO — Spec 010 (sem alteração)
│   │   └── IAppointmentReadRepository.cs           # MANTIDO — Spec 010 (sem alteração)
│   ├── Persistence/
│   │   └── Migrations/
│   │       ├── Add_Agenda_ServicesAndProfessionals.sql       # NOVO — services + professionals + professional_services
│   │       ├── Add_Agenda_SchedulesAndBlocks.sql              # NOVO — weekly_schedules + schedule_blocks + btree_gist
│   │       ├── Add_Agenda_Appointments.sql                    # NOVO — appointments + UNIQUE parcial
│   │       └── Add_Agenda_Settings.sql                        # NOVO — agenda_settings singleton
│   ├── Authorization/
│   │   └── RedisKeys.cs                            # ESTENDIDO — AppointmentSlotLock
│   ├── AgentRuntime/
│   │   └── ToolRegistry.cs                         # ESTENDIDO — registra Spec 011 tools
│   └── WebSockets/
│       └── AppointmentEventPublisher.cs            # NOVO — publica appointment.changed
└── Program.cs                                       # ESTENDIDO — DI registrations
└── tests/omniDesk.Api.Tests/
    ├── Features/Agenda/
    │   ├── Services/ServicesEndpointTests.cs
    │   ├── Professionals/ProfessionalsEndpointTests.cs
    │   ├── Professionals/ProfessionalServicesEndpointTests.cs
    │   ├── Professionals/WeeklyScheduleEndpointTests.cs
    │   ├── Professionals/ScheduleBlocksEndpointTests.cs
    │   ├── Availability/AvailabilityCalculatorTests.cs
    │   ├── Availability/AvailabilityEndpointTests.cs
    │   ├── Appointments/CreateAppointmentCommandTests.cs
    │   ├── Appointments/AppointmentLifecycleTests.cs           # confirm/cancel/no-show/resend
    │   ├── Appointments/AppointmentVisibilityPolicyTests.cs
    │   ├── Appointments/ConcurrentAppointmentCreationTests.cs
    │   ├── Cancellation/ReminderResponseInterpreterTests.cs
    │   ├── Cancellation/CancelAppointmentByClientCommandTests.cs
    │   ├── Settings/AgendaSettingsEndpointTests.cs
    │   └── Tools/CheckAvailabilityToolTests.cs
    └── Infrastructure/Agenda/
        ├── AppointmentSlotLockServiceTests.cs
        └── AppointmentEventStoreTests.cs

src/omniDesk.Crm/src/app/
├── features/
│   ├── services-catalog/
│   │   ├── services-list.component.{ts,html,scss,spec.ts}      # NOVO — listagem + filtros ativos/inativos
│   │   ├── service-form.component.{ts,html,scss,spec.ts}        # NOVO — criar/editar
│   │   ├── services.service.{ts,spec.ts}                        # NOVO — HTTP client
│   │   └── services-catalog.routes.ts                           # NOVO — lazy load
│   ├── professionals/
│   │   ├── professionals-list.component.{ts,html,scss,spec.ts}  # NOVO
│   │   ├── professional-form.component.{ts,html,scss,spec.ts}   # NOVO — criar/editar
│   │   ├── professional-services.component.{ts,html,scss,spec.ts}  # NOVO — vínculos
│   │   ├── weekly-schedule.component.{ts,html,scss,spec.ts}     # NOVO — turnos
│   │   ├── schedule-blocks.component.{ts,html,scss,spec.ts}     # NOVO — bloqueios
│   │   ├── professionals.service.{ts,spec.ts}                   # NOVO
│   │   └── professionals.routes.ts                              # NOVO
│   ├── agenda/
│   │   ├── agenda-page.component.{ts,html,scss,spec.ts}         # NOVO — container 3 abas
│   │   ├── weekly-grid.component.{ts,html,scss,spec.ts}         # NOVO — PrimeNG calendar
│   │   ├── appointments-list.component.{ts,html,scss,spec.ts}   # NOVO — lista cronológica
│   │   ├── pending-appointments.component.{ts,html,scss,spec.ts}  # NOVO — aba pendentes
│   │   ├── appointment-detail.component.{ts,html,scss,spec.ts}  # NOVO — detalhe + ações
│   │   ├── appointment-form.component.{ts,html,scss,spec.ts}    # NOVO — criar/editar
│   │   ├── appointment-card.component.{ts,html,scss,spec.ts}    # NOVO — card reutilizável
│   │   ├── appointments.service.{ts,spec.ts}                    # NOVO — HTTP + WS subscribe
│   │   ├── availability.service.{ts,spec.ts}                    # NOVO — GET /availability
│   │   └── agenda.routes.ts                                     # NOVO
│   └── agenda-settings/
│       ├── settings-page.component.{ts,html,scss,spec.ts}       # NOVO — janela + textos
│       └── agenda-settings.service.{ts,spec.ts}                 # NOVO
└── app.routes.ts                                                # ESTENDIDO — /configuracoes/servicos, /configuracoes/profissionais, /agenda, /configuracoes/agenda
```

**Structure Decision**: Reaproveita estrutura `Features/<Domain>/{Endpoints,Commands,Queries,Handlers,Validators}` consolidada por Specs 005–010. Cria um único módulo `Features/Agenda/` contendo cinco sub-módulos coesos (Services, Professionals, Availability, Appointments, Cancellation, Settings, Tools) — Agenda é o domínio agregador. Sem novos projetos C# ou Angular. PrimeNG já está no projeto (Spec 007); o componente `p-calendar` cobre a grade semanal sem dependência adicional.

---

## Constitution Check — Post-Design Re-evaluation

Revalidado após data-model.md + contracts/ + quickstart.md:

| Princípio | Status pós-Fase 1 | Notas |
|---|---|---|
| **I. Multi-Tenant Isolation (NN)** | ✅ | Confirmado: 7 tabelas em `tenant_{slug}.*`, 0 em `public.*`. DDL em data-model §"Schema" e migrations referenciadas. Redis lock key documentado no contrato. `MongoDB {slug}_appointment_events` listado. |
| **II. AI-First, Human-Assisted** | ✅ | Tools `check_availability` e `create_appointment` documentadas em `contracts/ai-tool-calls.md`. `client_type` informado pela IA descartado pelo backend (FR-020 reforçado no contrato). Handoff humano preservado — IA continua podendo `transfer_to_human` mesmo durante agendamento se cliente pedir. |
| **III. Channel Agnosticism** | ✅ | `ReminderResponseInterpreter` vive em `Features/Agenda/Cancellation/` (domínio), não em `Features/WhatsApp/`. Webhook chama o domínio; preservada separação adapter↔domínio. |
| **IV. Security / LGPD (NN)** | ✅ | Dados tenant-scoped no Brasil. `cancellation_reason` opcional e plain text — sem PII especial (já está coberta pelos dados do contato vinculado). Endpoints com JWT + role check + `IAppointmentVisibilityPolicy`. Webhook valida origem da mensagem (número conhecido na conversa) antes de cancelar. |
| **V. Simplicity / YAGNI** | ✅ | 0 libs novas. Disponibilidade calculada via 1 query SQL (research §R1). Sem cache. Sem agendamento de grupo, sem reagendamento direto, sem capacidade > 1 — todos documentados como assumptions na spec. |
| **VI. Observability / Auditability** | ✅ | `{slug}_appointment_events` recebe entrada imutável por transição. Métricas listadas no contrato. Logs Serilog em pontos críticos. |
| **VII. Test Discipline** | ✅ | Backend tests com Testcontainers; sem mock DB. Constants centralizadas. Frontend `.spec.ts` co-localizados. `ConcurrentAppointmentCreationTests` valida SC-005. Quickstart §6 documenta como rodar suite. |

**Veredicto pós-Fase 1**: ✅ APROVADO. Nenhuma violação introduzida pelo design detalhado. Pronto para `/speckit-tasks`.

---

## Complexity Tracking

> Sem violações de constituição. Tabela não preenchida.
