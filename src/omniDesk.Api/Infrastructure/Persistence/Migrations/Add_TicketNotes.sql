-- Migration: Add_TicketNotes
-- Generated: 2026-05-11
-- Spec: 009-tickets-crm (T008)
-- Creates ticket_notes table (append-only — no UPDATE, no DELETE by design).

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ticket_notes (
    id            uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    ticket_id     uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.tickets(id) ON DELETE CASCADE,
    attendant_id  uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.attendants(id) ON DELETE RESTRICT,
    content       text         NOT NULL CHECK (length(content) BETWEEN 1 AND 10000),
    created_at    timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_ticket_notes_ticket_id
    ON {TENANT_SCHEMA}.ticket_notes (ticket_id, created_at);
