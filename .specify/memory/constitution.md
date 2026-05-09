<!--
Sync Impact Report
==================
Version change: 1.0.0 → 1.0.1
Bump rationale: PATCH — Principle II clarified after Spec 006 (Agentes de IA) experience.
  Replaced hardcoded "3+ unresolved exchanges" frustration heuristic with prompt-driven
  semantic detection. Substance preserved (handoff is mandatory; explicit keywords still
  hardcoded; no dead-ends). Mechanism updated. ADR-006-002 documents rationale.

Earlier history:
- 1.0.0 (2026-05-05): Initial population — 7 principles introduced (I Multi-Tenant
  Isolation NN, II AI-First, III Channel Agnosticism, IV Security/LGPD NN,
  V Simplicity, VI Observability, VII Test Discipline) + Tech Stack Constraints +
  Development Workflow + Governance.

Templates:
  - .specify/templates/plan-template.md       ✅ aligned (no change needed)
  - .specify/templates/spec-template.md       ✅ aligned
  - .specify/templates/tasks-template.md      ✅ aligned
  - .specify/templates/checklist-template.md  ⚠️ pending — verify manually
  - .specify/templates/commands/              ✅ N/A
-->

# OmniDesk CRM Constitution

## Core Principles

### I. Multi-Tenant Isolation (NON-NEGOTIABLE)

Every feature MUST respect tenant boundaries at every layer of the stack:

- Each tenant occupies a dedicated PostgreSQL schema: `tenant_{slug}`. All queries MUST
  operate within the resolved tenant schema — cross-tenant access is STRICTLY FORBIDDEN.
- Redis keys MUST follow the format `{tenant_slug}:{resource}:{id}` with no exceptions.
- MongoDB collections MUST follow the pattern `{tenant_slug}_{collection_name}`.
- MinIO buckets MUST follow `tenant-{slug}` (lowercase, hyphens only).
- The `TenantResolverMiddleware` MUST be the first middleware to execute and MUST resolve
  tenant context from the subdomain before any business logic runs.
- The `public` schema is reserved for system-level tables (`tenants`, `tenant_configs`) only;
  no feature data may be placed there.

**Rationale**: Shared infrastructure with schema-level isolation gives clear data boundaries,
enables per-tenant migrations without risk of data leakage, and allows future extraction of
high-volume tenants to dedicated instances.

### II. AI-First, Human-Assisted

All customer-facing conversations MUST be handled by the AI Orchestrator as the first point
of contact:

- The Orchestrator MUST evaluate all active sub-agents before generating any response.
- Sub-agents are tenant-configured (name, short description, full prompt, linked department,
  enabled/disabled). The Orchestrator uses the short description to route — never hardcoded logic.
- Handoff to a human MUST transfer the complete conversation context (including all AI messages)
  to the resulting ticket. No conversation history may be lost or truncated during handoff.
- The system MUST detect handoff triggers: (a) explicit keywords detected in client messages
  ("atendente", "humano", "gerente"), enforced by the backend regardless of agent prompt;
  (b) semantic frustration signals — delegated to the agent prompt, with mandatory guidance
  in the global Orchestrator template recommending transfer when the client shows frustration,
  repeats unanswered questions, or raises out-of-scope topics. (Mechanism amended in PATCH 1.0.1
  per ADR-006-002 — see docs/adr/006-002-frustration-detection-via-prompt.md.)
- Every AI response path MUST have a graceful fallback. Dead ends — where the AI neither
  resolves nor hands off — are NOT acceptable.
- Human attendants MUST be able to see everything the AI said before they take over.

**Rationale**: The core value proposition of OmniDesk is instant, context-aware first contact.
If the AI path fails silently or loses context, the product fails its primary promise.

### III. Channel Agnosticism

The core conversation model, ticket lifecycle, and AI pipeline MUST be channel-agnostic:

- Live Chat (WebSocket) and WhatsApp (webhook) MUST feed into the same message processing
  pipeline without branching in business logic.
- All channel-specific code MUST be isolated in dedicated adapter classes. Adapters translate
  channel events into the internal `IncomingMessage` model and send `OutgoingMessage` to the
  channel — nothing else.
- Adding a new channel in the future MUST require only a new adapter, with zero changes to
  AI agents, ticket logic, or CRM views.

**Rationale**: Channels are a distribution concern; conversations and tickets are a business
concern. Mixing them creates coupling that makes every channel change break unrelated logic.

### IV. Security and LGPD Compliance (NON-NEGOTIABLE)

All data handling MUST comply with Brazil's Lei Geral de Proteção de Dados (LGPD):

- All data at rest MUST reside on infrastructure located in Brazilian national territory.
- The Live Chat widget MUST present an explicit data collection consent notice (opt-in) before
  capturing any PII. Conversations MUST NOT begin before consent is recorded.
- JWT access tokens MUST expire in ≤ 15 minutes. Refresh tokens MUST expire in ≤ 7 days.
- Refresh tokens MUST be stored only in `httpOnly` cookies — never in `localStorage` or
  `sessionStorage`.
- All public-facing forms (login, password recovery) MUST be protected by Cloudflare Turnstile.
- Secrets (API keys, JWT secret, database credentials) MUST never appear in source code, logs,
  or version control. Use environment variables exclusively.
- Production data MUST use soft delete (`deleted_at` timestamp). Physical deletes are FORBIDDEN
  in production data paths.
- Data retention per tenant MUST be configurable.

**Rationale**: LGPD non-compliance creates legal liability for the operator and tenants.
Security failures on a CRM handling client PII are existential risks for the product.

### V. Simplicity and Deliberate Scope

Scope and complexity are actively managed constraints, not passive outcomes:

- V1 scope is defined by the PRD. Features explicitly listed as "V2+" MUST NOT be implemented
  in V1. Scope additions require explicit product approval.
- Every non-obvious architectural pattern MUST be preceded by an ADR. Patterns introduced
  without an ADR are subject to mandatory removal.
- Third-party dependencies are introduced only to solve a specific, proven problem — not
  speculatively. Prefer standard .NET 10 and Angular 21 built-ins.
- The `plan.md` Complexity Tracking table MUST document every justified deviation from
  standard patterns before implementation begins.
- YAGNI applies: no abstractions for hypothetical future requirements. Three similar code paths
  are preferable to a premature abstraction that couples unrelated concerns.

**Rationale**: OmniDesk is a V1 product with a defined target. Scope and complexity creep are
the primary risks to on-time delivery and maintainability.

### VI. Observability and Auditability

The system MUST produce measurable, auditable records of its behavior at all times:

- Every ticket MUST retain a complete, immutable conversation history — including all AI messages,
  agent transitions, and human responses. No history may be deleted or overwritten.
- All backend structured logs MUST be produced via Serilog and persisted to MongoDB.
- Every AI-to-human and human-to-AI transition MUST be recorded as a discrete, timestamped event.
- The system MUST expose data sufficient to measure all PRD success metrics:
  - Median time to first AI response (target < 5 s)
  - AI resolution rate without handoff (target > 40%)
  - Median human response time after handoff (target < 10 min)
  - Platform uptime (target > 99.5%)
- Internal attendant notes MUST be visually and structurally separated from customer-facing
  conversation history in both storage and the CRM UI.

**Rationale**: Without auditability, tenants cannot trust the system with client PII. Without
observability, the operator cannot detect degradation before tenants do.

### VII. Test Discipline

Testing practices are defined by layer and MUST be followed consistently:

- **Backend**: Integration tests MUST use Testcontainers with real database instances.
  Mocking the database is FORBIDDEN in integration tests — a prior incident confirmed
  that mocked tests mask real migration failures.
- **Frontend**: Every Angular component and service MUST have a co-located `.spec.ts` file.
  Angular test files live alongside the file under test — no separate `tests/` folder.
- **No magic strings**: All repeated string constants (role names, queue names, error codes)
  MUST be defined in dedicated static classes (e.g., `Roles.TenantAdmin`, `QueueNames.Incoming`).
- **API contracts**: Contract tests validating endpoint request/response shapes MUST pass
  before integration tests run against those endpoints.

**Rationale**: Test coverage that does not reflect production behavior provides false confidence.
The database mock restriction is a direct consequence of a documented prod incident.

---

## Technology Stack Constraints

The following technology decisions are fixed for V1. Changes require a new ADR and a
constitution amendment before any implementation begins.

| Layer | Technology | Non-Negotiable Constraint |
|---|---|---|
| API runtime | .NET 10 | Minimal API + endpoint groups only — no controllers |
| ORM | Entity Framework Core 9 + Migrations | No manual SQL in production paths |
| Validation | FluentValidation | Applied at API boundary; never inside domain entities |
| Background jobs | Hangfire + Redis | RabbitMQ deferred to V2+ per ADR-004 |
| WebSocket | ASP.NET Core native WebSocket + Redis Pub/Sub | No SignalR per ADR-005 |
| Frontend | Angular 21 Standalone Components | No NgModules; Signals for local state |
| UI components | PrimeNG 21+ | No mixing of UI libraries |
| Auth | JWT Bearer + httpOnly refresh cookie | No session cookies, no `localStorage` for tokens |
| AI engine | OpenAI API — `gpt-4o` via openai-dotnet | No LangChain or intermediary AI frameworks |
| Email | SendGrid .NET SDK | Single transactional email provider |
| Hosting | Oracle Cloud ARM64 + Cloudflare Zero Trust Tunnel | All Docker images MUST target `linux/arm64` |
| Frontend CDN | Cloudflare Pages + Workers | No Nginx or Traefik |
| CI | GitHub Actions → Docker Hub push | CD via Portainer (manual pull + recreate) per ADR-007 |

---

## Development Workflow

All implementation MUST follow this sequence — no steps may be skipped:

1. **Spec first**: A feature `spec.md` MUST exist and be reviewed before any implementation.
2. **Plan before code**: A `plan.md` with a completed Constitution Check gate MUST be approved
   before writing production code.
3. **Migrations only**: Database changes MUST go through EF Core migrations. Ad-hoc SQL against
   production is FORBIDDEN.
4. **Environment variables**: All configuration values MUST use environment variables (API: `.env`;
   Frontend: `environment.ts` / `environment.prod.ts`). Hardcoded values are FORBIDDEN.
5. **Lazy loading**: All Angular feature routes MUST use lazy loading.
6. **Strict TypeScript**: `strict: true` is mandatory in all `tsconfig.json` files. `any` types
   are FORBIDDEN.
7. **Branch strategy**: All PRs target `main`. Force-push to `main` is FORBIDDEN.
8. **Commit hygiene**: Commits MUST be scoped to a single logical change. Mix of unrelated changes
   in one commit is discouraged and subject to review rejection.

---

## Governance

- This constitution supersedes all other development practices, guidance documents, and
  informal conventions. In case of conflict, the constitution wins.
- **Amendment procedure**:
  1. Identify the principle or section to change and the motivation.
  2. Increment the version number per semantic versioning:
     - MAJOR: removal or redefinition of an existing principle or NON-NEGOTIABLE rule.
     - MINOR: addition of a new principle or section; material expansion of guidance.
     - PATCH: clarification, wording, or typo fix with no semantic change.
  3. Update `LAST_AMENDED_DATE` to the amendment date.
  4. Propagate changes to all affected templates and specs (run consistency checklist).
  5. Record the change in the Sync Impact Report at the top of this file.
- **NON-NEGOTIABLE principles** (Principles I and IV) require escalation to the product owner
  before any workaround or exception is applied. No workaround is valid until documented
  and approved.
- **Constitution Check** in `plan.md` is a mandatory gate. No implementation phase begins
  without a passing Constitution Check for that feature.
- All ADRs are recorded in `docs/ARCHITECTURE.md` under the ADRs section and must reference
  the specific constitution principle they relate to.

**Version**: 1.0.1 | **Ratified**: 2026-05-05 | **Last Amended**: 2026-05-08
