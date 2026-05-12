-- Migration: Add_Contacts
-- Generated: 2026-05-11
-- Spec: 009-tickets-crm (T007)
-- Creates the contacts table + indexes + FK from tickets.contact_id → contacts.id (deferred from T006).

BEGIN;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.contacts (
    id                uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    name              varchar(255),
    email             varchar(255),
    phone             varchar(20),
    phone_normalized  varchar(20),
    notes             text,
    source_channels   text[]       NOT NULL DEFAULT '{}',
    created_at        timestamptz  NOT NULL DEFAULT now(),
    updated_at        timestamptz  NOT NULL DEFAULT now(),
    deleted_at        timestamptz
);

-- Unique indexes for dedup (partial — only active records)
CREATE UNIQUE INDEX IF NOT EXISTS uq_contacts_email_lower
    ON {TENANT_SCHEMA}.contacts (lower(email))
    WHERE email IS NOT NULL AND deleted_at IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_contacts_phone_normalized
    ON {TENANT_SCHEMA}.contacts (phone_normalized)
    WHERE phone_normalized IS NOT NULL AND deleted_at IS NULL;

-- Lookup indexes
CREATE INDEX IF NOT EXISTS idx_contacts_email_lower
    ON {TENANT_SCHEMA}.contacts (lower(email));
CREATE INDEX IF NOT EXISTS idx_contacts_phone_normalized
    ON {TENANT_SCHEMA}.contacts (phone_normalized);

-- FK from tickets.contact_id → contacts (deferred from Add_Tickets_FullModel.sql)
ALTER TABLE {TENANT_SCHEMA}.tickets
    ADD CONSTRAINT fk_tickets_contact
    FOREIGN KEY (contact_id) REFERENCES {TENANT_SCHEMA}.contacts(id) ON DELETE SET NULL
    NOT VALID;  -- skip validation for existing NULL rows (backfill job populates later)

COMMIT;
