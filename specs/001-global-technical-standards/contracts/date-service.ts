/**
 * Contract: DateDisplayService
 *
 * Both Angular projects MUST provide a service conforming to this interface.
 * Implementation: src/<project>/src/app/core/services/date-display.service.ts
 *
 * Dependencies:
 *   - date-fns-tz (toZonedTime, fromZonedTime, format)
 *   - date-fns/locale/pt-BR
 *   - TenantContextService (provides timezone Signal<string>)
 */

export interface IDateDisplayService {
  /**
   * Converts a UTC ISO 8601 timestamp to a localized display string
   * in the tenant's configured timezone.
   *
   * @param utcIso  - UTC timestamp, e.g. "2026-05-05T20:00:00Z"
   * @param fmt     - date-fns format string (default: 'dd/MM/yyyy HH:mm')
   * @returns       - Formatted string, e.g. "05/05/2026 17:00"
   *
   * Contract:
   * - MUST read timezone from TenantContextService (never from a hardcoded value)
   * - MUST use ptBR locale for month/day names
   * - MUST NOT throw on a null/undefined input — return empty string instead
   */
  toDisplay(utcIso: string, fmt?: string): string;

  /**
   * Converts a local Date (in the tenant's timezone) back to a UTC ISO string
   * suitable for sending to the API.
   *
   * @param local - Date object representing local time in the tenant's timezone
   * @returns     - UTC ISO 8601 string, e.g. "2026-05-05T20:00:00.000Z"
   */
  toUtc(local: Date): string;
}

/**
 * Contract: ThemeService
 *
 * Implementation: src/<project>/src/app/core/services/theme.service.ts
 */
export interface IThemeService {
  /** Current dark-mode state as a Signal. Read-only from outside the service. */
  readonly isDark: import('@angular/core').Signal<boolean>;

  /**
   * Toggle dark mode.
   * - Flips isDark signal
   * - Adds/removes `.dark` class on document.documentElement
   * - Persists choice to localStorage under key 'theme'
   */
  toggle(): void;
}

/**
 * Contract: TurnstileComponent (Angular Standalone Component)
 *
 * Implementation: src/<project>/src/app/shared/components/turnstile/
 *
 * Selector: app-turnstile
 *
 * Outputs:
 *   tokenChange: EventEmitter<string | null>
 *     - Emits a non-null string when Turnstile issues a valid token
 *     - Emits null when the token expires or an error occurs
 *
 * Usage:
 *   <app-turnstile (tokenChange)="onToken($event)" />
 *
 * The consuming form MUST:
 *   - Disable the submit button while tokenChange value is null
 *   - Include the current token in the API request body as `turnstileToken`
 */
export interface TurnstileComponentContract {
  tokenChange: import('@angular/core').EventEmitter<string | null>;
}
