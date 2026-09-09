# Phase 32b — Per-location permission scope (FR-3.3, `architecture-spec.md` §3.7)

## TL;DR

The role editor now has the **two sections the live product has**: *Organization-wide Permissions*
("Apply across all billing locations") and *Location-specific Permissions* ("Scoped to individual
billing locations"), the second being the **transaction keys alone** replicated per location — 77 of
this codebase's 222 keys, derived from `DocumentMechanisms.LocationBearing` rather than listed.
A location-scoped grant is a `RolePermission` row carrying a **`LocationId` FK**, where **null is the
organization-wide grant**, so the migration needs no backfill and nothing is seeded per location.

Enforcement is **one extra branch inside `AuthorizationBehavior`**, not a sixth pipeline stage: when
the organization-wide check fails and the key is location-scopable, the caller gets a second chance
against the location the request actually touches. That is phase-27a's `AttachmentAccess` pattern for
the third time, promoted from a per-handler re-check to the pipeline because this instance spans
~120 requests rather than one. Four marker interfaces say what each request is, and
`LocationScopeSweepGuardTests` fails the build if a request with a location-scopable key declares
none of them.

**The phase's defining event is that the confirm-live pass falsified the design that was already
written down.** Four documents — `roadmap.md`'s 32b entry, `phase-32-status.md`'s Decision A,
`erp-module-scan.md`'s 2026-09-07 appendix, and `TenantSettings.LocationWiseReportPermission`'s own
doc comment — all recorded that turning that toggle on is "what would pull the 52 Reports keys into
location scope". It is not. With the toggle **on** and persisted across a hard reload, the live
matrix stayed at **0 of 282 = 94 × 3, with no Reports group anywhere**. The toggle scopes report
*rows*, not report *keys*, and its consumer shipped here on that basis.

Tests: Domain 443, Application.UnitTests 893 (+14), Angular 212; `dotnet build`, `ng build`,
`ng test` clean. Manual E2E: 54 steps, every status code printed, both directions of the negative
path proved on the same user.

---

## The confirm-live pass (2026-09-09, `cadehi.tigg.app`)

Read-only except **one** reversible tenant setting, flipped with the user's explicit permission and
reverted and verified afterwards. Written up in full in `erp-module-scan.md`'s appendix.

### 1. The two sections are independent stores *(observed)*

Role **Sales**, `#/config/user-permission/role-reference/{id}/edit`:

| Section | Count |
|---|---|
| Organization-wide — *"Apply across all billing locations"* | 43 of 175 (General 5/20, **Transactions 25/94**, Settings 1/9, Reports 12/52) |
| Location-specific — *"Scoped to individual billing locations"* | **0 of 282** (HeadOffice 0/94, POS Restaurant 0/94, POS Retail 0/94) |

25 organization-wide transaction grants coexisting with 0 location grants settles the open question
the roadmap raised: **a location-scoped grant does not default to, mirror, or inherit the
organization-wide one.** Combined with that section's own subtitle, the effective rule is
organization-wide **OR** location-specific — never AND, which would have broken every existing role
the moment a tenant enabled locations.

HeadOffice's 94 expands to Sales 25 / Purchase 25 / Accounting 24 / Inventory 20, and Sales 25 to
five documents × five actions (Quotation, Sales Order, Invoice, Credit Note, **Customer Payment** ×
View/Create/Edit/Approve/Void).

### 2. `LocationWiseReportPermission` does **not** add Reports to the matrix *(the correction)*

Procedure: `Organization > Features > Billing Location > Advanced`, ticked *Implement Location Wise
Permission for Report View* (it saves inline — the card has no Save button), hard-reloaded the whole
app, re-opened the Advanced panel and confirmed `checked: true` had persisted. Re-opened the same
role: Location-specific still **0 of 282**, still three locations at 94 each, **still no Reports
group**. The Users screen gained nothing either. Then unticked, reloaded, verified `checked: false`.

So the recorded hypothesis is false, and the label is the whole of it: *"Restrict users to view
reports only for locations they have access to."* The toggle narrows report **rows** to the locations
a role holds location-specific grants at. There is no per-location Reports matrix, which is also why
this phase adds **no new permission keys at all**.

### 3. Not observable without a write *(derived, and recorded as such)*

What the editor does when a location is **added or deactivated**. All three locations were active,
there was no inactive row to compare against, and creating one would leave a permanent row on the
user's tenant (the live product deactivates, never deletes). The storage decision makes it a
non-question: grants default to absent, sections are named after the active location rows, so a new
location is a new all-off section that writes zero rows and a deactivated one drops out of the editor
while its rows survive. Recorded here rather than guessed at in code.

---

## Scope decisions

### Decision A — a `RolePermission.LocationId` FK, not a prefixed key string

`architecture-spec.md` §3.7 guessed `"HeadOffice.Sales.Invoice.Approve"`, and the live editor
confirms that *display* shape. It is not the storage shape, for the reason phase 32 already settled
for documents: **a location code is renameable.** Persisting it inside the key string would orphan
every grant the first time an Admin edits a code, silently and with no error. The prefixed string
survives as a rendering — `LocationScopedPermissions.Describe` — used in the 403 message and the
checkbox label, and never written.

Three consequences worth stating:

- **`null` is the organization-wide grant**, so every row written before this phase is already
  correct and the migration has **no backfill**;
- the unique index becomes `(RoleId, PermissionKey, LocationId)` and is deliberately **unfiltered** —
  see Migration below;
- **the roadmap's seed-size warning dissolves.** It flagged N × 94 as "the first permission set whose
  row count is a function of tenant data". It is not: an absent row is a denial, the live default is
  0 of 94 at every location, so only *granted* rows exist and a fresh tenant writes none.

### Decision B — one extra branch in `AuthorizationBehavior`, plus four markers

This is the `AttachmentAccess` pattern's third instance (27a, 31, here), and three is the usual bar
for a shared helper — but this instance differs **in kind**, not just in count. 27a and 31 were one
handler each. This one is ~120 requests, and a single missed re-check is an open door rather than a
bug. A per-handler re-check was therefore never viable.

The check lives **inside `AuthorizationBehavior`** rather than in a sixth pipeline stage, because the
location half needs to know whether the organization-wide half already passed, and passing that
between two behaviors means a scoped context object that a nested `ISender.Send` would corrupt.
Keeping both halves in one method also makes it impossible to add a request that clears the first
gate and skips the second.

Two changes there, both load-bearing:

1. the grant join now requires **`LocationId == null`**. Without it a single-branch grant would
   satisfy the organization-wide check — the exact escalation this phase exists to prevent. Every
   organization-wide read in `GrantedPermissionReader` gained the same filter;
2. a failed organization-wide check falls through to `EnsureGrantedByLocationAsync`, which resolves
   the locations the request touches and compares them against the caller's.

**Decision D falls out of the ordering.** The organization-wide check short-circuits first, so an
Admin — or any role on any tenant that never opens the second section — pays **not one extra query**.
A single-location tenant additionally never sees the second section at all: the matrix query returns
it empty when the tenant has ≤ 1 active location.

Four marker interfaces, and the guard test requires exactly one of them on any request whose key is
location-scopable:

| Marker | Who | What the pipeline does |
|---|---|---|
| `ILocationBearingCommand` *(phase 32, reused)* | the 17 Create + 15 Update commands | checks the location the request **writes** |
| `ILocationScopedDocument` *(new)* | 15 Update + 15 Get + 5 conversion templates + 11 polymorphic (attachments, comments, custom fields, tags, status, print, activities) | checks the location of the **row targeted** |
| `ILocationFilteredQuery` *(new)* | 18 list queries | lets the caller through; the handler **narrows the rows** |
| `ILocationAgnosticRequest` *(new)* | 5 `Preview*GlPosting`, `GetBomTemplateQuery`, `GetInboxDocumentPrefillQuery` | no row and no location — any single grant suffices |

An **Update implements two of them**, and that is the point: the row it edits and the location it
writes are different places to be allowed. Without the second, a HeadOffice-scoped clerk could take a
POS invoice and rewrite it to HeadOffice.

**The thirty Approve/Void commands needed no edit at all.** They already declare
`ILockDateSensitiveDocument`, which names a document by `(type, id)` in exactly the shape needed, so
`LocationScopeResolver` *reads* that marker when the location one is absent. This is reading an
existing marker, not merging the two — the sets genuinely differ, because a lock date freezes writes
while a location grant also governs reads — and a guard test pins that the reuse still covers them.

**The document's type is derived from the permission key**, not declared per request:
`Sales.Invoice.Approve` names Invoice, and `LocationScopedPermissions.DocumentTypeOf` is the one
place that reads it. Declaring it as well would be two values that must agree forever, and it would
force a nullable type onto the polymorphic requests whose parent may be a Contact. One request
overrides it — see Bugs below.

### Decision C — `LocationWiseReportPermission` ships its consumer, and it is not what was recorded

The confirm-live pass (§2 above) replaced the recorded design. The toggle narrows report **rows**,
so its consumer is `LocationAccessScope.ForReportsAsync`, wired into the **Sales Master Report** —
which is the only report in the codebase with a location dimension at all (phase 32 wired the
location filter there and on the Invoice list, and carried the rest).

That is the honest boundary and it is stated on the field rather than left to be discovered: the
toggle now does exactly what its label promises, on every report that *has* a location to restrict,
and gaining more reports is phase 32's carried item #4 rather than a second mechanism. Enforcing it
on reports that carry no location would have been a lie in the other direction — the phase-31
lesson (a) trap, inverted.

It returns `null` (unrestricted) both when the toggle is off and when the role holds no
location-specific grant at all, because an organization-wide role is not what the setting is meant to
blind.

---

## Migration

`20260909124550_Phase32bRolePermissionLocationScope` — one nullable column, an FK to
`tenancy.BillingLocations` with `Restrict`, and the index swap.

**Hand-trimmed.** `dotnet ef migrations add` emitted an
`UpdateData(..., column: "LocationId", value: null)` for all ~440 seeded `RolePermission` rows —
3,500 lines setting a just-created nullable column to the null it already held. CLAUDE.md's "read any
migration that replaces or retypes a column and reorder by hand" rule, applied to one that merely
*adds* one (phase-31 lesson (e)). The trimmed file is 110 lines and says why.

**`HasFilter(null)` is load-bearing, and overrides both EF's convention and CLAUDE.md's own standing
gotcha.** The unique index `(RoleId, PermissionKey, LocationId)` carries no
`WHERE [LocationId] IS NOT NULL`, because NULL here is not "no value" but the sentinel for *the*
organization-wide grant, of which there must be at most one per (role, key). SQL Server treats NULLs
as equal in a unique index, so the unfiltered form is the enforcement. **This is phase 32's numbering
counter inverting the same gotcha for the second time** — which is why the rule now reads "ask what a
NULL in that column *means* before applying the filter" rather than "always filter".

The swap cannot fail on live data: with every existing row's `LocationId` null, the new index
constrains exactly the rows the old `(RoleId, PermissionKey)` one did.

---

## Bugs found, and what found them

### 1. `ApplyPaymentAllocationCommand` would have read a Journal Voucher out of the Payments table

Found by `LocationScopeSweepGuardTests.The_lock_date_document_type_agrees_with_the_type_derived_from_the_permission_key`, on its first run — not by review.

The command always rides `Payments.Payment.Edit`, but the credit it applies can be a
**JournalVoucher line**, and its `LockDateDocumentType` says so. Deriving the target type from the
permission key would therefore have looked a Journal Voucher id up in `Payments`, found nothing,
concluded "row missing", and let the request through unchecked.

Fix: `ILocationScopedDocument.LocationDocumentTypeOverride`, a default-implemented member returning
null that only this one request overrides — and a second guard test asserting it stays the only one.
The general rule survives (one statement of the type, derived from the key) with its single exception
named and pinned.

### 2. Three requests were reachable with a location-scopable key and no marker

Same test, same run. `GetInboxDocumentPrefillQuery` rides a document **Create** key while its own id
names an `InboxDocument` — so it is `ILocationAgnosticRequest`, like `GetBomTemplateQuery`.
`ListLookupsQuery<T>` and `DeleteLookupCommand<T>` are open generics whose key comes from
`LookupPermissionKeys` (every arm a `Configuration.*` lookup, never scopable); they are named in an
`ExemptOpenGenerics` dictionary **with the reason**, following `DocumentMechanisms`'
`NotApplicableReasons` idiom, rather than filtered out by a predicate that would also swallow a
future request.

---

## Manual E2E

`scratchpad/e2e_phase32b.py`, 54 steps against real SQL Server, reusing phase 32's harness. Every
status code printed. A **fresh Organization** (phase baseline discipline), master data seeded through
the API, browser clicks reserved for the phase's own new UI.

The negative path is proved in **both directions on the same user** (phase-31 rule (d)) — a custom
role holding `Sales.Invoice.View`/`.Approve` **at HeadOffice only**, carried by a real Member:

| # | Step | Result |
|---|---|---|
| 46 | open the HeadOffice invoice | **200** |
| 47 | open the BR1 invoice | **403**, message names `BR1.Sales.Invoice.View` |
| 48 | open a nonexistent invoice id | **404** — the leg that shows the clerk holds the pipeline key |
| 49 | approve the BR1 draft | **403**, names `BR1.Sales.Invoice.Approve` (an Approve command, scoped through its lock-date target) |
| 50 | list invoices | **200**, **1 of 3** rows — a list is narrowed, not refused |
| 52–53 | after adding the org-wide `Sales.Invoice.View` | BR1 invoice **200**, list **3 of 3** |
| 54 | approve the BR1 draft again | **403** — the two sections are independent stores |

Also proved: the matrix returns **222 organization-wide keys and 77 per location × 2 locations**; a
`Reports.*` key posted under a location is a **400** naming it rather than a stored row nothing would
read; and `sqlcmd` confirms two `RolePermissions` rows with a non-null `LocationId`.

---

## Carried items

1. **Reports other than the Sales Master Report do not restrict by location**, because they have no
   location dimension to restrict — phase 32's carried item #4 (the location filter reaches only the
   Invoice list and Sales Master Report) is now this feature's blocker too, and closing that one
   closes this one for free.
2. **The two opening-balance lists narrow their joined figures, not their rows.** They enumerate
   accounts and products with the opening line left-joined, so a location-scoped caller sees the whole
   chart of accounts and only their own locations' balances. That is the right shape for those
   screens, but it means the LOCATION dimension there is a value rather than a row filter.
3. **A grant at a deactivated location is kept, not revoked** — deliberate (reactivating restores it),
   but the editor simply stops showing it, so an Admin cannot see or clear it from the UI.
4. **No per-user location assignment.** The live tenant's Users screen has no location column and the
   role carries the scope, which is what this phase implements; if a later reading finds one, it is a
   second mechanism, not an extension of this one.
5. **`LocationDocumentTypeOverride` has one user.** If a second appears, the "derive the type from the
   key" rule is worth re-examining rather than adding a third override.
