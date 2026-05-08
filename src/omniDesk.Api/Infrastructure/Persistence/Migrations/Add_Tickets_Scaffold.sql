-- Migration: Add_Tickets_Scaffold
-- Generated: 2026-05-08
-- Spec: 005-departments-attendants (Ticket scaffold para US2/US3 — distribuição + lock).
-- Spec 008 (Tickets) substituirá esta tabela com a versão completa; este scaffold contém
-- apenas as colunas exigidas pelo TicketAssignmentService e PickupTicketEndpoint.

CREATE SEQUENCE IF NOT EXISTS {TENANT_SCHEMA}.ticket_number_seq START 1000;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.tickets (
    id                       uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    number                   bigint       NOT NULL DEFAULT nextval('{TENANT_SCHEMA}.ticket_number_seq'),
    subject                  varchar(255) NOT NULL DEFAULT '',
    department_id            uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.departments(id) ON DELETE RESTRICT,
    assigned_attendant_id    uuid         REFERENCES {TENANT_SCHEMA}.attendants(id) ON DELETE SET NULL,
    assigned_at              timestamptz,
    status                   varchar(16)  NOT NULL DEFAULT 'queued'
                                          CHECK (status IN ('queued','assigned','open','resolved','closed')),
    sla_started_at           timestamptz,
    created_at               timestamptz  NOT NULL DEFAULT now(),
    updated_at               timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_tickets_department_id
    ON {TENANT_SCHEMA}.tickets (department_id);
CREATE INDEX IF NOT EXISTS idx_tickets_assigned_attendant_id
    ON {TENANT_SCHEMA}.tickets (assigned_attendant_id);
CREATE INDEX IF NOT EXISTS idx_tickets_status
    ON {TENANT_SCHEMA}.tickets (status);
