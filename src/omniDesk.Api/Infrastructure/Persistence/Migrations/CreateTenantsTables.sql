-- Migration: CreateTenantsTables
-- Generated: 2026-05-06

DO $$ BEGIN
    CREATE TYPE tenant_status AS ENUM ('Provisioning', 'Active', 'Blocked', 'Error');
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE TYPE contact_type AS ENUM ('Financial', 'Technical');
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    CREATE TYPE agent_type AS ENUM ('Orchestrator', 'SubAgent');
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

CREATE TABLE IF NOT EXISTS public.tenants (
    id                    uuid           NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    slug                  varchar(50)    NOT NULL,
    razao_social          varchar(255)   NOT NULL,
    nome_fantasia         varchar(255),
    cnpj                  varchar(18)    NOT NULL,
    status                tenant_status  NOT NULL DEFAULT 'Provisioning',
    openai_api_key_enc    varchar(512),
    openai_organization   varchar(255),
    openai_project        varchar(255),
    timezone              varchar(50)    NOT NULL DEFAULT 'America/Sao_Paulo',
    locale                varchar(10)    NOT NULL DEFAULT 'pt-BR',
    currency              varchar(3)     NOT NULL DEFAULT 'BRL',
    date_format           varchar(20)    NOT NULL DEFAULT 'dd/MM/yyyy',
    provisioning_error_log text,
    created_at            timestamptz    NOT NULL DEFAULT now(),
    updated_at            timestamptz    NOT NULL DEFAULT now(),
    blocked_at            timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_tenants_slug   ON public.tenants (slug);
CREATE UNIQUE INDEX IF NOT EXISTS idx_tenants_cnpj   ON public.tenants (cnpj);
CREATE INDEX        IF NOT EXISTS idx_tenants_status ON public.tenants (status);

CREATE TABLE IF NOT EXISTS public.tenant_contacts (
    id          uuid          NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    tenant_id   uuid          NOT NULL REFERENCES public.tenants(id) ON DELETE CASCADE,
    type        contact_type  NOT NULL,
    name        varchar(255)  NOT NULL,
    email       varchar(255)  NOT NULL,
    phone       varchar(20)   NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_contacts_tenant_type ON public.tenant_contacts (tenant_id, type);
CREATE INDEX        IF NOT EXISTS idx_tenant_contacts_tenant_id  ON public.tenant_contacts (tenant_id);

CREATE TABLE IF NOT EXISTS public.agent_templates (
    id                          uuid        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    name                        varchar(255) NOT NULL,
    type                        agent_type  NOT NULL,
    description                 text        NOT NULL,
    prompt                      text,
    is_active                   boolean     NOT NULL DEFAULT true,
    used_in_provisioning_count  integer     NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    deleted_at                  timestamptz
);

CREATE INDEX IF NOT EXISTS idx_agent_templates_active ON public.agent_templates (is_active) WHERE deleted_at IS NULL;
