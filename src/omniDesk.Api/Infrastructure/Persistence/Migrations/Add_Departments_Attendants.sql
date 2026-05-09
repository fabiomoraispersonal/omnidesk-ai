-- Migration: Add_Departments_Attendants
-- Generated: 2026-05-07
-- Spec: 005-departments-attendants (T002)
--
-- Creates the 5 tables required by Spec 005 inside the tenant schema (`tenant_{slug}`).
-- This script is idempotent and run by the tenant provisioning pipeline (Spec 003) for every
-- new tenant. Existing tenants must run it once via `dotnet ef database update` per schema.
--
-- Convention (Constitution §I): the schema name is interpolated by the provisioner;
-- this file uses `{TENANT_SCHEMA}` as a placeholder that the provisioner replaces with the
-- actual schema (e.g. `tenant_clinica_x`).

-- ---------------------------------------------------------------------------
-- 1. departments
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.departments (
    id                          uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    name                        varchar(100) NOT NULL,
    description                 text,
    business_hours_start        time,
    business_hours_end          time,
    business_days               int[],
    sla_first_response_minutes  int          CHECK (sla_first_response_minutes IS NULL OR sla_first_response_minutes > 0),
    sla_resolution_minutes      int          CHECK (sla_resolution_minutes IS NULL OR sla_resolution_minutes > 0),
    is_active                   boolean      NOT NULL DEFAULT true,
    created_at                  timestamptz  NOT NULL DEFAULT now(),
    updated_at                  timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT departments_business_hours_consistency CHECK (
        (business_hours_start IS NULL AND business_hours_end IS NULL AND business_days IS NULL)
        OR
        (business_hours_start IS NOT NULL AND business_hours_end IS NOT NULL AND business_days IS NOT NULL
         AND business_hours_start < business_hours_end)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS uniq_departments_name_active
    ON {TENANT_SCHEMA}.departments (lower(name)) WHERE is_active = true;
CREATE INDEX IF NOT EXISTS idx_departments_is_active
    ON {TENANT_SCHEMA}.departments (is_active);

-- ---------------------------------------------------------------------------
-- 2. attendants
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.attendants (
    id                       uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    user_id                  uuid         NOT NULL REFERENCES public.users(id) ON DELETE RESTRICT,
    name                     varchar(255) NOT NULL,
    avatar_url               varchar(500),
    max_simultaneous_chats   int          NOT NULL DEFAULT 5
                                          CHECK (max_simultaneous_chats BETWEEN 1 AND 100),
    active_ticket_count      int          NOT NULL DEFAULT 0
                                          CHECK (active_ticket_count >= 0),
    is_active                boolean      NOT NULL DEFAULT true,
    created_at               timestamptz  NOT NULL DEFAULT now(),
    updated_at               timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uniq_attendants_user_id
    ON {TENANT_SCHEMA}.attendants (user_id);
CREATE INDEX IF NOT EXISTS idx_attendants_is_active
    ON {TENANT_SCHEMA}.attendants (is_active);

-- ---------------------------------------------------------------------------
-- 3. attendant_departments  (N:N)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.attendant_departments (
    attendant_id   uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.attendants(id) ON DELETE CASCADE,
    department_id  uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.departments(id) ON DELETE RESTRICT,
    is_primary     boolean      NOT NULL DEFAULT false,
    created_at     timestamptz  NOT NULL DEFAULT now(),
    PRIMARY KEY (attendant_id, department_id)
);

CREATE INDEX IF NOT EXISTS idx_attendant_departments_dept
    ON {TENANT_SCHEMA}.attendant_departments (department_id);

-- One primary department per attendant (partial unique index — only the rows where is_primary=true).
CREATE UNIQUE INDEX IF NOT EXISTS uniq_attendant_departments_primary
    ON {TENANT_SCHEMA}.attendant_departments (attendant_id) WHERE is_primary = true;

-- ---------------------------------------------------------------------------
-- 4. attendant_status (1:1 — current presence snapshot for reports)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.attendant_status (
    attendant_id        uuid         NOT NULL PRIMARY KEY
                                     REFERENCES {TENANT_SCHEMA}.attendants(id) ON DELETE CASCADE,
    status              varchar(10)  NOT NULL DEFAULT 'offline'
                                     CHECK (status IN ('online', 'away', 'offline')),
    changed_at          timestamptz  NOT NULL DEFAULT now(),
    changed_by          varchar(8)   NOT NULL DEFAULT 'manual'
                                     CHECK (changed_by IN ('manual', 'system')),
    last_heartbeat_at   timestamptz
);

CREATE INDEX IF NOT EXISTS idx_attendant_status_status
    ON {TENANT_SCHEMA}.attendant_status (status);

-- ---------------------------------------------------------------------------
-- 5. canned_responses
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.canned_responses (
    id              uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    title           varchar(100) NOT NULL,
    content         text         NOT NULL,
    department_id   uuid         REFERENCES {TENANT_SCHEMA}.departments(id) ON DELETE CASCADE,
    created_by      uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.attendants(id) ON DELETE RESTRICT,
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_canned_responses_department_id
    ON {TENANT_SCHEMA}.canned_responses (department_id);

-- Title uniqueness per scope (NULL department = global). The expression coalesces NULL to a sentinel
-- so the unique index treats global and per-dept titles independently.
CREATE UNIQUE INDEX IF NOT EXISTS uniq_canned_responses_title_per_scope
    ON {TENANT_SCHEMA}.canned_responses (
        lower(title),
        coalesce(department_id, '00000000-0000-0000-0000-000000000000'::uuid)
    );

-- pg_trgm for fast title search (already enabled cluster-wide).
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX IF NOT EXISTS idx_canned_responses_title_trgm
    ON {TENANT_SCHEMA}.canned_responses USING gin (title gin_trgm_ops);
