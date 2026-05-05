# Research: Global Technical Standards

**Date**: 2026-05-05
**Branch**: `001-global-technical-standards`

---

## 1. ngx-mask + PrimeNG pInputText Compatibility

**Decision**: Apply `mask` directive attribute alongside `pInputText` on the same `<input>`.
Use `[dropSpecialCharacters]="true"` (default) so the form control value contains only raw
characters — mask formatting is purely visual.

**Rationale**: `pInputText` is a structural CSS directive — it adds styling classes to the
host element without touching the value pipeline. `ngx-mask` operates through Angular's
`ControlValueAccessor` pipeline. They do not conflict. The `dropSpecialCharacters` default
ensures `.`, `/`, `-`, `(`, `)` and spaces are stripped before the value reaches the
reactive form control, satisfying FR-016.

**Key configuration**:
```typescript
// app.config.ts
provideNgxMask({ dropSpecialCharacters: true })
```

```html
<!-- Works: pInputText + mask on the same input -->
<input pInputText formControlName="cnpj"
       mask="00.000.000/0000-00"
       [showMaskTyped]="true" />
```

**Alternatives considered**:
- Writing a custom mask pipe: rejected — ngx-mask is the standard Angular mask library, active maintenance, tree-shakable.
- PrimeNG InputMask component (`p-inputMask`): rejected — it does not support the flexible phone mask `(00) 0000-00009` (optional 9th digit) and strips the value from `formControl` in a non-standard way.

---

## 2. PrimeNG 21 Aura Theme + CSS Custom Properties

**Decision**: Use PrimeNG's Design Token API (introduced in PrimeNG 18, extended in 21).
Extend the `Aura` preset with a custom `definePreset()` call that maps OmniDesk brand
colors to PrimeNG's semantic token names. Supplement with the global `tokens.css` file
for tokens that are outside PrimeNG's scope (e.g., typography, custom spacing).

**Rationale**: PrimeNG 21 generates CSS Custom Properties from its Design Token API at
runtime, injected into `:root`. Our `tokens.css` defines additional properties with the
`--color-*`, `--font-*`, `--spacing-*`, `--radius-*`, `--shadow-*` naming convention.
By overriding PrimeNG's generated tokens in our preset, we avoid having two disconnected
token systems.

**Key patterns**:
```typescript
// app.config.ts
import { definePreset } from '@primeng/themes';
import Aura from '@primeng/themes/aura';

const OmniDeskPreset = definePreset(Aura, {
  semantic: {
    primary: {
      500: '#6F7D5C',
      600: '#5E6B4E',
      700: '#4A563E',
    },
    colorScheme: {
      light: {
        surface: {
          0:   '#FFFFFF',
          50:  '#F4F1EC',
          100: '#EDE7DF',
        },
      },
      dark: {
        surface: {
          0:   '#2A2A2A',
          50:  '#1E1E1E',
          100: '#333333',
        },
      },
    },
  },
});

providePrimeNG({ theme: { preset: OmniDeskPreset, options: { darkModeSelector: '.dark' } } })
```

The `darkModeSelector: '.dark'` option tells PrimeNG to activate dark token overrides when
the `.dark` class is present on `<html>`, consistent with our theme toggle strategy.

**Alternatives considered**:
- CSS overrides without preset: rejected — fragile, breaks on PrimeNG version updates.
- SCSS theming (PrimeNG SCSS variables): rejected — PrimeNG 21 has deprecated SCSS-based
  theming in favor of the Design Token API.

---

## 3. Cloudflare Turnstile in Angular 21

**Decision**: Use the direct DOM approach (global Turnstile script + div widget) wrapped
in a thin `TurnstileComponent` (Standalone, Angular 21). Do NOT use `ngx-turnstile` in V1.

**Rationale**: `ngx-turnstile` (v0.x) has not been updated for Angular 21's standalone
component model. Using it would introduce a peer-dependency conflict. The direct DOM approach
is 15–20 lines of code and is fully Angular-lifecycle-aware.

**Implementation pattern**:
```typescript
// shared/components/turnstile/turnstile.component.ts
@Component({
  selector: 'app-turnstile',
  standalone: true,
  template: `<div #widget></div>`,
})
export class TurnstileComponent implements AfterViewInit, OnDestroy {
  @ViewChild('widget') widgetEl!: ElementRef<HTMLDivElement>;
  @Output() tokenChange = new EventEmitter<string | null>();

  private widgetId: string | null = null;
  private readonly siteKey = inject(TURNSTILE_SITE_KEY);

  ngAfterViewInit() {
    // window.turnstile is available because the script is loaded in index.html
    this.widgetId = (window as any).turnstile.render(this.widgetEl.nativeElement, {
      sitekey: this.siteKey,
      callback: (token: string) => this.tokenChange.emit(token),
      'expired-callback': () => this.tokenChange.emit(null),
      'error-callback': () => this.tokenChange.emit(null),
      theme: 'auto',
    });
  }

  ngOnDestroy() {
    if (this.widgetId) (window as any).turnstile.remove(this.widgetId);
  }
}
```

```typescript
// InjectionToken for site key (environment-driven)
export const TURNSTILE_SITE_KEY = new InjectionToken<string>('TURNSTILE_SITE_KEY');
// Provided in app.config.ts:
// { provide: TURNSTILE_SITE_KEY, useValue: environment.turnstileSiteKey }
```

**Login form integration**:
```typescript
// turnstileToken = signal<string | null>(null)
// submitDisabled = computed(() => !this.form.valid || !this.turnstileToken())
```

**Alternatives considered**:
- `ngx-turnstile` library: rejected — Angular 21 peer-dependency conflict; not worth
  maintaining a local fork for a thin wrapper.
- Purely declarative widget (no JS): rejected — Turnstile's callback model requires
  programmatic widget registration to capture the token.

---

## 4. Dark Mode — Flash of Unstyled Content Prevention

**Decision**: Apply theme class via an inline script in `<head>` before any CSS is parsed.

**Pattern**:
```html
<!-- index.html — MUST appear before any <link> stylesheet -->
<head>
  <script>
    (function () {
      var t = localStorage.getItem('theme');
      if (t === 'dark') document.documentElement.classList.add('dark');
    }());
  </script>
  <link rel="stylesheet" href="styles.css" />
  ...
</head>
```

**Rationale**: The browser applies CSS after parsing `<head>`. By setting the class before
the stylesheet link, the correct dark/light token set is active before first paint.
Angular hydration does not interfere because the class is on `<html>` before Angular boots.

**ThemeService pattern**:
```typescript
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly isDark = signal(localStorage.getItem('theme') === 'dark');

  toggle(): void {
    const next = !this.isDark();
    this.isDark.set(next);
    document.documentElement.classList.toggle('dark', next);
    localStorage.setItem('theme', next ? 'dark' : 'light');
  }
}
```

**Alternatives considered**:
- `prefers-color-scheme` media query following: deferred to V2 (spec FR-006 mandates
  user-initiated toggle only in V1).
- Angular SSR-aware solution: not applicable — both frontends are static SPAs.

---

## 5. date-fns-tz in Angular Services

**Decision**: Create `DateDisplayService` as a root-level singleton that reads tenant
timezone from `TenantContextService` and provides two public methods:
`toDisplay(utcIso: string): string` and `toUtc(localDate: Date): string`.

**Pattern**:
```typescript
@Injectable({ providedIn: 'root' })
export class DateDisplayService {
  private readonly tenantCtx = inject(TenantContextService);

  toDisplay(utcIso: string, format = 'dd/MM/yyyy HH:mm'): string {
    const tz = this.tenantCtx.timezone();   // signal<string>
    const zoned = toZonedTime(new Date(utcIso), tz);
    return formatTz(zoned, format, { locale: ptBR });
  }

  toUtc(local: Date): string {
    const tz = this.tenantCtx.timezone();
    return fromZonedTime(local, tz).toISOString();
  }
}
```

`TenantContextService` exposes `timezone: Signal<string>` populated from the JWT claims
or tenant profile API response after login.

**Rationale**: Centralizing timezone conversion in one service ensures every component
uses the same logic. Components use the Angular `date` pipe for simple cases where the
timezone is known statically (`dd/MM/yyyy` from an already-zoned date), and call
`DateDisplayService` when the timezone comes from tenant context.

**Alternatives considered**:
- Using Angular's built-in `DatePipe` with `timezone` parameter: partially viable, but
  `DatePipe` accepts IANA timezone only on Angular 16+ and requires the `Intl.DateTimeFormat`
  API, which has formatting inconsistencies for pt-BR decimal separators. `date-fns-tz` is
  already in the stack and produces consistent output.
- Moment.js + moment-timezone: rejected — deprecated, large bundle size.

---

## 6. Backend Turnstile Verification Service (.NET 10)

**Decision**: Implement `TurnstileService` as a typed `HttpClient`-backed service using
`IHttpClientFactory`. Endpoint: `POST https://challenges.cloudflare.com/turnstile/v0/siteverify`.
Request: `application/x-www-form-urlencoded`. Response: deserialize to `TurnstileResponse`.

**Rationale**: Using `IHttpClientFactory` enables resilience policies (retry, timeout) and
testability via `MockHttpMessageHandler` in xUnit tests. The service is registered as
`AddHttpClient<TurnstileService>()` with a base address.

**Error handling**: Any network error or `success: false` response from Cloudflare returns
`TurnstileResult.Failure`. The calling endpoint returns HTTP 403. No fallback/bypass path
exists — if Turnstile is unavailable, the endpoint is blocked (fail-closed).

**Alternatives considered**:
- Fail-open (allow requests when Turnstile is unavailable): rejected — fail-open on a
  security control is not acceptable per Constitution Principle IV.
- Caching verified tokens: rejected — Turnstile tokens are single-use; caching would
  allow replay attacks.

---

## 7. CNPJ and CPF Validation Algorithm

**Decision**: Implement as pure TypeScript functions (`ValidatorFn`) with no external
dependencies. Algorithm is the standard Brazilian Receita Federal check-digit method.

**Rationale**: The algorithm is deterministic, well-documented by Receita Federal, and
requires no network call. Pure functions are trivially testable with parameterized xUnit-style
Jasmine `it.each` tests covering: valid CNPJ/CPF, all-same-digit sequences, off-by-one
check digits, and masked input (should be stripped before validation).

**Edge cases confirmed**:
- `00.000.000/0000-00` — all zeros, must be INVALID (FR-017 explicitly rejects same-digit sequences)
- `11.111.111/1111-11` — all 1s, must be INVALID
- Input received as masked string (with `.`, `/`, `-`): the validator strips non-digits first
  so it works regardless of whether `dropSpecialCharacters` has already stripped them

---

## 8. .editorconfig + UTF-8 Enforcement

**Decision**: Place a single `.editorconfig` at the repository root. Both Angular CLI and
dotnet CLI honor `.editorconfig`. No additional tooling required.

**Confirmed compatibility**:
- Angular CLI 17+: reads `.editorconfig` for file generation
- VS Code + JetBrains IDEs: read `.editorconfig` natively
- dotnet CLI: reads `.editorconfig` for formatting (`dotnet format`)
- Git: LF enforcement via `.gitattributes` (complementary, handled during repo init)

**Recommended `.gitattributes` addition** (not in `.editorconfig`):
```
* text=auto eol=lf
*.bat text eol=crlf
```
This ensures cross-platform contributors always commit LF, even on Windows.
