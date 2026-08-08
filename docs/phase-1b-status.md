# Phase 1b status — Organization & membership (Tenancy context)

**Status: COMPLETE.** Full flow (login → create Organization via 3-step wizard → land on its
dashboard → invite a second user → accept from that user's account) confirmed working by hand
in-browser, backed by real SQL Server persistence, real email delivery for the invite, no
mocked data.

## Roadmap Phase 1b exit criteria — final status

- [x] `Organization` aggregate + EF Core mapping (schema `tenancy`): Name, Industry, Address,
      AccountingStartDate, IsVatRegistered, WorkspaceName (unique), LockDate?
- [x] `CheckWorkspaceNameAvailabilityQuery` — debounced live availability check, called on every
      keystroke from the wizard's Step 1
- [x] `CreateOrganizationCommand` — single command backing the whole 3-step wizard: creates the
      Organization, seeds `TenantSettings`, seeds a 15-day-trial `TenantSubscription` carrying
      the 7 Accounting Features checkboxes as entitlement flags, creates the creator's Admin
      `OrganizationMembership`
- [x] `OrganizationMembership` join entity, `MembershipRole` (Admin/Member stub per Phase 1c),
      `MembershipStatus` (Requested/Invited/Accepted)
- [x] `InviteUserCommand` (email + role, admin-only) → pending membership (email-based, resolves
      to a `UserId` immediately if the invitee already has an account, otherwise later at accept
      time) + invite email via real SMTP; `AcceptInvitationCommand` (self-service, invitee-only);
      `AcceptRequestCommand` (admin-only) — see "Scope decisions" below on why this last one has
      nothing to accept yet
- [x] `MyOrganizationsQuery` — powers the 3-tab landing page
- [x] Angular: Organization List (3 tabs), New Organization wizard (3 steps, live workspace-name
      check + Step 2 feature checkboxes + Step 3 review), Welcome page, Organization dashboard
      with a company switcher and the invite-user form (Admin-only)
- [x] 6 new Domain unit tests, 14 new Application handler tests, all existing backend tests still
      green (Docker-backed integration test included), `ng build` + `ng test` green
- [x] Manual E2E: registered an Admin user, created "Acme Traders" through the wizard, landed on
      its dashboard, invited a second (not-yet-registered) email as Member, registered that
      second user, accepted the invitation from their Invitations tab, confirmed the Member role
      correctly hides the invite panel

## Scope decisions

1. **`AcceptRequestCommand` has no origination flow yet.** The roadmap asks for
   `InviteUserCommand` / `AcceptInvitationCommand` / `AcceptRequestCommand` but no
   "browse/search workspaces and request to join" UI — Angular task list only covers List /
   Wizard / Welcome / switcher. Rather than skip the explicitly-named command or invent an
   unscoped discovery UI, `OrganizationMembership.Request(...)` and `AcceptRequestCommand` are
   both built and tested (an org admin approving someone else's pending join request), just with
   nothing yet creating a `Requested`-status row. The "Requests" tab will show its empty state
   until a later phase adds the request-to-join origination flow.
2. **Invites are email-based, not user-id-based**, matching the reference product's
   `UserInvitation { email, ... }` shape (`erp-module-scan.md` item 19's data model note) — an
   admin can invite someone who hasn't registered yet. `OrganizationMembership.UserId` is
   nullable; it's resolved at invite time if a matching account already exists, or at accept
   time (matched by the logged-in user's email) otherwise.
3. **`TenantSettings` is seeded as a near-empty marker row** (`Id`, `OrganizationId`,
   `CreatedAt`) rather than pre-building Phase 2's real settings fields (Suggest Selling Price
   mode, Inventory Tracking mode, etc.) — those aren't designed yet and the roadmap only asks
   this phase to seed the row so it exists.
4. **Industry is free text with suggestions**, not Tigg's ~70-entry seeded combobox — a
   `<datalist>` of ~25 common Nepali SME sectors plus "Other" covers the spirit without building
   a full searchable-combobox component for data that's ultimately free text anyway.
5. **No Bikram Sambat date picker.** Accounting Start Date is a plain Gregorian `<input
   type="date">` / `DateOnly` — BS conversion wasn't asked for and would be a meaningfully sized
   sub-feature on its own.
6. **Cloudflare Turnstile stays deferred**, per Phase 1a's precedent and the original roadmap
   note (later hardening item, not a blocker for a working vertical slice).

## New cross-cutting pieces (will matter for Phase 1c+)

- **`ICurrentUserService`** (`Application.Common.Security`, implemented in `Api/Services` over
  `IHttpContextAccessor`): the acting user's ID, resolved from the JWT `sub` claim rather than
  trusted from client input. Every Tenancy command that needs to know "who is doing this" depends
  on this now; Phase 1c's `IAuthorizationBehavior` will likely build on the same interface.
- **`ForbiddenException` → HTTP 403** added alongside the existing exception-to-ProblemDetails
  map, for "authenticated but not allowed" (not an org admin) as distinct from 401/`EmailNotVerifiedException`'s 403.
- **Enums now serialize as strings** (`JsonStringEnumConverter` registered globally in
  `Program.cs`) — `MembershipRole` is the first enum to cross the Api boundary; this setting
  applies to every future enum DTO too.

## Bugs hit and fixed along the way

1. **Migration validated against a throwaway Docker container, not the actual dev database.**
   `dotnet ef database update` was run against a temporary SQL Server container to sanity-check
   the migration SQL (indexes, FKs, cascade paths) before relying on it — but that's a different
   database from the one `ConnectionStrings:Default` (user-secrets) actually points the dev Api
   at. First manual browser test hit `Invalid object name 'tenancy.Organizations'` on every
   `/api/organizations/*` call until `dotnet ef database update` was re-run with no `--connection`
   override, applying it to the real local SQL Server Express instance. **Lesson for next
   phase**: a migration "applying cleanly" against a scratch database doesn't mean it's been
   applied to the database the app you're about to click-test actually uses — always follow up
   with `dotnet ef database update` (no override) before manual verification.
2. **Stale dev-server processes from an earlier session blocked both `dotnet build` and
   `preview_start`.** A leftover `dotnet run --project src/Api` process held a file lock on the
   build output (`MSB3027`/`MSB3021` copy errors) until `dotnet build-server shutdown` cleared
   MSBuild's node-reuse workers; separately, a leftover `ng serve` from an earlier session
   already owned port 4200. Neither was this session's own process — both were artifacts of a
   previous conversation's dev servers never being stopped. Fixed by finding the owning PID
   (`Get-CimInstance Win32_Process -Filter "Name='...'" | Select CommandLine`) and stopping it
   before retrying.
3. **Nested `<form>` in the wizard template.** An early draft of the New Organization wizard's
   Step 1 accidentally wrapped the fields in a second bare `<form>` inside the outer
   `[formGroup]`-bound one. Caught before running `ng build`/`ng test` by re-reading the
   generated template; fixed by using a plain `<div class="form-fields">` instead — only the
   outer element needs to be a `<form>`.

## Tooling notes for future sessions

- **Querying the dev database directly is faster than round-tripping through real email** when
  manually testing verification-code flows: `sqlcmd -S 'DESKTOP-H0R00ME\SQLEXPRESS' -d ErpApp -E
  -C -Q "SELECT ... FROM [identity].VerificationCodes ..."` (note the `[identity]` brackets —
  `identity` is a SQL Server reserved-adjacent word and breaks unbracketed). Real SMTP delivery
  was still exercised end-to-end for the invite email itself.
- `.claude/launch.json` now has an `erp-api` entry (`dotnet run --project src/Api
  --launch-profile https`, port 7104) alongside the existing `erp-web` entry, so both dev
  servers can be driven via `preview_start` in future sessions instead of only the frontend.

## What's next

**Phase 1c — Minimal role/permission stub** (see `roadmap.md`): `Role` + `RolePermission`
tables seeded with just Admin/Member, `IAuthorizationBehavior` pipeline wiring so every command
from Phase 2 onward goes through real (if trivial) permission checks instead of the ad hoc
admin-membership checks `InviteUserCommand`/`AcceptRequestCommand` do inline today.
