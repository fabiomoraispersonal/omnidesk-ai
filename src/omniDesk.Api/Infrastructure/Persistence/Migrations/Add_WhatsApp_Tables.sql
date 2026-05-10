-- Migration: Add_WhatsApp_Tables
-- Generated: 2026-05-10
-- Spec: 008-whatsapp-channel
-- Cria tabelas tenant-scoped para o canal WhatsApp Business: whatsapp_config (1:1 tenant)
-- e whatsapp_templates. Reaproveita conversations/messages/visitors da Spec 007 — apenas
-- adiciona 2 colunas em conversations via Add_WhatsApp_Conversation_Fields.sql.

-- ============================================================================
-- 1. whatsapp_config (1:1 com tenant)
-- ============================================================================
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.whatsapp_config (
    tenant_id                  uuid         NOT NULL PRIMARY KEY,
    is_enabled                 boolean      NOT NULL DEFAULT false,
    phone_number               varchar(20)
                               CHECK (phone_number IS NULL OR phone_number ~ '^\+[1-9]\d{6,18}$'),
    display_name               varchar(100),
    waba_id                    varchar(100),
    phone_number_id            varchar(100),
    access_token_ciphertext    text,
    app_secret_ciphertext      text,
    webhook_verify_token       varchar(64)  NOT NULL,
    business_hours_enabled     boolean      NOT NULL DEFAULT false,
    created_at                 timestamptz  NOT NULL DEFAULT now(),
    updated_at                 timestamptz  NOT NULL DEFAULT now(),
    deleted_at                 timestamptz
);

-- ============================================================================
-- 2. whatsapp_templates
-- ============================================================================
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.whatsapp_templates (
    id                  uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    tenant_id           uuid         NOT NULL,
    meta_template_id    varchar(100),
    type                varchar(40)  NOT NULL
                        CHECK (type IN ('appointment_reminder','appointment_confirmation','appointment_cancellation','follow_up','custom')),
    name                varchar(100) NOT NULL,
    category            varchar(20)  NOT NULL DEFAULT 'utility'
                        CHECK (category = 'utility'),
    language            varchar(10)  NOT NULL DEFAULT 'pt_BR'
                        CHECK (language = 'pt_BR'),
    status              varchar(20)  NOT NULL DEFAULT 'draft'
                        CHECK (status IN ('draft','pending_meta','approved','rejected')),
    body_template       text         NOT NULL,
    variable_labels     text[]       NOT NULL DEFAULT '{}',
    rejection_reason    text,
    submitted_at        timestamptz,
    approved_at         timestamptz,
    rejected_at         timestamptz,
    created_at          timestamptz  NOT NULL DEFAULT now(),
    updated_at          timestamptz  NOT NULL DEFAULT now(),
    deleted_at          timestamptz
);

-- Unique parcial: nomes únicos entre templates não-deletados.
CREATE UNIQUE INDEX IF NOT EXISTS ux_whatsapp_templates_tenant_name
    ON {TENANT_SCHEMA}.whatsapp_templates (tenant_id, name)
    WHERE deleted_at IS NULL;

-- Index para listagens filtradas por status (CRM).
CREATE INDEX IF NOT EXISTS idx_whatsapp_templates_status
    ON {TENANT_SCHEMA}.whatsapp_templates (status)
    WHERE deleted_at IS NULL;
