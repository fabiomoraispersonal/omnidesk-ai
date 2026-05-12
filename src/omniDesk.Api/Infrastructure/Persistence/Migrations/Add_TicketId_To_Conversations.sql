-- Migration: Add_TicketId_To_Conversations
-- Generated: 2026-05-11
-- Spec: 009-tickets-crm (T011)
-- Adds ticket_id FK to conversations table.
-- Note: Spec 007 commented a placeholder: `ticket_id uuid, -- FK lógica → tickets (Spec 008+)`
-- This migration officially adds the column and constraint.

ALTER TABLE {TENANT_SCHEMA}.conversations
    ADD COLUMN IF NOT EXISTS ticket_id uuid REFERENCES {TENANT_SCHEMA}.tickets(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_conversations_ticket_id
    ON {TENANT_SCHEMA}.conversations (ticket_id);
