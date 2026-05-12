-- Migration: Add_ContactId_To_Visitors
-- Generated: 2026-05-11
-- Spec: 009-tickets-crm (T010)
-- Adds contact_id FK to visitors table (links visitor → deduplicated contact).

ALTER TABLE {TENANT_SCHEMA}.visitors
    ADD COLUMN IF NOT EXISTS contact_id uuid REFERENCES {TENANT_SCHEMA}.contacts(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_visitors_contact_id
    ON {TENANT_SCHEMA}.visitors (contact_id);
