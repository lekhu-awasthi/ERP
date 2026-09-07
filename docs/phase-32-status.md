# Phase 32 — Billing Locations (FR-2.3, FR-3.3)

## TL;DR

**The phase that could not be confirm-lived, was.** The roadmap scoped phase 32 as derived work,
because Billing Location is an entitlement that is **off** on the Moonbeam UAT tenant every earlier
phase read: no location-enabled screen could be opened, and phase-21c's "derive when confirm-live is
impossible" precedent was expected to govern the whole phase. **It does not apply.** The user
supplied a second tenant, `cadehi.tigg.app`, with `Location Enabled = Yes`, and every location
surface in this phase is **observed, not derived** (read-only pass, 2026-09-07, written up in
`erp-module-scan.md`'s new appendix).

Shipped: a **`BillingLocation`** aggregate under Tenancy with the Organization > Features card behind
it, a **HeadOffice row seeded unconditionally at Organization creation**, `MultipleLocations` as a
**cap at one** (phase-20f Decision #4's shape for the third time), a nullable **`LocationId` on all
17 location-bearing types** in one hand-written migration, the **Advanced panel** that answers the
user's original question, **location-wise document numbering**, and the location filter/column on the
Invoice list and the Sales Master Report.

**Split out to phase 32b:** the per-location permission matrix (agreed with the user up front, not
discovered late) and, with it, the enforcement of `LocationWiseReportPermission`.

Five things generalise, in **Lessons** below. The headline: **a blocked confirm-live is a state, not
a verdict** — ask whether another tenant exists before invoking the derive-instead precedent.

---

## The confirm-live pass that was supposed to be impossible

The roadmap's own text: *"the feature is an entitlement that is off on this tenant, so its screens
cannot be read"*, and *"otherwise the phase-21c precedent applies and the status doc says so"*. The
session opened with a ten-minute check that this was still true — and the user pointed at a second
tenant they had configured. Everything below is a reading of that tenant.

Full detail is in `erp-module-scan.md` under **"Confirm-live pass on a LOCATION-ENABLED tenant"**;
the findings that changed the design are:

1. **The Advanced panel exists, and it is what the user could not find.** `Organization > Features`'s
   Billing Location card gains a collapsed **Advanced** disclosure — *"Choose how locations should be
   used in your organization"* — holding three controls: an **Enable Location in Sales Transactions
   Only** radio badged *Default*, an **Enable Location in All Transactions** checkbox, and
   **Implement Location Wise Permission for Report View**. None of it renders on a tenant without the
   entitlement, which is why no earlier phase, and no amount of clicking on Moonbeam, could surface
   it.
2. **Location scope is a runtime tenant setting, not a fixed list.** This is the single most
   design-changing fact of the phase, and nothing in the scan predicted it. It is why `LocationId` is
   nullable on *all seventeen* types rather than on the four the default mode names — see Decision B.
3. **There is no Location Type field.** The Add New Location dialog is Location Code\*, Location
   Name\*, Address\*, Warehouse\* and Save. `LocationType` is system-assigned, correcting both
   `erp-module-scan.md`'s Features section and `architecture-spec.md`, which modelled it as if the
   tenant chose it.
4. **The picker is a document *header* control, not a form field.** A borderless select immediately
   left of Save, rendering `Name (Code)`, defaulting to `HeadOffice (HO)`, listing every active
   location including the POS ones. The roadmap had assumed a body field beside Warehouse.
5. **The role editor's second matrix has an exact shape**: two top-level sections, not N scope groups.
   *Organization-wide* is General 20 + Transactions 94 + Settings 9 + Reports 52 = **175**;
   *Location-specific* is the **Transactions group alone**, replicated per location — 94 × 3 = **282**,
   broken down identically on both sides as Sales 25 / Purchase 25 / Accounting 24 / Inventory 20.
   That is also *why* "Implement Location Wise Permission for Report View" is a separate toggle:
   turning it on is what would pull the 52 Reports keys into location scope.
6. **`LocationWiseNumbering` has a real control**: "Enable Location-wise Next Number", on the per-rule
   Document Numbering dialog, visible only with the entitlement on.

Nothing was saved on that tenant. Two dialogs were opened and dismissed, and the role editor was
navigated away from without pressing Save (verified afterwards: the role list is unchanged).

---

## Scope decisions

### Decision A — the per-location permission matrix is phase 32b *(agreed with the user)*

Put to the user before any code was written, with the shape already confirmed live. Reasons it
splits cleanly:

- it is **exactly the Transactions group replicated per location**, so it is mechanical but large,
  and nothing else in phase 32 depends on it;
- it touches `AuthorizationBehavior`, **the only mechanism verifying org membership at all**, and
  deserves its own session and its own negative-path proof;
- the reference product itself treats it as a layer on top: a separate collapsible, with its own
  opt-in cousin for reports.

The key shape it will need is already settled by the live reading and by this codebase's own
precedent: a location-scoped key is a location segment prefixed onto a **transaction** key
(`"HeadOffice.Sales.Invoice.Approve"` — `architecture-spec.md` §3.7's guess, now confirmed and
*narrowed*: only transaction keys ever carry one), and since the real key depends on the row's own
`LocationId`, it is phase-27a's `AttachmentAccess` pattern a third time — a blanket key on the
request, the real key re-checked inside the handler.

**Consequence, recorded rather than buried:** `TenantSettings.LocationWiseReportPermission` ships
**stored, editable, screen-reachable and read by no enforcer.** That is phase-31 lesson (a) held at
arm's length: the field has a command, an endpoint and a control from day one, so it is not an
*absent* feature; what it lacks is a consumer, named here, on the field itself, and in the handoff.

### Decision B — `LocationId` nullable on all 17 types, one migration *(agreed with the user)*

The alternative was "the sales four now, the rest later". The live Advanced panel kills it: an Admin
can widen the scope from Sales-only to All Transactions with one click, at any moment. If the schema
only covered the sales types, that click would be **a lie** until some later phase shipped the rest —
the switch would appear to work and silently record nothing on eleven document types.

So all 17 of `DocumentMechanisms.LocationBearing` carry a nullable `LocationId` (the 15 transactional
types plus both opening-balance kinds — identical to the `ReportingTags` list, and for a related
reason), in **one** migration with a **hand-written** seed-and-backfill. See Migration below.

### Decision C — HeadOffice is seeded unconditionally

Not gated on the entitlement, and the argument is `Currency.CreateBase`'s, verbatim: every document
defaults to a location, so a tenant with no `BillingLocation` row would have a document header
pointing at a location its own list does not contain. Seeding is also what makes `MultipleLocations`
expressible as a **cap** rather than a block — a tenant without it has exactly one location and is
capped there, and the *second* location is what the entitlement buys.

That makes this **the third instance of phase-20f Decision #4's shape** (warehouses 20f, currencies
28, locations 32). As in 28, **no document command is feature-gated**: the picker is populated from
the tenant's own list, so a one-entry list degenerates the surface to "HeadOffice, fixed" by itself.
Gating the documents too would be a second enforcement of one rule, and the one that breaks first
when the two disagree.

### Decision D — phase 32 *does* consume `LocationWiseNumbering`

The roadmap asked for this to be decided explicitly. It is consumed, because the live pass found it
is not a dormant field but a **labelled control** ("Enable Location-wise Next Number") that a tenant
can already switch on. Leaving it would have shipped a toggle that changes nothing.

The counter key gained a nullable `LocationId`: **null is the settings row** (one per org+type,
carrying Prefix/Mode/the flags, and the shared counter while the toggle is off); non-null is one
branch's own counter, created lazily on first approval from there. No backfill is needed — every
existing row already *is* the settings row — so numbering is unchanged for every tenant until an
Admin turns the toggle on. Prefix always comes from the settings row, so a per-location counter can
never drift onto a stale prefix.

### Decision E — permission keys, derived per feature

`Tenancy.BillingLocation.View` (Admin + **Member**) and `Tenancy.BillingLocation.Manage`
(**Admin only**), derived exactly as `Tenancy.Currency.*` was.

- **View to Members**, and more strongly than for currencies: this key also gates
  `GetBillingLocationSettingsQuery`, which **every document form reads** to decide whether to render
  a picker at all. A Member without it would lose the control, not just the list. The data is
  Code/Name/Address/Warehouse — no PAN, no contact identity, no per-transaction row: the
  bounded-rollup end of the derivation rule.
- **Manage Admin-only, and one key for the whole card** including the Advanced panel, rather than a
  third `GeneralSettingsManage`-style key for two toggles. Both adding a location and widening the
  scope change what every document form in the tenant offers — the same structural-topology bar that
  makes Manage Admin-only in the first place. Phase-31 lesson (c), reuse-don't-invent, applied to a
  permission key.
- Reading a *document's own* `LocationId` needs no key: one more header field on a document the
  caller already holds the View key for, exactly as `CurrencyCode` was in 28.

### Deliberately not taken

- **Per-location permission matrix** and the enforcement of `LocationWiseReportPermission` — Decision A.
- **The picker on the other 16 document types' forms.** The schema, commands, API and resolver cover
  all 17; the *header control* is wired on the Invoice, which is the type the default scope actually
  covers and the one read live. Sales Order and Credit Note are the same three-line change and are
  named in the handoff.
- **POS location types.** Modelled (`BillingLocationType` reserves both members, so the 32b matrix can
  name a POS location) but not built — `architecture-spec.md` §6's standing deferral.
- **The location filter beyond Invoice list + Sales Master Report.** Those two are what the live pass
  actually read; extending to the other registers is mechanical and listed in the handoff.

---

## What was built

**Domain** — `BillingLocation` (+ `BillingLocationType`), `LocationScopeMode`, `DocumentLocationScope`
(the one place that turns a mode + a `DocumentType` into an answer), `TenantSettings.LocationScopeMode`
/ `.LocationWiseReportPermission` with their own mutator, `DocumentMechanisms.LocationBearing` and
`.LocationBearingSalesOnly`, `DocumentNumberingRule.LocationId`, and `LocationId` + `SetLocation` on
all 17 aggregates.

**Application** — `Create`/`Update BillingLocation`, `ListBillingLocations`,
`Get`/`UpdateBillingLocationSettings`, `ILocationBearingCommand` (32 commands) and `LocationResolver`
(the four rules all 17 handlers share), `LocationId` threaded through 22 + 10 handlers, the Invoice
list filter and the Sales Master Report's Billing Location filter + Location column.

**Infrastructure** — `BillingLocationConfiguration`, FK-with-`Restrict` on all 17 configurations,
location-aware `DocumentNumberGenerator`, two migrations.

**Api** — five endpoints under `/api/organizations/{id}/billing-location(s|-settings)`, `LocationId`
on **all 32** command constructions and on every request record behind them (audited by compiler
error, not by eye — see Bugs).

**Web** — the Billing Locations page with the Advanced panel, the Invoice header picker, the invoice
list's location badge and filter, dashboard link, feature-guarded route.

---

## The migration, and why it is read by hand

`20260907161040_Phase32BillingLocations` carries **two hand-written steps**, and their position
between `CreateTable` and the `AddForeignKey` calls is load-bearing:

1. seed one HeadOffice row per **pre-existing** Organization — the scaffold cannot know to, because
   the seeding lives in `CreateOrganizationCommandHandler` and so only ever runs for organizations
   created from now on;
2. backfill `LocationId` on all 17 tables to that organization's HeadOffice.

SQL Server validates a new FK against the rows already present, so a backfill placed *after* the
foreign keys would either fail the constraint or leave the columns null while the migration still
reported success. Verified on the dev database: **98 organizations → 98 locations, 168 invoices and
25 journal vouchers with zero nulls.**

The columns are nullable, so unlike phase 31's `DueDate` there is no NOT NULL step and no stray
default constraint. The backfill is still hand-written, because *nullable removes the crash, not the
wrong answer* — leaving history at null would make every location-filtered report silently omit it.

---

## Bugs and near-misses

**1. EF auto-filtered the numbering unique index, inverting its purpose.** `migrations add` scaffolded
`filter: "[LocationId] IS NOT NULL"` on the rebuilt `(OrganizationId, DocumentType, LocationId)`
unique index — normally correct, and exactly what CLAUDE.md's own gotcha asks for. Here it is
precisely wrong: SQL Server treating NULLs as **equal** is the invariant being bought, because it is
what admits one and only one *settings* row per (org, type). Under EF's default filter those rows
fall outside the index entirely, a tenant could acquire two settings rows and two competing counters,
and `DocumentNumberGenerator`'s lazy-create race — which relies on a concurrent loser's INSERT
violating this index — would silently stop being guarded. Caught by reading the scaffold; fixed with
`HasFilter(null)`, and verified against the live database (`has_filter = 0`).

**2. The Invoice detail DTO dropped `LocationId`, and only the E2E saw it.** The list returns the
aggregate, so the field came for free; the detail query projects an explicit DTO, and omitting it
there left the write path perfect and the header picker unable to show the stored value on an
existing invoice. No unit test failed. The E2E's *re-read the invoice* step is what caught it — the
argument for a round-trip assertion rather than a create-and-assume one.

**3. A stray `LocationId` on `BillOfMaterialsRequest`.** A bulk edit anchored on a shared record tail
hit three records where two were intended; a BOM is master data and is not in
`DocumentMechanisms.LocationBearing`. Caught by re-reading the patched file, and the reason every
bulk edit in this phase asserted its anchor count before writing.

**4. Two E2E seeding traps, now recorded.** `POST /accounts` takes `groupId`, not `accountGroupId`.
And `POST /products` takes **`type`, not `productType`** — sending the wrong name silently produces a
*Goods* product, whose line then consumes stock and 409s at Approve with a message about the
warehouse, which reads like a seeding problem rather than a typo.

---

## Lessons

**(a) A blocked confirm-live is a state, not a verdict.** The roadmap did not merely predict this
phase would be derived; it pre-authorised the precedent for doing so. Had that been taken at face
value, the phase would have shipped a body field instead of a header picker, a user-chosen
`LocationType`, a fixed scope list instead of a tenant setting, and no Advanced panel at all — four
wrong things, all confidently documented. **Before invoking "derive because we cannot observe", ask
whether a different tenant, account or environment can observe it.** The cost of asking was one
question; the cost of not asking was most of the phase's design.

**(b) A setting that changes *scope* forces the schema wider than the setting's current value.** This
is new, and it is phase-31 lesson (a) turned inside out. Phase 31's rule is that a field with no
command is an absent feature. Here the command exists and the field is one click from a value the
schema must already support — so the schema has to be sized for the **widest** setting, not the
default one. The general form: *when a setting selects among sets, the storage is the union; only
behaviour is the selection.*

**(c) When EF's convention and the codebase's gotcha agree with each other and are both wrong, say so
at the index.** The `HasFilter(null)` above is not a special case of "read your migrations" — it is a
case where the standing rule (*a unique index over a nullable column needs a filter*) would itself
have introduced the bug. A comment at the call site is not enough; the reason lives in the migration
and in `known-gotchas.md`, because the next person's instinct will be to "fix" the missing filter.

**(d) Two lists that must not drift belong in the file that exists to stop drift.**
`DocumentLocationScope` began with its own copies of the 17 and the 3. They moved into
`DocumentMechanisms` — phase 27a's single source of truth — so that
`DocumentMechanismSweepGuardTests` fails the build when a later phase adds a `DocumentType` without
deciding whether it carries a location. The guard that already exists is worth more than the guard
you write.

**(e) A sweep across 32 commands is safe only if every bulk edit asserts its own anchor count.** Every
script in this phase counted its anchor and refused to write on anything but the expected number
(and preserved CRLF and BOM per file). That caught bug 3 and would have caught worse. The
counter-discipline to CLAUDE.md's `sed`-over-a-glob warning: not "never script", but "never script
without a pre-flight count".

---

## Verification

**Tests:** Domain 443 (+45), Application.UnitTests 879 (+49), Angular 212 (+5), Api.IntegrationTests
18 (unchanged). `dotnet build`, `dotnet test`, `ng build`, `ng test` all clean. (`tsc --noEmit` does
not cover `web/src/app`; `ng build` is the check that does.)

**Manual E2E** — two scripts, every status code printed, against a real SQL Server:

*Part 1, 45 steps.* Fresh organization with `multipleLocations: true` → the seeded HeadOffice is
present and typed `HeadOffice`; the Advanced settings default to `SalesTransactionsOnly` with
`Invoice` in and `PurchaseBill` out of the bearing set; a second location is created (201) and a
duplicate code refused (409); HeadOffice renames (204) but refuses to deactivate (409); widening the
scope returns all 17 bearing types and narrowing restores 3; then a full chart of accounts, contact,
service product and warehouse are seeded, an **Invoice is raised at the branch**, re-read (the stored
location round-trips), approved, and both the **list filter** (branch 1 / HeadOffice 0) and the
**Sales Master Report** filter return it with `locationName = "Pokhara Branch"`. A second
organization with the entitlement **off** still gets its seeded HeadOffice (the cap, not a block) and
is refused both a second location and the Advanced panel with a 403 naming the feature.

*Part 2, 10 steps — the permission negative path.* A Member user registered, verified via
`[identity].VerificationCodes` (brackets: `identity` is a reserved word), invited, and **accepted**
(`/api/organizations/memberships/{id}/accept-invitation`, no org segment). Proved in **both**
directions on the same user: `GET /billing-locations` → **200** (Member holds
`Tenancy.BillingLocation.View`), `POST /billing-locations` → **403 naming
`Tenancy.BillingLocation.Manage`**, and `PUT /billing-location-settings` → **403 naming the same
key**.

**Browser pass** (Angular dev server over the `erp-web-ssl` profile, auth cookie transplanted per
phase 25 — no credentials typed anywhere): the Billing Locations page renders both locations and the
Advanced panel with the three live-worded controls; toggling to **All Transactions** updates the
resolved list to all 17 types live and the radios are mutually exclusive (the reference product's own
DOM has both checked simultaneously — a bug there we did not copy); the **Invoice header picker**
renders `Head Office (HO)` in the page header left of the actions, with both locations in its
dropdown and the HeadOffice row selected by identity rather than by sort order; the invoice list
shows the **Pokhara Branch** badge and its filter empties the list when switched to HeadOffice.

---

## Carried items

1. The **per-location permission matrix**, and with it the enforcement of
   `LocationWiseReportPermission` — phase 32b (Decision A).
2. The header **picker on Sales Order and Credit Note** (the other two types in the default scope),
   and on the eleven All-Transactions types. Schema, commands, API and resolver already cover them.
3. **Quotation's membership of the sales-only scope is unresolved.** The live label names Invoice,
   Sales Order, POS and Credit Note; the confirm-live tenant runs in All-Transactions mode, so its
   Quotation form proves nothing about the narrow mode. Deliberately excluded rather than guessed
   (phase-30's "a list sampled from a few screens becomes a wrong list"). One line plus a test if a
   later reading settles it.
4. **The location filter on the remaining registers and Master reports** — only the Invoice list and
   Sales Master Report were wired, matching what was read live.
5. **No Delete for a location** — the live list deactivates, and the FK is `Restrict`. Intentional,
   but there is no UI for reactivating from the list beyond the Edit form's Active checkbox.
6. **`BillingLocation.WarehouseId` is not used by anything yet** — it is captured and displayed, but
   no document defaults its Warehouse from the location's. The live product's relationship between
   the two was not probed.
7. The **POS location types** stay modelled and unbuilt.
