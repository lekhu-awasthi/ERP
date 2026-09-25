# Phase 58 — physical-movement inventory: Delivery Note, GRN, and a second ledger that moves nothing

## TL;DR

**The kickoff's structural premise was false, and the live read said so before any code was written.**
The plan was that *Physical Movement* moves FIFO consumption from Invoice / Purchase Bill Approve to
Delivery Note / GRN Approve, with a goods-received-not-billed account to hold the gap. Written and
read on a tenant made for the purpose (*Hamro Samaan*, 2026-09-24; `erp-module-scan.md`, "Physical
Movement, read and written"), the vendor does neither. **It keeps two stock ledgers at all times**:
a physical one that GRN and DN write, and an accounting one that Purchase Bill, Invoice and the
return notes write. Inventory Adjustment writes both, and so (inferred) do Warehouse Transfer,
Production Journal and Opening Stock. **The setting only picks which ledger the inventory screens
read by default.** Neither document posts to the GL, so there is nothing for a GRNI account to clear.

**So this phase adds a ledger and changes none.** The accounting ledger (FIFO layers, `StockMovement`,
the GL, and phase 37's conservation law) is untouched in both modes: DN and GRN write zero rows to
`GlJournalEntries`, `StockMovements` and `StockLedgerEntries`, proven in SQL. The physical ledger
is **derived and quantity-only** (Decision A, the user's choice). It is a new append-only
`PhysicalStockMovement` table for DN/GRN, plus the existing `StockMovements` rows whose type is one of
the four shared ones. There are no dual writes and no backfill, so flipping the setting is instant
and history is already in both ledgers.

**Every rule is per ledger, and one setting governs both.** A Delivery Note checks the physical
balance and an Invoice checks the accounting balance, both under the same Negative Item Balance
verdict. Measured live: an Invoice was refused on 3 − 5 although physical held 7. A DN's Void puts
back exactly what its Approve took. A **GRN's Void is refused (409) once its goods have left**, which
is where this phase diverges from the vendor: the vendor lets the void through and carries −3 at
rate 0. This is the rule the accounting ledger already applies to a consumed Purchase Bill layer
(Decision E).

**Conversions are parallel and one-shot.** An approved Purchase Order converts to a GRN once
(`ReceivedAt`) and to a Bill independently. A Sales Order converts to a DN once (`DeliveredAt`) and to
an Invoice independently. A received or delivered order refuses Void. There is no GRN → Bill and no
DN → Invoice, because the vendor has neither.

**The Inventory Variance Report** compares Book (accounting) against Actual (physical) per product,
with the difference always positive and the direction carried in the Remarks column, as the live
column prints it. Inventory Position gained the **Mode of Inventory Tracking** filter; it defaults to
the tenant's mode and hides Rate and Amount on the physical ledger.

**Eleven permission keys, all Admin+Member**, seeded through `HasData`. **Two indexes made covering,
each with a number.** The physical balance check went from 318 logical reads to 5 on its own table,
and its shared-type half on `StockMovements` went from 319 to 5. The untargeted paths stayed flat
(304 → 305). The report load is a full read in every variant, so it was measured and refused.

Tests: Domain 764 (+26), Application.UnitTests 1378 (+22), Infrastructure.UnitTests 13,
Api.IntegrationTests 30, Angular 627 (+8). All four suites are green; `ng build` is clean at
645.57 kB initial.

---

## Step 1 — the live read

`hamrosamman.tigg.app` was a fresh trial the user created for this phase and cleared for writes; the
user signed in. The full record is the scan appendix. It holds the ledger table, the measured
sequence `(physical, accounting)` = (10, 0) → (10, 5) → (7, 5) → (7, 3) → (6, 5) → (6, 6), the Trial
Balance after each step, the per-ledger 400s, the void behaviour, both forms field by field, the
conversions, and everything else the flag switches. Three findings changed the phase:

- **Two ledgers, not one moved.** This settled Decisions A–C, and it deleted the GRNI account from
  scope.
- **Conversions run in parallel**, with separate `received_quantity`/`billed_quantity` counters per PO
  line. This settled Decision F, and it is why no GRN → Bill chain exists.
- **The flag gates surfaces, not behaviour.** DN/GRN nav entries disappear in Accounting mode but
  their routes still load. The Mode filter appears on four reports. Custom Fields, Custom Status,
  Printing Templates and Email Templates all gain both types. The Variance report refuses an
  Accounting tenant. This settled Decision G and the mechanism sweep.

An aside the same pass found: live Custom Fields now lists **Warehouse Transfer**, which phase 27a
recorded as absent. It is recorded in the scan and left for its own decision. `DocumentMechanisms`
carries a comment saying so, and the list was not widened.

---

## Decision A — the physical ledger is derived and quantity-only

The vendor values each ledger at its own documents' rates (physical 7 @ 100 against accounting 3 @
120 in the live sequence). Offered the choice, the user took **quantity-only**:

- **Physical** = `PhysicalStockMovement` (DN out, GRN in, and their reversals) plus `StockMovements`
  rows whose `SourceDocumentType ∈ StockBooks.Shared`.
- **Accounting** = `StockMovements` / `StockLedgerEntries`, exactly as before.

`Domain/Inventory/StockBooks` classifies every `DocumentType` into AccountingOnly, PhysicalOnly,
Shared, or a "moves no stock, because…" reason. A guard requires that the four sets cover the enum,
so the next `DocumentType` member fails the build until someone decides which ledger it writes.
Injecting the regression (dropping Inventory Adjustment from Shared) failed two tests, and the file
was then restored.

**Why derived and not a second copy.** A shared-type document already writes `StockMovements`, so
copying its rows into the physical table would be two stores for one fact, which is phase 37's
two-of-three-views failure in waiting. Reading both tables at query time costs one extra grouped
query (measured below). It also means switching the setting needs no migration of history: the
physical balance of a tenant that never ran Physical mode is already correct.

**Why no value.** Nothing posts from the physical ledger. A second FIFO valuation would be a second
conservation law with no GL leg to anchor it. The Position report in Physical mode hides Rate and
Amount rather than printing zeros, and its export drops the two columns
(`ExportPhysicalInventoryPosition`).

## Decision B — neither document posts, and neither touches the accounting ledger

This is confirmed live: the Trial Balance after GRN + Purchase Bill held the bill and nothing else,
with no COGS line, because the vendor is periodic. Here, DN and GRN Approve write
`PhysicalStockMovement` rows and nothing else. The E2E proves this in SQL on the phase's tenant:
DN/GRN rows in `GlJournalEntries` = **0**, in `StockMovements` = **0**, in `StockLedgerEntries` =
**0**. That held after Accounting-mode and Physical-mode runs alike.

## Decision C — the setting picks a default, never a behaviour

No handler reads `InventoryTrackingMode`. A DN approved in Accounting mode writes the physical ledger
exactly as one approved in Physical mode does, because the vendor does the same (DN and GRN are
written in both modes; only the nav hides them). The setting's one reader is
`StockFactReader.ResolveModeAsync`, which supplies a report's default ledger when the request names
none.

**A bug caught by construction here.** `InventoryTrackingMode.PhysicalMovement` is ordinal **0**. A
non-nullable projection of the setting would read *Physical* for any tenant whose settings row was
absent, and would silently flip the default ledger. The select is therefore nullable, with
`?? AccountingMovement` stated once, and a test pins the default.

## Decision D — the negative-stock check is per ledger, under one verdict

`IStockAvailabilityPolicy.CheckPhysicalRequirementsAsync` sums the physical balance for a DN's Goods
lines at their **primary** quantity. Service lines are skipped. The result is fed through the same
private `VerdictForShortfallAsync` the Invoice uses, so Reject → 409 and Warn → 422 with
`warningKind: "StockAvailability"`, and the DN's Approve takes `{overrideWarning}` exactly as the
Invoice's does. The E2E drove both directions in Physical mode:

- an Invoice for 9 warned against the accounting balance (7), although physical held 11;
- a DN for 12 warned against the physical balance (11) and was confirmed with `overrideWarning`,
  leaving physical at −1.

## Decision E — Void mirrors Approve, and a receipt's void is refused once its goods have left

- **DN Void** writes the mirror rows (Out 2 → In 2), whatever the physical balance.
- **GRN Void** writes the mirror rows only if the physical balance can absorb them. Otherwise it
  returns **409**: *"some of the goods it received have already left the warehouse. Void the delivery
  notes or adjustments that took them first."*

The vendor lets that void through and carries −3 at rate 0. We do not, because this is the rule the
accounting ledger already enforces: a Purchase Bill whose FIFO layer has been consumed cannot be
voided (`ReverseIncrementAsync`), and Opening Stock correction refuses the same way (phase 16a). A
receipt's void that leaves the shelf negative says the goods that shipped were never received. That
is a typo to fix at its source, not an oversell to warn about, which is phase 51's reasoning for
serials. The refusal is independent of Negative Item Balance for the same reason.

The E2E proved both halves in SQL. With GRN 3 approved and DN 9 taking physical to 1, the GRN Void
returned 409 and moved nothing. After the DN was voided the GRN Void returned 200, and the GRN's rows
read `In:3, Out:3`.

## Decision F — conversions are parallel and one-shot

The vendor keeps per-line `received_quantity`/`billed_quantity` (and `delivered`/`invoiced`), so a PO
can be received in parts and billed independently. This phase ships the one-shot version:

- `PurchaseOrder.MarkReceived()` stamps `ReceivedAt`. It is valid on Approved **or** Converted,
  because billing first is legal, and only once. Create GRN with a PO referrer checks
  Approved/Converted, not received, and the same supplier, then marks it. A second GRN from the same
  PO → **409**.
- `SalesOrder.MarkDelivered()` is the mirror (`DeliveredAt`). The DN template carries the SO's Terms,
  and its Expected Delivery Date is the SO's Delivery Date, or tomorrow.
- A received PO or a delivered SO **refuses Void** (409). Phase 43's rule is that a Void owes the
  mirror of what its document did to stock. An order whose goods have physically moved cannot
  un-happen while the note that moved them stands.
- **Convert to Bill / Invoice is untouched and independent**, as it is live. In the E2E the PO
  showed *Approved · Received* with Convert to Bill still offered.

Partial receipts are the carried item (below). One-shot is the phase-6 minimum that enforces
something: `ReferrerType`/`ReferrerId` alone enforce nothing.

## Decision G — surfaces: where this diverges from the flag, and why

| Surface | Vendor | Here | Why |
|---|---|---|---|
| DN / GRN nav entries | hidden in Accounting mode (routes still load) | always shown | Nav is **derived from routes** (phase 34b). A per-setting filter would be the first mode-aware nav rule, and the vendor's own routes stay reachable anyway. The documents work in both modes. |
| Inventory Variance Report | refuses an Accounting tenant | opens in both modes, with a note on Accounting tenants | DN/GRN still write in Accounting mode, so the gap is real there too. A report that refuses a question it can answer is a gate with no reason. |
| Mode of Inventory Tracking filter | on Position, Ageing, Movement, Ledger | on **Position** only | The other three are carried (below); Position is the one the Variance report must agree with. |
| Edit | offered on approved DN/GRN | Draft only | Codebase-wide lifecycle: approved documents are immutable, and changes go through Void. |
| Per-line Warehouse | yes | header Warehouse only | The other 13 line-item types here have none. Carried. |
| Mark as Delivered / Processed | list-default filter flags | not built | They filter a list and change nothing else. Carried. |

## Decision H — the `DocumentType` sweep, from rules

Two new members. Each mechanism list was decided by the rule that defines it, never by sampling, and
each has a guard on both sides:

- **Transactional**: both (Draft/Approve, a number at Approve, the four-tab detail page, live).
  Reporting Tags and Location Bearing spread from it.
- **Custom Fields**: both (live Configurations > Custom Fields lists both).
- **Custom Status**: both. The rule is the list-grid Stage column; DN has a section live, GRN a
  column and seeded statuses.
- **Terms and Conditions**: **DN only**, because the live DN form carries the block and the GRN form
  does not. Phase 27b's dividing line predicts it: a DN is issued to the customer, a GRN records
  something received.
- **Emailable**: both. Phase 30's rule holds: the live Email Template Type picker lists both, and so
  both detail pages carry Send Email. The GRN is the first *received* document on the list, so "sent
  to a counterparty" was a gloss on the other six, not the rule.
- **Print**: both (every transactional type).
- **Metered**: **neither**. The vendor's own definition of a metered transaction is one where
  "accounting entry are affected", and neither posts.

Every multi-way switch on `DocumentType` gained both arms: permissions, existence, lock date,
location reader and backfill, approval queue, transaction list, global search, print, email
composition and merge fields, and custom status. The four parent enums (Attachment, Comment, Task,
Email) are bridged by name. On the web side, the per-type unions and maps were swept, and
`document-mechanism-sweep-guard.spec.ts` now lists 18 detail pages, 6 custom-status grids, 6 terms
forms and 9 emailable templates.

## Decision I — eleven keys, all Admin+Member

`Sales.DeliveryNote.{View,Create,Edit,Approve,Void}`, `Purchasing.GoodsReceivedNote.{…}` and
`Reports.InventoryVariance.View`. The full reasoning is in `PermissionKeys.cs`:

- A DN and a GRN are a warehouse's most routine working data, and they expose nothing a Member does
  not already see on the order they come from. Neither posts, so Approve moves no balance an
  accountant owns.
- The Variance report is a per-product quantity rollup with no counterparty. It gets its own key
  because the vendor gives it one, and a tenant may want a storekeeper to see stock without seeing
  where the books and the shelf disagree.
- The vendor has **no key** for Mark as Delivered/Processed, and it is not built.

The keys are seeded via `RolePermissionConfiguration.HasData` (ids `01cb`–`01e0`).

## Decision J — the Variance report's shape

Seven columns, as read live: Item Code, Item Name, Item Category, Book Balance, Actual Balance,
Difference, Remarks. **Difference is the absolute gap**, and Remarks reads *Quantity To Be Shipped*
when Actual is above Book, or *Quantity To Be Received* when it is below. The live pair was Book 3 /
Actual 7 → 4, *Shipped*; Book 3 / Actual −3 → 6, *Received*. The web spec pins that pair.

## Decision K — an as-of date, and filters rather than a grouping

The live report takes a date range and "Group by Item/Category". Here it takes one **as-of date**
(both columns are balances, and a balance has a date, not a period), **Category and Product
filters**, and the Billing Location. It lists **only products whose ledgers differ**. That last
choice is inferred, not confirmed: every live row sampled differed. An empty result reads *"Book and
actual balances agree for every product"*, which is the report's answer rather than an absence.

---

## The measurement

The kickoff's rule was a number or an explicit "measured and refused" for every index decision.
Neither table held data worth a plan, so `tools/scale/seed-phase58.sql` seeds the 34c tenant with
**200,000 physical movements and 200,000 accounting movements** (2,000 products, one in four
accounting rows a shared type). Like phase 54's layers, these are sentinel-marked read-path fixtures,
not documents. Every probed statement is the SQL EF issued, copied from the API's own log. Logical
reads, all paths in every variant (phase 34c).

**`PhysicalStockMovements`** (`run-probe-phase58.py`):

| variant | A report load | B availability | C void lookup |
|---|---:|---:|---:|
| **shipped: covering product index + source index** | 6,651 | **5** | **9** |
| product index without INCLUDE (first scaffold) | 6,651 | 318 | 9 |
| no source index | 6,651 | 5 | 6,651 |
| no product index | 6,651 | 318 | 9 |
| neither index | 6,651 | 318 | 6,651 |

- **B:** without INCLUDE the optimiser **never chose the composite at all**. The `ProductId` FK index
  plus key lookups won, at 318 reads either way, so the first scaffold's index was dead weight for
  the path it was declared for. Covering `Direction, Quantity` → 5.
- **C:** the source index is 9 reads against a full scan, and it stays.
- **A:** a full read in every variant. No index helps a report that reads every row up to a date, so
  none is added: **measured and refused**. The absolute figure tracks the table's page count: 4,584
  on the first seed, 6,651 after a delete-and-reseed on random-GUID keys. The comparison is within a
  pass.

**`StockMovements`** (`run-probe-phase58-shared.py`). This index belongs to the accounting ledger, and
the physical balance sums its shared types on every DN Approve and GRN Void:

| variant | S shared half | R shared load | L one product's history |
|---|---:|---:|---:|
| before phase 58: plain composite | 319 | 7,495 | 304 |
| **shipped: covering (Direction, Quantity, SourceDocumentType)** | **5** | 7,495 | 305 |

It is a strict superset of the index it replaces, the untargeted path is flat, and the report load is
unchanged, so it **replaces** it (phase 57's covering `AccountId`). The first pass read 535 for the
plain index, measured against the index the seed had fragmented; the table reports the rebuilt one.
Both changes are migration `Phase58CoveringStockIndexes`.

**What this does not measure:** write cost. Both tables are append-only, and each index gains two or
three narrow columns. That was not timed, and it is not claimed free.

---

## What was built

- **Domain:** `DeliveryNote`/`DeliveryNoteLine`/`DeliveryNoteStatus`; `GoodsReceivedNote`/…;
  `PhysicalStockMovement` (positive `PrimaryQuantity`, PhysicalOnly source types, `Reverse`);
  `StockBooks`; `PurchaseOrder.ReceivedAt/MarkReceived`; `SalesOrder.DeliveredAt/MarkDelivered`;
  two `DocumentType` members plus the four parent enums and `EmailTemplateContext`.
- **Application:** Create/Update/Approve/Void, Get, List, and the conversion template for each
  document; `PhysicalStockReader`/`PhysicalStockWriter`; `CheckPhysicalRequirementsAsync`; the
  Inventory Variance query; `InventoryPositionReportQuery.Mode`; `StockFactReader`'s physical loader
  and `ResolveModeAsync`; every switch-site arm; Void PO/SO refusals.
- **Infrastructure:** five EF configurations; migrations `Phase58PhysicalMovement` and
  `Phase58CoveringStockIndexes`; `TenantIndexConvention` classification; permission seed.
- **Api:** `PhysicalMovementEndpoints` (CRUD + approve/void + both templates); `mode` on
  inventory-position and its export; `/reports/inventory-variance` and its export.
- **Web:** list and detail pages for both documents; Convert buttons and Received/Delivered badges on
  the PO/SO pages; the Mode filter; the Variance page and route; the report category; every per-type
  union.
- **tools/scale:** `seed-phase58.sql`, both probes, both runners, and the result files.

## E2E (2026-09-25)

The run used a fresh Organization `b78f7fb7-…` seeded through the API with a cookie jar
(`e2e58.py`). Every status code was printed. It had one Goods and one Service product, and Negative
Item Balance set to Reject, then Warn.

- **Accounting mode:**
  - Opening Stock 10 → both ledgers (10, 10).
  - PO → GRN 5 → (10, 15). The PO is stamped received. A second GRN from it → 409, and PO Void → 409.
  - Variance: Book 10 / Actual 15 / 5 / *ToBeShipped*.
  - Purchase Bill 5 → (15, 15), and the Variance report is empty.
  - SO → DN 8 → (15, 7); SO Void → 409. Variance: 15 / 7 / 8 / *ToBeReceived*.
  - Invoice 8 → (7, 7).
  - DN for 20 → 409 and moves nothing.
  - DN 2 approve then void → rows `Out:2, In:2`.
  - GRN 3 / DN 9 / GRN Void → 409; DN Void, then GRN Void → 200, rows `In:3, Out:3`.
  - A Service-line DN writes no physical row.
  - Position defaults to Accounting (7 @ 100 = 700); asked for Physical it gives 7, rate 0.
- **Physical mode:**
  - Position now defaults to Physical.
  - GRN 4 → physical 11, accounting 7.
  - Invoice for 9 → 422 against 7. DN 12 → 422, then confirmed → physical −1.
  - Inventory Adjustment +2 → both (9, 1).
  - Variance: 9 / 1 / 8 / *ToBeReceived*, mode Physical. Both exports → 200.
- **Conservation, both modes:** layers = movements = GL Inventory **+ opening stock value** at every
  checkpoint. At the end the figures are 900 = 900 = −100 + 1,000. Opening Stock posts no GL entry
  (phase 37 §7), so on a tenant seeded with it the Inventory account is short by exactly its value.
  The first formulation of the check omitted that term and failed by a constant 1,000 at all four
  checkpoints, which is itself the proof that no phase-58 step moved the relation.
- **The 403, last:**
  - As Admin, `POST /delivery-notes/{ghost}/approve` → **404** and `POST
    /goods-received-notes/{ghost}/void` → **404**.
  - The user was then moved to a custom role holding only the two View keys and
    `Reports.InventoryPosition.View`. `GET` both lists → 200, and the same two calls → **403 naming
    `Sales.DeliveryNote.Approve`** and **`Purchasing.GoodsReceivedNote.Void`**.
  - `GET` inventory-position → 200 beside inventory-variance → **403 naming
    `Reports.InventoryVariance.View`**.
  - The role was restored via `sqlcmd`, and `GET /roles` → 200.
- **Browser** (`erp-web-ssl`, cookie transplant):
  - Both lists render with their pickers and badges, and both detail pages render. The console was
    clean throughout.
  - The Position page's Mode select round-trips: Physical hides Rate and Amount at 1.000, Accounting
    shows 9 @ 100 = 900. Variance renders the row.
  - SO → **Convert to Delivery Note** → the prefilled form (customer locked, expected date from the
    SO), then Save Draft → Approve, all by clicks, gave DN 0006. SQL confirms `DeliveredAt` and
    physical 1 → 0.
  - The empty-Warehouse save showed the per-field error. Both print routes return a PDF.

## Bugs and surprises

1. **The kickoff premise** (FIFO moves; a GRNI account). It was falsified by the first write on the
   live tenant, before code. The design pivoted with the user's decision.
2. **Ordinal zero is a value** (Decision C). A default-valued enum projection would have made
   Physical every settings-less tenant's default.
3. **A `computed()` over plain referrer fields** on the new detail pages froze, which is the zoneless
   gotcha. The fields became signals.
4. **The a11y guard caught a field error on a date.** `app-bs-date-input` owns its input and exposes
   none of the three bindings a field error needs, so the DN's Expected Delivery Date reports through
   the banner.
5. **The first probe measured the wrong query.** Wrapping the report load in `COUNT(*)` let the
   optimiser drop the unread columns and answer from the narrowest index: 1,839 reads, against a real
   4,584. It now materialises `INTO #a`.
6. **An index declared for a path was never chosen by it** (318 either way until it covered). An
   uncovered composite can lose to a narrower FK index plus lookups.
7. **The heredoc ate backslash escapes three times** (the known gotcha). It also turned out to have
   done so in phase 57: `roadmap.md` carried a literal ` n m m---` line, and `e2e-recipes.md` and
   CLAUDE.md each carried a broken gotcha line (the one *about* this). All three were fixed in this
   phase's doc pass.
8. **git-bash `grep -c $'\r$'` reports 0 on a CRLF file.** The guard spec had 274 CRLF lines and
   grep said none. Newline detection has to count bytes (Python `open(p,'rb')`).
9. **Found, out of scope, and filed as its own task:** `FifoStockAvailabilityPolicy.CheckAsync` (the
   Invoice's gate) sums the line's **entered** `Quantity`, while consumption uses `PrimaryQuantity`.
   Under a unit whose factor is not 1 the gate compares the wrong number. The DN's check sums primary
   quantity. This is a phase-52 miss that "an optional field ends a sweep" predicts.
10. **Not bugs, checked:** the converted DN's Warehouse is empty because the seed is the *location's*
    default warehouse, and this tenant's Head Office has none. The Invoice behaves identically. The
    line table prints the raw `ThirteenPercentVat`, which is the existing convention on every form.

## What was not added, and why

- **No GRNI / clearing account.** There is nothing to clear (Decision B).
- **No second valuation.** Quantity-only was the user's choice (Decision A).
- **No mode branch in any handler** (Decision C).
- **No import type for DN/GRN.** The vendor's `/config/import-statement` stages them (phase 55), but
  no importer was asked for and the import sweep is its own question.

## Carried into a later phase

1. **The Mode filter on Inventory Movement, Ledger and Ageing** (the vendor has it on all four). The
   reader already takes a mode, so each is a query parameter plus a select.
2. **Partial receipts and deliveries.** Per-line received/billed and delivered/invoiced counters
   replace one-shot `MarkReceived`/`MarkDelivered`. Phase 6's caps-net-of-reversals rule applies.
3. **Mark as Delivered / Processed.** A reversible list-filter flag; no key exists for it.
4. **Batch and serial on DN/GRN lines** (phase 51's keys on a second ledger).
5. **Per-line warehouse** on both forms.
6. **Warehouse Transfer in Custom Fields**, a vendor change noticed in passing.
7. **The Invoice availability `PrimaryQuantity` bug** (bug 9), filed as a separate task.
8. Hiding **Void** on a received PO or delivered SO. The server refuses it with a clear 409, and the
   button still shows.
