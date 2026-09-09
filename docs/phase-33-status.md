# Phase 33 — Platform chrome: global search, History, Quick Links

## TL;DR

The top bar this app never had: a **global search** (Ctrl + /) in the shell on every
in-organization screen, a **History** popover of recently visited screens, and the personalisable
**Quick Links** tray phase 23 recorded as "still not built". Behind all of it, the phase's real
deliverable: **`UserPreference`, the per-user server store**, decided once — a row per
`(OrganizationId, UserId, Key)` with an opaque JSON value.

**The confirm-live pass overturned three of the four things that were written down.**

1. **History is client-only** — `localStorage["history"]` in the reference product, written on
   navigation with **no request at all**. Decision C was settled by observation rather than by
   costing a write-per-document-open.
2. **History is a list of *screens*, one per module — not "recently opened records"**, which is what
   both the roadmap and the module scan said. It stores a record-detail *url* but derives the label
   from the route, so a contact detail page still renders as "CRM · Contacts".
3. **Global search is half a command palette.** Its navigation half (screens, and `Add X` create
   actions) is usually the majority of a result set, and the roadmap's "contacts, products and
   document numbers" omitted both it and a whole `collection` (Accounts).
4. Quick Links *is* server-stored, which is the one thing the scan had right — and it is what makes
   the per-user store worth building at all.

Search is permission-filtered per collection and **location-filtered per document**, reusing
`LocationAccessScope.ForKeyAsync`. Building it surfaced a **real pre-existing defect**:
`TransactionApprovalQueryHandler` and `RecentTransactionsQueryHandler` still carried pre-32b inlined
permission joins with no `LocationId == null` filter, so a branch-scoped grant read as an
organization-wide one in both. Fixed here.

Tests: Domain 443, Application.UnitTests 921 (+28), Api.IntegrationTests 18, Angular 225 (+13).
E2E: 63 steps, every status code printed, both directions of the permission proof corroborated by
`sqlcmd`.

---

## The confirm-live pass (2026-09-09, both tenants)

Read on **`cadehi.tigg.app`** (Billing Location enabled, almost no data) and
**`moonbeamtradingandsuppliers.tigguat.com/erp/`** (years of real data). Read-only on moonbeam; on
cadehi one Quick Link was added and removed again, leaving the tenant exactly as found. Written up
in full in `erp-module-scan.md`'s 2026-09-09 appendix; the findings that changed the design:

### 1. Global search returns two shapes from one endpoint

`GET /search?namespace=&search_phrase=`, capped at **10**, one flat array of either
`nav_type: "UI"` (a screen: `app_id`, `name`, `type ∈ {APP,LIST,ADD,DETAIL}`, `url`) or
`nav_type: "SERVER"` (a stored row: `collection`, `id`, `code`).

The `SERVER` half splits again, and that split is the whole design:

- **Master data** — `Contact`, `Product`, `Account` — carries a **`name`**, and is matched on name
  *or* code (`0001` found the customer coded `0001`; `2026` found the product *Test 2026*).
- **Documents** — `Journal Voucher`, `Purchase Bill`, … — carries **no name at all**, only `code`
  and a `source` naming the type. So **a document is matched on its number and nothing else**:
  searching a customer's name does not surface that customer's invoices.

Typing `INV` on a tenant full of invoices returned ten rows, *all* navigation. No permission or
entitlement filtering was visible on that half — moonbeam offered Delivery Notes and Goods Received
Notes, cadehi offered Marketplace, on tenants that have never used them.

### 2. History is `localStorage`, and it lists screens

```json
[{"app_id":"crm","subdomain_id":"contacts","title":"","url":"/crm/contacts/60847df1-…/overview"}]
```

Observed by walking seven screens and reading the key after each: most-recent-first, written on
**leaving** a screen, **deduplicated by `app_id`** (Tigg Subscriptions then Users & Permissions
leaves one `config` entry; Sales, CRM, Inventory and Accounting coexist), `title` always empty so
the popover's labels are derived client-side. Opening it makes no request. Navigating makes no
request either.

### 3. Quick Links is server-stored, whole-list replace, screens only

`GET`/`POST /quick-links?namespace=`, body `{"items":[{name, app_id, type, url}]}` — the search
endpoint's UI row minus the discriminator, i.e. **one shared vocabulary of navigation targets**.
Nothing is sent until *Done*. The picker ("Search For Quick Links") offers list and add screens
only: **a record can never be a Quick Link.** The tray is drag-reorderable and the order is what
persists. No cap at six — moonbeam holds nine; cadehi's six look like a seeded default.

**Per-user vs per-tenant was not observable** and is recorded as such: the endpoint takes only a
namespace, identity comes from the JWT, and there is one account per tenant. See Decision A.

### 4. Tigg Subscriptions is already built here

`#/config/tigg-subscription`: Plan `Standard ( 0 Txn, 0 Products)`, Subscription Amount `0.00`,
Expiry Date, then Location Enabled / Warehouse Enabled / IRD Verified / IRD Sync Enabled. No
controls at all. See Decision D.

---

## Scope decisions

### Decision A — one `UserPreference` table, **a row per key**, with a JSON value

Not "one table" versus "a JSON blob per user" as the question was posed, but a third answer that the
framing hides: **a row per `(OrganizationId, UserId, Key)`**, each holding that one setting's JSON.

*Why a store at all, now.* Phase 23 Decision C weighed a table, a configuration, a command, a query
and permission plumbing against one boolean and chose `localStorage`. That was right for one
boolean. It is not right for Quick Links, which the reference product keeps server-side — a shortcut
tray that vanished on another machine would be worse than none. So the cost phase 23 declined is
paid regardless, and every later per-user setting rides it free. **Phase 23's decision was this
phase's premise, and it did not have to be revisited to be reversed** — its own doc named this
service as "the single seam to move behind an endpoint if that changes".

*Why not one blob per user.* A single JSON document per user makes every write a read-modify-write
of the same row, so saving the tray in one tab and flipping the calendar in another silently
discards one of them. A row per key makes each setting independently writable — "last writer wins"
should mean last writer *of that setting*. `UserPreferenceTests` pins it.

*Why the value is opaque JSON.* The two day-one consumers could not be less alike (an ordered list
of navigation targets; a two-valued enum) and the vocabulary will grow. Typed columns would make
every new preference a migration; a JSON string makes it a constant in `UserPreferenceKeys` plus a
validator rule. The Domain knows nothing about what a key means — all shape checking is in
`SetUserPreferenceCommandValidator`, which is also where the **closed vocabulary** is enforced, so
the table cannot become a place for any client to write anything.

*Day-one consumers: two, not three.* The kickoff named three (Quick Links, History, the calendar
boolean). **History dropped out on observation** — see Decision C.

*One security consequence worth naming.* A stored url that a client later navigates to is an
**open-redirect surface** if it may be absolute. The validator requires a single-slash application
path — no scheme, no `//host`, no backslash — and the E2E proves the 400.

### Decision B — yes, search respects permissions; and after 32b, locations

Three layers, none of which is the request's own key:

- `Platform.GlobalSearch.View` is a **blanket key** in exactly the sense `TransactionApprovalView`
  (12) and `RecentTransactionView` (23) are — its job is that `AuthorizationBehavior`, the only
  thing verifying org membership at all, runs. Granting it alone finds nothing.
- **Per collection**, the handler re-derives the caller's own `*.View` key: `Contacts.Contact.View`,
  `Catalog.Product.View`, `Accounting.Account.View`, and per document type
  `DocumentPermissions.ViewPermissionFor`.
- **Per location**, documents are narrowed through `LocationAccessScope.ForKeyAsync` — the existing
  seam, reused rather than duplicated, exactly as the kickoff required. A row carrying **no**
  location stays hidden from a location-restricted caller, matching
  `AuthorizationBehavior`'s own `LocationScopeOutcome.NoLocation` branch: there is nothing a
  location grant can cover.

The document set is `DocumentMechanisms.Transactional` — the codebase's own rule for "has a number
and a detail page", which is precisely what a search-by-number can be about. Phase-30's lesson
taken deliberately: the rule, not a sample, with `GlobalSearchSweepGuardTests` failing the build if
the handler's switch drifts from it.

**This is stricter than the reference product**, whose navigation half offers screens for features
the tenant has never used. Offering a link to a 403 is worse than omitting it.

### Decision C — History is client-only, and it was observed, not costed

The reference product writes nothing server-side when a document is opened. The cost argument (a
write on every navigation) pointed the same way, but it did not have to be made — which is the
better outcome, because a cost argument can be wrong and an observation of the shipped product
cannot.

Two consequences follow from *what* it stores rather than from where. It deduplicates **by area**,
which bounds the list to the number of modules and needs no length cap. And it labels a record url
with its **list screen** ("Sales · Invoices"), never the record's name — copied deliberately,
because it is what makes a history of screens coherent rather than a half-hearted history of
records.

### Decision D — Tigg Subscriptions is **carried**, and most of it already shipped

`organizations/:id/features` (phase 20f, plus 31's Renew) already carries the plan, the expiry and
seven entitlement flags. What the live screen has that it does not: **Subscription Amount**, the two
**quotas**, and **IRD Verified**.

Adding those three is a migration for columns **nothing can write and nothing reads** — no billing
system sets an amount, no quota is enforced anywhere, and IRD e-filing is explicitly deferred past
this roadmap. That is exactly phase-31's lesson in reverse: *a tenant-level field is only reachable
if you can name the command that writes it*. Building the columns first would make the missing write
path invisible, which is the failure mode that lesson exists to prevent.

**Re-entry condition:** a billing or plan feature that actually sets an amount or enforces a quota.
Then the three columns and the four display rows are an hour's work on a screen that already exists.

### Decision E — the navigation half is client-side, and derived from the router

The reference product serves its navigation results from the server alongside the record hits. This
codebase does not, and the reasoning generalises: **the route table, which routes are feature-gated,
and which are permission-gated are all already client knowledge.** A server-side second copy is a
source of truth that drifts, which is the failure this codebase writes guard tests to prevent.

`NavigationCatalog` goes one step further and derives itself from `Router.config` at runtime, so
there is no second copy on the client either — a route added by a later phase appears in the search
*and* in the Quick Links picker with no edit, and a route deleted disappears from both. A list
route with a sibling `:id` detail route also yields an `Add` target at `<list>/new`, because this
codebase serves create and edit from one component addressed by a literal `new` (phase 3). That
mirrors the reference product's own `Invoice` / `Add Invoice` pair.

It also renders on the first keystroke with no round trip, which is what makes one- and two-letter
typing feel instant while the server half is still debouncing.

### Decision F — the chrome lives in the shell, not on the dashboards

`app.html` had been a bare `<router-outlet>` since phase 0. The bar now renders there, gated on the
url being inside an organization (everything on it is tenant-scoped, and the login and
organization-picker screens have nothing to search). Two pages would have made a "global" search
reachable from two of 130 routes, and the shell is also the only place that sees every navigation,
which History has to.

**What this deliberately is not** is the full left-nav shell. That is a re-layout of every existing
page, and NFR-6.1 — one interaction model across every screen — is phase 34's whole subject.

---

## The defect this phase found

`GrantedPermissionReader`'s own doc comment says it exists because "phase 12's
`TransactionApprovalQueryHandler` and phase 23's `RecentTransactionsQueryHandler` each inlined their
own copy of this join... so it is one method now."

**They were never switched over.** Both still carried the inlined join, and it predated phase 32b's
split, so neither filtered `LocationId == null`:

```csharp
where rolePermission.IsGranted            // ← no LocationId filter
select rolePermission.PermissionKey
```

A role granted `Sales.Invoice.View` **at HeadOffice only** therefore had that key in both handlers'
granted-key sets, and saw **every** location's rows — in the Transaction Approval queue and in the
Home dashboard's recent-activity feed. That is precisely the escalation `GrantedPermissionReader`'s
remarks describe as "the exact privilege escalation this phase exists to prevent". Both now read
through the shared method.

**The generalisable part:** extracting a shared helper is only half the job; the extraction is not
done until the copies it replaced are deleted. A doc comment claiming "it is one method now" is not
evidence — `grep` is. This was found only because the search handler was about to become the third
copy of the same pattern.

---

## What was built

**Server**

- `Domain/Identity/UserPreference` + configuration + migration `Phase33PlatformChrome` (one table,
  one unique index over the three non-nullable key columns, four permission-seed rows).
- `Application/Platform/`: `UserPreferenceKeys` (closed vocabulary), `QuickLinkDto`,
  `SetUserPreferenceCommand` (+ validator, + handler upsert), `GetUserPreferencesQuery`,
  `GlobalSearchQuery` (+ validator, + handler).
- Two permission keys, both Admin+Member: `Platform.GlobalSearch.View` (blanket),
  `Platform.UserPreference.Manage` (one key, no View/Manage pair — there is no caller who reads a
  preference without writing it, so a View key would be one nothing checks).
- `Api/Endpoints/PlatformEndpoints`: `GET /search`, `GET /preferences`, `PUT /preferences/{key}`.
- The two-handler location fix above.

**Client**

- `core/platform/`: models + `PlatformService`.
- `shared/navigation/`: `NavigationCatalog` (derived from the router), `HistoryService`
  (localStorage), `search-routes.ts` (hit → router link).
- `shared/platform/`: `GlobalSearch`, `HistoryMenu`, `QuickLinks`.
- `app.ts` / `app.html`: the shell bar, `HistoryService.start()`, and
  `DatePreferenceService.activate()`.
- `DatePreferenceService` now writes through to the new store, keeping `localStorage` as the
  synchronous cache so the first paint never flashes the wrong calendar.

**Guards**

- `GlobalSearchSweepGuardTests`: every `DocumentMechanisms.Transactional` type has a switch arm, and
  all fifteen aggregates really have the four properties the generic query reads **by string**
  through `EF.Property` — a rename would otherwise be a runtime translation failure on a keystroke.
- `navigation-catalog.spec.ts`: every in-organization route is catalogued or excluded with a stated
  reason; no catalogue url is a non-route; every entry's url is application-relative.

---

## Bugs hit while building

**1. `!scoped || (…)` inside an expression tree does not short-circuit.** The first draft folded the
location filter into one predicate with a captured `bool`. An expression tree has no short-circuit,
so EF would receive a **null list** to translate `Contains` against — throwing during translation,
and only on the path where the caller is *unrestricted*, i.e. almost everyone. Fixed by composing a
second `Where`, which is what every phase-32b list handler already does. **Neither the compiler nor
an InMemory test would have caught it.**

**2. A required signal input read in a constructor throws NG0950** — and it surfaces as *every test
of the host page* failing, with nothing in the message about the component at fault. `QuickLinks`
loads in `ngOnInit` instead.

**3. `sed -i` flipped a new test file from CRLF to LF**, after which every `\r\n`-based patch script
silently failed its anchor assertion. CLAUDE.md's existing `sed -i` gotcha is about *other* files
being rewritten; this is the same tool damaging the file it was aimed at.

---

## Carried items

1. **An Account hit has nowhere to go.** The Chart of Accounts has a list screen but no per-account
   detail page, so an account result renders without a link and says "no detail page". The reference
   product's row offers **View Ledger**, and this codebase has the report to back it
   (`reports/detail-general-ledger`) — it just does not take an account as a route parameter yet.
   Same for a Contact's ledger.
2. **Quick Links reorder is buttons, not drag.** The reference product's tray is a
   `sortable-container`. Ordering is fully persisted; only the gesture is different.
3. **Per-user vs per-tenant Quick Links is derived, not observed** — one account per tenant made the
   experiment impossible. The store is per-user here, which is the only sensible reading of a
   "personalisable" tray, but it is not an observation. Settling it needs a second user on one live
   tenant.
4. **Search has no result-kind filter and no "see all results" page.** Neither does the reference
   product; both become worth it once a tenant has enough data for the per-collection cap of 5 to
   bite.
5. **The shell bar is not the shell.** No left nav, no Create New flyout, no company switcher, no
   global date filter — the reference product's top bar carries all four. NFR-6.1 in phase 34.
6. **Tigg Subscriptions' three dead fields** (Amount, the two quotas, IRD Verified) — Decision D.

---

## E2E (63 steps, all printed)

Fresh organization seeded through the API on the phase-32b harness. Beyond the happy path:

- Master data matched on **name and code**; an invoice matched on its **number**; the customer's
  name returning **no documents** — the confirm-live behaviour, asserted.
- A one-character term → **400**.
- The per-user store: two keys stored as independent rows; an **absolute url → 400**; an unknown key
  → 400; an unknown calendar value → 400.
- **Negative #1:** a custom role without `Platform.GlobalSearch.View` → **403 naming that key**, and
  the preference store refused too.
- **Negative #2, both directions on the same Member** (phase-31 rule (d)): the clerk *finds* the
  contact and the HeadOffice invoice by number, and *does not find* the BR1 invoice — with
  `sqlcmd` confirming `sales.Invoices` holds exactly one row with that code, so the empty result is
  a refusal and not an absent row. A product they hold no key for is likewise invisible.
- The store is per user: the clerk reads 0 rows while the Admin has 2 in the same organization, and
  the clerk's own calendar choice leaves the Admin's untouched.

**Browser pass** (dev cert + `erp-web-ssl` + transplanted `erp_auth` cookie, phase-25 Step 3): the
bar renders on every in-organization screen; search returns all four collections with the Account
row correctly marked "no detail page"; clicking a contact hit and a document hit each navigate to
the right detail page; the navigation half returns `Invoices` / `Add Invoices` / `Invoice Age`;
History fills with `SALES · Invoices` and `CONTACTS · Contacts` — **screen labels for a record
url**, as designed; a Quick Link added through the picker and saved with *Done* was confirmed in
`identity.UserPreferences` by `sqlcmd`; and the calendar toggle wrote `"AD"` through to the same
table.

The strongest incidental proof: the dashboard first painted in **Bikram Sambat** in a browser whose
`localStorage` had never held that setting — the preference had come from the server row the E2E
wrote minutes earlier, which is the cross-device sync phase 23 explicitly did not have.
