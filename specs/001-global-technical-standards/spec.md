# Feature Specification: Global Technical Standards

**Feature Branch**: `001-global-technical-standards`
**Created**: 2026-05-05
**Status**: Draft
**Scope**: Cross-cutting — applies to all frontend screens and API responses across `omniDesk.Admin` and `omniDesk.Crm`

> This spec is the authoritative reference for brand assets, design system, field formatting,
> validation, locale, encoding, and security on public forms. All other feature specs depend on it.

---

## User Scenarios & Testing

### User Story 1 — Brazilian Data Entry with Visual Formatting (Priority: P1)

A clinic receptionist enters client data into the CRM: CNPJ, CPF, phone number, and CEP. She
types digits freely — the fields automatically display the correct visual mask — and receives
instant feedback if the CNPJ or CPF digit verification fails. When the form is submitted, the
system stores only digits (no formatting characters) in the database.

**Why this priority**: Formatting errors in CNPJ and CPF cause downstream issues in billing,
medical records, and legal compliance. Brazilian users expect masked fields as a standard UX
convention.

**Independent Test**: Open any form containing CNPJ, CPF, phone, or CEP fields. Enter values
with and without the correct format. The test passes if: (1) masks display automatically while
typing, (2) invalid CNPJ/CPF triggers a visible error message, (3) the stored value contains
digits only, (4) a valid CNPJ/CPF clears the error.

**Acceptance Scenarios**:

1. **Given** a form with a CNPJ field, **When** the user types 14 digits, **Then** the field
   displays `XX.XXX.XXX/XXXX-XX` and no error appears if digits are valid.
2. **Given** a CNPJ field with a valid mask, **When** the user submits the form, **Then** the
   API receives `12345678000199` (digits only, no formatting characters).
3. **Given** a CNPJ field, **When** the user enters a CNPJ with invalid check digits,
   **Then** an error message "CNPJ inválido." appears before submission is allowed.
4. **Given** a CPF field with an invalid value, **When** the user blurs the field, **Then**
   "CPF inválido." is displayed.
5. **Given** a phone field, **When** the user types 8 digits (landline), **Then** the mask
   displays `(XX) XXXX-XXXX`; when 9 digits are typed (mobile), it displays `(XX) XXXXX-XXXX`.

---

### User Story 2 — Consistent Date, Time, and Currency Display (Priority: P1)

A clinic manager opens ticket history, appointment schedules, and payment summaries. All dates
appear in `dd/MM/yyyy` format, times in `HH:mm`, and monetary values in `R$ X.XXX,XX`. The
dates reflect the timezone configured for that clinic — not the server timezone.

**Why this priority**: Misformatted dates (ISO 8601 instead of Brazilian format) and wrong
timezones cause scheduling errors that directly harm client relationships.

**Independent Test**: View any screen that displays dates, times, or currency values. Verify
that: (1) dates are `dd/MM/yyyy`, (2) times are `HH:mm`, (3) currency shows `R$` symbol with
Brazilian decimal separator, (4) a date stored as UTC 20:00 displays as 17:00 for a tenant in
`America/Sao_Paulo` (UTC-3).

**Acceptance Scenarios**:

1. **Given** a ticket created at `2026-05-03T20:00:00Z` by a tenant in `America/Sao_Paulo`,
   **When** the attendant views the ticket, **Then** the creation date shows `03/05/2026 17:00`.
2. **Given** a price of 1250.00, **When** it is displayed in any CRM view,
   **Then** it shows `R$ 1.250,00`.
3. **Given** the API returns dates in UTC ISO 8601,
   **When** any frontend component displays them,
   **Then** they are converted to the tenant's timezone before rendering.

---

### User Story 3 — Dark Mode Preference (Priority: P2)

Any user of the Admin panel or CRM can toggle between light and dark mode using a control in
the application header. The chosen preference is remembered between sessions. Every screen,
dialog, and dropdown renders correctly in both modes — no elements appear invisible or
illegibly low-contrast.

**Why this priority**: Clinic staff often work in low-light environments (examination rooms,
evening shifts). Dark mode prevents eye strain and is a baseline expectation for modern
professional software.

**Independent Test**: Toggle dark mode. Navigate through all main screens (ticket list, ticket
detail, kanban, agenda, chat). The test passes if no element has invisible text, no color
appears as pure black (`#000000`), and the toggle persists after page reload.

**Acceptance Scenarios**:

1. **Given** the user is in light mode, **When** they toggle the dark mode control,
   **Then** the interface switches immediately without full page reload.
2. **Given** the user chose dark mode, **When** they close and reopen the browser,
   **Then** dark mode is still active.
3. **Given** dark mode is active, **When** any screen is opened,
   **Then** no text is rendered on a same-color background and no pure black (`#000000`)
   surfaces appear.

---

### User Story 4 — Bot-Protected Login and Password Recovery (Priority: P2)

A user opening the login or password recovery page completes a visual challenge before the
submit button becomes active. Bots and automated attacks that skip the challenge cannot submit
the form — the server rejects the request even if the challenge token is forged or absent.

**Why this priority**: Login endpoints are the highest-value attack surface on a multi-tenant
CRM. A successful brute-force attack on one tenant's account would expose other tenants'
data.

**Independent Test**: Open the login page. The test passes if: (1) the submit button is
disabled on load, (2) the challenge widget is visible, (3) after passing the challenge the
button enables, (4) submitting with a tampered or missing token returns a 403 error.

**Acceptance Scenarios**:

1. **Given** the login page loads, **When** the page is ready,
   **Then** the submit button is disabled until the challenge widget emits a valid token.
2. **Given** a valid challenge token has been emitted, **When** the user submits the form,
   **Then** the API verifies the token server-side before processing credentials.
3. **Given** a POST request to `/api/auth/login` without a challenge token,
   **When** the request reaches the server,
   **Then** the server returns HTTP 403 before checking credentials.
4. **Given** the challenge token expires (~5 min of inactivity),
   **When** the widget auto-refreshes the token,
   **Then** the new token is captured and the submit button remains enabled.

---

### User Story 5 — Tenant Timezone Configuration (Priority: P3)

The SaaS admin creates or edits a tenant and selects the clinic's timezone from a dropdown
listing all Brazilian IANA timezones. All subsequent date displays for that tenant reflect
the selected timezone.

**Why this priority**: Brazil spans four timezones. A clinic in Manaus (UTC-4) and one in
São Paulo (UTC-3) show the same UTC timestamp differently — without this, appointment
reminders fire at the wrong local time.

**Independent Test**: Create a tenant with `America/Manaus` timezone. Create a ticket with
a timestamp of `2026-05-05T20:00:00Z`. The test passes if the ticket displays `05/05/2026
16:00` (UTC-4, not UTC-3).

**Acceptance Scenarios**:

1. **Given** the admin is creating a tenant, **When** the timezone field is shown,
   **Then** a dropdown lists the 9 Brazilian IANA timezones with `America/Sao_Paulo` as default.
2. **Given** a tenant has `America/Manaus` set, **When** any date is displayed in their CRM,
   **Then** it reflects UTC-4 offset.
3. **Given** locale, currency, and date format fields exist on the tenant record,
   **When** edited in V1, **Then** only timezone is editable — the others are fixed constants.

---

### Edge Cases

- What happens when a CNPJ or CPF field receives paste input with formatting characters?
  (The mask must strip them and reformat correctly.)
- How does the system handle a tenant record with no timezone set?
  (Must default to `America/Sao_Paulo`, never display raw UTC.)
- What happens if the Turnstile widget fails to load (network error, ad blocker)?
  (The submit button must remain disabled and an explanatory message must be shown.)
- What happens when a user's browser does not support SVG?
  (PNG fallback must render correctly in email templates.)
- What happens if dark mode preference is cleared from `localStorage`?
  (System must fall back to light mode silently, with no JavaScript error.)

---

## Requirements

### Functional Requirements

**Brand Assets**

- **FR-001**: The system MUST serve brand assets exclusively from `assets/images/` within each
  Angular project — never from external URLs or hardcoded paths.
- **FR-002**: The SVG logo MUST be used as the primary image format in all web views. PNG is
  used only for email templates.
- **FR-003**: The favicon MUST be referenced as `favicon.ico` in `index.html`.

**Design System**

- **FR-004**: All CSS color values MUST use CSS Custom Property tokens (`--color-*`).
  Hardcoded hex or rgb values are FORBIDDEN in component stylesheets.
- **FR-005**: The design system MUST support light and dark modes. Dark mode is activated by
  adding the `.dark` class to the `<html>` element.
- **FR-006**: Theme preference (`'dark' | 'light'`) MUST be persisted in `localStorage` and
  applied before first paint via an inline script in `index.html` to prevent flash.
- **FR-007**: All shadow values MUST use `--shadow-*` tokens. No shadow may be more prominent
  than `--shadow-md`.
- **FR-008**: All spacing values MUST use `--spacing-*` tokens.
- **FR-009**: The application MUST use PrimeNG components for all standard UI elements.
  Custom components MUST NOT duplicate functionality already available in PrimeNG.
- **FR-010**: All asynchronous actions MUST display a loading state.
- **FR-011**: All empty list views MUST display an empty state with an illustration and a
  call-to-action.

**Field Masks**

- **FR-012**: CNPJ fields MUST display the mask `XX.XXX.XXX/XXXX-XX` while the user types.
- **FR-013**: CPF fields MUST display the mask `XXX.XXX.XXX-XX` while the user types.
- **FR-014**: Phone fields MUST accept both 8-digit (landline) and 9-digit (mobile) numbers,
  switching mask format automatically.
- **FR-015**: CEP fields MUST display the mask `XXXXX-XXX`.
- **FR-016**: Before sending data to the API, the frontend MUST strip all mask characters,
  sending only digits for CNPJ, CPF, phone, and CEP fields.

**Validation**

- **FR-017**: CNPJ fields MUST validate the two check digits using the standard algorithm.
  Sequences of identical digits (e.g., `00000000000000`) MUST be rejected.
- **FR-018**: CPF fields MUST validate the two check digits using the standard algorithm.
  Sequences of identical digits MUST be rejected.
- **FR-019**: Form error messages MUST be displayed in pt-BR and follow the standard messages
  defined in `FORM_ERRORS`.

**Locale and Internationalization**

- **FR-020**: All Angular projects MUST register the `pt-BR` locale and set `LOCALE_ID` to
  `'pt-BR'`.
- **FR-021**: Dates MUST be displayed using the format `dd/MM/yyyy` via the Angular `date` pipe.
- **FR-022**: Date-times MUST be displayed using `dd/MM/yyyy HH:mm`.
- **FR-023**: Currency values MUST be displayed as `R$ X.XXX,XX` using the `currency` pipe
  with `'BRL':'symbol':'1.2-2'` parameters, or via `p-inputNumber` in edit mode.
- **FR-024**: The API MUST return all timestamps in UTC ISO 8601 format (ending with `Z`).
- **FR-025**: The frontend MUST convert UTC timestamps to the tenant's configured timezone
  before display, using `date-fns-tz`.
- **FR-026**: The frontend MUST convert local dates back to UTC before sending to the API.
- **FR-027**: All TypeScript/C# identifiers MUST be written in English. All user-visible
  strings in HTML templates MUST be written in pt-BR. System logs MUST be in English.

**Encoding**

- **FR-028**: All source files MUST be saved as UTF-8 without BOM.
- **FR-029**: Line endings MUST be LF (Unix-style) across all files.
- **FR-030**: Indentation MUST be 2 spaces for TypeScript/HTML/CSS/JSON and 4 spaces for C#.
- **FR-031**: The PostgreSQL database MUST be created with `ENCODING = 'UTF8'` and
  `LC_COLLATE = 'pt_BR.UTF-8'`.

**Tenant Locale**

- **FR-032**: Each tenant record MUST store a `timezone` field in IANA format.
- **FR-033**: The default tenant timezone MUST be `America/Sao_Paulo`.
- **FR-034**: The admin interface for tenant creation MUST offer a dropdown of 9 Brazilian IANA
  timezones: `America/Sao_Paulo`, `America/Manaus`, `America/Belem`, `America/Fortaleza`,
  `America/Recife`, `America/Noronha`, `America/Porto_Velho`, `America/Boa_Vista`,
  `America/Rio_Branco`.
- **FR-035**: In V1, `locale`, `currency`, and `date_format` are fixed constants (`pt-BR`,
  `BRL`, `dd/MM/yyyy`) and MUST NOT be configurable by tenants.

**Security — Cloudflare Turnstile**

- **FR-036**: The login form on both `omniDesk.Admin` and `omniDesk.Crm` MUST include the
  Cloudflare Turnstile widget.
- **FR-037**: The password recovery form on `omniDesk.Crm` MUST include the Cloudflare
  Turnstile widget.
- **FR-038**: The submit button on Turnstile-protected forms MUST be disabled until the widget
  emits a valid token.
- **FR-039**: When a Turnstile token expires, the widget MUST automatically issue a new one
  and the form state MUST update to reflect it.
- **FR-040**: The API endpoint `POST /api/auth/login` MUST verify the Turnstile token
  server-side before processing any authentication logic.
- **FR-041**: The API endpoint `POST /api/auth/forgot-password` MUST verify the Turnstile
  token server-side before processing the request.
- **FR-042**: Any request to a Turnstile-protected endpoint without a valid token MUST return
  HTTP 403 before credentials are evaluated.
- **FR-043**: The Turnstile secret key MUST be stored as a server-side environment variable
  and MUST NEVER appear in frontend code or public resources.

### Key Entities

- **DesignToken**: A named CSS Custom Property that maps to a concrete color, spacing, shadow,
  or typography value. Tokens are the only permitted way to reference visual constants.
- **TenantLocale**: The locale configuration stored per tenant — timezone (configurable),
  locale code, currency code, date format (V1 constants).
- **FieldMask**: A visual formatting pattern applied to an input field during editing that is
  stripped before data reaches the API.
- **Validator**: A client-side function that applies business rules (e.g., check-digit
  verification) to a form control and returns an error or null.

---

## Success Criteria

### Measurable Outcomes

- **SC-001**: A developer can implement any new CRM or Admin screen without referencing
  external color, spacing, or shadow values — 100% of CSS values use token variables.
- **SC-002**: CNPJ and CPF validation rejects all sequences of identical digits and all values
  with incorrect check digits, with 0 false positives on valid inputs.
- **SC-003**: All dates displayed in any screen of a tenant configured to `America/Manaus`
  (UTC-4) reflect the correct local time — no UTC leakage visible in the UI.
- **SC-004**: Dark mode toggle completes the full theme switch in under 100ms with no
  visible flash of incorrect colors on page load.
- **SC-005**: 100% of automated login attempts (no Turnstile token) to `POST /api/auth/login`
  are rejected at the server with HTTP 403, regardless of credential validity.
- **SC-006**: All forms containing CNPJ or CPF fields store only digits in the database,
  with no formatting characters persisted.
- **SC-007**: A first-time developer can identify the correct design token for any color, size,
  or spacing need within 2 minutes by reading `styles/tokens.css`.

---

## Assumptions

- All users access the application via a modern browser (Chrome 110+, Firefox 115+, Safari 16+,
  Edge 110+) that supports CSS Custom Properties and `localStorage` natively.
- The Angular projects use the Angular CLI build system; UTF-8 output encoding is default and
  does not require additional configuration.
- Cloudflare Turnstile site key and secret key pairs are provisioned separately for
  development/staging and production environments.
- The `ngx-mask` library supports the flexible phone mask (`(00) 0000-00009`) allowing
  both 8-digit and 9-digit Brazilian phone numbers.
- Dark mode is user-initiated via a toggle in the app header; it does not automatically
  follow the OS `prefers-color-scheme` media query (this feature is deferred to V2).
- LGPD compliance for data collection consent (Live Chat widget) is covered in Spec 06
  (Live Chat) and Spec 01 (Auth); this spec covers encoding, locale, and form security only.
- The `public.tenants` table structure including `timezone`, `locale`, `currency`, and
  `date_format` columns is defined in Spec 02 (Tenants); this spec defines the V1 behavior
  of those columns.
