-- Migration: Add_Pipelines
-- Generated: 2026-05-11
-- Spec: 009-tickets-crm (T009)
-- Creates pipelines + pipeline_columns tables.
-- Bootstraps 1 pipeline per existing department with 3 default columns.

BEGIN;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.pipelines (
    id             uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    department_id  uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.departments(id) ON DELETE CASCADE,
    name           varchar(100) NOT NULL DEFAULT 'Pipeline',
    created_at     timestamptz  NOT NULL DEFAULT now(),
    updated_at     timestamptz  NOT NULL DEFAULT now(),
    deleted_at     timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_pipelines_department_id
    ON {TENANT_SCHEMA}.pipelines (department_id)
    WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.pipeline_columns (
    id              uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    pipeline_id     uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.pipelines(id) ON DELETE CASCADE,
    name            varchar(100) NOT NULL,
    status_mapping  varchar(16)  NOT NULL
                    CHECK (status_mapping IN ('new','in_progress','waiting_client')),
    "order"         int          NOT NULL CHECK ("order" >= 1),
    color           varchar(7)   CHECK (color IS NULL OR color ~ '^#[0-9A-Fa-f]{6}$'),
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_pipeline_columns_pipeline_status
    ON {TENANT_SCHEMA}.pipeline_columns (pipeline_id, status_mapping);

-- Bootstrap: create 1 pipeline per existing department (idempotent via ON CONFLICT DO NOTHING)
INSERT INTO {TENANT_SCHEMA}.pipelines (department_id, name)
SELECT id, 'Pipeline'
FROM {TENANT_SCHEMA}.departments
WHERE deleted_at IS NULL
  AND id NOT IN (
      SELECT department_id FROM {TENANT_SCHEMA}.pipelines WHERE deleted_at IS NULL
  )
ON CONFLICT DO NOTHING;

-- Bootstrap: create 3 default columns per pipeline (idempotent)
INSERT INTO {TENANT_SCHEMA}.pipeline_columns (pipeline_id, name, status_mapping, "order")
SELECT p.id, c.name, c.status_mapping, c."order"
FROM {TENANT_SCHEMA}.pipelines p
CROSS JOIN (VALUES
    ('Na Fila',            'new',            1),
    ('Em Andamento',       'in_progress',    2),
    ('Aguardando Cliente', 'waiting_client', 3)
) AS c(name, status_mapping, "order")
ON CONFLICT (pipeline_id, status_mapping) DO NOTHING;

COMMIT;
