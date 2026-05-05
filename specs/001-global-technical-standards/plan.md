# Implementation Plan: Global Technical Standards

**Branch**: `001-global-technical-standards` | **Date**: 2026-05-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-global-technical-standards/spec.md`

## Summary

Establish the shared foundational layer used by every screen in `omniDesk.Admin` and
`omniDesk.Crm`, and by the `omniDesk.Api` on public endpoints. This covers:
design token system (CSS Custom Properties over PrimeNG Aura), brand asset conventions,
Brazilian field masks (ngx-mask), CNPJ/CPF check-digit validators, pt-BR locale
configuration, timezone-aware date display, encoding standards (.editorconfig, UTF-8),
and Cloudflare Turnstile bot protection on login and password recovery forms.

Because this is a cross-cutting standards layer (not a single feature module), it has no
isolated runtime and no standalone deployable. It is verified by running all feature-level
tests and manually checking each acceptance criterion in the spec after implementation.

## Technical Context

**Language/Version**: TypeScript 5.x / Angular 21 (frontend); C# 13 / .NET 10 (backend Turnstile service)
**Primary Dependencies**: PrimeNG 21+, ngx-mask (latest), date-fns + date-fns-tz (latest),
  Lucide Angular, Cloudflare Turnstile script / ngx-turnstile wrapper
**Storage**: `localStorage` (theme preference, key `theme`); `public.tenants.timezone` (owned by Spec 02, referenced here)
**Testing**: Karma + Jasmine, co-located `.spec.ts` files (Angular); xUnit (backend Turnstile service)
**Target Platform**: Web SPA on Cloudflare Pages (Chrome 110+, Firefox 115+, Safari 16+, Edge 110+); .NET 10 API on Oracle ARM64
**Project Type**: Shared standards layer — affects `omniDesk.Admin`, `omniDesk.Crm`, and `omniDesk.Api`
**Performance Goals**: Dark mode toggle < 100ms; no flash on initial page load; form validation feedback < 50ms
**Constraints**: No hardcoded CSS hex/rgb values; no BOM; LF line endings; masks strip before API submission; Turnstile always verified server-side

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Verdict | Notes |
|---|---|---|
| I. Multi-Tenant Isolation | ✅ Pass | `tenant.timezone` is stored per-tenant; `localStorage['theme']` is per-browser-session, not shared across tenants; no cross-tenant data paths |
| II. AI-First, Human-Assisted | ✅ N/A | This spec establishes UI/UX standards, not a conversation or agent feature |
| III. Channel Agnosticism | ✅ N/A | This spec covers form and display standards, not channel-specific logic |
| IV. Security and LGPD Compliance | ✅ Pass | Turnstile on all public forms; secret key never in frontend code; tokens verified server-side; no PII exposed in error messages |
| V. Simplicity and Deliberate Scope | ✅ Pass | `locale`, `currency`, `date_format` are V1 constants (not configurable). `ngx-turnstile` wrapper considered but direct DOM approach used if version compatibility issues — see research.md |
| VI. Observability and Auditability | ✅ Pass | Turnstile verification failures logged (English, structured via Serilog) before returning 403 |
| VII. Test Discipline | ✅ Pass | All validators and services have co-located `.spec.ts`; no DB mocks needed (validators are pure functions; Turnstile service tested with HttpClientTestingModule) |

**Gate result: PASS — no violations. Proceed to Phase 0.**

## Project Structure

### Documentation (this feature)

```text
specs/001-global-technical-standards/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── validators.ts    # TypeScript interfaces for shared validators
│   ├── date-service.ts  # DateDisplayService contract
│   └── turnstile.md     # Turnstile verification API contract (backend)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
# Shared design system and configuration assets
assets/brand/                  ← brand assets (logo.svg, logo-icon.svg, favicon.ico, etc.)

# Admin Angular project
src/omniDesk.Admin/src/
├── index.html                 ← FOUC prevention script + Turnstile script + Manrope font
├── favicon.ico
├── assets/images/             ← copied brand assets (logo.svg, logo-icon.svg, logo.png)
├── styles/
│   └── tokens.css             ← ALL CSS Custom Property tokens (colors, typography, spacing, shadows)
└── app/
    ├── app.config.ts          ← LOCALE_ID = 'pt-BR', provideNgxMask(), providePrimeNG(preset)
    ├── core/
    │   ├── services/
    │   │   ├── theme.service.ts        ← toggleTheme(), current signal, localStorage persistence
    │   │   └── date-display.service.ts ← toDisplay(utcDate, tz), toUtc(localDate, tz)
    │   └── interceptors/
    │       └── tenant-context.interceptor.ts  (reads timezone for date conversion)
    └── shared/
        └── validators/
            ├── cnpj.validator.ts    ← cnpjValidator(): ValidatorFn
            ├── cnpj.validator.spec.ts
            ├── cpf.validator.ts     ← cpfValidator(): ValidatorFn
            ├── cpf.validator.spec.ts
            └── form-errors.ts       ← FORM_ERRORS constant map

# CRM Angular project (mirrors Admin structure for standards-relevant files)
src/omniDesk.Crm/src/
├── index.html
├── favicon.ico
├── assets/images/
├── styles/
│   └── tokens.css
└── app/
    ├── app.config.ts
    ├── core/
    │   └── services/
    │       ├── theme.service.ts
    │       └── date-display.service.ts
    └── shared/
        └── validators/
            ├── cnpj.validator.ts + .spec.ts
            ├── cpf.validator.ts + .spec.ts
            └── form-errors.ts

# API (.NET 10)
src/omniDesk.Api/
├── Infrastructure/
│   └── Security/
│       └── TurnstileService.cs          ← VerifyAsync(token, ip): Task<TurnstileResult>
├── Features/
│   └── Auth/
│       └── LoginEndpoint.cs             ← validates Turnstile before any auth logic
└── .editorconfig                        ← root .editorconfig (UTF-8, LF, 4-space C#)

# Repository root
.editorconfig                            ← root encoding / indent rules for all file types
```

**Structure Decision**: Standards layer distributed across both Angular projects and the API.
No new module/feature directory is created — these files slot into existing `core/` and
`shared/` directories. The `tokens.css` file is the single source of truth for all
visual constants and is duplicated (not symlinked) between Admin and CRM projects to keep
each project self-contained for Cloudflare Pages builds.

## Complexity Tracking

> No constitution violations — table omitted per instructions.
