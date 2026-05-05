---
description: "Task list for Global Technical Standards implementation"
---

# Tasks: Global Technical Standards

**Input**: Design documents from `specs/001-global-technical-standards/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Tests**: Constitution Principle VII mandates co-located `.spec.ts` for all services and
validators — test tasks are included for validators and services regardless of explicit TDD request.

**Organization**: Tasks organized by user story to enable independent implementation and
testing. US1 and US2 are both P1 and may be started in parallel after Phase 2 completes.

---

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no blocking dependencies)
- **[Story]**: Which user story (US1–US5)

---

## Phase 1: Setup

**Purpose**: Repository-level standards files that all three projects depend on.

- [x] T001 Create `.editorconfig` at repository root with UTF-8 charset, LF line endings, 2-space indent for TS/HTML/CSS/JSON, 4-space for C#, no trailing whitespace
- [x] T002 [P] Create `.gitattributes` at repository root to enforce LF on commit (`* text=auto eol=lf`, `*.bat text eol=crlf`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core configuration all user stories depend on. MUST be complete before any
user story begins. Affects `omniDesk.Admin`, `omniDesk.Crm`, and shared assets.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 Create `src/omniDesk.Admin/src/styles/tokens.css` with the full CSS Custom Property token set: primary colors (`--color-primary-500/600/700`), secondary, semantic (success/warning/danger), surface tokens for light and dark (`.dark` block), text tokens, border/accent, typography (Manrope family, all sizes and weights, line heights), spacing (`--spacing-1` through `--spacing-12`), border radii, and shadows — per spec §2.2–2.4
- [x] T004 [P] Create `src/omniDesk.Crm/src/styles/tokens.css` with identical token set to T003 (each project is self-contained for Cloudflare Pages builds)
- [ ] T005 Add `tokens.css` to the `styles` array in `src/omniDesk.Admin/angular.json` (build → options → styles) so tokens are globally available without imports in component stylesheets
- [ ] T006 [P] Add `tokens.css` to the `styles` array in `src/omniDesk.Crm/angular.json`
- [x] T007 Configure `src/omniDesk.Admin/src/app/app.config.ts`: (1) register pt-BR locale with `registerLocaleData(localePt)`, (2) provide `{ provide: LOCALE_ID, useValue: 'pt-BR' }`, (3) `provideNgxMask({ dropSpecialCharacters: true })`, (4) `providePrimeNG({ theme: { preset: OmniDeskPreset, options: { darkModeSelector: '.dark' } } })` with OmniDesk Aura preset extending brand primary colors per research.md §2
- [x] T008 [P] Apply identical app.config.ts configuration to `src/omniDesk.Crm/src/app/app.config.ts`
- [x] T009 Update `src/omniDesk.Admin/src/index.html`: (1) add Manrope Google Fonts preconnect + stylesheet link in `<head>`, (2) add FOUC-prevention inline script before any `<link>` stylesheet that reads `localStorage.getItem('theme')` and adds `.dark` class to `<html>` if value is `'dark'`, (3) add Cloudflare Turnstile script tag (`https://challenges.cloudflare.com/turnstile/v0/api.js` with async defer), (4) ensure `<link rel="icon">` references `favicon.ico`
- [x] T010 [P] Apply identical index.html changes to `src/omniDesk.Crm/src/index.html`
- [ ] T011 [P] Copy brand assets (`logo.svg`, `logo-icon.svg`, `logo.png`) from `assets/brand/` to `src/omniDesk.Admin/src/assets/images/`; copy `favicon.ico` to `src/omniDesk.Admin/src/favicon.ico`; add `assets/images` entry to angular.json assets array if not already present
- [ ] T012 [P] Copy brand assets from `assets/brand/` to `src/omniDesk.Crm/src/assets/images/` and `src/omniDesk.Crm/src/favicon.ico` with same angular.json update
- [x] T013 [P] Create `src/omniDesk.Admin/src/app/shared/validators/form-errors.ts` exporting the `FORM_ERRORS` constant map with all 8 pt-BR error messages (required, email, cnpj, cpf, minlength, maxlength, min, max) per data-model.md §2.4
- [x] T014 [P] Create `src/omniDesk.Crm/src/app/shared/validators/form-errors.ts` with the same `FORM_ERRORS` export

**Checkpoint**: Both Angular projects compile without errors (`ng build`); tokens.css is loaded globally; pt-BR locale is active; no FOUC on hard reload with `localStorage.theme = 'dark'` set.

---

## Phase 3: User Story 1 — Brazilian Data Entry (Priority: P1) 🎯 MVP

**Goal**: CNPJ/CPF check-digit validators and pt-BR field masks in both Angular projects.

**Independent Test**: Open any form with CNPJ and CPF fields. Type a valid CNPJ — no error.
Type `11111111111111` — "CNPJ inválido." appears. Submit — API receives digits only.

- [x] T015 [P] [US1] Create `src/omniDesk.Admin/src/app/shared/validators/cnpj.validator.ts` exporting `cnpjValidator(): ValidatorFn` per contracts/validators.ts; create co-located `cnpj.validator.spec.ts` with test cases: valid CNPJ (e.g., `11222333000181`), all-zeros (`00000000000000`), all-same-digit (`11111111111111`), bad first check digit, bad second check digit, masked input with formatting characters
- [x] T016 [P] [US1] Create `src/omniDesk.Admin/src/app/shared/validators/cpf.validator.ts` exporting `cpfValidator(): ValidatorFn`; create co-located `cpf.validator.spec.ts` with same test matrix applied to CPF (valid, all-zeros, all-same-digit, bad check digits, masked input)
- [x] T017 [P] [US1] Replicate `cnpj.validator.ts` + `cnpj.validator.spec.ts` in `src/omniDesk.Crm/src/app/shared/validators/` (identical logic and tests)
- [x] T018 [P] [US1] Replicate `cpf.validator.ts` + `cpf.validator.spec.ts` in `src/omniDesk.Crm/src/app/shared/validators/`

**Checkpoint**: `ng test` in both projects — all 4 validator spec files pass (CNPJ/CPF Admin + CRM). Validators correctly reject all-same-digit sequences and invalid check digits.

---

## Phase 4: User Story 2 — Date, Time, and Currency Display (Priority: P1)

**Goal**: `TenantContextService` (timezone Signal) and `DateDisplayService` (UTC→local
conversion) in both Angular projects, enabling timezone-aware date display.

**Independent Test**: Create `DateDisplayService` pointing to `America/Sao_Paulo`. Call
`toDisplay('2026-05-05T20:00:00Z')` — expect `'05/05/2026 17:00'`. Point to
`America/Manaus` — expect `'05/05/2026 16:00'`.

- [x] T019 [P] [US2] Create `src/omniDesk.Admin/src/app/core/services/tenant-context.service.ts` as a root-level singleton exposing `timezone: Signal<string>` (default `'America/Sao_Paulo'`); the service will later be updated by the auth flow (Spec 01) to read from JWT/profile response
- [x] T020 [P] [US2] Create `src/omniDesk.Admin/src/app/core/services/date-display.service.ts` + co-located `date-display.service.spec.ts` implementing `IDateDisplayService` from contracts/date-service.ts; `toDisplay(utcIso, fmt?)` uses `toZonedTime` + `format` from `date-fns-tz` with `ptBR` locale; `toUtc(local)` uses `fromZonedTime`; spec covers São Paulo (UTC-3), Manaus (UTC-4), null/empty input (returns `''`)
- [x] T021 [P] [US2] Replicate `TenantContextService` at `src/omniDesk.Crm/src/app/core/services/tenant-context.service.ts`
- [x] T022 [P] [US2] Replicate `DateDisplayService` + spec at `src/omniDesk.Crm/src/app/core/services/date-display.service.ts`

**Checkpoint**: `ng test` in both projects — `DateDisplayService` specs pass for UTC→São Paulo and UTC→Manaus conversions; `toDisplay(null)` returns `''` without throwing.

---

## Phase 5: User Story 3 — Dark Mode Preference (Priority: P2)

**Goal**: `ThemeService` with toggle + localStorage persistence + `isDark` Signal in both
projects; dark mode toggle button wired into each app's header.

**Independent Test**: Toggle dark mode → `.dark` class added to `<html>`; reload page → class
restored; all screens render without invisible text in dark mode.

- [x] T023 [P] [US3] Create `src/omniDesk.Admin/src/app/core/services/theme.service.ts` + co-located `theme.service.spec.ts` implementing `IThemeService` from contracts/date-service.ts; `toggle()` flips `isDark` signal, toggles `.dark` on `document.documentElement`, and persists to `localStorage` under key `'theme'`; spec covers: initial state from storage, toggle on/off, class addition/removal on html element
- [x] T024 [P] [US3] Replicate `ThemeService` + spec at `src/omniDesk.Crm/src/app/core/services/theme.service.ts`
- [ ] T025 [US3] Inject `ThemeService` into the Admin layout header component (`src/omniDesk.Admin/src/app/layout/header/`); add a toggle button that calls `themeService.toggle()` and reflects the current `isDark()` state visually (icon or label change); depends on T023
- [ ] T026 [US3] Inject `ThemeService` into the CRM layout header component (`src/omniDesk.Crm/src/app/layout/header/`); same toggle button; depends on T024

**Checkpoint**: Toggle dark mode in both apps; hard reload — preference persists. `ng test` — ThemeService specs pass. Navigate through all main views — no invisible text in dark mode.

---

## Phase 6: User Story 4 — Bot-Protected Login and Password Recovery (Priority: P2)

**Goal**: Cloudflare Turnstile widget in all public-facing forms; server-side token
verification before any auth logic executes; submit blocked until token is emitted.

**Independent Test**: Submit POST `/api/auth/login` with `turnstileToken: "fake"` → HTTP 403.
Open login page → submit button disabled on load; after widget completes → button enabled.

- [x] T027 [P] [US4] Add `turnstileSiteKey: '__TURNSTILE_SITE_KEY__'` to `src/omniDesk.Admin/src/environments/environment.ts` and `environment.prod.ts` (prod value is set via Cloudflare Pages env variable)
- [x] T028 [P] [US4] Add `turnstileSiteKey` to both environment files in `src/omniDesk.Crm/src/environments/`
- [x] T029 [P] [US4] Create `src/omniDesk.Admin/src/app/core/tokens/turnstile.tokens.ts` exporting `TURNSTILE_SITE_KEY = new InjectionToken<string>('TURNSTILE_SITE_KEY')`; register in `app.config.ts` as `{ provide: TURNSTILE_SITE_KEY, useValue: environment.turnstileSiteKey }`
- [x] T030 [P] [US4] Replicate `TURNSTILE_SITE_KEY` token + app.config.ts provider in `src/omniDesk.Crm/src/app/core/tokens/`
- [x] T031 [US4] Create `TurnstileComponent` (Standalone) at `src/omniDesk.Admin/src/app/shared/components/turnstile/turnstile.component.ts` per contracts/date-service.ts `TurnstileComponentContract`; uses `AfterViewInit` + `@ViewChild` + `window.turnstile.render()` approach from research.md §3; emits token via `tokenChange EventEmitter<string | null>`; cleans up via `ngOnDestroy`; depends on T029
- [x] T032 [US4] Create `TurnstileComponent` at `src/omniDesk.Crm/src/app/shared/components/turnstile/turnstile.component.ts` (same implementation); depends on T030
- [ ] T033 [US4] Integrate `TurnstileComponent` into Admin login form component (`src/omniDesk.Admin/src/app/features/auth/login/`): add `<app-turnstile>` to template, capture `tokenChange` into a `turnstileToken = signal<string | null>(null)`, compute `submitDisabled = computed(() => !form.valid || !turnstileToken())`, include `turnstileToken()` in the `LoginRequest` body; depends on T031
- [ ] T034 [US4] Integrate `TurnstileComponent` into CRM login form (`src/omniDesk.Crm/src/app/features/auth/login/`) using same pattern; depends on T032
- [ ] T035 [US4] Integrate `TurnstileComponent` into CRM forgot-password form (`src/omniDesk.Crm/src/app/features/auth/forgot-password/`) using same pattern; depends on T032
- [x] T036 [US4] Create `src/omniDesk.Api/Infrastructure/Security/TurnstileService.cs` implementing `ITurnstileService` per contracts/turnstile.md; uses `IHttpClientFactory` with base address `https://challenges.cloudflare.com`; sends `application/x-www-form-urlencoded` to `/turnstile/v0/siteverify`; fail-closed on network errors (returns `TurnstileResult.Fail`)
- [x] T037 [P] [US4] Create `tests/omniDesk.Api.Tests/Infrastructure/Security/TurnstileServiceTests.cs` with xUnit tests using `MockHttpMessageHandler`: valid Cloudflare response (`success: true`) → `TurnstileResult.Ok()`; invalid (`success: false`) → `TurnstileResult.Fail()`; network exception → `TurnstileResult.Fail()`
- [ ] T038 [US4] Register `ITurnstileService` / `TurnstileService` in `src/omniDesk.Api/Program.cs` using `builder.Services.AddHttpClient<TurnstileService>()` with configured base address from `TURNSTILE_API_URL` env var; depends on T036
- [ ] T039 [US4] Update `LoginEndpoint.cs` in `src/omniDesk.Api/Features/Auth/` to: (1) require `TurnstileToken` string in `LoginRequest`, (2) call `ITurnstileService.VerifyAsync(request.TurnstileToken, remoteIp)` before any credential check, (3) return `Results.Problem(statusCode: 403)` on failure; depends on T038
- [ ] T040 [US4] Update or create `ForgotPasswordEndpoint.cs` in `src/omniDesk.Api/Features/Auth/` with same Turnstile validation pattern (FR-041, FR-042); depends on T038

**Checkpoint**: `dotnet test` — all TurnstileServiceTests pass. `curl` with missing token → HTTP 400. `curl` with `turnstileToken: "fake"` → HTTP 403. Admin and CRM login pages: submit button disabled on load, enabled after widget completes.

---

## Phase 7: User Story 5 — Tenant Timezone Configuration (Priority: P3)

**Goal**: `BrazilianTimezone` type + dropdown of 9 IANA zones on tenant create/edit form
(Admin); `TenantContextService` reads timezone from API/JWT after login.

**Independent Test**: Create tenant with `America/Manaus`; create a record with a known UTC
timestamp; verify the displayed time is UTC-4 (not UTC-3).

- [x] T041 [P] [US5] Create `src/omniDesk.Admin/src/app/core/constants/timezones.ts` exporting `BrazilianTimezone` union type and `BRAZILIAN_TIMEZONES` array of 9 IANA timezone strings per data-model.md §2.2
- [x] T042 [P] [US5] Replicate `timezones.ts` at `src/omniDesk.Crm/src/app/core/constants/timezones.ts`
- [ ] T043 [US5] Add timezone `<p-select>` dropdown to the tenant create/edit form in `src/omniDesk.Admin/src/app/features/tenants/`; options from `BRAZILIAN_TIMEZONES`; default value `'America/Sao_Paulo'`; field is required; maps to `timezone` field in the `CreateTenantRequest` / `UpdateTenantRequest` body; depends on T041
- [ ] T044 [US5] Update `TenantContextService` in both `src/omniDesk.Admin` and `src/omniDesk.Crm` to accept the tenant's `timezone` from the auth service after successful login (receives the value from the profile API response and updates the `timezone` Signal); depends on T019, T021

**Checkpoint**: Create tenant with `America/Manaus` timezone. All date-time displays in that tenant's session show UTC-4 offset. `DateDisplayService.toDisplay('2026-05-05T20:00:00Z')` returns `'05/05/2026 16:00'` for this tenant.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Verification, cleanup, and non-functional requirements across all stories.

- [x] T045 [P] Run grep check for hardcoded CSS color values in all component stylesheets in both Angular projects; fix any occurrences by replacing with the appropriate `var(--color-*)` token: `grep -rnE '#[0-9a-fA-F]{3,6}|rgb\(|rgba\(' src/omniDesk.Admin/src/app/**/*.css src/omniDesk.Crm/src/app/**/*.css`
- [x] T046 [P] Run encoding checks per quickstart.md §6; fix any BOM or CRLF violations found: `find src/ -name "*.ts" | xargs grep -rl $'\xef\xbb\xbf'` and `find src/ -name "*.ts" | xargs grep -rcl $'\r'`
- [x] T047 [P] Add `_redirects` file (`/*    /index.html    200`) to `src/omniDesk.Admin/src/` and `src/omniDesk.Crm/src/`; add both to each project's `angular.json` assets array so they are copied to `dist/` on build (required for Cloudflare Pages SPA routing)
- [x] T048 [P] Add `_headers` file with security headers (X-Frame-Options, X-Content-Type-Options, CSP including `challenges.cloudflare.com`) to `src/omniDesk.Admin/src/` and `src/omniDesk.Crm/src/`; add to `angular.json` assets; refer to ARCHITECTURE.md §2.5 for exact header values
- [x] T049 Run all 7 validation scenarios from `specs/001-global-technical-standards/quickstart.md` and confirm each passes; document any failures for follow-up

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — no dependency on US2/US3/US4/US5
- **US2 (Phase 4)**: Depends on Phase 2 — no dependency on US1/US3/US4/US5
- **US3 (Phase 5)**: Depends on Phase 2 — no dependency on US1/US2/US4/US5
- **US4 (Phase 6)**: Depends on Phase 2 — T039/T040 depend on T038; T033-T035 depend on T031/T032
- **US5 (Phase 7)**: Depends on Phase 2; T044 depends on T019/T021 (US2); T043 depends on T041
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 and US2 (both P1)**: Can run in parallel after Phase 2 — no shared files
- **US3 (P2)**: Can run after Phase 2, in parallel with US1 and US2
- **US4 (P2)**: Can run after Phase 2, in parallel with US1/US2/US3 (different files: Auth endpoints, Turnstile component)
- **US5 (P3)**: Can start after Phase 2; T044 depends on US2 TenantContextService (T019, T021)

### Within Each User Story

- Admin and CRM tasks within the same story marked [P] can run in parallel (different project directories)
- Backend tasks (US4: TurnstileService, tests) are independent of frontend and can run in parallel

---

## Parallel Execution Examples

### Foundational Phase (all [P] tasks together after T003)

```bash
# After T003 is done:
Task: "Create tokens.css in omniDesk.Crm"               → T004
Task: "Add tokens.css to Admin angular.json"             → T005
Task: "Configure Admin app.config.ts"                    → T007
Task: "Copy brand assets to Admin"                       → T011
Task: "Create Admin FORM_ERRORS"                         → T013
# (and their CRM mirrors: T006, T008, T012, T014)
```

### US1 — Both Projects in Parallel

```bash
Task: "Create cnpj.validator + spec in Admin"  → T015
Task: "Create cpf.validator + spec in Admin"   → T016
Task: "Create cnpj.validator + spec in CRM"    → T017
Task: "Create cpf.validator + spec in CRM"     → T018
```

### US4 — Frontend and Backend in Parallel

```bash
# Frontend track:
Task: "Create Admin environment.ts"     → T027
Task: "Create CRM environment.ts"       → T028
Task: "Create Admin InjectionToken"     → T029
Task: "Create CRM InjectionToken"       → T030

# Backend track (parallel with frontend):
Task: "Create TurnstileService.cs"     → T036
Task: "Create TurnstileServiceTests"   → T037
```

---

## Implementation Strategy

### MVP First (US1 + US2 Only)

1. Complete Phase 1: Setup (.editorconfig, .gitattributes)
2. Complete Phase 2: Foundational (tokens, config, assets, FORM_ERRORS)
3. Complete Phase 3: US1 — CNPJ/CPF validators + masks
4. Complete Phase 4: US2 — DateDisplayService + timezone
5. **STOP and VALIDATE**: Run quickstart.md §§ 3–4; confirm masks, validators, and date display work
6. Deploy / demo to first clinic

### Incremental Delivery

1. MVP (US1 + US2) → validators and dates work
2. US3 → dark mode across all screens
3. US4 → login/recovery protected by Turnstile
4. US5 → tenant timezone dropdown in admin

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Developer A: US1 (CNPJ/CPF validators — both projects)
3. Developer B: US2 (DateDisplayService — both projects)
4. Developer C: US4 (Turnstile — backend + frontend, can start independently)
5. US3 and US5 follow when A and B are free

---

## Notes

- `[P]` = different project directories or different files — truly parallelizable
- Each user story phase is independently deployable after completion
- Constitution Principle VII requires `.spec.ts` files — included for all validators and services
- Admin and CRM receive identical implementations for all standards — no deduplication via shared library (each project is self-contained for Cloudflare Pages builds)
- `tokens.css` is duplicated between projects intentionally (not symlinked); see plan.md Structure Decision
