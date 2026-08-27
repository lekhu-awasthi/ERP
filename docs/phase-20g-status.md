# Phase 20g — Turnstile bot-check on registration (FR-1.1)

## TL;DR

The Phase 1 hardening deferral, closed out: `RegisterUserCommand` gained a required
`TurnstileToken` field, verified server-side by a new `ITurnstileVerifier` (Infrastructure:
`TurnstileVerifier`, a typed `HttpClient` calling Cloudflare's `siteverify` endpoint) before any
uniqueness check or `User.Register()` call — a failed/missing token throws
`TurnstileVerificationFailedException` (400), never reaching user creation. Frontend: a small
reusable `app-turnstile-widget` (script tag in `index.html`, `window.turnstile.render()`, no new
npm dependency) wired into the registration page only — scope match to the roadmap title and
FR-1.1, not the New Organization wizard's two additional Turnstile checks (module-scan §5), which
stay out of scope for this sub-phase. Domain unchanged (134 tests), Application 288 tests (+1),
Angular 7 specs (unchanged, `RegisterRequest` extended), `dotnet build`/`ng build`/`tsc --noEmit`
clean. Manual E2E
via live browser + curl + `sqlcmd` against local dev: full positive round-trip (Turnstile widget
auto-resolves with Cloudflare's documented always-passes dummy sitekey, register → 201 → verify-
email page); a 400 with a validation error naming `TurnstileToken` for an empty token; and — the
one genuinely non-obvious step — a true negative-path proof required *temporarily swapping the
configured secret key* to Cloudflare's always-*fails* dummy secret (`2x000...AA`) and restarting
the Api, because the always-*passes* dummy secret (`1x000...AA`) accepts literally any token value,
so it cannot itself prove the server-side check would reject a bad one. `sqlcmd` confirmed the two
rejected attempts never persisted a `Users` row while the two accepted ones did.

## Scope decision

Roadmap's own phrasing ("Turnstile bot-check **on registration**") and FR-1.1 ("A visitor shall be
able to register a new account ... protected by a bot-check") both point at the standalone
`me.tiggapp.com/#/register` form specifically, not organization creation. `erp-module-scan.md`'s
Signup & Onboarding section independently confirms the shape: Turnstile appears three times in the
live product — once on Registration (§1), and twice more inside the New Organization wizard (§5,
Step 1 and Step 3) — three separate widget instances, not one shared control. Wiring only the
registration one keeps this sub-phase "small and isolable" as the roadmap calls for; the wizard's
two checks are a mechanical follow-up if ever prioritized (same `app-turnstile-widget` component,
just dropped into `new-organization-wizard.html` twice more with their own token fields threaded
into `CreateOrganizationCommand`), not attempted here to avoid scope creep on a phase explicitly
sized as small.

## What shipped

**Backend**
- `Application/Common/BotProtection/ITurnstileVerifier.cs` — `Task<bool> VerifyAsync(token, ct)`.
- `Application/Common/Exceptions/TurnstileVerificationFailedException.cs` → mapped to 400 in
  `ExceptionHandling.cs`, alongside `InvalidVerificationCodeException`'s existing pattern.
- `RegisterUserCommand`/`Validator`/`Handler`: new required `TurnstileToken` (`NotEmpty()`), verified
  as the *first* thing the handler does — before the email-uniqueness check — so a bot never
  learns whether an email is already registered.
- `Infrastructure/BotProtection/{TurnstileOptions,TurnstileVerifier}.cs` — typed `HttpClient` (new
  `Microsoft.Extensions.Http` package reference on `Infrastructure`, this codebase's first
  outbound-HTTP-call Infrastructure service) POSTing form-encoded `secret`/`response` to
  `https://challenges.cloudflare.com/turnstile/v0/siteverify`. Response JSON uses hyphenated key
  `error-codes`, not camelCase — needs an explicit `[JsonPropertyName]`, not
  `PropertyNameCaseInsensitive` (that only handles casing, not the hyphen).
- DI: `AddOptions<TurnstileOptions>().Bind(...).Validate(...).ValidateOnStart()` +
  `AddHttpClient<ITurnstileVerifier, TurnstileVerifier>()`, mirroring `EmailOptions`'s existing
  user-secrets pointer-comment pattern in `appsettings.Development.json`.

**Frontend**
- `shared/turnstile/turnstile-widget.ts` — script tag lives in `index.html` as `async defer`, so
  `window.turnstile` may not exist yet when the component mounts; render is deferred behind a
  capped poll (100 × 100ms) rather than assumed ready. Exposes a public `reset()` (Cloudflare
  tokens are single-use — the register page calls it after any failed submit, including a
  server-rejected token, so the user gets a fresh challenge without a full page reload) and
  `verified`/`expired`/`failed` outputs.
- `register-page.ts`/`.html`: submit is gated on both form validity *and* a captured Turnstile
  token; a touched-but-unsolved state shows an inline message rather than a disabled button (matches
  this page's existing touched/invalid pattern for other fields).
- `environment.ts`/`environment.development.ts`: `turnstileSiteKey`, Cloudflare's public-by-design
  always-passes dummy sitekey (`1x00000000000000000000AA`) checked in directly — unlike the secret
  key, a Turnstile sitekey is meant to be embedded in client HTML, so this isn't a secrets-hygiene
  violation; a real deployment swaps it for the tenant's actual sitekey from the Cloudflare
  dashboard at build/deploy time.

## Known limitations / follow-ups

- New Organization wizard's two Turnstile checks (module-scan §5) are out of scope per the Scope
  decision above — mechanical follow-up reusing `app-turnstile-widget`, not attempted here.
- No retry/backoff around the `siteverify` HTTP call — a Cloudflare outage surfaces as every
  registration attempt failing closed (400), which is the conservative default for a bot-check, not
  treated as a bug.
