-- Migration: Add_LiveChat_Tables
-- Generated: 2026-05-09
-- Spec: 007-live-chat-widget
-- Cria tabelas tenant-scoped para o módulo Live Chat: widget_config (1:1 tenant), visitors,
-- conversations (canal-agnóstica via channel) e messages. Substitui o uso da tabela transitional
-- ai_threads da Spec 006 — ai_threads NÃO é dropada (rollback safe; deferida para spec futura).

-- ============================================================================
-- 1. widget_config (1:1 com tenant)
-- ============================================================================
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.widget_config (
    tenant_id                  uuid         NOT NULL PRIMARY KEY,
    is_enabled                 boolean      NOT NULL DEFAULT true,
    primary_color              varchar(7)   NOT NULL DEFAULT '#2563EB'
                               CHECK (primary_color ~ '^#[0-9A-Fa-f]{6}$'),
    launcher_icon              varchar(16)  NOT NULL DEFAULT 'chat'
                               CHECK (launcher_icon IN ('chat','message','support')),
    company_name               varchar(100) NOT NULL DEFAULT 'Atendimento',
    welcome_message            text         NOT NULL DEFAULT 'Olá! Como posso ajudar?',
    input_placeholder          varchar(150),
    position                   varchar(16)  NOT NULL DEFAULT 'bottom_right'
                               CHECK (position IN ('bottom_right','bottom_left')),
    require_identification     boolean      NOT NULL DEFAULT false,
    identification_fields      jsonb,
    allowed_domains            text[],
    privacy_policy_text        text,
    privacy_policy_url         varchar(500),
    abandonment_timeout_hours  int          NOT NULL DEFAULT 8
                               CHECK (abandonment_timeout_hours BETWEEN 1 AND 168),
    inactivity_close_hours     int          NOT NULL DEFAULT 24
                               CHECK (inactivity_close_hours BETWEEN 1 AND 168),
    updated_at                 timestamptz  NOT NULL DEFAULT now()
);

-- ============================================================================
-- 2. visitors
-- ============================================================================
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.visitors (
    id            uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    anonymous_id  uuid         NOT NULL UNIQUE,
    name          varchar(255),
    email         varchar(255),
    phone         varchar(20),
    created_at    timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_visitors_anonymous_id
    ON {TENANT_SCHEMA}.visitors (anonymous_id);

-- ============================================================================
-- 3. conversations (canal-agnóstica)
-- ============================================================================
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.conversations (
    id                uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    visitor_id        uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.visitors(id) ON DELETE RESTRICT,
    contact_id        uuid,                    -- FK lógica → contacts (Spec 008+)
    channel           varchar(16)  NOT NULL CHECK (channel IN ('live_chat','whatsapp')),
    status            varchar(16)  NOT NULL DEFAULT 'open'
                      CHECK (status IN ('open','resolved','abandoned')),
    agent_id          uuid,                    -- FK lógica → ai_agents (Spec 006, mesmo schema)
    attendant_id      uuid,                    -- FK lógica → attendants (Spec 005, mesmo schema)
    department_id     uuid,                    -- FK lógica → departments (Spec 005, mesmo schema)
    ticket_id         uuid,                    -- FK lógica → tickets (Spec 008+)
    openai_thread_id  varchar(64),
    lgpd_consent_at   timestamptz,
    ended_by          varchar(24)
                      CHECK (ended_by IN ('attendant','ai_agent','system_inactivity','system_disable')),
    ended_at          timestamptz,
    metadata          jsonb,
    last_message_at   timestamptz  NOT NULL DEFAULT now(),
    created_at        timestamptz  NOT NULL DEFAULT now(),
    updated_at        timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_conversations_visitor_id
    ON {TENANT_SCHEMA}.conversations (visitor_id);
CREATE INDEX IF NOT EXISTS idx_conversations_status_channel
    ON {TENANT_SCHEMA}.conversations (status, channel);
CREATE INDEX IF NOT EXISTS idx_conversations_attendant_id
    ON {TENANT_SCHEMA}.conversations (attendant_id) WHERE attendant_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_conversations_department_id
    ON {TENANT_SCHEMA}.conversations (department_id) WHERE department_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_conversations_open_idle
    ON {TENANT_SCHEMA}.conversations (status, last_message_at) WHERE status = 'open';
CREATE INDEX IF NOT EXISTS idx_conversations_openai_thread
    ON {TENANT_SCHEMA}.conversations (openai_thread_id) WHERE openai_thread_id IS NOT NULL;

-- ============================================================================
-- 4. messages
-- ============================================================================
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.messages (
    id                     uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    conversation_id        uuid         NOT NULL REFERENCES {TENANT_SCHEMA}.conversations(id) ON DELETE CASCADE,
    sender_type            varchar(16)  NOT NULL
                           CHECK (sender_type IN ('visitor','ai_agent','attendant','system')),
    sender_id              uuid,
    client_message_id      uuid,
    content_type           varchar(16)  NOT NULL
                           CHECK (content_type IN ('text','image','file','system_event')),
    content                text,
    attachment_url         varchar(500),
    attachment_name        varchar(255),
    attachment_size_bytes  int,
    is_read                boolean      NOT NULL DEFAULT false,
    created_at             timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_messages_conversation_id_created
    ON {TENANT_SCHEMA}.messages (conversation_id, created_at);

CREATE UNIQUE INDEX IF NOT EXISTS ux_messages_idempotency
    ON {TENANT_SCHEMA}.messages (conversation_id, client_message_id)
    WHERE client_message_id IS NOT NULL;

-- ============================================================================
-- 5. trigger para materializar last_message_at em conversations
-- ============================================================================
-- Trigger por schema é necessário porque o nome de tabela é qualificado.
-- A função é local ao schema do tenant.
CREATE OR REPLACE FUNCTION {TENANT_SCHEMA}.trg_update_last_message_at()
RETURNS TRIGGER AS $$
BEGIN
    UPDATE {TENANT_SCHEMA}.conversations
       SET last_message_at = NEW.created_at,
           updated_at      = now()
     WHERE id = NEW.conversation_id;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_messages_after_insert ON {TENANT_SCHEMA}.messages;
CREATE TRIGGER trg_messages_after_insert
AFTER INSERT ON {TENANT_SCHEMA}.messages
FOR EACH ROW EXECUTE FUNCTION {TENANT_SCHEMA}.trg_update_last_message_at();
