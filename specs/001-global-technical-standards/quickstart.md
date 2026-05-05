# Quickstart: Global Technical Standards

**Branch**: `001-global-technical-standards`
**Purpose**: Verify that standards are applied correctly in any new feature implementation.

---

## 1. Verify Design Tokens

After implementing any new Angular component, open the component's stylesheet and grep
for hardcoded color values:

```bash
grep -nE '#[0-9a-fA-F]{3,6}|rgb\(|rgba\(' src/omniDesk.Crm/src/app/features/your-feature/**/*.css
```

Expected output: **no matches**. All color references must use `var(--color-*)` tokens.

---

## 2. Test Dark Mode

1. Open the CRM or Admin app in a browser.
2. Click the dark mode toggle in the header.
3. Navigate to the feature you implemented.
4. Verify:
   - No text is invisible (white on white, dark on dark).
   - No element uses `#000000` as a background or foreground color.
   - The toggle persists after a page reload (`localStorage.getItem('theme')` returns `'dark'`).

---

## 3. Verify Field Masks

For a form with CNPJ, CPF, phone, or CEP fields:

1. Type digits into each masked field — verify the mask appears automatically.
2. Submit the form or inspect the HTTP request payload in DevTools:
   ```json
   { "cnpj": "12345678000199" }   // ✅ correct — digits only
   { "cnpj": "12.345.678/0001-99" } // ❌ wrong — mask characters present
   ```
3. Enter an invalid CNPJ (e.g., `11111111111111`) — verify "CNPJ inválido." appears.
4. Enter a valid CNPJ (e.g., `11222333000181`) — verify no error appears.

---

## 4. Verify Date Display

1. Find a record with a known UTC timestamp (e.g., `2026-05-05T20:00:00Z`).
2. Check that the UI displays `05/05/2026 17:00` for a tenant with `America/Sao_Paulo`
   timezone (UTC-3), or `05/05/2026 16:00` for `America/Manaus` (UTC-4).
3. Switch the tenant's timezone in the admin panel and verify the displayed time updates.

```typescript
// Quick unit test for DateDisplayService
it('converts UTC to São Paulo time', () => {
  // Configure TenantContextService with 'America/Sao_Paulo'
  expect(service.toDisplay('2026-05-05T20:00:00Z')).toBe('05/05/2026 17:00');
});
```

---

## 5. Verify Turnstile on Login

1. Open the login page.
2. Confirm the Turnstile widget renders (a small challenge box or checkmark).
3. Confirm the submit button is disabled until the widget completes.
4. Open DevTools → Network, submit the login form and verify the request body includes
   `"turnstileToken": "<non-empty string>"`.
5. Make a direct `curl` POST to `/api/auth/login` without the token:
   ```bash
   curl -X POST https://api.omnideskcrm.com.br/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"test@test.com","password":"pass"}'
   ```
   Expected: HTTP 400 (missing required field) or HTTP 403.
6. Make a POST with a forged token:
   ```bash
   curl -X POST https://api.omnideskcrm.com.br/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"test@test.com","password":"pass","turnstileToken":"fake"}'
   ```
   Expected: HTTP 403.

---

## 6. Verify Encoding

```bash
# Check for BOM in any TypeScript file
find src/ -name "*.ts" | xargs grep -rl $'\xef\xbb\xbf' && echo "BOM FOUND" || echo "OK"

# Check line endings (should be all LF, not CRLF)
find src/ -name "*.ts" | xargs grep -rcl $'\r' && echo "CRLF FOUND" || echo "OK"
```

Expected output for both: `OK`.

---

## 7. Verify Locale Configuration

1. Open the browser console on any CRM page:
   ```javascript
   // Angular's injected LOCALE_ID
   // Should log 'pt-BR' (visible in the Angular DevTools panel or via injection)
   ```
2. Find any currency display and verify it shows `R$ X.XXX,XX` format (comma as decimal
   separator, period as thousands separator).
3. Find any date and verify it is `dd/MM/yyyy` format.

---

## Reference: File Locations

| Standard | File |
|---|---|
| CSS tokens | `src/<project>/src/styles/tokens.css` |
| CNPJ validator | `src/<project>/src/app/shared/validators/cnpj.validator.ts` |
| CPF validator | `src/<project>/src/app/shared/validators/cpf.validator.ts` |
| Form error messages | `src/<project>/src/app/shared/validators/form-errors.ts` |
| Theme service | `src/<project>/src/app/core/services/theme.service.ts` |
| Date display service | `src/<project>/src/app/core/services/date-display.service.ts` |
| Turnstile component | `src/<project>/src/app/shared/components/turnstile/` |
| Turnstile backend service | `src/omniDesk.Api/Infrastructure/Security/TurnstileService.cs` |
| Root .editorconfig | `.editorconfig` (repository root) |
