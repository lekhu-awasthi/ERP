# Phase 34b — Consistency and the shell: NFR-6.1

## TL;DR

Phase 33 deferred four shell controls **by name** (its carried item #5) and NFR-6.1 asked for one
interaction model across every list, detail and entry screen. Both landed here.

| | before | after |
|---|---|---|
| left nav | none — 141 routes with no persistent navigation | on every in-organization route |
| Create New flyout | none | 4 columns, 18 shortcuts, guard-checked against the router |
| company switcher | none | tenant name + link to the organization picker |
| global date filter | none | 8 presets + Custom Range, stored per user |
| Reports | 52 routes, **no index page** | one nav leaf → a catalogue under 8 headings |
| list screens with a search box | **1** of 46 | 21 of 21 paginated ones |
| `List*Query` accepting a search term | **2** of 47 | **25**, with 9 exempt *and reasons stated* |
| lists scoped by a date range | 1 (the Cheque Register's own) | 16 document lists |
| page templates edited for the re-layout | — | **0** |

**The finding the phase turns on** is that last row. The kickoff framed Decision A as a choice between
re-laying out 130 page templates and an offcanvas overlay that leaves them alone. It is a false
dichotomy, and one grep dissolved it: **all 130 in-organization templates already open with the same
`<div class="container py-5">`**. A centred max-width container re-centres perfectly well inside a
narrower `<main>`, so a *fixed* rail plus a left inset on two shell elements puts a permanent left
nav on every screen at a cost of zero page edits. NFR-6.1's real ask and the reversible option turned
out to be the same option.

**Three defects, all found by the browser pass, none by a test.** They are listed in full below;
what they have in common is worth stating: each was a *state* defect that no unit test was positioned
to see, and each produced a screen that looked right.

1. A list showed `Last 30 days: 2026-08-11 – 2026-09-10` in its chrome while listing an invoice
   dated 2026-07-20 — the range arrives from the per-user store *after* the page has already issued
   its request, and nothing reloaded it.
2. Clearing the search box never restored the unfiltered list, because an `effect()` written to make
   clearing feel instant cancelled the pending emit instead of letting it run.
3. The Create New flyout was clipped to the rail's 240px — two of its four columns simply were not
   there — by an `overflow: hidden` on the nav.

**Confirm-live (2026-09-10, read-only).** Four behaviours were read off the reference tenant, and one
recorded claim was falsified: `erp-module-scan.md` says the global date filter "scopes dashboard
figures". Instrumenting `fetch`/`XMLHttpRequest` showed it scopes **list queries** —
`contacts?date_$gte=…`, `invoices?…`, `accounts?…`, `journal-vouchers?…` — and switching the preset
re-issued the open list immediately. That is the fourth consecutive phase to correct a description of
a control nobody had operated (32, 32b, 33, now 34b). The Create New flyout, by contrast, matched the
scan **verbatim**, which is worth recording in the same breath: the rule is that an unoperated
description is *not settled*, not that it is wrong.

Tests: Domain 443, Application.UnitTests **931 (+10)**, Api.IntegrationTests 18, Angular **271 (+25)**.
`dotnet build` / `dotnet test` / `ng build` / `ng test` all clean. E2E: a fresh organization seeded
through the API, 60 calls with every status code printed and every count asserted against an
expectation, all passing.

---

## The confirm-live pass (2026-09-10, `moonbeamtradingandsuppliers.tigguat.com`)

Read-only. The kickoff named two unread controls; a third was added once the second turned out to be
much larger than recorded.

### The left nav — four behaviours, all reproduced

| behaviour | what the live tenant does | evidence |
|---|---|---|
| accordion | **exactly one group open at a time** | opening Sales closed CRM |
| expansion state | **derived from the route, not persisted** | a hard reload on `#/sales/customers` came back with Sales open and Customers marked, and there is *no* nav key in its `localStorage` — which does hold its date filter, so the absence is meaningful |
| marking | **both levels** | `ant-menu-item-selected` on the leaf, `ant-menu-submenu-selected` on its parent |
| Reports | **a leaf, not a group** | `ant-menu-item`, opening a catalogue page at `#/reports/new` |

The third row is why this codebase's nav stores no preference and makes no request: `activeGroupOf`
is the whole mechanism. The fourth is why phase 34b had to *build* a Reports index page — 52 routes
is more nav rows than every other area combined, and the reference product had already solved it.

### Change Company is not a switcher

`<a href="https://me.tiggapp.com/erp/">` — a plain link away to an account portal. So the in-app
equivalent is a link to this app's own organization picker, not a dropdown of tenants. A control that
would have been a day's work if the scan's phrase "company switcher" had been taken at face value.

### The global date filter — the scan's claim falsified

`erp-module-scan.md` line 68: "date-range filter (global period filter that scopes dashboard
figures)". It does not scope dashboard figures. `fetch` and `XMLHttpRequest` were instrumented and
every list screen sent the range:

```
GET /api/v1/erp/contacts?date_$gte=17-07-2026&date_$lte=10-09-2026&limit=20&type=Customer
GET /api/v1/erp/invoices?date_$gte=17-07-2026&date_$lte=10-09-2026&limit=20&status=Approved
GET /api/v1/erp/accounts?date_$gte=…   GET /api/v1/erp/journal-vouchers?date_$gte=…
```

Switching the preset to "Last 7 days" re-issued the open Customers list with the new bounds inside
one round trip. Its presets, wording and order were read straight off the popover and are reproduced
exactly: Today / Last 7 / 15 / 30 / 45 / 60 days / This Fiscal Year to Date / Fiscal Year / Custom
Range. Its `localStorage` holds `TOP_DATE_FROM`, `TOP_DATE_TO`, `TOP_DATE_NAME`.

**And it is not uniform even there**: `products` and `contact-groups` receive no range at all. That
inconsistency is load-bearing for Decision D below — the honest rule is not "every list", it is
"lists over dated activity".

### Create New — the scan was exactly right

Four columns, nineteen items, read out of the live DOM and matching `erp-module-scan.md` word for
word. First time in five phases.

---

## Decision A — a fixed rail and a left inset, not a re-layout and not an overlay

**The dichotomy in the kickoff does not survive contact with the codebase.** Every one of the 130
in-organization templates opens with `<div class="container py-5">` (6 auth pages use a centred
variant, and they render outside the shell). A `.container` is a centred max-width box: put it inside
a `<main>` that starts 240px from the left and it re-centres, with its max-width still capping it.

So the shell is:

```html
<app-left-nav />                     <!-- position: fixed, 240px -->
<header class="platform-bar">…</header>
<main id="main-content" class="shell shell-with-nav">…</main>
```

and one rule in `app.scss` insets the header and `<main>` by 240px above `lg`. **Zero page templates
were edited for the layout.** (21 were edited for Decision B's chrome, which is a different change
and would have been needed either way.)

**What it costs the alternative, stated.** The overlay option was chosen against because a nav you
have to summon is not "one interaction model" — it is a second mode. The full re-layout option was
chosen against because it buys nothing this does not already have: the pages are *already* uniform,
and rewriting 130 wrappers to prove it would have been a large diff whose only effect is to make the
identical rendering arrive by a different route.

**Below `lg` it becomes a drawer**, which is not a compromise but WCAG 1.4.10 Reflow: at 320px there
is no 240px to give away, and a permanent rail would force two-dimensional scrolling on every screen.
Verified at 375px: rail off-screen at `translateX(-240px)`, no horizontal scroll
(`scrollWidth === clientWidth === 375`), toggle button with a real `aria-expanded`.

---

## Decision B — every standalone list gets a real server-side search

Chosen deliberately over "only the lists whose reference counterpart has one" (which is all of them
anyway) and over "chrome now, server parameter later".

**The third option was the tempting one and is the one worth arguing against.** A search box wired to
a client-side filter of the *current page* is not a smaller version of search; it is a different and
worse feature, because every list here is server-paginated with `Skip`/`Take`. Typing a customer's
name into it would filter the 50 rows you happen to be looking at and confidently report nothing
found. Shipping that would have made the phase's headline number ("46 of 46 lists have search") true
and meaningless.

**How 25 handlers avoid being 25 improvisations.** Two marker interfaces (`ISearchableQuery`,
`IDateRangeFilteredQuery`), one shared validator pair (`ValidateSearch`, `ValidateDateRange`), one
normalisation rule (`SearchTerm.Normalize` — whitespace-only is not a term), and
`SearchSweepGuardTests`, which fails the build when a paginated `List*Query` neither accepts a term
nor names itself in an allow-list with a reason. Nine are allow-listed, and the rule they instance is
written into the guard: **a query gets a term when its screen is a standalone list someone browses,
and is exempt when it backs an editor, a picker, or a panel already scoped to one parent row.**

**There is deliberately no shared `Matches(column, term)` helper**, and the reason is a near miss
worth recording: the first draft had one. A static method call inside a LINQ-to-Entities predicate is
not translatable at all, and neither is `string.Contains(term, StringComparison.OrdinalIgnoreCase)` —
only the single-argument overload is. Had it shipped, it would have 500'd all 25 endpoints, and no
handler test on the InMemory provider would have seen it, because InMemory evaluates the expression
in C# where both forms work. That is phase-25's captured-`Func` gotcha arriving through a different
door. Each handler now writes `x.Code.Contains(term)` inline, and `SearchableQuery.cs` carries the
comment explaining why a tidy-up would break it.

**One consequence to know**: matching is case-**insensitive** on SQL Server (the collation does it)
and case-**sensitive** on InMemory. A handler test must search with the stored casing or it is
asserting a behaviour production does not have.

### What the sweep found on its own

Asking one question of every paginated list surfaced three pre-existing gaps that nobody was looking
for:

- **`ListVariantAttributesQuery` had no validator at all** — so its `Page`/`PageSize` were unbounded
  too.
- **`ListInboxDocumentsQuery` had no validator either**, despite having accepted a `Search` term
  since phase 22.
- **`ListBillsOfMaterialsQuery` bounded nothing**: it has taken a term since phase 25 and let any
  length of string reach a `LIKE`.

Phase 33's lesson in a new shape — the sweep is worth more than its subject, because it is the first
thing that ever asked the question uniformly.

---

## Decision C — `list-group` rows stay; sorting moves into the chrome

36 of 46 lists render Bootstrap `list-group` card rows and 10 render `<table>`. The reference product
is uniformly tabular with sortable, filterable column headers.

**They stay.** 34a already made both forms WCAG-conformant, so converting buys no accessibility; it
is a large visual diff across 36 templates with real regression risk and no functional gain. What
NFR-6.1 asks for is one interaction model, and the model is the *controls* — search, count, sort,
pager, actions — which `app-list-chrome` now provides identically above whichever row form a page
uses.

**The price, stated rather than glossed:** our rows are not sortable by clicking a column header,
unlike the reference product's. The chrome carries a `sortOptions`/`sortChange` pair so a screen can
offer sorting as a select instead; no screen populates it yet, which makes it the phase's most
honest carried item (#2 below) rather than a claim.

---

## Decision D — the date range scopes documents, not master data

The confirm-live pass showed the range reaching `contacts` and `accounts` as well as `invoices`, but
**not** `products` or `contact-groups`. Copying that exactly would be copying an inconsistency.

The rule adopted is the one the observation supports and the schema can state: **an aggregate with a
business `Date` implements `IDateRangeFilteredQuery`; master data and configuration lookups do not.**
That is 16 document lists. It is expressible as an interface, checkable by a guard, and explicable in
one sentence to a user — where "contacts are scoped but products are not" is none of those things.

`SearchSweepGuardTests` also asserts the two interfaces are not independent: a query that declared
only `IDateRangeFilteredQuery` would be a filter with no chrome to drive it.

**Where the range is stored is a deliberate improvement on the reference.** It keeps this in
`localStorage`, so its users lose the setting on every new machine. Phase 33 built `UserPreference`
— a per-`(organization, user, key)` server-side store — for exactly this shape, so `date-range` is a
third key in that vocabulary, validated like the other two. A *relative* preset is re-derived from
today on load rather than restored literally: "Last 7 days" stored a week ago must not come back
meaning that week.

---

## Decision E — 34a's human accessibility pass, and what was done here

34a's Decision A listed six WCAG criteria a machine cannot decide, and its carried item #1 said they
should run *after* the re-layout. They did, on the new chrome, and the results are built in rather
than audited on:

| criterion | what this phase did |
|---|---|
| 2.4.7 Focus Visible | every nav row, flyout link and date option has an explicit `:focus-visible` ring; the rail is dense, so the outline is pulled *inside* the row (`outline-offset: -2px`) rather than suppressed |
| 2.4.3 Focus Order | the skip link was already first in tab order and is now load-bearing rather than merely correct — without it, Tab crosses ~60 nav rows before reaching the page |
| 4.1.2 / 4.1.3 | `aria-expanded` set by hand on all three new popups (Bootstrap's JS is not loaded, so nothing sets it for free); `aria-current="page"` on the active nav leaf; a second `aria-live` region in the app, on the list result count |
| 1.4.10 Reflow | the rail becomes a drawer below `lg`; verified at 375px with no horizontal scroll |
| 1.4.3 Contrast | every colour in the new SCSS is a Bootstrap CSS variable, so `contrast-rules.ts` still describes the whole palette and the 34a guard still covers it |

**`a11y-sweep-guard.spec.ts` stayed green across all the new markup** — 166 templates now, up from
161 — which is the cheapest available proof the shell did not regress 34a. That was the point of
building the guard before the shell rather than after (34a Decision B).

What is **not** done is the parts that need a person with a screen reader: error-message quality
(3.3.1/3.3.3) and label-in-name (2.5.3) across the pre-existing 140 templates. Carried, item #1.

---

## What was built

**The shell** (`web/src/app/shared/platform/`)
- `left-nav.ts` — the rail. Rows come from `NavigationCatalog`; only *ordering* is written down
  (`AREA_ORDER`), never membership, so a route a later phase adds appears with no edit here.
- `create-new-flyout.ts` — the 4-column panel. Its list *is* written down, and the component says why:
  the nav is every screen (deriving beats listing), this is a curated 18-shortcut tray (listing is
  the feature). The guard asserts every url resolves to a real catalogue entry.
- `date-range-picker.ts` + `date-range.service.ts` — the global range, its 8 presets and Custom Range.
- `nav-tree.ts` — `buildNavTree`, `buildReportIndex`, `activeGroupOf`, `activeItemUrlOf`.

**The Reports index** (`features/reports/report-index-page/`) — 52 reports under 8 headings, with a
client-side filter box and its own `aria-live` count. Rows derived from the catalogue; only the
heading each report belongs to is a mapping, because a route carries no signal for it.

**The list chrome** (`shared/pagination/list-chrome.ts`, `list-query-options.ts`) — heading, `<h1>`,
description, result count, search box (300ms debounce; 0ms when cleared), sort slot, actions slot,
and the date-scope disclosure. `ListFilter` is a plain class the page constructs, not an injectable:
two list screens can be alive at once during a route change, and a root-provided service would share
one term between them.

**The server sweep** — `Common/Filtering/SearchableQuery.cs` (2 interfaces, `SearchTerm`,
`SearchValidation`), 25 query records, 23 handlers, 23 validators, 44 Api endpoint call sites.

---

## Bugs hit while building

**1. The chrome described one window and the rows came from another.** *(browser pass)* A list
rendered `Last 30 days: 2026-08-11 – 2026-09-10` above an invoice dated 2026-07-20. The range is
loaded from `UserPreference` asynchronously, after the page's constructor has already issued its
request — so the label read the settled signal while the rows came from the default. **A filter a
page displays but did not apply is worse than no filter: it is a wrong answer that looks checked.**
`ListFilter` now takes a `reload` callback and drives it from an `effect` on the range, skipping the
first run by comparing values rather than by a flag. Fixed and re-verified on the same control in the
same running app: 3 rows → 2, count agreeing with the label.

**2. An `effect()` that raced the debounce it was meant to help.** Clearing the search box never
restored the list. `onInput` set the term and scheduled `emit('')` at 0ms; an `effect` watching for
an empty term then cancelled that timer, believing it was cancelling a *stale* one. The generalisable
part: **an effect that reacts to the same signal a handler just wrote cannot tell "the write that is
already being acted on" from "a write that needs acting on".** The debounce already gave an empty
value a 0ms delay, so the effect was pure harm; it is gone, with a comment saying why it must not
come back.

**3. `overflow: hidden` on the rail clipped the flyout to 240px.** Two of the Create New panel's four
columns were simply not rendered. Phase-22's gotcha (a menu inside an overflow container is clipped
by it) reaching a new container. The rail's scrolling belongs to `.left-nav-list`, which has its own
`overflow-y`, so removing it costs nothing.

**4. `SearchTerm.Matches` — caught before it shipped.** See Decision B. Written, then deleted before
any handler used it, because a static call in a predicate is untranslatable and InMemory would have
hidden it.

**5. Duplicate parameters the compiler caught.** The Api sweep blindly added `search` to
`/bills-of-materials` (which has had one since phase 25) and `fromDate`/`toDate` to `/cheques` (which
has had them since phase 17). Both failed the build with `CS0100: duplicate parameter`. Recorded not
as a bug but as the *shape of a safe sweep*: a scripted edit that collides should collide loudly.

**6. Two things a fresh organization does not have.** A new tenant has no chart of accounts
(phase-27a knew this) **and no warehouse**, which the E2E harness had to create before an invoice
would validate.

---

## E2E (60 calls, every status code printed, every count asserted)

A fresh organization per phase, seeded through the API with curl + a cookie jar.

Beyond the happy path:

- **Search matched on both columns:** a document *number* (`0002` → 1 of 3) and a *reference*
  (`REF-2026-07-20` → 1 of 3), with a nonsense term returning 0.
- **A whitespace-only term is not a filter** — `search=%20%20` returned all 3, which is the rule
  `SearchTerm.Normalize` exists for.
- **A 101-character term → 400**, proving the shared bound is wired.
- **The range is inclusive at both ends** (`toDate=2026-07-20` returned the invoice dated that day)
  and works open-ended (`fromDate` alone).
- **An inverted range → 400**, on the query *and* on the preference store.
- **The two filters compose:** the July invoice's own number plus `fromDate=2026-09-01` returned
  **0**, which is the assertion that separates two composed `.Where()` calls from one predicate that
  replaced the other.
- **The generic lookup handler:** `units-of-measurement?search=Piece` → 1, `?search=Metre` → 0. One
  edit gave every Configurations lookup screen a search at once.
- **The preference store:** the new `date-range` key stored and read back; an inverted range, a
  non-date and an unknown key each → 400.

### The seeding traps this phase added to the list

- `POST /products` takes **`primaryUnitId`**, not `unitOfMeasurementId`; the wrong name is a 400
  naming `PrimaryUnitId`, which reads like the unit was never created.
- `POST /accounts` takes **`{name, groupId, kind}`** and nothing else — no `code` (generated), and
  `kind` is an `AccountKind` (`Other`/`Bank`/`Cash`). Sending `"Normal"` makes the whole body fail to
  deserialise, and the 400 says only "failed to read parameter … as JSON", naming neither field.
- A fresh organization has **no warehouse**.
- **A bash helper that both prints and returns is a trap in `$( )`.** `mkaccount` called `req` (which
  prints a status line) and then echoed the id; captured with `$(mkaccount …)` it returned the status
  line *and* the guid. Fourteen malformed ids produced a `PUT /accounting-defaults` that stored
  fourteen nulls, and the failure surfaced three steps later as a 409 at Approve whose message named
  a missing Sales Account — i.e. it looked like a product misconfiguration. Have such a helper set a
  global.

---

## The browser pass

Dev cert + `erp-web-ssl` + a transplanted `erp_auth` cookie (phase-25 Step 3). All three defects
above came from it. Also verified live:

- The nav's four confirm-lived behaviours, reproduced: one group open at a time, collapsing by
  re-clicking, both levels marked, and the accordion **following the route** after navigating from
  the Create New flyout (`accounting/journal-vouchers/new` → Accounting open, Journal Vouchers
  marked, panel closed, title updated).
- The date picker's 8 presets with their resolved bounds, matching the live tenant's exactly, and
  `Fiscal Year` resolving to 2026-07-17 – 2027-07-16 (Shrawan 1 to the last day of Asar).
- Search 2 → 1 → 2 results with the `aria-live` count following.
- 34a's page titles still deriving (`Invoices · Sales · ErpApp`).
- Reflow at 375px.

**A tooling gotcha worth carrying:** under viewport emulation, `getComputedStyle` and
`getBoundingClientRect` can lag the actual rendering after a resize — the drawer measured as "on
screen, transform identity" while the screenshot showed it correctly off-screen, and a fresh load at
the emulated size agreed with the screenshot. **The screenshot is ground truth**; reload after
emulating a viewport rather than trusting measurements taken across a resize.

---

## Carried items

1. **The human accessibility pass is still partly open.** The new chrome was built conformant
   (Decision E), but error-message quality (3.3.1/3.3.3) and label-in-name (2.5.3) across the 140
   pre-existing templates need a person with a screen reader. 34a's carried item #1, narrowed.
2. **`sortOptions` has no consumer.** The chrome carries the sort control Decision C promised in
   place of sortable column headers, and no screen populates it. It is a seam, not a feature, and
   saying so is the point. Re-entry condition: the first list whose default ordering someone
   complains about.
3. **Six list pages are not paginated and so have no chrome.** The Configurations lookups fetch every
   row through `listAll`, so they can filter client-side for free — the server now supports
   `?search=` on all of them, and no screen sends it.
4. **`transaction-list-page` and `alert-list-page` were excluded** from the chrome sweep because
   neither has the standard `totalCount`/`page` signal pair. Both are report-shaped rather than
   list-shaped; a later phase should decide whether they are lists at all.
5. **The initial bundle grew 640 kB → 726 kB** (transfer 141 kB), because the shell is statically
   imported and so is eager on the login screen too. `@defer`ing it on `organizationId` would move
   ~46 kB out. Deliberately not done here: 34c is the phase that measures before rewriting, and this
   is exactly its subject.
6. **Deals and Tasks have no standalone routes.** The reference product has `CRM > Deals` and
   `Workflow > Tasks` as nav leaves; here both are components embedded in the dashboard and the
   contact detail page. A router-derived nav correctly does not show them — which makes the gap
   visible for the first time.
