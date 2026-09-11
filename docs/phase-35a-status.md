# Phase 35a — Ledger drill-down and the location dimension on documents

## TL;DR

Phase 35 as the roadmap scoped it was two unrelated halves and, once surveyed, far more than one
session: **16 document forms and 15 lists had no location picker at all** (only Invoice did), ~25
reports were candidates for a location filter, and Product-to-Location is a new many-to-many. Split
with the user up front into **35a** (this doc: drill-down + the document-side sweep, Decisions A
and D) and **35b** (reports + Product-to-location, Decisions B and C) — the 34a/b/c precedent.

**What shipped.** The two ledger reports now take their subject as a route parameter and are
reachable: an Account hit in global search and a **View Ledger** row action on the Chart of Accounts
both open `reports/detail-general-ledger?accountId=…`, closing phase-33 carried item #1. The billing
location picker was extracted out of the Invoice form into `app-document-location-picker` and swept
onto **all fifteen** document forms; the LOCATION cell and a Billing Location filter were swept onto
all fifteen lists, the filter living in `ListChrome` and its value in `ListFilter` so each page
contributes three bindings rather than thirty lines. `BillingLocation.WarehouseId` became a real
prefill (Decision D), settled by observation rather than guesswork.

**The finding that justifies the phase.** Phase 32 put `LocationId` on all 17 types and proved the
*write* path with a sweep guard — and **fourteen of the fifteen detail queries silently dropped the
field on the way back out**, because each projects an explicit DTO and only `InvoiceDetailDto` named
it. Every one of those forms could store a branch and never show it, and would post the picker's
default over the stored value on the next save. Nothing failed; phase 32's own carried note predicts
this for one type. Same defect in a second place: **all five conversion templates** dropped it too,
so converting a branch document silently moved it to HeadOffice.

**A correction to a CLAUDE.md gotcha, measured.** Five handlers used
`allowedLocations == null || allowedLocations.Contains(…)` inside a predicate — the shape
`known-gotchas.md` forbids. I rewrote them as composed `.Where()` calls and expected to demonstrate
a 500. **They were not broken:** the pre-fix shape, injected back and run against SQL Server with
real rows, returns **200 and the right answer**. The gotcha's final generalisation ("the same
applies to any `x == null || …` over a captured reference") is **too broad** — see Lessons.

Tests: Domain 443, Application **969** (+38), Api.IntegrationTests 18, Angular **283** (+10).

---

## Scope: why 35 became 35a and 35b

The kickoff's own numbers were an underestimate, and the survey is the reason the split happened
before any code was written:

| the roadmap said | the survey found |
|---|---|
| "the header picker on Sales Order, Credit Note and the eleven All-Transactions types" | 15 forms and 15 lists, none of which had it; only Invoice did |
| "the location filter … on the remaining registers and Master reports" | the Sales Master Report's filter exists **server-side only** — its Angular page never sends it and shows no LOCATION column |
| "a Product carries a Location selector" | a new many-to-many with an unobserved enforcement rule |
| — | 43 of 49 live reports carry the filter, **including the GL ones**, and `GlJournalEntry` has no `LocationId` |

That last row is 35b's central problem and was invisible before the confirm-live pass.

---

## Confirm-live (2026-09-10, `cadehi.tigg.app`, read-only)

Written up in full in `erp-module-scan.md` under *"Confirm-live pass 3"*. Nothing saved; two modals
opened and dismissed, one unsaved Add-Invoice form's picker switched and navigated away from.

The pass was a **census, not a sample**: all 49 report screens in the catalogue were opened and
their filter bars read. That distinction matters — phase-30's "a list sampled from a few screens
becomes a wrong list" is an argument against *incompleteness*, and a complete enumeration is not a
sample. It is what makes the six exceptions trustworthy.

Findings that governed this phase:

1. **`BillingLocation.WarehouseId` is a default.** The Add New Location dialog's required fourth
   field is `Warehouse *` with the placeholder **`Select Default Warehouse`**; two of the three
   seeded locations have **no warehouse at all**; and switching an open Invoice form's location to
   one of those left its Warehouse untouched. Prefill, not constraint, not reactive.
2. **The header picker is not a sales control.** It renders on the Journal Voucher form too, so the
   All-Transactions scope reaches accounting — the sweep is right to be uniform.
3. **Every document grid carries LOCATION in second position**, with a per-column filter funnel —
   Quotation, Invoice, Journal Voucher, Purchase Bill and Warehouse Transfer all confirmed.
4. **43 of 49 reports carry a Billing Location filter.** The six without are the three IRD filings
   (VAT Summary, TDS, Annex 13), the two whole-organization analytics (Ratio Analysis, Exceptional)
   and User Log. **Annex 5 carries one despite being an IRD annex** — recorded as observed and not
   reconciled, which is exactly why the pass read all 49 rather than one report per group.
5. Three filters the roadmap carries from the Moonbeam read (Reporting Tags on the Journal report,
   Reporting Tags + group-by-warehouse on Inventory Position, Group By Bill / Include Credit Note on
   the Sales Register) are **absent on Cadehi**. Tenant- or entitlement-dependent; 35b must re-read
   Moonbeam before building any of them.

---

## Scope decisions

### Decision A — an Account hit opens the ledger; there is no per-account page

Phase 33 rendered an Account search hit with the words "no detail page". The tempting fix is to
build one. **The reference product does not have one either** — what its result row offers is a
**View Ledger** quick action (module scan, global-search section: *"Contacts and Accounts
additionally carry a View Ledger quick-action button inside the result row; products and documents
do not"*). So the ledger **is** the drill-down, and this codebase already had the report.

- **Account** → `reports/detail-general-ledger?accountId=…`, from two entry points: the search hit's
  own target, and a **View Ledger** action on every Chart of Accounts row.
- **Contact** → keeps its detail page as the hit's target. It has one, and its Overview tab has
  carried a *View Full Statement* link to that contact's ledger since phase 10, so the contact's
  ledger is reachable and always was — what phase 33's carried item asked for.

**The live row's second button is deliberately not reproduced in the dropdown.** The results list is
an ARIA `listbox` and an interactive control inside a `role="option"` breaks the combobox pattern
phase 33 built. On the Chart of Accounts, which is an ordinary list, the button is fine and is
exactly where it went.

`customer-statement-page` has taken `?contactId=` since phase 10; the new `?accountId=` deliberately
mirrors that convention rather than inventing a second one.

**Not gated in the client.** Detail General Ledger is Admin-only, so a Member clicking View Ledger
gets a 403. There is no client-side permission mechanism anywhere in this app — the nav, the Reports
index and the Quick Links tray all show what the server will refuse — and inventing one for a single
button would be the first of a kind. Recorded, not built.

### Decision D — the location's warehouse is a prefill, and the evidence is better than the live tenant's

Phase 32 carried item #6 said the relationship "was not probed". Three observations settle it (above),
and this codebase can prove it **more sharply than Cadehi can**, because Cadehi has exactly one
warehouse and cannot distinguish "the location's default" from "the only one there is".

The E2E seeds two warehouses named so that alphabetical order and the location default disagree:
HeadOffice's default is **Main Warehouse** while the picker's first real option is **Branch Store**.
A new Purchase Bill opens showing *Main Warehouse* — so the prefill comes from the location. Switching
the header picker to Branch One (whose default is Branch Store) leaves it on *Main Warehouse* — so it
is not reactive.

Implemented as `defaultWarehouseSeed`, a five-line helper rather than a copy per form, because the
subtle part is a race: the warehouse list and the location list are two independent requests and
either can land first. `offer()` from the picker's one-shot output and `retry()` from the host's own
warehouse callback, whichever is second wins. Seeding an id the `<select>` has no `<option>` for
would be the phase-5 native-select race, so the seed waits for the list.

### Decision E — the shared control lives in the chrome, not in thirty pages

The picker was inlined in `invoice-detail-page` and the filter in `invoice-list-page`. Both are now:

- `BillingLocationStore` — one cached read of a tenant's locations *and* settings, keyed by
  organization. A grid resolving a name per row must not be a grid of requests.
- `DocumentLocationPicker` — the header control, which renders nothing when the type is out of the
  tenant's scope, so every form can carry the tag unconditionally.
- `LocationListFilter`, wired into **`ListChrome`** behind a `locationDocumentType` input — so a
  list opts in with one attribute, and `ListQueryOptions.locationId` carries the value through the
  `applyListOptions` seam every list service already uses.
- `LocationName` — one LOCATION cell.

**Both inlined copies were deleted**, which is the point: phase-33's lesson is that an extraction is
not done until the copies it replaced are gone, and `GrantedPermissionReader` is the counter-example
that shipped saying it had replaced two joins and had not.

The Billing Location list page calls `invalidate()` after a save, because it is the only writer and
everything else now reads through the cache.

---

## What was found on the way

### 1. Fourteen detail DTOs dropped the location (the phase's real defect)

`LocationBearingCommandSweepGuardTests` has proved since phase 32 that every location-bearing type
has a command that can *write* a location. It was green while fourteen of fifteen detail queries
dropped it on the way out. The asymmetry is structural: a **list** query returns the aggregate, so a
new column is exposed for free; a **detail** query projects a DTO, so it must be told. Phase 32's own
carried note says exactly this about `GetInvoiceQuery` — the note was right and applied to one type.

The consequence is worse than "the form cannot show it": the picker defaults to HeadOffice when the
DTO gives it nothing, so **editing a branch document and pressing Save moved it to HeadOffice**.

Same defect in the conversion templates: all five dropped it, so converting an approved Quotation
raised at a branch produced an Invoice at HeadOffice.

`LocationReadPathSweepGuardTests` now asserts all three read paths — detail query, conversion
template, list query — derived from `DocumentMechanisms.LocationBearing` rather than listed, with
the three non-grid lists exempted by name and reason. It was made to fail by injecting each of the
three regressions and restoring the files **by hash** (phase-34a's rule; `git checkout --` would have
reverted the phase's own work on those files).

### 2. The expression-tree gotcha is narrower than CLAUDE.md says — measured

Five handlers (`ListProductionOrders`, `ListProductionJournals`, `ListAllocatablePayments`, and both
opening-balance lists) folded phase-32b's location scope into a predicate as
`allowedLocations == null || (x.LocationId != null && allowedLocations.Contains(…))`. That is the
shape `known-gotchas.md` forbids, and its narrative ends: *"The same applies to any `flag ? a : b`
or `x == null || …` over a captured reference in a predicate."*

I rewrote all five to composed `.Where()` calls, then set out to demonstrate the bug by putting the
old shape back in one handler and running it against SQL Server with real rows.

**It returned 200 and the correct row.** So the generalisation is wrong for this case. The plausible
reason — stated as a hypothesis, not a finding — is that EF parameterises a captured **bool**
(`!scoped` becomes `@__scoped_0`, which cannot be folded, so both sides must translate and the null
list throws), while `collection == null` is parameter-independent, funcletized to a constant, and
the `||` optimised away before `Contains` is ever translated.

The rewrite stays: it is the codebase's stated shape and is immune to either mechanism. But it is a
**consistency fix, not a bug fix**, and the phase would have shipped a false claim if the experiment
had not been run. Recorded as a correction in `known-gotchas.md`.

### 3. `ng test` has to be run from `web/`

`a11y-sweep-guard.spec.ts` reads the shipped stylesheet with
`readFileSync(resolve(process.cwd(), 'src/styles.scss'))`. Run as `npx --prefix web ng test` from the
repo root it fails one test — for the wrong reason entirely, and with a message about the stylesheet
reading back empty that looks exactly like the phase-34a gotcha it was written to prevent. Half an
hour was spent on that.

### 4. A lazily-created cached signal cannot be written by a synchronous source

`BillingLocationStore` creates its signal on first read and subscribes there. When the first reader
is a `computed()` (which it always is — the picker's `visible()`), a source that resolves
**synchronously** writes the signal inside that computed: `NG0600: Writing to signals is not allowed
in a computed`. Real HTTP resolves later and has no active consumer, so this appears only under a
test double — which is what caught it. `untracked()` around the subscribe and the writes.

---

## E2E (67 assertions, all printed)

A fresh organization per phase, seeded through the API (curl + cookie jar), with browser clicks
reserved for this phase's own UI. Scripts in the session scratchpad; every status code printed
(phase-26c's rule).

**Setup:** MultipleLocations + MultipleWarehouses + TrackInventory on at organization creation,
`LocationScopeMode = AllTransactions`, two warehouses (*Main Warehouse*, *Branch Store*), HeadOffice
defaulting to Main and a new **BR1 / Branch One** defaulting to Branch Store, a chart of accounts
(nothing seeds one), a Service product and a Goods product.

**Assertions, 55 of 55 (`e2e-assert.sh`):**

- **All fifteen document types** were created at BR1 and read back with `locationId = BR1` — the
  defect this phase fixes, proved once per type rather than once.
- **All fifteen lists** filter: `?locationId=BR1` returns the branch row, `?locationId=<HeadOffice>`
  returns none, and for Invoice (which has a row at each) the unfiltered list returns 2, each filter
  returns 1, and the returned id is the right one.
- The five formerly-folded handlers return **200** on the unrestricted branch.
- Approving the BR1 quotation and reading its **invoice conversion template** returns `BR1`.
- `reports/detail-general-ledger?accountId=` and `reports/customer-statement?contactId=` both 200.

**Negatives, 12 of 12 (`e2e-negative.sh`) — both directions on the same member (phase-31 rule d):**

A custom role with `Sales.Invoice.View` **false organization-wide and true at BR1 only** (an org-wide
grant would make `LocationAccessScope` return unrestricted and prove nothing), a real invited user
who registers, verifies through `[identity].VerificationCodes` and accepts.

| | |
|---|---|
| clerk's invoice list | **1 row**, and it is the BR1 one |
| clerk filters to their own branch | 1 row |
| clerk filters to HeadOffice | **0 rows** |
| `sqlcmd` | `sales.Invoices` holds **1 row at HeadOffice** and 0 with a null location |
| clerk opens the HeadOffice invoice by id | **403 — `HO.Sales.Invoice.View`** (the location-scoped key, named) |
| clerk opens a **nonexistent** invoice id | **404, not 403** — so the caller holds the pipeline key and the check fired before the handler |
| clerk opens the purchase-bill list | **403 — `Purchasing.PurchaseBill.View`** |

The sqlcmd row is what makes the empty result a **refusal** rather than an absent row.

**Browser (this phase's own UI only):**

- Chart of Accounts renders **View Ledger** on every row; clicking it on *Cash In Hand* opens
  `reports/detail-general-ledger?accountId=…` with the Account picker showing **Cash In Hand (0001)**.
- Global search for *Accounts Receivable* from `/contacts` navigates to the same report with
  **Accounts Receivable (0003)** selected — and the row no longer says "no detail page".
- **New Journal Voucher** renders the `HeadOffice (HO)` header picker: the sweep reaches accounting.
- **New Purchase Bill** opens at HeadOffice with Warehouse = **Main Warehouse** while the picker's
  first real option is *Branch Store* — the prefill is the location's default, not the first option.
  Switching to Branch One leaves the warehouse untouched — not reactive. Decision D, both halves.
- The Invoices list shows the **Billing location** control in the shared chrome and a **LOCATION**
  badge per row; filtering narrows 2 rows to the right 1 each way.

---

## Lessons

**(a) A guard over the write path says nothing about the read path.** Phase 32 shipped a sweep guard
proving every location-bearing type could *store* a location, and fourteen types could not show one
back. The asymmetry has a cause worth naming: a list query returning the aggregate gets a new column
for free, and a detail query projecting a DTO does not. Any phase adding a field to many aggregates
owes three assertions, not one — write, read, and every prefill in between (a conversion template is
a read path wearing a different hat).

**(b) Run the experiment even when the rule is already written down.** Five handlers matched a shape
`known-gotchas.md` forbids. Rewriting them was right; *claiming they were broken* would have been
wrong, and only injecting the old shape and hitting SQL Server showed the difference. A gotcha's
worked example is evidence; its closing generalisation is a hypothesis.

**(c) A census is not a sample.** Phase-30's rule against lists is an argument against
incompleteness. Reading all 49 report filter bars cost about ten minutes and produced a defensible
set of six exceptions *including one that breaks the obvious rule* (Annex 5). Had the pass sampled
one report per group, the "statutory reports have no location" rule would have looked clean and been
wrong.

**(d) When the live tenant cannot distinguish two rules, build the discriminator in your own.**
Cadehi has one warehouse, so "the location's default" and "the only warehouse" are the same
behaviour there. Seeding two warehouses whose alphabetical order disagrees with the location default
turned an unobservable question into a one-line assertion.

**(e) A shared control belongs at the level that already exists.** Putting the location filter in
`ListChrome` and its value in `ListFilter` — both phase-34b's — turned a 15-page sweep into three
bindings per page. The sweep that needs a script is the one whose home has not been found yet.

---

## Carried items

1. **35b, the whole report half:** the location filter/column on the reports, Product-to-location,
   and the three filters that were absent on Cadehi. Decisions B and C are 35b's.
2. **The GL reports need a schema decision.** The live product filters Trial Balance, Balance Sheet,
   Income Statement, the Journal report and all three General Ledger reports by Billing Location.
   `GlJournalEntry` has **no `LocationId`** — phase 32 sized the schema for the 15 documents plus the
   two opening-balance kinds. Either the entry carries a location stamped at post time, or every GL
   report joins back across 11 source types (phase-26a's expensive shape, and 34c's finding that the
   report layer's cost is the period). This is the first thing 35b must decide.
3. **The Sales Master Report's filter still has no UI.** The query, DTO column and endpoint have
   carried `locationId` since phase 32; its Angular page sends nothing and shows no LOCATION column.
   Left to 35b so the report screens are done in one pass rather than one now and 24 later.
4. **`ListAllocatablePayments` and the two opening-balance lists have no user-facing filter**, by
   decision — the first is a picker inside the Allocate dialog whose branch policy is phase 36's
   question, and the other two narrow *joined figures* rather than rows (phase-32b carried item #2).
   Named with reasons in the guard's exemption map.
5. **A document written while its type was out of scope reads back with no location**, and the picker
   then shows HeadOffice — so re-saving it would assign one. Inherited from phase 32's Invoice
   behaviour and swept unchanged rather than silently varied; worth a decision if a tenant ever
   narrows `LocationScopeMode` after trading.
6. **No client-side permission gating on View Ledger** (Decision A). Consistent with the whole app,
   but it is the first *row action* pointing at an Admin-only report.
