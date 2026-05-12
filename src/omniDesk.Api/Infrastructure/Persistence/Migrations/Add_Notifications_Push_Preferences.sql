-- Migration: Add_Notifications_Push_Preferences
-- Generated: 2026-05-12
-- Spec: 010-notifications (T008)
--
-- Creates the three tenant-scoped tables for Spec 010 Notifications:
--   1) notifications                          — in-app alert feed (1 row per addressed attendant)
--   2) push_subscriptions                     — Web Push subscriptions (1 per browser per attendant)
--   3) attendant_notification_preferences     — per-attendant push gate + per-event flags
--
-- Idempotent (`CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`).
-- Schema placeholder `{TENANT_SCHEMA}` is interpolated by the provisioner per tenant.

BEGIN;

-- ---------------------------------------------------------------------------
-- 1) notifications
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.notifications (
    id            uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    attendant_id  uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.attendants(id) ON DELETE CASCADE,
    event_type    varchar(64)  NOT NULL,
    title         varchar(255) NOT NULL,
    body          text         NOT NULL,
    entity_type   varchar(50)  NOT NULL CHECK (entity_type IN ('ticket','conversation')),
    entity_id     uuid         NOT NULL,
    is_read       boolean      NOT NULL DEFAULT false,
    created_at    timestamptz  NOT NULL DEFAULT now(),
    archived_at   timestamptz
);

-- Hot read path: attendant's unread/recent feed (partial — archived rows excluded).
CREATE INDEX IF NOT EXISTS idx_notifications_feed
    ON {TENANT_SCHEMA}.notifications (attendant_id, is_read, created_at DESC)
    WHERE archived_at IS NULL;

-- Archiver sweep candidate scan.
CREATE INDEX IF NOT EXISTS idx_notifications_archive_candidates
    ON {TENANT_SCHEMA}.notifications (created_at)
    WHERE archived_at IS NULL;

-- Entity lookup (navigate from notification to source row).
CREATE INDEX IF NOT EXISTS idx_notifications_entity
    ON {TENANT_SCHEMA}.notifications (entity_type, entity_id);

-- ---------------------------------------------------------------------------
-- 2) push_subscriptions
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.push_subscriptions (
    id            uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    attendant_id  uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.attendants(id) ON DELETE CASCADE,
    endpoint      text         NOT NULL,
    p256dh        text         NOT NULL,
    auth          text         NOT NULL,
    user_agent    varchar(255),
    created_at    timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT uq_push_subscriptions_endpoint UNIQUE (endpoint)
);

CREATE INDEX IF NOT EXISTS idx_push_subscriptions_attendant
    ON {TENANT_SCHEMA}.push_subscriptions (attendant_id);

-- ---------------------------------------------------------------------------
-- 3) attendant_notification_preferences
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.attendant_notification_preferences (
    attendant_id      uuid        NOT NULL PRIMARY KEY
                      REFERENCES {TENANT_SCHEMA}.attendants(id) ON DELETE CASCADE,
    push_enabled      boolean     NOT NULL DEFAULT true,
    event_push_flags  jsonb       NOT NULL DEFAULT '{}'::jsonb,
    updated_at        timestamptz NOT NULL DEFAULT now()
);

COMMIT;
