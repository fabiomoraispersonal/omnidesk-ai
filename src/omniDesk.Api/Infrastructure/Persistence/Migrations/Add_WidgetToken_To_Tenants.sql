-- Migration: Add_WidgetToken_To_Tenants
-- Generated: 2026-05-09
-- Spec: 007-live-chat-widget
-- Adiciona coluna pública widget_token em public.tenants. UUID público, fixo, imutável,
-- não-secreto, identificador do tenant nas requisições do widget JS embarcado em sites de terceiros.

ALTER TABLE public.tenants
    ADD COLUMN IF NOT EXISTS widget_token uuid NOT NULL DEFAULT gen_random_uuid();

CREATE UNIQUE INDEX IF NOT EXISTS ux_tenants_widget_token
    ON public.tenants (widget_token);

-- Após backfill via DEFAULT, remove o default para forçar geração explícita no
-- TenantProvisioningJob (assim cada tenant novo recebe um token gerado em código,
-- auditável no log de provisioning).
ALTER TABLE public.tenants
    ALTER COLUMN widget_token DROP DEFAULT;
