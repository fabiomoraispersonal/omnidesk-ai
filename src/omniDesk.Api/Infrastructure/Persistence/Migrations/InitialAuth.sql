-- Migration: InitialAuth
-- Generated: 2026-05-05
-- Run via: dotnet ef migrations add InitialAuth (after dotnet new scaffold)
-- Or apply directly to PostgreSQL for initial setup

-- Enum user_role
DO $$ BEGIN
    CREATE TYPE user_role AS ENUM ('SaasAdmin', 'TenantAdmin', 'Supervisor', 'Attendant');
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- users
CREATE TABLE IF NOT EXISTS public.users (
    id             uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    email          varchar(255) NOT NULL,
    name           varchar(100) NOT NULL DEFAULT '',
    password_hash  text         NOT NULL,
    role           user_role    NOT NULL,
    tenant_id      uuid,
    is_active      boolean      NOT NULL DEFAULT true,
    email_verified boolean      NOT NULL DEFAULT false,
    totp_secret    text,
    totp_enabled   boolean      NOT NULL DEFAULT false,
    last_login_at  timestamptz,
    created_at     timestamptz  NOT NULL DEFAULT now(),
    updated_at     timestamptz  NOT NULL DEFAULT now(),
    deactivated_at timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_users_email ON public.users (email);
CREATE INDEX IF NOT EXISTS idx_users_tenant_id ON public.users (tenant_id);

-- refresh_tokens
CREATE TABLE IF NOT EXISTS public.refresh_tokens (
    id          uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    user_id     uuid         NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    token_hash  text         NOT NULL,
    expires_at  timestamptz  NOT NULL,
    revoked     boolean      NOT NULL DEFAULT false,
    revoked_at  timestamptz,
    user_agent  varchar(512),
    ip_address  varchar(45),
    created_at  timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_refresh_tokens_token_hash ON public.refresh_tokens (token_hash);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id ON public.refresh_tokens (user_id);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_expires_at ON public.refresh_tokens (expires_at);

-- invite_tokens
CREATE TABLE IF NOT EXISTS public.invite_tokens (
    id              uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    email           varchar(255) NOT NULL,
    role            user_role    NOT NULL,
    tenant_id       uuid,
    token_hash      text         NOT NULL,
    expires_at      timestamptz  NOT NULL,
    accepted_at     timestamptz,
    invalidated_at  timestamptz,
    created_by      uuid         NOT NULL REFERENCES public.users(id),
    created_at      timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_invite_tokens_token_hash ON public.invite_tokens (token_hash);
CREATE INDEX IF NOT EXISTS idx_invite_tokens_email ON public.invite_tokens (email);

-- password_reset_tokens
CREATE TABLE IF NOT EXISTS public.password_reset_tokens (
    id          uuid        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    user_id     uuid        NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    token_hash  text        NOT NULL,
    expires_at  timestamptz NOT NULL,
    used_at     timestamptz,
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_password_reset_tokens_token_hash ON public.password_reset_tokens (token_hash);
CREATE INDEX IF NOT EXISTS idx_password_reset_tokens_user_id ON public.password_reset_tokens (user_id);

-- totp_recovery_codes
CREATE TABLE IF NOT EXISTS public.totp_recovery_codes (
    id          uuid        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    user_id     uuid        NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    code_hash   text        NOT NULL,
    used_at     timestamptz,
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_totp_recovery_codes_code_hash ON public.totp_recovery_codes (code_hash);
CREATE INDEX IF NOT EXISTS idx_totp_recovery_codes_user_id ON public.totp_recovery_codes (user_id);
