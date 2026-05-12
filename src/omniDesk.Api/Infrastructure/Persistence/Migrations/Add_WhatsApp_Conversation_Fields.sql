-- Migration: Add_WhatsApp_Conversation_Fields
-- Generated: 2026-05-10
-- Spec: 008-whatsapp-channel
-- Adiciona campos específicos do canal WhatsApp em conversations (Spec 007 canal-agnóstica).
-- Ambos NULL para conversas live_chat existentes — sem migração de dados.

ALTER TABLE {TENANT_SCHEMA}.conversations
    ADD COLUMN IF NOT EXISTS wa_contact_phone      varchar(20),
    ADD COLUMN IF NOT EXISTS wa_session_expires_at timestamptz;

-- E.164 simples (apenas quando preenchido). DROP IF EXISTS evita falha se já aplicado.
ALTER TABLE {TENANT_SCHEMA}.conversations
    DROP CONSTRAINT IF EXISTS chk_conv_wa_phone_format;
ALTER TABLE {TENANT_SCHEMA}.conversations
    ADD CONSTRAINT chk_conv_wa_phone_format
    CHECK (wa_contact_phone IS NULL OR wa_contact_phone ~ '^\+[1-9]\d{6,18}$');

-- Index parcial para o sweep do WaSessionExpiringNotifierJob (50 convs ativas/tenant em pico).
CREATE INDEX IF NOT EXISTS idx_conversations_wa_session_expiring
    ON {TENANT_SCHEMA}.conversations (wa_session_expires_at)
    WHERE channel = 'whatsapp' AND status = 'open';

-- Index parcial para lookup de conversa ativa por número (incoming webhook).
CREATE INDEX IF NOT EXISTS idx_conversations_wa_contact_phone
    ON {TENANT_SCHEMA}.conversations (wa_contact_phone)
    WHERE channel = 'whatsapp' AND wa_contact_phone IS NOT NULL;
