-- Migration: Add_Messages_SearchVector
-- Generated: 2026-05-11
-- Spec: 009-tickets-crm (T012) — coordination with Spec 007.
-- Adds full-text search column to conversation_messages.
-- If this migration is not applied, ticket search (SearchTicketsQuery) degrades gracefully
-- to searching only tickets.search_vector + contacts.name (FR-038).

ALTER TABLE {TENANT_SCHEMA}.conversation_messages
    ADD COLUMN IF NOT EXISTS content_tsv tsvector
        GENERATED ALWAYS AS (to_tsvector('portuguese', coalesce(content, ''))) STORED;

CREATE INDEX IF NOT EXISTS idx_conversation_messages_content_tsv
    ON {TENANT_SCHEMA}.conversation_messages USING GIN (content_tsv);
