-- Migration: Add_Agenda_SchedulesAndBlocks
-- Generated: 2026-05-13
-- Spec: 011-agenda-services (T018)
--
-- Cria as 2 tabelas de disponibilidade:
--   1) weekly_schedules — turnos recorrentes (day_of_week 0..6, start_time, end_time)
--   2) schedule_blocks  — bloqueios pontuais (start_at, end_at, reason)
--
-- Habilita btree_gist e cria índice GIST sobre tstzrange para detecção rápida de overlap
-- entre bloqueios e agendamentos (research §R3).
--
-- Idempotente. Depende de Add_Agenda_ServicesAndProfessionals.sql (professionals table).

BEGIN;

-- Extension para suportar (professional_id, tstzrange) em um único índice GIST.
CREATE EXTENSION IF NOT EXISTS btree_gist;

-- ---------------------------------------------------------------------------
-- 1) weekly_schedules — turnos recorrentes
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.weekly_schedules (
    id                uuid     NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    professional_id   uuid     NOT NULL REFERENCES {TENANT_SCHEMA}.professionals(id) ON DELETE CASCADE,
    day_of_week       smallint NOT NULL CHECK (day_of_week BETWEEN 0 AND 6),
    start_time        time     NOT NULL,
    end_time          time     NOT NULL,
    CONSTRAINT chk_ws_range CHECK (start_time < end_time)
);

CREATE INDEX IF NOT EXISTS idx_ws_professional_day
    ON {TENANT_SCHEMA}.weekly_schedules (professional_id, day_of_week);

-- ---------------------------------------------------------------------------
-- 2) schedule_blocks — bloqueios pontuais
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.schedule_blocks (
    id                uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    professional_id   uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.professionals(id) ON DELETE CASCADE,
    start_at          timestamptz  NOT NULL,
    end_at            timestamptz  NOT NULL,
    reason            varchar(255) NULL,
    created_at        timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT chk_sb_range CHECK (start_at < end_at)
);

-- GIST: detecção de overlap O(log n) (research §R3). Operador && em tstzrange.
CREATE INDEX IF NOT EXISTS idx_sb_overlap
    ON {TENANT_SCHEMA}.schedule_blocks
    USING gist (professional_id, tstzrange(start_at, end_at, '[)'));

COMMIT;
