-- Migration: Add_ApiKeys
-- Generated: 2026-05-13
-- Spec: 012-audit-observabilidade (T009)
--
-- Cria a tabela api_keys no schema do tenant para gestão de API Keys
-- usadas por ferramentas externas (Metabase, etc.) para acessar os audit logs.
-- Máximo 5 keys ativas por tenant (enforced na application layer).
--
-- Idempotente. Sem dependências de outras migrations.

BEGIN;

CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.api_keys (
    id              uuid            NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    tenant_id       uuid            NOT NULL,
    name            varchar(100)    NOT NULL,
    key_hash        text            NOT NULL,
    scopes          text[]          NOT NULL DEFAULT ARRAY['audit_logs:read'],
    last_used_at    timestamptz,
    expires_at      timestamptz,
    revoked         boolean         NOT NULL DEFAULT false,
    created_at      timestamptz     NOT NULL DEFAULT now(),

    CONSTRAINT uq_api_keys_hash UNIQUE (key_hash)
);

CREATE INDEX IF NOT EXISTS idx_api_keys_tenant_active
    ON {TENANT_SCHEMA}.api_keys (tenant_id, revoked)
    WHERE revoked = false;

COMMIT;
