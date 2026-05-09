-- Migration: Add_DefaultDepartmentId_To_Tenants
-- Generated: 2026-05-08
-- Spec: 006-ai-agents (extensão da Spec 003)
-- Adiciona coluna usada pelo transbordo automático do Orchestrator quando ele não tem departamento próprio.

ALTER TABLE public.tenants
    ADD COLUMN IF NOT EXISTS default_department_id uuid;

COMMENT ON COLUMN public.tenants.default_department_id IS
    'FK lógica para tenant_{slug}.departments(id). Validação cross-schema feita na aplicação. Usado em transbordo automático de Orchestrator (Spec 006).';
