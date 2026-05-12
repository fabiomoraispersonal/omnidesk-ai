-- Migration: Add_TenantNotificationSettings
-- Generated: 2026-05-12
-- Spec: 010-notifications (T009)
--
-- Creates `public.tenant_notification_settings` — one row per tenant, holds tenant-admin
-- toggles for the WhatsApp follow-up automation and the daily appointment reminder job.
--
-- Defaults: both toggles OFF, reminder_time 20:00. Row is lazy-upserted on first PUT.
-- Idempotent.

CREATE TABLE IF NOT EXISTS public.tenant_notification_settings (
    tenant_id          uuid        NOT NULL PRIMARY KEY
                       REFERENCES public.tenants(id) ON DELETE CASCADE,
    follow_up_enabled  boolean     NOT NULL DEFAULT false,
    reminder_enabled   boolean     NOT NULL DEFAULT false,
    reminder_time      time        NOT NULL DEFAULT '20:00',
    updated_at         timestamptz NOT NULL DEFAULT now()
);
