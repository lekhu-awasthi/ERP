# Phase 1c status — Minimal role/permission stub (Identity/Tenancy context)

**Status: COMPLETE.** Every command from Phase 2 onward now has a real (if currently small)
MediatR pipeline behavior to check a permission against, in place of the ad hoc "is this user an
Accepted Admin member" checks Phase 1b inlined by hand. Confirmed by hand in-browser: a fresh
Admin can still create an Organization and invite a Member exactly as before; a Member calling
the invite endpoint directly gets a real HTTP 403 with the expected ProblemDetails body. Backed
by a real SQL Server migration applied to the actual local dev database (not just a scratch
container), with a hand-verified data backfill of every pre-existing membership row.

## Roadmap Phase 1c exit criteria — final status

- [x] `Role` + `RolePermission` entities (`schema tenancy`), seeded with exactly two rows --
      Admin (every currently-defined permission granted) and Member (explicitly denied) -- via
      `RoleConfiguration`/`RolePermissionConfiguration`'s `HasData`
- [x] `IAuthorizationBehavior`-equivalent (`AuthorizationBehavior<TRequest, TResponse>`) wired
      into the MediatR pipeline in `src/Application/Common/Behaviors/`, alongside
      `LoggingBehavior`/`ValidationBehavior`
- [x] Marker-interface-based permission declaration (`IRequirePermission`, `IOrganizationScoped`,
      `ITargetsMembership` in `Application.Common.Security`) rather than an attribute -- see
      "Scope decisions" below for why
- [x] `CreateOrganizationCommand`, `InviteUserCommand`, `AcceptRequestCommand` all implement
      `IRequirePermission`, proving the pipeline fires for a global permission, an
      organization-scoped permission, and a membership-target-resolved permission respectively
- [x] `ForbiddenException` (already mapping to HTTP 403) thrown from `AuthorizationBehavior` on a
      denied check -- no new exception type
- [x] `InviteUserCommandHandler`/`AcceptRequestCommandHandler`'s inline admin checks deleted,
      replaced entirely by the pipeline
- [x] `OrganizationMembership.Role` (the Phase 1b `MembershipRole` enum stub) migrated to a real
      `RoleId` FK into `Role` -- see "Scope decisions" below for the exact shape chosen
- [x] `MyOrganizationsQuery`'s DTOs updated to join `Role.Name` so the Angular contract (and thus
      `organization-dashboard-page`'s `org.role === 'Admin'` gate) needed **zero** changes
- [x] EF Core migration (`AddRoleAuthorization`) hand-edited to backfill every existing
      membership's `RoleId` from the old `Role` string column before dropping it, applied to the
      real local dev database with the backfill verified via `sqlcmd`
- [x] 1 new Domain unit test, 8 new Application tests (`AuthorizationBehaviorTests` +
      updates to the `InviteUser`/`AcceptRequest`/`CreateOrganization`/`MyOrganizations` handler
      tests), all existing backend tests still green including the Docker-backed integration
      test, `dotnet build`/`ng build`/`ng test` all green
- [x] Manual E2E: registered a fresh Admin, created an Organization (exercises the global
      `CreateOrganizationCommand` permission), invited a second user as Member, that user
      registered and accepted, dashboard correctly showed "Member" and hid the invite panel, and
      a direct `fetch()` call to `POST /api/organizations/{id}/invitations` from that Member's
      authenticated session returned `403 { "title": "You do not have permission to perform this
      action (Tenancy.Organization.InviteUser)." }`

## Scope decisions

1. **`Role`/`RolePermission` are system-level, not per-Organization.** The roadmap left this an
   explicit open choice ("per Organization at creation time, or as system-level roles referenced
   by every org, whichever keeps `CreateOrganizationCommand`'s seeding simplest"). Went with
   system-level: two fixed rows (`Role.AdminId`/`Role.MemberId`, well-known GUID constants)
   shared by every Organization, seeded once via migration `HasData` rather than re-created (and
   re-granted) on every `CreateOrganizationCommand`. There's nothing yet to customize per-tenant
   -- no Role Reference editor, no per-document-type permission matrix -- so a shared catalog is
   strictly simpler and there was no correctness reason to prefer per-org rows. Revisit when the
   roadmap's later "Role Reference full editor" (Phase 8+) needs per-tenant customization.
2. **`MembershipRole` (the Phase 1b enum) survives, but only as an API/UI-facing role
   *selector*, not as what's persisted.** `OrganizationMembership.RoleId` (a `Guid` FK) is the
   real persisted column now. Every `OrganizationMembership` factory method
   (`CreateAccepted`/`Invite`/`Request`) still takes a `MembershipRole` parameter for
   readability at call sites (and to avoid touching every existing call site/test), and maps it
   internally via `Role.ResolveId(MembershipRole)` to the well-known `RoleId`. This means
   `InviteUserCommand`'s `Role` parameter, the invite dropdown, and `InviteUserResult` are
   completely unchanged -- the FK migration is invisible at the API boundary. Considered exposing
   a raw `RoleId` end to end instead; rejected because nothing in this phase needs per-tenant
   custom roles yet, so the extra indirection would have been pure ceremony.
3. **Permission declaration is marker interfaces, not an attribute.** The task description
   offered either `[RequiresPermission("...")]` or a marker interface
   (`IRequirePermission { PermissionKey }`); went with the interface because MediatR pipeline
   behaviors resolve generically over `TRequest`, and a plain interface check (`request is
   IRequirePermission`) is simpler and more discoverable (compiler-checked, IDE "Find
   Implementations" works) than reflecting for a custom attribute. Two further marker interfaces
   distinguish *how* `AuthorizationBehavior` finds the relevant Organization: `IOrganizationScoped`
   (the request carries `OrganizationId` directly -- `InviteUserCommand`) and
   `ITargetsMembership` (the request only carries a `MembershipId`, so the behavior resolves
   `OrganizationId` from that membership row -- `AcceptRequestCommand`, which has no
   `OrganizationId` in its route at all). A request implementing `IRequirePermission` alone (no
   scope interface) -- only `CreateOrganizationCommand` today -- is treated as a **global**
   permission: any authenticated user may proceed, since creating an Organization is by
   definition the one action that predates any membership (and thus any role) in it.
4. **`AcceptInvitationCommand` was deliberately left out of the permission pipeline.** It's
   self-service (the invitee accepting their own invitation, gated by "is this membership
   addressed to me" inside the handler, unrelated to org-admin permissions) -- adding a
   permission check there would be checking the wrong thing. Only `CreateOrganizationCommand`,
   `InviteUserCommand`, and `AcceptRequestCommand` were named in the task and are the only
   Phase 1b commands where "is the *acting* user an org admin" is the real question.
5. **The permission-key catalog (`PermissionKeys`) currently has three entries** --
   `Tenancy.Organization.Create` (global), `Tenancy.Organization.InviteUser`,
   `Tenancy.Organization.AcceptRequest` -- deliberately not the full
   `(scope, module, documentType, action)` matrix architecture-spec.md §3.7 describes for the
   eventual Role Reference editor. `PermissionKey` stays a flat string for now, exactly per the
   roadmap's "just enough to unblock later phases" framing; each later phase's commands add their
   own constants as they're built.

## New cross-cutting pieces (will matter for Phase 2+)

- **`AuthorizationBehavior<TRequest, TResponse>`** (`Application.Common.Behaviors`) is now part
  of the MediatR pipeline for *every* command/query (Logging → Validation → Authorization →
  Handler), not just Tenancy ones. Any new command that should be permission-gated just
  implements `IRequirePermission` (+ `IOrganizationScoped` or `ITargetsMembership` if it's
  org-scoped) and adds a `RolePermission` seed row for it -- no pipeline wiring needed.
- Because `AuthorizationBehavior` is constructed for *every* request regardless of whether it
  implements `IRequirePermission`, any DI container that resolves the MediatR pipeline now needs
  `IAppDbContext`/`ICurrentUserService` registered even for requests that don't use them --
  bit `PingQueryTests`' minimal hand-rolled container (see "Bugs hit" below).
- **`Role.AdminId`/`Role.MemberId`** are stable, well-known GUID constants (not
  configuration-driven) -- any future code needing "the Admin role" or "the Member role"
  specifically (as opposed to querying by permission) should reference these rather than
  querying `Roles` by `Name`.

## Bugs hit and fixed along the way

1. **The scaffolded migration dropped the old `Role` column before adding `RoleId`,** which
   would have silently discarded every existing membership's role. `dotnet ef migrations add`
   orders operations by model diff, not by data-safety -- it doesn't know `RoleId` needs to be
   backfilled *from* the column it's about to drop. Hand-edited the generated migration:
   create+seed `Roles`/`RolePermissions` first, add `RoleId` (nullable-by-default-value, not
   nullable-then-backfilled -- SQL Server allows adding a `NOT NULL` column with a `DEFAULT` to a
   non-empty table), run a raw-SQL `UPDATE ... CASE WHEN [Role] = N'Admin' THEN ... END` backfill
   while the old column was still present, *then* drop it. Verified against the real local dev
   database (not a scratch container -- see Phase 1b's bug #1 lesson) with a `sqlcmd` query
   confirming all 8 pre-existing membership rows backfilled to the correct `RoleId`.
2. **`PingQueryTests` (Phase 0's minimal "pipeline fires" proof) broke** once
   `AuthorizationBehavior` was registered: its hand-rolled `ServiceCollection` only registered
   `AddApplication()` + logging, not `IAppDbContext`/`ICurrentUserService`, and MediatR
   constructs every registered `IPipelineBehavior<,>` for a request regardless of whether that
   behavior's `Handle` actually touches its dependencies. Fixed by registering
   `TestAppDbContext.Create()` and a `FakeCurrentUserService` in that test's container, matching
   what the real `AddApplication()`/`Program.cs` composition root provides.
3. **This session's Node.js (v16.20.2 via a stale PATH entry) was below Angular 21's minimum
   (v20.19+),** breaking `ng build`/`ng test` on the first attempt with an unrelated-looking CLI
   version error. `nvm-windows` was already installed with a 20.20.2 runtime available but not
   selected; switched with `nvm use 20.20.2` (requires elevation) before `ng build`/`ng test`
   would run. `ng test` also needed to run from a native PowerShell shell rather than Git Bash --
   Vitest's forked worker pool failed to start ("Timeout waiting for worker to respond") under
   Git Bash's process model in this environment, but ran cleanly from PowerShell.
4. **Docker Desktop wasn't running at session start,** so `Api.IntegrationTests` (which needs a
   Testcontainers SQL Server) failed with a `docker info` connection error on the first attempt.
   Started Docker Desktop and polled `docker info` until ready before re-running; no code changes
   needed, purely an environment-startup-order issue (same class of problem as Phase 1b's bug #2
   about stale dev-server processes -- local tooling has to actually be up before its dependent
   tests will pass).

## What's next

**Phase 2 — Configuration foundation** (see `roadmap.md`): generic `LookupList<T>` CRUD +
`CreditTerm`/`PaymentMode`/`CustomStatus`/`ReportingTagCategory` lookups, the real `TenantSettings`
fields, `DocumentNumberingRule` + `IDocumentNumberGenerator`, and `CustomFieldDefinition`/
`CustomFieldValue`. Every command Phase 2 adds should implement `IRequirePermission` (+
`IOrganizationScoped` where relevant) from the start now that the pipeline is real, rather than
retrofitting permission checks later -- add the corresponding `PermissionKeys` constant and
`RolePermission` seed rows (Admin=granted, Member=whatever's appropriate for that action) in the
same change.
