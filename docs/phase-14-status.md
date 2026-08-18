# Phase 14 status — Role Reference

**Status: COMPLETE.** `Role` (`Domain.Tenancy`) gains a nullable `OrganizationId`, upgrading Phase
1c's two-hardcoded-system-role stub (`Role.AdminId`/`Role.MemberId`, shared across every
Organization) into a real per-tenant permission-matrix editor — the item `docs/roadmap.md`'s
Phase 8+ section named directly ("upgrade Phase 1c's hardcoded-role stub into the real
per-document-type permission-matrix editor, once enough document types exist to make the matrix
meaningful"). That condition is now met: `PermissionKeys.cs` has grown to 107 constants across
every phase since Phase 1c. Confirmed live in `erp-module-scan.md`'s §16 "Users & Permissions"
section — 5 pre-seeded roles (Accountant, Admin, Purchase, Sales, View Only), an "Edit Role
Reference" panel with collapsible permission-group sections, 150+ checkboxes on a single role.
`architecture-spec.md` §3.7's own recommended shape (`RolePermission { RoleId, PermissionKey,
IsGranted }`, evaluated by one pipeline behavior) is exactly what `AuthorizationBehavior.cs`
already does — this phase extends the *data* behind it (real per-org `Role` rows, a real matrix
UI), not the check itself.

## Roadmap/brief exit criteria — final status

- [x] `Role.OrganizationId` (nullable) — `null` for the two seeded system rows (kept exactly
      as-is, zero migration risk to existing data), non-null for a tenant's own custom role
      (scope decision #1)
- [x] `ListRolesQuery(OrganizationId)` returns both the two shared system rows and this org's own
      custom rows
- [x] `CreateRoleCommand`/`UpdateRoleCommand`/`DeleteRoleCommand` — plain CRUD over custom roles
      only; both mutation commands reject a system-role target (409, not silently ignored)
- [x] `DeleteRoleCommand` rejects (409) if any `OrganizationMembership` still references the
      RoleId, mirroring this codebase's existing Restrict-delete precedent (scope decision #4)
- [x] `GetRolePermissionMatrixQuery(OrganizationId, RoleId)` — every `PermissionKeyCatalog` key,
      left-joined against the role's existing grants, grouped by module (scope decision #3)
- [x] `UpdateRolePermissionsCommand` — a diff-and-save bulk replace, not a blind clear-and-reinsert
      (scope decision #4)
- [x] `UpdateMembershipRoleCommand` — reassigns an existing Accepted member's Role after invite time
- [x] `InviteUserCommand` moved from a hardcoded `MembershipRole` selector to a real `RoleId`
      (scope decision #5) — a real behavior change, not just additive
- [x] `Tenancy.Role.View`/`Tenancy.Role.Manage` — Admin-only (scope decision #6)
- [x] HeadOffice/POS Restaurant/POS Retail permission sections explicitly out of scope (scope
      decision #2)
- [x] Angular: `role-list-page` (list + create/edit custom roles + Members role-reassignment),
      `role-permission-matrix-page` (checkbox grid grouped by module, single Save), invite form's
      role dropdown now sources `ListRolesQuery`
- [x] `RoleCommandHandlerTests` (16 tests: custom-role CRUD scoped per org, delete-blocked-while-
      referenced, permission-matrix diff-and-save, `ListRolesQuery` union, `InviteUserCommand`
      role-not-found)
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged, Application.UnitTests 172 — 16
      new + 156 pre-existing, Api.IntegrationTests 4, all green, Docker Desktop running this
      session) and `ng build`/`ng test` (7 pre-existing specs green, no new Angular specs) all pass
- [x] Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below)

## Scope decisions

1. **`Role` gets a nullable `OrganizationId` rather than staying a shared system catalog with
   per-org overrides layered on top.** This was the brief's one flagged "real judgment call."
   Weighed directly against the alternative (an `Organization`-scoped override table sitting on
   top of the existing shared rows): the nullable-`OrganizationId` approach needs no new table, no
   new join, and reuses `RolePermission`'s existing shape unchanged — a custom role's grants are
   just more rows in the same table, keyed by a different `RoleId`. It also keeps `Role.AdminId`/
   `Role.MemberId`'s well-known-Guid pattern (`Role.ResolveId(MembershipRole)`, still used by
   `CreateOrganizationCommandHandler` for the org-creator's own Admin membership) completely
   untouched. The cost is the one explicit tradeoff the brief called out and this phase leans into
   deliberately: the two system rows' `RolePermission` grants are *shared globally* across every
   tenant, so they are **not editable** through this phase's new per-org UI at all —
   `UpdateRoleCommandHandler`/`DeleteRoleCommandHandler`/`UpdateRolePermissionsCommandHandler` all
   reject (409 `ConflictException`) any attempt to target a row with `OrganizationId == null`.
   `GetRolePermissionMatrixQuery` still *reads* a system role's grants (so an Admin can use
   Admin/Member as a reference while designing a custom role), it just can't write them; the
   Angular matrix page disables every checkbox and hides Save when `isSystemRole` is true.

2. **HeadOffice/POS Restaurant/POS Retail sections are explicitly out of scope, not silently
   dropped.** `erp-module-scan.md`'s confirmed Role Reference panel has 6 sections total —
   General/Transactions/Settings/Reports plus HeadOffice and POS Restaurant/POS Retail, the latter
   three all `BillingLocation`-scoped duplicates of the same Sales/Inventory matrix
   (architecture-spec.md §3.7: `scope ∈ {default location, HeadOffice, PosRestaurant, PosRetail}`).
   `BillingLocation`/multi-location has no backing implementation anywhere in this codebase —
   Track Inventory/Multiple Locations/POS Retail/POS Restaurant are opt-in checkboxes recorded
   once at org creation (`AccountingFeatureSelections`) with no follow-on module ever built against
   them (confirmed by grep — no `BillingLocation` entity exists). Building location-scoped
   permission sections with nothing real to scope would invent UI for a feature that doesn't
   exist, so this phase ships only the sections with real backing: one flat matrix over
   `PermissionKeyCatalog`'s actual keys, grouped by the module prefix each key's own dotted
   `{Module}.{Entity}.{Action}` shape already encodes.

3. **`PermissionKeyCatalog` enumerates `PermissionKeys.cs` via reflection, not a hand-maintained
   parallel list.** The brief's second flagged judgment call, weighed explicitly against
   `LookupPermissionKeys.cs`'s own established precedent (a plain `switch`, deliberately avoiding
   reflection/attributes, per that file's own doc comment). The two helpers answer genuinely
   different questions: `LookupPermissionKeys` maps one *specific, closed* `TLookup` type to its
   own key — a small, meaningfully-different-per-case business decision, exactly what a switch is
   for. `PermissionKeyCatalog` instead answers "what is the *complete* universe of keys that exist
   right now" — a pure enumeration, not a per-case decision. `PermissionKeys.cs` has grown by
   several constants in nearly every phase since Phase 1c (107 as of this phase); a hand-maintained
   parallel array here would silently drift the next time a phase adds a key — a missed key just
   never appears in the matrix, no build error, no test failure, a real tenant-facing bug that's
   easy to miss. Reflection (`GetFields(Public | Static)` filtered to `IsLiteral && FieldType ==
   typeof(string)`, reading `GetRawConstantValue()`) has zero drift risk by construction, and only
   ever runs when an Admin opens the Role Reference matrix page — not a hot path, so the usual
   "avoid reflection-heavy generic machinery" bias doesn't carry the weight here it does for a
   per-request LINQ-translated property access (the actual gotcha that precedent was guarding
   against).

4. **`UpdateRolePermissionsCommand`'s `Grants` is the complete desired state, diffed against
   existing rows — not a delta the caller computes.** The Angular matrix page submits every key's
   current checkbox state each save (the brief's own framing: "a bulk replace over potentially
   100+ rows per save"). The handler reads this codebase's own known-gotchas entry on replacing an
   entire encapsulated child collection in one save (the Phase 4 Clear+re-Add InMemory-provider
   mis-tracking bug) as the operative discipline even though `RolePermission` isn't a child
   collection of an aggregate here — it diffs the requested grant state against each existing
   row's `IsGranted` explicitly and only touches what changed: `RolePermission.SetGranted(bool)`
   updates an existing row in place, a new row is only added the first time a key is touched (an
   absent row + a `false` request is left absent, matching `GetRolePermissionMatrixQuery`'s own
   "no row = false" read semantics), and no row is ever deleted. `RoleCommandHandlerTests`'s
   `UpdateRolePermissions_grants_a_key_revokes_a_key_and_touches_nothing_else` test asserts this
   directly: seeding two existing rows, granting one new key and revoking one existing key, then
   reopening a fresh `TestAppDbContext` instance against the same named InMemory database and
   confirming exactly the touched rows changed and the untouched row's `Id` is unchanged.
   `DeleteRoleCommandHandler` follows the same discipline in the opposite direction — it explicitly
   `RemoveRange`s a custom role's `RolePermission` rows itself rather than relying on the FK's
   `OnDelete(Cascade)` to fire under the InMemory provider (that cascade is real for the SQL Server
   provider via `ON DELETE CASCADE`, but the InMemory provider doesn't reliably simulate cascade
   delete for entities that aren't loaded via a navigation).

5. **`InviteUserCommand` moved from a hardcoded `MembershipRole` selector to a real `RoleId` — a
   real behavior change, not a silently-additive one.** Traced every `MembershipRole` call site
   first (`grep -rn MembershipRole src/ web/src`) before deciding what could change: `Role.
   ResolveId(MembershipRole)`, `OrganizationMembership.CreateAccepted`/`Request` all keep their
   existing `MembershipRole` parameter unchanged (the org-creator's own Admin membership, and the
   never-yet-exercised join-request flow, both still only ever need Admin-or-Member). Only
   `OrganizationMembership.Invite` and `InviteUserCommand` changed — `Invite`'s signature now takes
   a raw `Guid roleId` directly (not a `MembershipRole` selector resolved internally), since the
   inviter now picks from `ListRolesQuery`'s full set, which a two-value enum can't express.
   `InviteUserCommandHandler` validates the `RoleId` resolves to a role this Organization may
   actually assign (a system role, or this org's own custom role) before creating the membership,
   the same guard shape every other Role-targeting handler in this phase uses. The Angular invite
   form's role `<select>` now sources `roles()` (loaded via `ListRolesQuery`) instead of two
   hardcoded `<option>` tags, using `[selected]` per-option (not `[value]` on the `<select>`) per
   CLAUDE.md's own known-gotchas entry on that race — defensive here even though the control is
   Reactive-Forms-managed (`formControlName`), not signal-`[value]`-bound, since the options
   themselves are still populated by an async signal that can resolve on its own timeline relative
   to the form's own value.

6. **`Tenancy.Role.View`/`Tenancy.Role.Manage` are Admin-only — the one deliberate exception to
   this codebase's usual "grant Member whatever routine daily-use working data needs" default.**
   The brief called this out explicitly as worth a one-line note: granting a Member `RoleView`
   would let them see every other Role's exact grants (a privilege-escalation reconnaissance
   surface); granting `RoleManage` would let a Member grant themselves — or any custom role they
   belong to — anything at all. This is the one place in the whole permission system where Member
   access would be self-defeating, unlike every other Admin-only key in this codebase (the various
   `Reports.*.View` keys), which are Admin-only purely for PAN/identity/flat-fact-table exposure
   reasons, not a security-model contradiction.

7. **A small additive change to `ListOrganizationMembersQuery`'s `OrganizationMemberDto`** —
   `MembershipId` and `RoleId` were added alongside the existing `UserId`/`FullName`/`Email`/
   `RoleName` fields. Necessary supporting infrastructure for the Role Reference page's Members
   section (role reassignment targets a membership row via `UpdateMembershipRoleCommand`, not a
   bare `UserId`), the same "necessary supporting infrastructure, gated on the feature's own
   existing key rather than minting a new one" call Phase 13 made for this same query when it was
   first added. Its only other consumer (the Task feature's Assigned-To picker) is unaffected — a
   purely additive DTO change.

## Command/query surface

`Application.Tenancy`:
- `CreateRoleCommand`/`UpdateRoleCommand`/`DeleteRoleCommand` (Commands/{Create,Update,Delete}Role)
- `ListRolesQuery` (Queries/ListRoles)
- `GetRolePermissionMatrixQuery` (Queries/GetRolePermissionMatrix)
- `UpdateRolePermissionsCommand` (Commands/UpdateRolePermissions)
- `UpdateMembershipRoleCommand` (Commands/UpdateMembershipRole)
- `InviteUserCommand` rewritten in place (Commands/InviteUser) — `MembershipRole Role` → `Guid RoleId`

`Application.Common.Security`:
- `PermissionKeyCatalog` — reflection-based key enumeration (scope decision #3)
- `PermissionKeys.RoleView`/`RoleManage` — new Admin-only keys

Api (`OrganizationEndpoints.cs`), all under `/api/organizations/{organizationId}`:
- `GET|POST /roles`, `PUT|DELETE /roles/{id}`
- `GET|PUT /roles/{id}/permissions`
- `PUT /memberships/{membershipId}/role`
- `POST /invitations` — request body changed from `{ email, role }` to `{ email, roleId }`

No new schema tables — one migration (`AddPhase14RoleReference`) adding `Roles.OrganizationId`
(nullable `uniqueidentifier`), a filtered unique index `(OrganizationId, Name) WHERE OrganizationId
IS NOT NULL`, and the `Tenancy.Role.View`/`Manage` permission-seed rows for the two system roles.

## Angular

- `features/tenancy/role-list-page` — the Role Reference list: system rows (read-only, "System"
  badge, no Edit/Delete) plus this org's own custom roles (inline create/edit form, same chrome as
  `credit-term-list-page`'s established Configuration-lookup CRUD pattern, delete with a confirm
  step), plus a **Members** section (added beyond the brief's named page list, see scope decision
  #7) listing every Accepted member with an inline Role-reassignment `<select>` per row.
- `features/tenancy/role-permission-matrix-page` — every `PermissionKeyCatalog` key as a checkbox,
  grouped into cards by module, with per-group "All"/"None" bulk-toggle links and a single Save.
  Read-only (every checkbox disabled, Save hidden) when `isSystemRole`.
- `organization-dashboard-page`'s invite form: role `<select>` now populated from `ListRolesQuery`
  (defaulting to the system Member role once loaded), with a link to Role Reference for the full
  permission set.
- New routes: `organizations/:id/roles`, `organizations/:id/roles/:roleId/permissions`.

## Bugs hit and fixed

No codebase defects hit this phase — a clean build on the first `dotnet build` after each of the
Application/Api layers landed. Two environment/tooling snags during manual E2E, neither a shipped-
code bug:

1. **Registering a user does not itself create a `VerificationCode` row** — `RegisterUserCommand`
   only creates the `User`; the verification code is created by a separate `POST
   /api/auth/request-verification-code` call, which must be issued before the code can be read from
   `[identity].[VerificationCodes]` via `sqlcmd`. Assumed (incorrectly, from a stale memory of an
   earlier phase's script) that registration alone would populate the table — the first `sqlcmd`
   query correctly returned zero rows, which was the tell. Worth remembering for the next phase's
   manual E2E script: always call `request-verification-code` explicitly, don't assume it fires on
   register.
2. A background `dotnet run` process started via a raw `nohup ... &` inside the Bash tool exited
   immediately once the wrapping shell command returned (nohup detaching from a already-ephemeral
   subshell isn't equivalent to a persistent background process here) — recognized via a `[health]`
   probe failing and the process no longer listening on its port, and fixed by restarting the API
   through the Bash tool's own `run_in_background: true` parameter instead, which keeps the process
   alive independently of the invoking call's return. Worth remembering: always prefer the tool's
   native background flag over manual `nohup`/`&` for a long-running dev server in this environment.

## Manual E2E

Confirmed by hand end-to-end against the real API/DB/browser, seeded via curl + a cookie jar per
this session's own memory note (reserve browser clicks for this phase's own new UI): a fresh Admin
registered/verified/logged in and created an Organization, then created a custom "Sales Rep" role
via `POST /roles` and granted it exactly `Sales.Quotation.{View,Create,Edit,Approve}` +
`Sales.Invoice.View` via `PUT /roles/{id}/permissions` (confirmed via `GET /roles/{id}/permissions`
that every other key defaulted to `false`). Invited a second user under that role, who
registered/verified/logged in and accepted the invitation. As that user: created a Quotation
successfully (`201`, proving `Sales.Quotation.Create` works), listed Invoices successfully (`200`,
proving `Sales.Invoice.View` works), then hit a real `403` naming `Purchasing.PurchaseBill.Approve`
attempting to approve a PurchaseBill and a real `403` naming `Sales.Invoice.Edit` attempting to edit
an Invoice — both denials fired from `AuthorizationBehavior` before either handler ever ran (proven
by both requests targeting a nonexistent document id and still getting `403`, not `404`) — the
actual proof this phase exists for, closing the exact gap Phase 12's own manual E2E flagged.

Then drove the real Angular UI in the Browser tool: logged in as the Admin, opened Role Reference,
confirmed the Sales Rep role's checkbox grid showed exactly the five granted keys checked (matching
the curl-driven state exactly) and every other key across all 12 module cards unchecked; the
Members section correctly showed both users with a role-reassignment dropdown, the invited user's
dropdown correctly pre-selected on "Sales Rep". Toggled `Sales.Quotation.Edit` off and
`Sales.Invoice.Create` on, clicked Save, saw "Permissions saved.", and confirmed the round-trip two
ways: directly against `[tenancy].[RolePermissions]` via `sqlcmd` (both rows' `IsGranted` flipped
exactly as toggled, nothing else touched), and by re-running the same two curl calls as the invited
user — editing the Quotation now correctly 403'd naming `Sales.Quotation.Edit`, and creating an
Invoice now passed the permission gate entirely (reaching the handler and 404'ing on a deliberately
fake `WarehouseId`, proving the *authorization* check itself passed rather than the request merely
being malformed).
