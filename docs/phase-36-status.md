# Phase 36 — allocation, forex and ageing consistency

## TL;DR

The phase was framed as reconciliation — *two reports that must agree, two posting paths that must
agree, one client guard stricter than the server it guards* — and that is what it did, with two
carried items from 35b built on top.

1. **The warehouse-guard bug, first and cheapest.** `featureGuard('MultipleWarehouses')` on
   `organizations/:id/warehouses` blocked the page outright, so a flag-off tenant could not create
   its **first** warehouse and therefore could not raise an Invoice or a Purchase Bill at all. The
   server always enforced a *cap*, not a block (phase-20f Decision #4). The route guard is gone and
   the cap is shown on the page instead.
2. **The further-allocation path posts its realised forex leg.** Approve-time allocation has booked
   it since phase 28; phase 17's Allocate screens added a `PaymentAllocation` row and touched the
   ledger not at all, so the same two documents settled at the same two rates produced a forex
   figure through one door and a permanent residue on the control account through the other. Both
   doors now agree, and the *cross-currency refusal* and the *currency filter* moved onto the
   screens so the choice is never offered.
3. **The two ageing reports now agree by construction.** Phase 9's Ageing Summary and phase 26b's
   Invoice Age / Purchase Bill Age were two implementations of one netting; they drifted twice and
   were patched twice. They now read one `OutstandingDocumentReader`, so a bucket total is a
   partition of the rows the other report lists — asserted on data exercising every candidate type.
4. **Credit Terms became a server-side default** (`DueDateResolver`) and the **credit-limit
   comparison converts currency** — which meant folding the whole contact-ledger family to base,
   including the rule that a settlement folds at the rate of *what it settles*.
5. **Product-to-location, the 35b carried item, was settled by experiment, not inference.** One
   write on the live tenant proved that a product scoped to one location vanishes from another
   location's line picker, server-side. Built: a `ProductLocation` child set (empty = everywhere),
   the picker filter, the product form's control, and the 11 document forms swept onto one shared
   `locationAwareProducts` with a guard that bites.
6. **The three Moonbeam-only report filters were re-read and built** — Reporting Tags on the Journal
   report, Reporting Tags + Group by Warehouse on Inventory Position, Group By Bill on the Sales
   Register — except **Display Warehouse in Column**, which could not be observed on a
   single-warehouse tenant and is recorded rather than guessed.

**One pre-existing 500 fell out of the work:** editing an Opening Balance line **twice** threw
`InvalidOperationException` out of `SingleAsync`, because after one edit the line already has three
entries. Nobody had edited one twice.

Tests: Domain 443, Application 1000, Api.IntegrationTests 18, Angular 301.

---

## Step 0 — the bug

`CreateWarehouseCommandHandler` has enforced a cap since phase 20f: the *second* warehouse is what
the entitlement buys, because Invoice and PurchaseBill both require a `WarehouseId` and nothing
seeds a default one. Phase 27b then wrote `featureGuard`, correctly, for the all-or-nothing flags —
and put it on the warehouses route too, where it is not all-or-nothing. The result was a tenant that
could not raise a sales or purchase document at all.

The route guard is removed (with a comment saying why this route differs from `currencies` and
`billing-locations`, which are genuinely on/off). The page shows the cap itself: the New Warehouse
form is withdrawn, and a notice explains why, **only** once the tenant is positively known to be
capped *and* already at the cap. An unknown entitlement (the subscription read failed, or is still
in flight) leaves the form available — a client that guessed "no" would reproduce the bug.

`featureGuard` now stamps the flag it gates onto the returned function, so
`warehouse-list-page.spec.ts` can assert *this route carries no feature guard* rather than restating
the route table, and can assert that the sibling routes still do — without which the first assertion
would pass even if the guard came back.

---

## Scope decisions

### A. The further-allocation forex leg is **its own GL entry**, and every reader was taught to expect one

A posted `GlJournalEntry` is append-only (phase 16a), so the correction cannot be folded into the
payment's Approve-time entry. It is a second entry against the same
`(SourceDocumentType, SourceDocumentId)` pair — which `GlJournalEntry`'s own doc comment has always
said is non-unique, and which its index has always permitted.

What was *not* written down is the narrower invariant every call site had quietly assumed: **while a
document is still Approved it has posted exactly one entry**, so a `SingleAsync` over the pair is
safe. Six sites assumed it. They are now `SourceDocumentGlEntries`:

| Site | Was | Now |
|---|---|---|
| `VoidPaymentCommandHandler` | `SingleAsync` + `PostReversalOf` | reverses everything outstanding |
| `TransitionChequeStatusCommandHandler` | same, with a comment asserting "exactly one entry exists" | same rule, comment corrected |
| `VoidJournalVoucherCommandHandler` | `SingleAsync` | reverses everything outstanding |
| `CreateOrUpdateOpeningBalanceLineCommandHandler` | `SingleAsync` — **already broken** | reverses the net |
| `GetPaymentQueryHandler` | `SingleOrDefaultAsync` (throws on two) | shows every entry's lines |
| `GetJournalVoucherQueryHandler` | same | same |

**The reversal nets rather than mirroring entry by entry**, because the Opening Balance case is not a
void: its entries are a history of posting, reversing and re-posting, and only their net is
outstanding. Netting handles the void case identically — a document voided from Approved has no
reversal yet — while being the only form that is also correct when reversals are already present.
Entries are grouped by `LocationId` first, so a document whose location changed between postings has
each location's balance reversed where it was posted (phase 35b's rule, applied across entries).

### B. The Journal Voucher branch books the same leg, for the same reason

`ApplyPaymentAllocationCommand` has two branches — a Payment, or a contact-tagged Journal Voucher
line (phase-17's polymorphic `PaymentAllocation.SourceType`). A voucher line's Debit/Credit are in
the *voucher's* currency (`ApproveJournalVoucherCommandHandler` converts them at its rate), so
settling an invoice booked at another rate leaves exactly the same residue on the control account.
The direction follows the contact's type the way it follows `PaymentDirection` on the other branch.
The entry is posted against the **voucher**, not the line: a line is not a GL source document, and
`VoidJournalVoucherCommandHandler` reverses by the voucher's id.

### C. Netting is per *this* allocation, never re-netted across the document's history

`PaymentForexCalculator` nets across the allocations it is given. At Approve that is all of them; on
this path it must be **only the one being posted now** — every earlier allocation was booked already,
at Approve or by an earlier call here, so re-netting would post their differences a second time.

### D. The currency filter is a screen rule, not a new server rule

Cross-currency settlement stays rejected (phase 28 Decision F) — nothing in the live product
contradicted it, and the refusal is in the shared calculator both doors call. What changed is that
the screens stop *offering* it: `AllocatablePaymentDto` carries the credit's own `CurrencyCode`, the
Allocate screen filters its target picker by it and says so when a contact has no open document in
that currency, and the two approve-time allocation pickers filter by the payment's currency.

**A phase-28 gap surfaced while doing it:** the **Supplier Payment** form never had the Currency +
Exchange Rate control at all — phase 28 put it on eleven screens including the customer twin and
missed this one — so a Paid payment could only ever be raised in the base currency and the realised
forex leg on the payable side was unreachable from the UI. The control is a shared component; wiring
it was four lines and the request already carried the fields.

### E. One reader answers "what does this contact still owe"

`OutstandingDocumentReader` is the phase's structural change. Both ageing reports read it; each
keeps only its own presentation (buckets and a contact-group filter on one side; the Txn Type filter,
Overdue/Current, age in days and the footer on the other).

Making them agree **changed the older report's output**, and that is stated rather than slipped in
(phase-26b Decision B's own words): the Ageing Summary now also sees **contact-tagged Journal
Vouchers** and **contact opening balances**, which the per-document report has listed since 26b. A
tenant that has neither sees no change.

### F. Quick Payment / Quick Receipt stay un-ageable

Re-examined (26b #2) and kept. In the reference product those are generic multi-line documents in
their own right; here phase-17 Decision #7 made them a thin variant of `Payment`, so there is no
document to age. An unallocated receipt is a **credit**, not an outstanding item with an age — it
already reduces the contact's balance through `ContactBalanceSummaryQuery`. Faking a row would give
it a due date it does not have.

### G. Credit Terms default on the server; an explicit due date always wins

Phase 31 seeded `DueDate` from `Contact.CreditTermId` **in the browser**. That made the term real for
anyone using the UI and invisible to everyone else — an API, an import, a conversion or a job got a
due date equal to the document date, and the ageing reports bucket from exactly that field.
`DueDateResolver` applies the term wherever a document is written (Create and Update, Invoice and
Purchase Bill). It is a **default, not a derivation**: an explicit due date wins, including one equal
to the document date, which is what the live form's freely-editable input produces.

### H. The contact ledger folds to base — and a settlement folds at the rate of what it settles

The credit-limit comparison could not "convert currency" on its own: it compares a document against
`ContactLedgerReader`'s running total and `Contact.CreditLimit`, and the reader summed mixed
currencies as though they were one unit (phase-31 carried item #3, inherited from phase 28). So the
family folds to base, each document at its own stored rate.

The non-obvious half: **a payment folds at the rate of what it settles**, allocation by allocation,
with only its unallocated remainder at its own rate. Folding the whole payment at its own rate would
leave a fully settled invoice showing a residual balance equal to the realised exchange difference —
a real number, but one that belongs in the P&L where `PaymentForexCalculator` books it, not in what a
customer still owes. `OutstandingDocumentReader` folds each document's *net* at that document's own
rate, which is exact because a cross-currency allocation is refused, and the two readers therefore
still agree.

### I. Product-to-location: what it restricts, proved rather than assumed

35b left this open because **no screen displays it**: the Products grid has no LOCATION column and no
location filter, so nothing on the tenant reveals what the selector does. The experiment the roadmap
described was run on the live tenant (2026-09-11), and it took **one** write, not two:

- A Goods product created with Location = *POS Retail* only.
- `#/sales/invoices/add` with the header location **HeadOffice**: the line picker returns **No data**.
- The same form, header switched to **POS Retail**: the picker offers the product immediately.
- `performance.getEntriesByType('resource')` shows one
  `products-minimized?…&location_id=<the document's location>` call per switch — so **the filtering
  is the server's**, driven by the document's location, not a client-side hide.

Built to match: `ProductLocation` child rows, **an empty set meaning every location** (which is what
the live control renders as `All`, and what every existing product has), `ListProductsQuery.LocationId`
filtering to *restricted-to-it or unrestricted*, and the Products grid asking for no location at all
so a restricted product stays findable and editable.

**Not built: enforcement at save.** The picker is what was observed; whether the reference product
also refuses a line naming an out-of-location product at Approve is unobserved, and rejecting would
be a rule that could invalidate documents already saved. Recorded below.

### J. The eleven document forms go through one helper, with a guard that bites

Every form called `catalogService.listAllProducts(this.organizationId)` on one identical line.
`locationAwareProducts(organizationId, locationId, target)` replaces it: it passes the document's own
location and **re-reads when the header location changes** — phase 34b's rule that a filter a screen
displays but does not apply is worse than no filter. `product-picker-location-sweep-guard.spec.ts`
asserts all four directions, including the mirror (a screen with no location of its own must keep
reading every product, or report filters would silently drop rows). Proved to bite by injecting the
old call into `invoice-detail-page.ts`: 2 of 5 assertions failed, and the file was restored **by
hash**, never `git checkout --`.

### K. The three Moonbeam filters, and the one that stayed unbuilt

Re-read on Moonbeam 2026-09-11. The scan's line that **"Show Filters reveals nothing"** is a *Cadehi*
observation and does not hold there: on Moonbeam the control opens a right-hand **drawer** that is a
superset of the inline row.

| Report | Drawer holds | Built |
|---|---|---|
| Journal report | Period, Transaction Type, **Reporting Tags** (one checkbox multi-select per tag category) | yes |
| Inventory Position | Period, Category, Product, **Group by Warehouse**, **Display Warehouse in Column**, View Options (positive/negative/all), **Reporting Tags** | all but *Display Warehouse in Column* |
| Sales Register | Period, **Include Credit Note In Calculation**, **Group By Bill** (both ticked by default) | Group By Bill (the other shipped in phase 31) |

**Group By Bill was measured, not guessed:** clearing it took the same period from 27 rows to 50,
added three columns (item name, quantity, unit) after the buyer's PAN, and **left the footer total
unchanged** — which is the property the implementation preserves, and what its test asserts. Clearing
*Include Credit Note* took it to 16 rows and re-totalled, re-confirming phase 31.

**Reporting Tags keeps the Sales Register's semantics** — a document matches when it carries *any* of
the selected options. What the live drawer does *across two tag categories* was not observable
(nothing on that tenant had two categories' worth of tagged data), and inventing a second rule for a
sibling report to disagree with is exactly what this phase exists to undo. A stock row carries no tag
of its own, so on Inventory Position the filter narrows the **movements** behind the balance to those
whose source document is tagged.

**Display Warehouse in Column is unbuilt on purpose.** Ticking Group by Warehouse on Moonbeam changed
nothing in the output — consistent with a single-warehouse tenant — so the two checkboxes could not be
told apart, and a column whose content nobody has seen is phase-8f's Annex 5 lesson waiting to
repeat. Group by Warehouse itself was built because its name is unambiguous and the reader already
holds per-warehouse facts.

---

## What shipped

**Domain** — `ProductLocation`; `Product.Locations` + `SetLocations` (reporting its own additions and
removals, phase-24 bug #1's remedy).

**Application** — `SourceDocumentGlEntries` (load/reverse every entry of a document);
`AllocationTargetRates` (the booking rates both settlement paths read);
`PaymentPostingRule.ForexLines` + `PaymentAccountResolver.ResolveControlAccountAsync` (the forex pair
on its own); the forex leg on both branches of `ApplyPaymentAllocationCommandHandler`;
`OutstandingDocumentReader` + `AgeableDocumentType` moved beside it, with both ageing handlers
rewritten onto it; `DueDateResolver` on four handlers; the base-currency fold in
`ContactLedgerReader` (including `SettlementBaseAmountsAsync`) and `OutstandingDocumentReader`;
`ContactCreditLimitPolicy`'s converted comparison; `ReportingTagFilter.ResolveMatchingDocumentsAsync`
(multi-type, keyed by pair) and the organization argument on the old overload;
`ListProductsQuery.LocationId`; `JournalReportQuery.TagOptionIds`;
`InventoryPositionReportQuery.GroupByWarehouse`/`TagOptionIds`; `SalesRegisterQuery.GroupByBill` and
`SalesReturnReader`'s line rows; `AllocatablePaymentDto.CurrencyCode`.

**Api** — `locationId` on the products list; `LocationIds` on both product request records (both, per
phase-27b's trailing-parameter trap); `tagOptionIds` on the journal report and its export;
`groupByWarehouse`/`tagOptionIds` on inventory position and its export; `groupByBill` on the sales
register and its export.

**Infrastructure** — `ProductLocationConfiguration` (catalog schema, unique `(ProductId, LocationId)`,
Restrict FK to `BillingLocation`) and the `AddProductLocations` migration, applied to the dev
database.

**Web** — the route guard removed and the cap rendered on the warehouse page; `featureKeyOf`;
`locationAwareProducts` + the sweep guard; the product form's Location checkboxes and its read-only
summary; the currency filter on the Allocate screen and both approve-time pickers; the Currency +
Exchange Rate control on the Supplier Payment form; Reporting Tags on the Journal report; Reporting
Tags + Group by Warehouse (with its WAREHOUSE column) on Inventory Position; Group By Bill (with its
three columns) on the Sales Register.

**Tests** — `ProductLocationTests` (6), `AgeingReportsAgreeTests` (4), five further-allocation forex
tests and three currency-ageing tests in `MultiCurrencyPostingTests`, the opening-balance re-edit
test, Group By Bill, the journal tag filter, inventory grouping, `warehouse-list-page.spec.ts` (5)
and `product-picker-location-sweep-guard.spec.ts` (5).

---

## Manual E2E

Seeded through the API with curl and a cookie jar; the browser used only for this phase's own UI.
Every status code printed (phase-26c's rule).

| Proof | Result |
|---|---|
| Flag-off tenant's **first** warehouse | `POST /warehouses` → **201** |
| …its **second** | **403**, naming the Multiple Warehouses entitlement |
| The warehouses **page** for that tenant | renders, lists Main Warehouse, shows the cap notice, form withdrawn |
| Product restricted to Branch One: picker at HeadOffice | `[]` |
| …picker at Branch One | `['Branch Only Service', 'Everywhere Service']` |
| …the Products grid (no location) | both products |
| …the detail read, and the form's checkboxes | `locations=[Branch One]`; HeadOffice unticked, Branch One ticked |
| USD invoice at 133, USD receipt at 130 approved **unallocated**, then allocated | `POST /payment-allocations/apply` → **200** |
| The GL, read with `sqlcmd` | **two** entries against the payment: 13,000 and 300, each balanced |
| The accounts that moved | Cash +13,000, **Forex Loss +300**, Sales −13,300 — **AR does not appear at all** |
| `groupByBill=true` vs `false` | 1 row with no item columns vs 1 row reading `Consulting 1.0 Piece`; **same total** |
| Member → Journal report (a phase-36-changed endpoint) | **403 — `Reports.JournalReport.View`** |
| Admin → the same | 200 |
| Member → Inventory Position with `groupByWarehouse=true` | 200 (Admin+Member, unchanged) |

The AR line is the phase in one row: before this, AR kept 300 no later document could ever clear.

---

## Bugs and snags hit

1. **Editing an Opening Balance line twice was a 500, and had been since phase 17.** The handler
   reverses "the prior entry" with `SingleAsync`; after one edit the line has three entries
   (original, reversal, corrected), so the second edit threw `InvalidOperationException`. Found by
   asking which call sites assume one entry per document, not by a report.
2. **`sqlcmd -i` cannot take a forward-slash absolute path here**: `-i C:/…/verify.sql` fails with
   *"The -E and the -U/-P options are mutually exclusive"* — it parses part of the path as options.
   Copying the script to a relative path ran it unchanged.
3. **`POST /auth/register` needs `turnstileToken` as well as `phone`**, and
   `POST /organizations/{id}/invitations` takes **`roleId`** (the system Member role is
   `00000000-0000-0000-0001-000000000002`), not a role name. Both failed as a bare 400 naming the
   field.
4. **An empty id propagates into the *next* request as "Failed to read parameter … as JSON"**
   (phase 28's trap, hit again): a re-run of the seed script 409'd on an already-created account
   group, left the id empty, and the failure surfaced three steps later on the payment. The script
   now creates its own organization per run and asserts every id is non-empty before continuing.
5. **The Angular suite times out nondeterministically under load.** Runs of the full 43-file suite
   failed 2 and then 7 tests — always the *first* test in a file, always at exactly 5000 ms,
   including files this phase never touched — then passed 301/301 clean. Every failing spec passes
   alone. It is machine contention (Docker Desktop had come up for the integration suite), not a
   regression; worth knowing before chasing a phantom.

---

## Known limitations / follow-ups

1. **Product-to-location is enforced on the picker, not at save** (Decision I). A line naming an
   out-of-location product still saves and approves. Whether the reference product refuses it is
   unobserved.
2. **Display Warehouse in Column is unbuilt** (Decision K) — needs a multi-warehouse tenant to tell
   it apart from Group by Warehouse.
3. **Cross-category Reporting Tag semantics are inherited, not observed** (Decision K): any-of across
   everything selected, the rule phase 19 chose for the Sales Register.
4. **`sales-summary`'s Group Wise location** — a group-*by* no other report has — is still unbuilt;
   35b gave that report the filter, not the grouping. It is a Cadehi control and was not re-read
   here.
5. **The Sales Register's money columns remain transaction-currency**, unchanged by this phase's fold
   (which touched the contact-ledger and ageing families only). A foreign invoice contributes its own
   100, not 13,300, to that statutory register — inherited from phase 19/28 and worth a decision of
   its own.
6. **A `Payment`'s own currency control is still absent from Quick Payment/Receipt** (only the two
   detail forms carry it).
