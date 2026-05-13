-- Migration: Add_Agenda_ServicesAndProfessionals
-- Generated: 2026-05-13
-- Spec: 011-agenda-services (T017)
--
-- Cria as 3 tabelas iniciais do módulo Agenda:
--   1) services                — catálogo (consultas, procedimentos, exames, avaliações)
--   2) professionals           — médicos / prestadores; vínculo opcional com atendente
--   3) professional_services   — junção N×N
--
-- Idempotente (CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS).
-- Schema placeholder {TENANT_SCHEMA} é interpolado pelo provisioner / TenantSchemaFixture.

BEGIN;

-- ---------------------------------------------------------------------------
-- 1) services — catálogo
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.services (
    id                      uuid          NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    name                    varchar(100)  NOT NULL,
    description             text          NULL,
    category                varchar(100)  NULL,
    duration_minutes        int           NOT NULL CHECK (duration_minutes > 0),
    price                   numeric(10,2) NULL CHECK (price IS NULL OR price >= 0),
    requires_confirmation   boolean       NOT NULL DEFAULT false,
    is_active               boolean       NOT NULL DEFAULT true,
    created_at              timestamptz   NOT NULL DEFAULT now(),
    updated_at              timestamptz   NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_services_active_name
    ON {TENANT_SCHEMA}.services (is_active, name);

-- ---------------------------------------------------------------------------
-- 2) professionals — médico / prestador
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.professionals (
    id              uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    name            varchar(255) NOT NULL,
    specialty       varchar(100) NULL,
    department_id   uuid         NULL REFERENCES {TENANT_SCHEMA}.departments(id) ON DELETE SET NULL,
    attendant_id    uuid         NULL REFERENCES {TENANT_SCHEMA}.attendants(id)  ON DELETE SET NULL,
    is_active       boolean      NOT NULL DEFAULT true,
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now()
);

-- Um atendente vinculado a no máximo 1 profissional (FR-008).
CREATE UNIQUE INDEX IF NOT EXISTS idx_professionals_attendant_unique
    ON {TENANT_SCHEMA}.professionals (attendant_id)
    WHERE attendant_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_professionals_active_name
    ON {TENANT_SCHEMA}.professionals (is_active, name);

-- ---------------------------------------------------------------------------
-- 3) professional_services — junção N×N
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.professional_services (
    id                uuid NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    professional_id   uuid NOT NULL REFERENCES {TENANT_SCHEMA}.professionals(id) ON DELETE CASCADE,
    service_id        uuid NOT NULL REFERENCES {TENANT_SCHEMA}.services(id)      ON DELETE RESTRICT,
    CONSTRAINT uq_ps_unique UNIQUE (professional_id, service_id)
);

CREATE INDEX IF NOT EXISTS idx_ps_service
    ON {TENANT_SCHEMA}.professional_services (service_id);

COMMIT;
