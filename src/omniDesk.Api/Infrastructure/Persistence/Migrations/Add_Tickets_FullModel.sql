-- Migration: Add_Tickets_FullModel
-- Generated: 2026-05-11
-- Spec: 009-tickets-crm (T006)
--
-- Expande o scaffold `tickets` da Spec 005 para a versão V2 completa exigida pela Spec 009:
--   * Mapeia status antigos (`queued`/`assigned`/`open`/`resolved`/`closed`) para o novo enum
--     (`new`/`in_progress`/`waiting_client`/`resolved`/`cancelled`).
--   * Adiciona 17 colunas novas (protocol, channel, priority, conversation_id, contact_id, tags,
--     resolved_at, cancelled_at, first_response_at, sla_first_response_deadline,
--     sla_resolution_deadline, sla_paused_duration_minutes, waiting_client_since,
--     has_reminder_alert, deleted_at, search_vector — generated coluna).
--   * Renomeia `assigned_attendant_id` → `attendant_id` (alinha com a Spec 009 §data-model).
--   * Cria índices: contact_id, conversation_id, created_at DESC, GIN(search_vector), GIN(tags),
--     UNIQUE parcial em protocol (WHERE protocol IS NOT NULL).
--
-- Migration idempotente e transacional. Roda dentro de `BEGIN/COMMIT` para garantir atomicidade
-- — se algo falhar, nada é persistido. Convenção §I do CLAUDE.md: o schema é interpolado pelo
-- provisioner via placeholder `{TENANT_SCHEMA}`.
--
-- NOTA IMPORTANTE — Constraints semânticas adiadas:
-- As CHECKs `chk_tickets_resolved_at`, `chk_tickets_cancelled_at` e `chk_tickets_waiting_client_since`
-- exigidas pela data-model.md NÃO são adicionadas aqui porque tickets já existentes (criados pelo
-- scaffold Spec 005) com status `resolved`/`cancelled` (mapeados de `resolved`/`closed`) NÃO têm
-- `resolved_at`/`cancelled_at` populados — adicionar a constraint iria falhar. A
-- `BackfillTicketProtocolJob` (T060) + `BackfillTicketTimestampsJob` preenchem esses timestamps em
-- batch após esta migração; em V1.1 (após backfill completar) uma migração subsequente adiciona as
-- constraints e o `protocol NOT NULL`. Ver `specs/009-tickets-crm/data-model.md §Migrations`.

BEGIN;

-- ---------------------------------------------------------------------------
-- 1) Remove o CHECK antigo de status (queued/assigned/open/resolved/closed)
--    para permitir o UPDATE de mapeamento sem violar a constraint.
-- ---------------------------------------------------------------------------
ALTER TABLE {TENANT_SCHEMA}.tickets
    DROP CONSTRAINT IF EXISTS tickets_status_check;

-- ---------------------------------------------------------------------------
-- 2) Data migration: mapeia status antigos → novos.
--    queued    → new            (na fila, sem atendente)
--    assigned  → in_progress    (atribuído, atendente trabalhando)
--    open      → in_progress    (em atendimento ativo)
--    resolved  → resolved       (idem)
--    closed    → cancelled      (semântica V2: closed virou cancelled)
--    ELSE: preserva (idempotência — se rodar 2x, o valor já novo passa intacto).
-- ---------------------------------------------------------------------------
UPDATE {TENANT_SCHEMA}.tickets SET status = CASE
    WHEN status = 'queued'   THEN 'new'
    WHEN status = 'assigned' THEN 'in_progress'
    WHEN status = 'open'     THEN 'in_progress'
    WHEN status = 'resolved' THEN 'resolved'
    WHEN status = 'closed'   THEN 'cancelled'
    ELSE status
END;

-- ---------------------------------------------------------------------------
-- 3) Recria o CHECK com os novos valores.
-- ---------------------------------------------------------------------------
ALTER TABLE {TENANT_SCHEMA}.tickets
    ADD CONSTRAINT tickets_status_check
    CHECK (status IN ('new','in_progress','waiting_client','resolved','cancelled'));

-- ---------------------------------------------------------------------------
-- 4) Adiciona as 17 colunas novas (todas com IF NOT EXISTS — idempotente).
--    `search_vector` é GENERATED ALWAYS — pesos: A=protocol, B=subject (busca FTS R4).
-- ---------------------------------------------------------------------------
ALTER TABLE {TENANT_SCHEMA}.tickets
    ADD COLUMN IF NOT EXISTS protocol                       varchar(20),
    ADD COLUMN IF NOT EXISTS channel                        varchar(16)
        CHECK (channel IN ('live_chat','whatsapp','manual')) DEFAULT 'manual',
    ADD COLUMN IF NOT EXISTS priority                       varchar(8)
        CHECK (priority IN ('low','normal','high','urgent')) DEFAULT 'normal',
    ADD COLUMN IF NOT EXISTS conversation_id                uuid,
    ADD COLUMN IF NOT EXISTS contact_id                     uuid,
    ADD COLUMN IF NOT EXISTS tags                           text[] DEFAULT '{}',
    ADD COLUMN IF NOT EXISTS resolved_at                    timestamptz,
    ADD COLUMN IF NOT EXISTS cancelled_at                   timestamptz,
    ADD COLUMN IF NOT EXISTS first_response_at              timestamptz,
    ADD COLUMN IF NOT EXISTS sla_first_response_deadline    timestamptz,
    ADD COLUMN IF NOT EXISTS sla_resolution_deadline        timestamptz,
    ADD COLUMN IF NOT EXISTS sla_paused_duration_minutes    int NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS waiting_client_since           timestamptz,
    ADD COLUMN IF NOT EXISTS has_reminder_alert             boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS deleted_at                     timestamptz,
    ADD COLUMN IF NOT EXISTS search_vector                  tsvector
        GENERATED ALWAYS AS (
            setweight(to_tsvector('portuguese', coalesce(protocol, '')), 'A') ||
            setweight(to_tsvector('portuguese', coalesce(subject, '')), 'B')
        ) STORED;

-- Defaults para `channel` e `priority` aplicam-se apenas a colunas recém-criadas (ADD COLUMN
-- com DEFAULT preenche linhas existentes em uma única operação). Após esta migração, o NOT NULL
-- semântico é garantido pelo código (CreateTicketEndpoint sempre seta esses campos).

-- ---------------------------------------------------------------------------
-- 5) Rename `assigned_attendant_id` → `attendant_id` (data-model.md §1).
--    PostgreSQL não suporta `RENAME COLUMN IF EXISTS` diretamente em todas as versões,
--    mas o rename é atômico — se a coluna já foi renomeada em uma execução anterior,
--    o bloco DO abaixo trata isso silenciosamente.
-- ---------------------------------------------------------------------------
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_schema = '{TENANT_SCHEMA}'
           AND table_name   = 'tickets'
           AND column_name  = 'assigned_attendant_id'
    ) THEN
        EXECUTE 'ALTER TABLE {TENANT_SCHEMA}.tickets RENAME COLUMN assigned_attendant_id TO attendant_id';
    END IF;
END $$;

ALTER INDEX IF EXISTS {TENANT_SCHEMA}.idx_tickets_assigned_attendant_id
                  RENAME TO idx_tickets_attendant_id;

-- ---------------------------------------------------------------------------
-- 6) Índices novos.
--    - `idx_tickets_status` e `idx_tickets_department_id` já existem (Spec 005).
--    - `idx_tickets_attendant_id` já foi renomeado acima.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS idx_tickets_contact_id
    ON {TENANT_SCHEMA}.tickets (contact_id);

CREATE INDEX IF NOT EXISTS idx_tickets_conversation_id
    ON {TENANT_SCHEMA}.tickets (conversation_id);

CREATE INDEX IF NOT EXISTS idx_tickets_created_at
    ON {TENANT_SCHEMA}.tickets (created_at DESC);

CREATE INDEX IF NOT EXISTS idx_tickets_search_vector
    ON {TENANT_SCHEMA}.tickets USING GIN (search_vector);

CREATE INDEX IF NOT EXISTS idx_tickets_tags
    ON {TENANT_SCHEMA}.tickets USING GIN (tags);

CREATE UNIQUE INDEX IF NOT EXISTS uq_tickets_protocol
    ON {TENANT_SCHEMA}.tickets (protocol) WHERE protocol IS NOT NULL;

-- ---------------------------------------------------------------------------
-- 7) FKs adicionais — DEFERIDAS.
--    `contact_id` referencia `contacts(id)` que é criada em `Add_Contacts.sql` (T007).
--    A FK é adicionada NAQUELA migração para respeitar a ordem topológica.
-- ---------------------------------------------------------------------------
-- ALTER TABLE {TENANT_SCHEMA}.tickets ADD CONSTRAINT fk_tickets_contact
--     FOREIGN KEY (contact_id) REFERENCES {TENANT_SCHEMA}.contacts(id) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------
-- 8) CHECKs semânticas — DEFERIDAS (ver NOTA no cabeçalho).
--    Tickets existentes com status `resolved`/`cancelled` (mapeados de `resolved`/`closed`)
--    não possuem `resolved_at`/`cancelled_at` populados — adicionar essas constraints agora
--    falharia. Os backfill jobs (T060+) preenchem esses timestamps; uma migração V1.1
--    posterior adiciona as constraints:
-- ALTER TABLE {TENANT_SCHEMA}.tickets
--     ADD CONSTRAINT chk_tickets_resolved_at        CHECK ((resolved_at        IS NULL) = (status != 'resolved')),
--     ADD CONSTRAINT chk_tickets_cancelled_at       CHECK ((cancelled_at       IS NULL) = (status != 'cancelled')),
--     ADD CONSTRAINT chk_tickets_waiting_client_since CHECK ((waiting_client_since IS NULL) = (status != 'waiting_client'));

COMMIT;

-- ---------------------------------------------------------------------------
-- POST-MIGRATION:
--   1. `BackfillTicketProtocolJob` (T060) gera `protocol` para todos os tickets existentes
--      (formato TK-YYYYMMDD-XXXXX baseado em `created_at` + sequence per-day).
--   2. Em V1.1, após o backfill completar para 100% dos tickets:
--        ALTER TABLE {TENANT_SCHEMA}.tickets ALTER COLUMN protocol SET NOT NULL;
--        + adicionar as 3 CHECKs semânticas comentadas acima.
-- ---------------------------------------------------------------------------
