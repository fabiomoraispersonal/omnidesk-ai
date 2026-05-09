-- Migration: Add_AiAgents_AiSettings
-- Generated: 2026-05-08
-- Spec: 006-ai-agents
-- Cria tabelas tenant-scoped para agentes de IA, configurações e bridge transitional para conversas.

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ai_agents (
    id                   uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    template_id          uuid,
    type                 varchar(16)  NOT NULL CHECK (type IN ('orchestrator','sub_agent')),
    name                 varchar(100) NOT NULL,
    short_description    varchar(300) NOT NULL DEFAULT '',
    prompt               text         NOT NULL,
    model                varchar(50)  NOT NULL DEFAULT 'gpt-4o',
    department_id        uuid         REFERENCES {TENANT_SCHEMA}.departments(id) ON DELETE RESTRICT,
    openai_assistant_id  varchar(100),
    is_active            boolean      NOT NULL DEFAULT true,
    created_by           uuid         NOT NULL,
    created_at           timestamptz  NOT NULL DEFAULT now(),
    updated_at           timestamptz  NOT NULL DEFAULT now(),
    deleted_at           timestamptz,
    CONSTRAINT chk_orchestrator_no_dept CHECK (
        (type = 'orchestrator' AND department_id IS NULL)
        OR (type = 'sub_agent' AND department_id IS NOT NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_agents_orchestrator
    ON {TENANT_SCHEMA}.ai_agents (type) WHERE type = 'orchestrator' AND deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_ai_agents_type_active
    ON {TENANT_SCHEMA}.ai_agents (type, is_active) WHERE deleted_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_ai_agents_department_id
    ON {TENANT_SCHEMA}.ai_agents (department_id) WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ai_settings (
    id                       uuid        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    tenant_id                uuid        NOT NULL UNIQUE,
    context_window_messages  int         NOT NULL DEFAULT 20
        CHECK (context_window_messages BETWEEN 5 AND 100),
    available_models         text[]      NOT NULL DEFAULT ARRAY[]::text[],
    updated_at               timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ai_threads (
    id                          uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    external_conversation_ref   varchar(100) NOT NULL,
    openai_thread_id            varchar(100) NOT NULL UNIQUE,
    current_agent_id            uuid REFERENCES {TENANT_SCHEMA}.ai_agents(id) ON DELETE SET NULL,
    handed_off_to_human_at      timestamptz,
    created_at                  timestamptz  NOT NULL DEFAULT now(),
    updated_at                  timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_threads_external_ref
    ON {TENANT_SCHEMA}.ai_threads (external_conversation_ref);

CREATE INDEX IF NOT EXISTS idx_ai_threads_current_agent
    ON {TENANT_SCHEMA}.ai_threads (current_agent_id) WHERE handed_off_to_human_at IS NULL;
