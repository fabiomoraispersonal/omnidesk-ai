# Agenda e Catálogo de Serviços — Spec 011

Módulo dual que cobre **catálogo de serviços** (consultas, procedimentos, exames, avaliações
com nome, duração e preço) e **agenda** (disponibilidade semanal dos profissionais, bloqueios
e agendamentos de clientes — 1 profissional × 1 cliente × 1 serviço por agendamento na V1).

Spec completa: [`specs/011-agenda-services/`](../../../../specs/011-agenda-services/).

## Sub-módulos

| Sub-pasta | Responsabilidade |
|---|---|
| `Services/` | CRUD do catálogo de serviços (CRM → Configurações → Serviços). |
| `Professionals/` | CRUD de profissionais, vínculos `professional_services`, disponibilidade semanal, bloqueios. |
| `Availability/` | `AvailabilityCalculator` — fonte única para REST + IA tool. |
| `Appointments/` | Ciclo de vida `pending_confirmation → confirmed → cancelled/no_show`. Visibility policy. |
| `Cancellation/` | `ReminderResponseInterpreter` — detecta "NÃO" no webhook WhatsApp e cancela. |
| `Settings/` | Singleton `agenda_settings` (janela + textos de cancelamento tardio). |
| `Tools/` | OpenAI tool calls `check_availability` / `create_appointment` (Spec 006). |
| `Validators/` | FluentValidation rules compartilhadas. |

## Fluxo principal — criação de agendamento

```
CRM ou IA
  → CreateAppointmentCommand
      → 1. AppointmentSlotLockService.AcquireAsync (Redis SETNX, TTL 10s)
      → 2. BEGIN TX
      → 3. Revalida disponibilidade (FOR UPDATE)
      → 4. ClientTypeResolver (autoritativo — descarta input da IA)
      → 5. INSERT (UNIQUE parcial protege contra duplicata)
      → 6. AppointmentEventStore.AppendAsync (Mongo {slug}_appointment_events)
      → 7. COMMIT
      → 8. AppointmentEventPublisher (WebSocket appointment.changed)
      → 9. Se status=confirmed → INotificationService.NotifyAppointmentConfirmedAsync
```

## Race-condition protection (3 camadas)

1. **Redis SETNX** — falha rápido (~99% dos conflitos).
2. **UNIQUE parcial PG** — `(professional_id, start_at) WHERE status IN (pending, confirmed)`.
3. **FOR UPDATE no profissional** — previne escalada para `unique_violation`.

Ver [research §R2](../../../../specs/011-agenda-services/research.md#r2--proteção-contra-race-condition-na-criação-de-agendamentos).

## Cancelamento via WhatsApp ("NÃO")

`WaWebhookProcessorJob` (Spec 008) chama `ReminderResponseInterpreter.TryInterpretAsync` ANTES
do orquestrador IA. Se a mensagem (normalizada lowercase + sem acentos) for `"nao"` E houver
appointment `confirmed` com `reminder_sent_at` nas últimas 26h vinculado à conversa, cancela
o appointment mais cedo no tempo, responde com texto de política + (se aplicável) aviso de
cancelamento tardio, e notifica o atendente via Spec 010 (`NotifyAppointmentCancelledByClientAsync`).

## Endpoints

- `/api/services` — catálogo
- `/api/professionals` (+ `/services`, `/schedule`, `/blocks`)
- `/api/availability?professional_id=&service_id=&date=` — usado pelo CRM e pela tool IA
- `/api/appointments` (+ `/confirm`, `/cancel`, `/no-show`, `/resend-reminder`)
- `/api/agenda-settings`

Contratos completos: [`specs/011-agenda-services/contracts/`](../../../../specs/011-agenda-services/contracts/).

## Permissions (`Domain/Authorization/Policies.cs`)

- `Agenda.ManageServices` — TenantAdmin
- `Agenda.ManageProfessionals` — TenantAdmin
- `Agenda.ConfigureAvailability` — TenantAdmin
- `Agenda.ConfigureCancellationPolicy` — TenantAdmin
- `Appointments.View` — Attendant+ (filtrado por `IAppointmentVisibilityPolicy`)
- `Appointments.Manage` — Attendant+ (no escopo de visibilidade)

## Persistência

Tabelas em `tenant_{slug}.*`: `services`, `professionals`, `professional_services`,
`weekly_schedules`, `schedule_blocks`, `appointments`, `agenda_settings`.

MongoDB: `{slug}_appointment_events` (append-only).

Redis: `{slug}:appointment_slot_lock:{prof}:{start_iso}` (TTL 10s).
