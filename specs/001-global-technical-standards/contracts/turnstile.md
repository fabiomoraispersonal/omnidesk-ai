# Contract: Turnstile Server-Side Verification

**Owner**: `omniDesk.Api` — `Infrastructure/Security/TurnstileService.cs`
**Used by**: Any endpoint that must be bot-protected (see FR-040, FR-041)

---

## TurnstileService Interface

```csharp
public interface ITurnstileService
{
    /// <summary>
    /// Verifies a Cloudflare Turnstile token with the siteverify API.
    /// Returns TurnstileResult.Ok() on success, TurnstileResult.Fail() on any failure
    /// including network errors (fail-closed — no bypass on error).
    /// </summary>
    Task<TurnstileResult> VerifyAsync(string token, string? remoteIp = null);
}
```

---

## External API Contract (Cloudflare)

**Endpoint**: `POST https://challenges.cloudflare.com/turnstile/v0/siteverify`

**Request** (`application/x-www-form-urlencoded`):

| Field | Required | Description |
|---|---|---|
| `secret` | Yes | `TURNSTILE_SECRET_KEY` env var — never exposed to client |
| `response` | Yes | Token submitted by the client form (`turnstileToken` request field) |
| `remoteip` | No | Client IP address — recommended for abuse detection |

**Response** (`application/json`):

```json
{
  "success": true,
  "challenge_ts": "2026-05-05T17:00:00Z",
  "hostname": "app.omnideskcrm.com.br",
  "error-codes": []
}
```

| Field | Type | Description |
|---|---|---|
| `success` | `bool` | `true` if the token is valid and unused |
| `challenge_ts` | `string?` | ISO timestamp of when the challenge was solved |
| `hostname` | `string?` | Hostname where the challenge was solved |
| `error-codes` | `string[]?` | Non-empty if `success` is false |

---

## Endpoint Integration Contract

Every endpoint protected by Turnstile MUST follow this pattern:

```csharp
// Pattern for any Turnstile-protected endpoint
app.MapPost("/api/auth/login", async (
    LoginRequest request,
    ITurnstileService turnstile,
    HttpContext ctx) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    var result = await turnstile.VerifyAsync(request.TurnstileToken, ip);
    if (!result.Success)
        return Results.Problem(statusCode: 403, detail: "Validação de segurança falhou.");

    // proceed with authentication logic
});
```

**Request body contract** (all Turnstile-protected endpoints):

All protected endpoint request types MUST include:

```csharp
public record LoginRequest(
    // ... other fields ...
    string TurnstileToken   // required; validated before any business logic
);
```

---

## Endpoints Requiring Turnstile (V1)

| Endpoint | Project | Protected By |
|---|---|---|
| `POST /api/auth/login` | `omniDesk.Api` | Turnstile + JWT |
| `POST /api/auth/forgot-password` | `omniDesk.Api` | Turnstile |

---

## Failure Behavior (Fail-Closed)

| Scenario | Behavior |
|---|---|
| Missing `turnstileToken` in request | HTTP 400 (validation error before Turnstile check) |
| Invalid/expired token (Cloudflare returns `success: false`) | HTTP 403 |
| Cloudflare network error / timeout | HTTP 403 (fail-closed — no bypass) |
| Replay of a previously used token | HTTP 403 (`success: false` from Cloudflare) |

---

## Environment Variables

| Variable | Scope | Description |
|---|---|---|
| `TURNSTILE_SECRET_KEY` | Server-side only | Cloudflare secret key — never in client code |
| `TURNSTILE_SITE_KEY` | Client (environment.prod.ts) | Cloudflare site key — safe to expose |
