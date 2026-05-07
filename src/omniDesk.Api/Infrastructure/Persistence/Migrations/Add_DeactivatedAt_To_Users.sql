-- Migration: Add_DeactivatedAt_To_Users
-- Generated: 2026-05-06
-- Spec: 004-roles-permissions (T002)
-- Adds deactivated_at column to public.users for audit trail of user deactivation.

ALTER TABLE public.users
    ADD COLUMN IF NOT EXISTS deactivated_at timestamptz NULL;

CREATE INDEX IF NOT EXISTS idx_users_is_active_deactivated_at
    ON public.users (is_active, deactivated_at);
