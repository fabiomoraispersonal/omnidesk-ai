-- Migration: Add_Ai_Handoff_Snapshots
-- Generated: 2026-05-08
-- Spec: 006-ai-agents (transitional — Spec 008 absorbs into ticket messages)
-- Purpose: snapshot the conversation history at the moment IA handed off to a human ticket.

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ai_handoff_snapshots (
    id              uuid        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    ticket_id       uuid        NOT NULL,
    thread_id       uuid        NOT NULL REFERENCES {TENANT_SCHEMA}.ai_threads(id) ON DELETE RESTRICT,
    history_json    jsonb       NOT NULL DEFAULT '[]'::jsonb,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_ai_handoff_snapshots_ticket
    ON {TENANT_SCHEMA}.ai_handoff_snapshots (ticket_id);
