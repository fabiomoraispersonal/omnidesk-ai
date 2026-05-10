-- Migration: Add_WhatsApp_Message_Fields
-- Generated: 2026-05-10
-- Spec: 008-whatsapp-channel
-- Adiciona campos necessários para rastrear mensagens via Meta WhatsApp:
-- - wa_message_id: ID da mensagem na Meta (wamid.HBgL...) para link 1:1 com status updates
-- Schema messages da Spec 007 não tem JSONB metadata, então usamos colunas dedicadas.

ALTER TABLE {TENANT_SCHEMA}.messages
    ADD COLUMN IF NOT EXISTS wa_message_id varchar(80);

-- Unique parcial: cada wa_message_id é único por tenant (Meta garante globalmente,
-- mas mantemos defesa local). Índice acelera lookup ao processar status updates.
CREATE UNIQUE INDEX IF NOT EXISTS ux_messages_wa_message_id
    ON {TENANT_SCHEMA}.messages (wa_message_id)
    WHERE wa_message_id IS NOT NULL;
