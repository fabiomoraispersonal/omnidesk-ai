-- Migration: Add_Agenda_Settings
-- Generated: 2026-05-13
-- Spec: 011-agenda-services (T020)
--
-- Cria o singleton agenda_settings (1 linha por tenant, garantido por CHECK (id = 1)).
-- Insere a linha default na própria migration (idempotente via ON CONFLICT).
--
-- Idempotente. Sem dependência de outras migrations da Spec 011.

BEGIN;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.agenda_settings (
    id                          smallint     NOT NULL DEFAULT 1 PRIMARY KEY,
    late_cancel_window_hours    int          NOT NULL DEFAULT 24,
    late_cancel_text            text         NOT NULL DEFAULT 'Cancelamentos com menos de 24h poderão ser cobrados.',
    cancellation_policy_text    text         NOT NULL DEFAULT '',
    updated_at                  timestamptz  NOT NULL DEFAULT now(),

    CONSTRAINT chk_settings_singleton CHECK (id = 1),
    CONSTRAINT chk_settings_window    CHECK (late_cancel_window_hours > 0)
);

-- Linha default — idempotente.
INSERT INTO {TENANT_SCHEMA}.agenda_settings (id)
VALUES (1)
ON CONFLICT (id) DO NOTHING;

COMMIT;
