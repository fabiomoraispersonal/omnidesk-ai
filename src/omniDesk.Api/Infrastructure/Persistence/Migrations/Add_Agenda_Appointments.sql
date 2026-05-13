-- Migration: Add_Agenda_Appointments
-- Generated: 2026-05-13
-- Spec: 011-agenda-services (T019)
--
-- Cria a tabela central appointments + UNIQUE parcial que protege contra race condition
-- na criação concorrente (research §R2, camada 2). FKs para professionals, services,
-- contacts (Spec 009), tickets (Spec 009), conversations (Specs 007/008).
--
-- Idempotente. Depende de Add_Agenda_ServicesAndProfessionals.sql + Add_Contacts.sql +
-- Add_Tickets_FullModel.sql + Add_LiveChat_Tables.sql.

BEGIN;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.appointments (
    id                    uuid          NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    professional_id       uuid          NOT NULL REFERENCES {TENANT_SCHEMA}.professionals(id) ON DELETE RESTRICT,
    service_id            uuid          NOT NULL REFERENCES {TENANT_SCHEMA}.services(id)      ON DELETE RESTRICT,
    contact_id            uuid          NULL     REFERENCES {TENANT_SCHEMA}.contacts(id)      ON DELETE SET NULL,
    ticket_id             uuid          NULL     REFERENCES {TENANT_SCHEMA}.tickets(id)       ON DELETE SET NULL,
    conversation_id       uuid          NULL     REFERENCES {TENANT_SCHEMA}.conversations(id) ON DELETE SET NULL,

    start_at              timestamptz   NOT NULL,
    end_at                timestamptz   NOT NULL,

    status                varchar(24)   NOT NULL
        CHECK (status IN ('pending_confirmation','confirmed','cancelled','no_show')),
    client_type           varchar(20)   NOT NULL
        CHECK (client_type IN ('new_client','returning_client')),
    created_by            varchar(20)   NOT NULL
        CHECK (created_by IN ('ai','attendant')),
    notes                 text          NULL,

    reminder_sent_at      timestamptz   NULL,
    cancelled_by          varchar(20)   NULL
        CHECK (cancelled_by IS NULL OR cancelled_by IN ('client','attendant','system')),
    cancelled_at          timestamptz   NULL,
    cancellation_reason   varchar(255)  NULL,

    created_at            timestamptz   NOT NULL DEFAULT now(),
    updated_at            timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT chk_ap_range CHECK (end_at > start_at)
);

-- Disponibilidade: turnos do profissional + slots ocupados.
CREATE INDEX IF NOT EXISTS idx_ap_prof_start
    ON {TENANT_SCHEMA}.appointments (professional_id, start_at);

-- Resolução autoritativa de client_type (histórico do contato).
CREATE INDEX IF NOT EXISTS idx_ap_contact_status_start
    ON {TENANT_SCHEMA}.appointments (contact_id, status, start_at);

-- Job de lembrete (Spec 010 — AppointmentReminderJob).
CREATE INDEX IF NOT EXISTS idx_ap_reminder_pending
    ON {TENANT_SCHEMA}.appointments (start_at)
    WHERE status = 'confirmed' AND reminder_sent_at IS NULL;

-- Lookup de cancelamento via "NÃO" no WhatsApp (FR-033).
CREATE INDEX IF NOT EXISTS idx_ap_conv_confirmed
    ON {TENANT_SCHEMA}.appointments (conversation_id, start_at)
    WHERE status = 'confirmed';

-- UNIQUE parcial: protege contra duplicata no slot (research §R2, camada 2).
-- Permite múltiplas linhas cancelled/no_show no mesmo slot histórico.
CREATE UNIQUE INDEX IF NOT EXISTS idx_ap_slot_unique
    ON {TENANT_SCHEMA}.appointments (professional_id, start_at)
    WHERE status IN ('pending_confirmation','confirmed');

COMMIT;
