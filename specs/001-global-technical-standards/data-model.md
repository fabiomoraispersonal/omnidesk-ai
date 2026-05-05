# Data Model: Global Technical Standards

**Date**: 2026-05-05
**Branch**: `001-global-technical-standards`

> This spec does not introduce new database tables (the `tenants.timezone` column is owned
> by Spec 02). It defines shared data shapes: localStorage keys, TypeScript value types,
> and the TenantLocale configuration model.

---

## 1. Client-Side State

### 1.1 localStorage — Theme Preference

| Key | Type | Values | Default |
|---|---|---|---|
| `theme` | `string` | `'dark'` \| `'light'` | `'light'` (absent = light) |

**Behavior**: Read synchronously before Angular boots (inline `<head>` script). Written by
`ThemeService.toggle()`. Cleared on browser data reset — system falls back to light mode.

---

## 2. TypeScript Value Types

### 2.1 ThemePreference

```typescript
type ThemePreference = 'dark' | 'light';
```

### 2.2 BrazilianTimezone

```typescript
type BrazilianTimezone =
  | 'America/Sao_Paulo'
  | 'America/Manaus'
  | 'America/Belem'
  | 'America/Fortaleza'
  | 'America/Recife'
  | 'America/Noronha'
  | 'America/Porto_Velho'
  | 'America/Boa_Vista'
  | 'America/Rio_Branco';

const BRAZILIAN_TIMEZONES: BrazilianTimezone[] = [
  'America/Sao_Paulo',
  'America/Manaus',
  'America/Belem',
  'America/Fortaleza',
  'America/Recife',
  'America/Noronha',
  'America/Porto_Velho',
  'America/Boa_Vista',
  'America/Rio_Branco',
];
```

### 2.3 TenantLocale

Represents the locale configuration resolved for the current tenant after login.
Read from JWT claims or tenant profile API response. Stored in `TenantContextService`.

```typescript
interface TenantLocale {
  timezone: BrazilianTimezone;  // configurable per tenant
  locale: 'pt-BR';              // V1 constant
  currency: 'BRL';              // V1 constant
  dateFormat: 'dd/MM/yyyy';     // V1 constant
}
```

**Source**: Populated by the auth flow (Spec 01) — the tenant profile returned after login
includes `timezone`. The constants (`locale`, `currency`, `dateFormat`) are hardcoded in the
`TenantContextService` default; only `timezone` comes from the API response.

### 2.4 FormErrorMap

```typescript
const FORM_ERRORS: Record<string, string> = {
  required:  'Campo obrigatório.',
  email:     'E-mail inválido.',
  cnpj:      'CNPJ inválido.',
  cpf:       'CPF inválido.',
  minlength: 'Muito curto.',
  maxlength: 'Muito longo.',
  min:       'Valor abaixo do mínimo.',
  max:       'Valor acima do máximo.',
};
```

---

## 3. Backend Value Objects

### 3.1 TurnstileVerificationRequest

Sent to Cloudflare's `siteverify` endpoint via `application/x-www-form-urlencoded`:

```csharp
public record TurnstileVerificationRequest(
    string Secret,      // TURNSTILE_SECRET_KEY env var
    string Response,    // token from the client
    string? RemoteIp    // client IP (optional but recommended)
);
```

### 3.2 TurnstileVerificationResponse

Deserialized from Cloudflare's JSON response:

```csharp
public record TurnstileVerificationResponse(
    [property: JsonPropertyName("success")]      bool Success,
    [property: JsonPropertyName("challenge_ts")] string? ChallengeTimestamp,
    [property: JsonPropertyName("hostname")]     string? Hostname,
    [property: JsonPropertyName("error-codes")]  string[]? ErrorCodes
);
```

### 3.3 TurnstileResult (domain result)

```csharp
public record TurnstileResult(bool Success, string? FailureReason = null)
{
    public static TurnstileResult Ok() => new(true);
    public static TurnstileResult Fail(string reason) => new(false, reason);
}
```

---

## 4. CSS Token Map (Design System Contract)

The following token groups are defined in `styles/tokens.css` and are the ONLY permitted
way to reference visual constants in component stylesheets.

| Group | Prefix | Examples |
|---|---|---|
| Brand colors | `--color-primary-{50..900}` | `--color-primary-500`, `--color-primary-600` |
| Secondary colors | `--color-secondary-{500}` | `--color-secondary-500` |
| Semantic colors | `--color-success`, `--color-warning`, `--color-danger` | |
| Surface (light) | `--color-surface-{0,50,100}` | |
| Surface (dark, under `.dark`) | `--color-surface-{0,50,100,800,900}` | |
| Text | `--color-text-primary`, `--color-text-muted`, `--color-text-inverse` | |
| Border/Accent | `--color-border`, `--color-accent` | |
| Typography — family | `--font-family-base`, `--font-family-mono` | |
| Typography — size | `--font-size-{xs,sm,base,md,lg,xl,2xl,3xl}` | |
| Typography — weight | `--font-weight-{normal,medium,semibold,bold}` | |
| Line height | `--line-height-{tight,normal,relaxed}` | |
| Spacing | `--spacing-{1,2,3,4,5,6,8,10,12}` | |
| Border radius | `--radius-{sm,md,lg,xl,full}` | |
| Shadow | `--shadow-{xs,sm,md,lg}` | |

**Dark mode**: Color tokens that change in dark mode are re-declared under `.dark { }` in
`tokens.css`. PrimeNG dark tokens are handled via `darkModeSelector: '.dark'` in the preset.

---

## 5. Field Mask → Storage Conversion Table

| Field | Mask display | Stored value | Digits |
|---|---|---|---|
| CNPJ | `12.345.678/0001-99` | `12345678000199` | 14 |
| CPF | `123.456.789-09` | `12345678909` | 11 |
| Celular | `(11) 98765-4321` | `11987654321` | 11 |
| Fixo | `(11) 3456-7890` | `1134567890` | 10 |
| CEP | `01310-100` | `01310100` | 8 |

All conversion is performed client-side before API submission.
The `dropSpecialCharacters: true` ngx-mask global config handles this automatically.
