# Data Model: Agenda e Catálogo de Serviços (Spec 011)

**Branch**: `011-agenda-services` | **Phase**: 1 | **Date**: 2026-05-12

Define o schema persistente da Spec 011. Todas as tabelas vivem em `tenant_{slug}.*` (Princípio I — Multi-Tenant Isolation).

DDL aqui é ilustrativo; SQL canônico fica em:

- `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Agenda_ServicesAndProfessionals.sql`
- `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Agenda_SchedulesAndBlocks.sql`
- `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Agenda_Appointments.sql`
- `src/omniDesk.Api/Infrastructure/Persistence/Migrations/Add_Agenda_Settings.sql`

EF Core mapping: `Infrastructure/Agenda/AgendaModelConfiguration.cs` (Fluent API; sem Data Annotations).

---

## Entity Overview

| Entity | Schema | Purpose | Lifecycle |
|---|---|---|---|
| **Service** | `tenant_{slug}.services` | Item do catálogo (consulta/procedimento/exame/avaliação). | Created/edited/soft-deleted; agendamentos preservados após soft-delete. |
| **Professional** | `tenant_{slug}.professionals` | Médico/prestador. Vínculo opcional com atendente do CRM. | Created/edited; soft-delete oculta de novos agendamentos. |
| **ProfessionalService** | `tenant_{slug}.professional_services` | Junção N×N: quais serviços cada profissional executa. | Diff-based update. |
| **WeeklySchedule** | `tenant_{slug}.weekly_schedules` | Turno recorrente por dia da semana. | Múltiplas entradas por dia; replace-all em update. |
| **ScheduleBlock** | `tenant_{slug}.schedule_blocks` | Bloqueio pontual (férias/congresso). | Criação valida overlap com appointments. |
| **Appointment** | `tenant_{slug}.appointments` | Agendamento — entidade central. | Pending → confirmed → cancelled/no_show. Imutável após cancelled/no_show. |
| **AgendaSettings** | `tenant_{slug}.agenda_settings` | Singleton de cfg (cancelamento). | Linha única criada pela migration; UPSERT-only. |
| **AppointmentEvent** | `mongo:{slug}_appointment_events` | Log imutável de transições de status. | Append-only. |
| **AppointmentStatus** | (constants em C#) | Conjunto fechado: pending_confirmation, confirmed, cancelled, no_show. | Código. |
| **ClientType** | (constants em C#) | new_client, returning_client. | Código. |
| **CreatedBy** | (constants em C#) | ai, attendant. | Código. |
| **CancelledBy** | (constants em C#) | client, attendant, system. | Código. |

---

## Service

```sql
CREATE TABLE tenant_{slug}.services (
    id                      uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    name                    varchar(100)  NOT NULL,
    description             text          NULL,
    category                varchar(100)  NULL,
    duration_minutes        int           NOT NULL,
    price                   numeric(10,2) NULL,
    requires_confirmation   boolean       NOT NULL DEFAULT false,
    is_active               boolean       NOT NULL DEFAULT true,
    created_at              timestamptz   NOT NULL DEFAULT now(),
    updated_at              timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT chk_services_duration CHECK (duration_minutes > 0),
    CONSTRAINT chk_services_price CHECK (price IS NULL OR price >= 0)
);

CREATE INDEX idx_services_active_name
    ON tenant_{slug}.services (is_active, name);
```

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK, server-generated. |
| `name` | `varchar(100)` | Display name. Obrigatório. |
| `description` | `text?` | Texto livre para exibição ao cliente (UI tooltip / catálogo). |
| `category` | `varchar(100)?` | Categoria livre — UI usa para agrupamento opcional. |
| `duration_minutes` | `int` | Define tamanho do slot. CHECK > 0. |
| `price` | `numeric(10,2)?` | `null` = a combinar. CHECK ≥ 0 quando informado. |
| `requires_confirmation` | `boolean` | Se `true`, força `pending_confirmation` mesmo para retornantes. |
| `is_active` | `boolean` | Soft delete; agendamentos preservados quando `false`. |
| `created_at` / `updated_at` | `timestamptz` | Audit. |

**Validation** (FluentValidation): `name` 1–100, `duration_minutes ≥ 1` (recomendado ≤ 480 = 8h), `price ≥ 0` se presente, `category` ≤ 100, `description` ≤ 2000.

---

## Professional

```sql
CREATE TABLE tenant_{slug}.professionals (
    id              uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    name            varchar(255)  NOT NULL,
    specialty       varchar(100)  NULL,
    department_id   uuid          NULL,
    attendant_id    uuid          NULL,
    is_active       boolean       NOT NULL DEFAULT true,
    created_at      timestamptz   NOT NULL DEFAULT now(),
    updated_at      timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT fk_professionals_department
        FOREIGN KEY (department_id) REFERENCES tenant_{slug}.departments(id) ON DELETE SET NULL,
    CONSTRAINT fk_professionals_attendant
        FOREIGN KEY (attendant_id) REFERENCES tenant_{slug}.attendants(id) ON DELETE SET NULL
);

-- Garante 1 atendente vinculado a no máximo 1 profissional.
CREATE UNIQUE INDEX idx_professionals_attendant_unique
    ON tenant_{slug}.professionals (attendant_id)
    WHERE attendant_id IS NOT NULL;

CREATE INDEX idx_professionals_active_name
    ON tenant_{slug}.professionals (is_active, name);
```

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK. |
| `name` | `varchar(255)` | "Dra. Ana Lima". Obrigatório. |
| `specialty` | `varchar(100)?` | "Fisioterapeuta". |
| `department_id` | `uuid?` | FK → `departments` (Spec 005). `ON DELETE SET NULL`. |
| `attendant_id` | `uuid?` | FK → `attendants` (Spec 005). Único parcial. `ON DELETE SET NULL`. |
| `is_active` | `boolean` | Soft delete; `false` esconde de novos agendamentos. |

**Validation**: `name` 1–255, `specialty` ≤ 100, `attendant_id` deve pertencer ao mesmo tenant (auto-garantido por FK + tenant schema).

---

## ProfessionalService (junção)

```sql
CREATE TABLE tenant_{slug}.professional_services (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    professional_id   uuid NOT NULL,
    service_id        uuid NOT NULL,

    CONSTRAINT fk_ps_professional
        FOREIGN KEY (professional_id) REFERENCES tenant_{slug}.professionals(id) ON DELETE CASCADE,
    CONSTRAINT fk_ps_service
        FOREIGN KEY (service_id) REFERENCES tenant_{slug}.services(id) ON DELETE RESTRICT,
    CONSTRAINT uq_ps_unique UNIQUE (professional_id, service_id)
);

CREATE INDEX idx_ps_service ON tenant_{slug}.professional_services (service_id);
```

**Notas**:

- `ON DELETE CASCADE` para `professional_id`: ao deletar profissional, vínculos somem.
- `ON DELETE RESTRICT` para `service_id`: serviço não pode ser deletado fisicamente se houver profissional vinculado — só soft-deletado via `is_active = false`. (Em V1 não há endpoint de hard delete; este é um guard defensivo.)
- Update via `PUT /api/professionals/{id}/services` executa diff atomicamente dentro de transação.

---

## WeeklySchedule

```sql
CREATE TABLE tenant_{slug}.weekly_schedules (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    professional_id   uuid NOT NULL,
    day_of_week       smallint NOT NULL,
    start_time        time NOT NULL,
    end_time          time NOT NULL,

    CONSTRAINT fk_ws_professional
        FOREIGN KEY (professional_id) REFERENCES tenant_{slug}.professionals(id) ON DELETE CASCADE,
    CONSTRAINT chk_ws_day CHECK (day_of_week BETWEEN 0 AND 6),
    CONSTRAINT chk_ws_range CHECK (start_time < end_time)
);

CREATE INDEX idx_ws_professional_day
    ON tenant_{slug}.weekly_schedules (professional_id, day_of_week);
```

**Notas**:

- Múltiplas entradas por `(professional_id, day_of_week)` permitidas (turnos).
- Validação de sobreposição entre turnos do mesmo dia feita **em aplicação** (FluentValidation no `UpdateWeeklyScheduleCommand`) — não há constraint trivial em SQL para "sem overlap entre linhas do mesmo dia"; é simples em código.
- Update via `PUT /api/professionals/{id}/schedule` faz replace-all dentro de transação (DELETE WHERE professional + INSERT do payload).

---

## ScheduleBlock

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE tenant_{slug}.schedule_blocks (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    professional_id   uuid NOT NULL,
    start_at          timestamptz NOT NULL,
    end_at            timestamptz NOT NULL,
    reason            varchar(255) NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT fk_sb_professional
        FOREIGN KEY (professional_id) REFERENCES tenant_{slug}.professionals(id) ON DELETE CASCADE,
    CONSTRAINT chk_sb_range CHECK (start_at < end_at)
);

-- Detecção rápida de overlap (research §R3).
CREATE INDEX idx_sb_overlap
    ON tenant_{slug}.schedule_blocks
    USING gist (professional_id, tstzrange(start_at, end_at, '[)'));
```

**Notas**:

- Não há UNIQUE entre bloqueios — admin pode criar 2 bloqueios sobrepostos (UX permissiva); o cálculo de disponibilidade trata corretamente.
- Validação ao criar: rejeita se há `appointments(status IN pending|confirmed)` do mesmo profissional sobrepondo o intervalo (FR-015). Erro `BLOCK_OVERLAPS_APPOINTMENTS` lista os IDs.

---

## Appointment

```sql
CREATE TABLE tenant_{slug}.appointments (
    id                    uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    professional_id       uuid          NOT NULL,
    service_id            uuid          NOT NULL,
    contact_id            uuid          NULL,
    ticket_id             uuid          NULL,
    conversation_id       uuid          NULL,

    start_at              timestamptz   NOT NULL,
    end_at                timestamptz   NOT NULL,

    status                varchar(24)   NOT NULL,    -- pending_confirmation, confirmed, cancelled, no_show
    client_type           varchar(20)   NOT NULL,    -- new_client, returning_client
    created_by            varchar(20)   NOT NULL,    -- ai, attendant
    notes                 text          NULL,

    reminder_sent_at      timestamptz   NULL,
    cancelled_by          varchar(20)   NULL,        -- client, attendant, system
    cancelled_at          timestamptz   NULL,
    cancellation_reason   varchar(255)  NULL,

    created_at            timestamptz   NOT NULL DEFAULT now(),
    updated_at            timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT fk_ap_professional
        FOREIGN KEY (professional_id) REFERENCES tenant_{slug}.professionals(id) ON DELETE RESTRICT,
    CONSTRAINT fk_ap_service
        FOREIGN KEY (service_id) REFERENCES tenant_{slug}.services(id) ON DELETE RESTRICT,
    CONSTRAINT fk_ap_contact
        FOREIGN KEY (contact_id) REFERENCES tenant_{slug}.contacts(id) ON DELETE SET NULL,
    CONSTRAINT fk_ap_ticket
        FOREIGN KEY (ticket_id) REFERENCES tenant_{slug}.tickets(id) ON DELETE SET NULL,
    CONSTRAINT fk_ap_conversation
        FOREIGN KEY (conversation_id) REFERENCES tenant_{slug}.conversations(id) ON DELETE SET NULL,

    CONSTRAINT chk_ap_range CHECK (end_at > start_at),
    CONSTRAINT chk_ap_status CHECK (status IN ('pending_confirmation','confirmed','cancelled','no_show')),
    CONSTRAINT chk_ap_client_type CHECK (client_type IN ('new_client','returning_client')),
    CONSTRAINT chk_ap_created_by CHECK (created_by IN ('ai','attendant')),
    CONSTRAINT chk_ap_cancelled_by CHECK (cancelled_by IS NULL OR cancelled_by IN ('client','attendant','system'))
);

-- Disponibilidade (turnos + slots ocupados).
CREATE INDEX idx_ap_prof_start
    ON tenant_{slug}.appointments (professional_id, start_at);

-- Histórico de cliente (resolução de client_type).
CREATE INDEX idx_ap_contact_status_start
    ON tenant_{slug}.appointments (contact_id, status, start_at);

-- Job de lembrete (Spec 010 — AppointmentReminderJob).
CREATE INDEX idx_ap_reminder_pending
    ON tenant_{slug}.appointments (start_at)
    WHERE status = 'confirmed' AND reminder_sent_at IS NULL;

-- Lookup de cancelamento via "NÃO" (FR-033).
CREATE INDEX idx_ap_conv_confirmed
    ON tenant_{slug}.appointments (conversation_id, start_at)
    WHERE status = 'confirmed';

-- Protege contra race condition (research §R2).
CREATE UNIQUE INDEX idx_ap_slot_unique
    ON tenant_{slug}.appointments (professional_id, start_at)
    WHERE status IN ('pending_confirmation','confirmed');
```

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `id` | `uuid` | PK. |
| `professional_id` | `uuid` | FK; RESTRICT (não deletar profissional com agendamentos). |
| `service_id` | `uuid` | FK; RESTRICT. |
| `contact_id` | `uuid?` | FK; SET NULL (contato pode ser fundido/limpo). |
| `ticket_id` | `uuid?` | FK; SET NULL. Origem (quando criado em ticket). |
| `conversation_id` | `uuid?` | FK; SET NULL. Origem WhatsApp/LiveChat. |
| `start_at` | `timestamptz` | Início. CHECK < `end_at`. |
| `end_at` | `timestamptz` | Calculado = `start_at + service.duration_minutes`. Setado pelo backend. |
| `status` | `varchar(24)` | Enum string (mais simples em PG/EF Core que enum tipado). |
| `client_type` | `varchar(20)` | Autoritativo (FR-020). |
| `created_by` | `varchar(20)` | `ai` ou `attendant`. |
| `notes` | `text?` | Anotações internas. |
| `reminder_sent_at` | `timestamptz?` | Preenchido pelo `AppointmentReminderJob`. |
| `cancelled_by` | `varchar(20)?` | Quem cancelou. |
| `cancelled_at` | `timestamptz?` | Quando. |
| `cancellation_reason` | `varchar(255)?` | Texto livre opcional. |

**State Transitions**:

```text
                ┌────────────────────────┐
                │   (criado)             │
                │                        │
              new_client OR              returning_client AND
              requires_confirmation      NOT requires_confirmation
                │                        │
                ▼                        ▼
   ┌──────────────────────┐    ┌──────────────────────┐
   │ pending_confirmation │    │      confirmed       │
   └─────────┬────────────┘    └───┬──────────────────┘
             │                     │           ▲
   confirm   │              cancel │           │ (atendente reabre? NÃO em V1)
             ▼                     ▼           │
   ┌──────────────────────┐    ┌──────────────────────┐
   │      confirmed       │    │     cancelled        │
   └─────────┬────────────┘    └──────────────────────┘
             │
             │  (start_at passou,
             │   cliente faltou)
             │
   no_show   │
             ▼
   ┌──────────────────────┐
   │       no_show        │
   └──────────────────────┘
```

**Permitidos** (FR-031):

- `pending_confirmation → confirmed` (atendente confirma)
- `pending_confirmation → cancelled` (atendente cancela)
- `confirmed → cancelled` (atendente cancela OR cliente cancela via WhatsApp OR sistema)
- `confirmed → no_show` (atendente marca após `start_at`)

**Bloqueados** (qualquer outra transição retorna `APPOINTMENT_INVALID_STATUS_TRANSITION`):

- `cancelled → *` (terminal)
- `no_show → *` (terminal)
- `pending_confirmation → no_show` (cliente que nem foi confirmado não pode dar no-show)

**Validation rules**:

- `start_at` > `now()` na criação (FR-024).
- `start_at` cai dentro de algum `weekly_schedules` do profissional e fora de todo `schedule_blocks` (FR-024).
- `service_id` pertence aos `professional_services` do profissional (FR-025).
- `end_at` é setado pelo backend, nunca aceito do payload (FR-019).
- Race condition protegida pelo `idx_ap_slot_unique` + Redis lock (research §R2).

---

## AgendaSettings (singleton)

```sql
CREATE TABLE tenant_{slug}.agenda_settings (
    id                          smallint     PRIMARY KEY DEFAULT 1,
    late_cancel_window_hours    int          NOT NULL DEFAULT 24,
    late_cancel_text            text         NOT NULL DEFAULT 'Cancelamentos com menos de 24h poderão ser cobrados.',
    cancellation_policy_text    text         NOT NULL DEFAULT '',
    updated_at                  timestamptz  NOT NULL DEFAULT now(),

    CONSTRAINT chk_settings_singleton CHECK (id = 1),
    CONSTRAINT chk_settings_window CHECK (late_cancel_window_hours > 0)
);

-- Linha default criada na migration (idempotente):
INSERT INTO tenant_{slug}.agenda_settings (id) VALUES (1) ON CONFLICT DO NOTHING;
```

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `id` | `smallint` | Fixo em 1; `CHECK (id = 1)` força singleton. |
| `late_cancel_window_hours` | `int` | > 0. Default 24. |
| `late_cancel_text` | `text` | Texto livre. Default conforme spec. |
| `cancellation_policy_text` | `text` | Texto livre. Default vazio. |

---

## AppointmentEvent (MongoDB)

Append-only log de transições. Reaproveita `IActivityLogStore` (Spec 006) com nova collection.

```text
Collection: {slug}_appointment_events
Document shape (BSON):
{
  "_id": ObjectId(...),
  "appointment_id": UUID,
  "action": "created" | "confirmed" | "cancelled" | "no_show" | "reminder_sent" | "reminder_resent" | "rescheduled",
  "actor_type": "system" | "attendant" | "client" | "ai",
  "actor_id": UUID | null,           // attendant id se actor_type = attendant
  "ticket_id": UUID | null,
  "conversation_id": UUID | null,
  "metadata": {                       // contexto adicional, varia por action
    "from_status": "pending_confirmation",   // em transições
    "to_status": "confirmed",
    "from_start_at": ...,                    // em rescheduled
    "to_start_at": ...,
    "cancellation_reason": "...",
    "channel": "crm" | "whatsapp" | "live_chat"
  },
  "created_at": ISODate(...)
}

Indexes:
- { appointment_id: 1, created_at: 1 } — leitura cronológica do histórico
- { created_at: -1 } — TTL/auditoria global
```

**Notas**:

- Imutável — apenas `INSERT`, nunca `UPDATE`/`DELETE`.
- Fonte da verdade para o histórico de ações exibido no detalhe do agendamento (FR-046).
- Origem complementar à `appointments.cancelled_*` columns: aquelas guardam o **estado atual**; aqui está o **trajeto histórico**.

---

## C# Constants (Domain/Agenda/*.cs)

```csharp
public static class AppointmentStatus
{
    public const string PendingConfirmation = "pending_confirmation";
    public const string Confirmed = "confirmed";
    public const string Cancelled = "cancelled";
    public const string NoShow = "no_show";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { PendingConfirmation, Confirmed, Cancelled, NoShow };

    public static readonly IReadOnlySet<string> ActiveForSlot =
        new HashSet<string> { PendingConfirmation, Confirmed };
}

public static class ClientType
{
    public const string NewClient = "new_client";
    public const string ReturningClient = "returning_client";
}

public static class CreatedBy
{
    public const string Ai = "ai";
    public const string Attendant = "attendant";
}

public static class CancelledBy
{
    public const string Client = "client";
    public const string Attendant = "attendant";
    public const string System = "system";
}
```

---

## Redis Keys (estende `Infrastructure/Authorization/RedisKeys.cs`)

```csharp
public static class RedisKeys
{
    // ...existing Spec 010 keys preserved...

    /// <summary>
    /// Spec 011 — slot lock for race-condition protection on appointment creation.
    /// TTL: 10s.
    /// </summary>
    public static string AppointmentSlotLock(string tenantSlug, Guid professionalId, DateTimeOffset startAt)
        => $"{tenantSlug}:appointment_slot_lock:{professionalId:N}:{startAt:O}";
}
```

---

## Migration ordering

Ordem das 4 migrations (timestamp prefix `2026MMDDHHMMSS_*`):

1. `Add_Agenda_ServicesAndProfessionals.sql` — cria `services`, `professionals`, `professional_services`, FKs e indexes básicos.
2. `Add_Agenda_SchedulesAndBlocks.sql` — habilita `btree_gist`, cria `weekly_schedules` e `schedule_blocks` com GIST index.
3. `Add_Agenda_Appointments.sql` — cria `appointments` com todos os FKs (depende de `professionals`, `services`, `contacts`, `tickets`, `conversations`) e indexes (incluindo o UNIQUE parcial).
4. `Add_Agenda_Settings.sql` — cria `agenda_settings` singleton e insere linha default.

Cada migration é aplicada a cada `tenant_{slug}` via o `TenantMigrationsRunner` (mesmo padrão de Spec 010).

---

## EF Core Mapping (resumo)

`Infrastructure/Agenda/AgendaModelConfiguration.cs` define:

- `Service`: `ToTable("services", schema: tenantSchema)`, PK `Id`, indexes via `HasIndex`. Conversões para `decimal?` no `Price`.
- `Professional`: FK opcional para `Department` e `Attendant` (navegação `HasOne(...).WithMany(...).OnDelete(DeleteBehavior.SetNull)`).
- `ProfessionalService`: PK composta opcional via `Id` único; `HasIndex(...).IsUnique()` em `(ProfessionalId, ServiceId)`.
- `WeeklySchedule`: `Property(x => x.DayOfWeek).HasColumnType("smallint")`; `Property(x => x.StartTime).HasColumnType("time")` (mapeado em `TimeOnly`).
- `ScheduleBlock`: índice GIST não é gerenciado pelo EF — declarado via `migrationBuilder.Sql(...)` na migration.
- `Appointment`: status, client_type, created_by, cancelled_by mapeados como strings (mais simples para multi-tenant + EF Core). Conversões para constantes via `HasConversion`.
- `AgendaSettings`: `HasKey(x => x.Id)`, `Property(x => x.Id).HasDefaultValue((short)1)`.

---

## Volumes esperados (Scale)

| Tabela | Linhas típicas/tenant | Cresce com... |
|---|---|---|
| `services` | 10–50 | adições do admin (estável) |
| `professionals` | 5–20 | adições do admin (estável) |
| `professional_services` | 50–500 | combinatória prof × serv |
| `weekly_schedules` | 50–200 | turnos × prof × dias |
| `schedule_blocks` | 0–20 ativos a qualquer momento | férias/eventos |
| `appointments` | 100–500/mês; 5k–50k acumulados/ano | volume operacional |
| `agenda_settings` | 1 (singleton) | nunca |
| `{slug}_appointment_events` | ~3x appointments | append-only |

**Observação de retenção**: appointments antigos (>2 anos cancelados/no-show) podem ser candidatos a soft-archive em V2+. V1 mantém indefinidamente para histórico clínico.
