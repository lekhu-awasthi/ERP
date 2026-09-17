# Phase 52 — A unit on the document line

## TL;DR

**The phase's central question had a live answer, and it was neither of the two the kickoff
framed.** The kickoff posed *a stored factor versus a live lookup*. The reference product stores
neither — it stores **the result**. This codebase stores the **factor** and derives the result, which
gets the same immutability for one fewer column.

- A line carries `UnitId` (the unit **lookup**'s id, not the product's secondary-unit row) and
  `ConversionFactor`, frozen at Create/Update. `PrimaryQuantity` is a **derived** property,
  `Quantity × ConversionFactor`, and never a column — a third column would be a second quantity able
  to contradict the other two, which is phase 51's `ProductBatch` argument and phase 37's
  two-of-three-views failure.
- **The money is untouched.** Choosing a secondary unit sets the line's Rate from that unit row's own
  price, and Amount stays `Quantity × Rate` in the *entered* unit. A secondary unit is two
  independent things — a price-book row and a stock conversion — and they do not interact.
- **The ledger is always primary units.** `StockLedgerEntry` and `StockMovement` gain nothing.

| | |
|---|---|
| Live read taken | **yes** — 2026-09-17, Moonbeam UAT, read + four reverted writes |
| The conservation law, in SQL Server | `FifoLayers = InventoryAccount = MovementHistory = 6000.00`, **HOLDS** |
| The unit law, in SQL Server | `EnteredTimesFactor 48 = LedgerQuantityIn 48`, **HOLDS** |
| Line types carrying a unit | **8** (incl. Warehouse Transfer, which carries no price) |
| Line types deferred, unread | 4, each named with its reason |
| New permission keys | **0** — the unit rides the document's own |
| Migration | 1, purely additive: 16 `AddColumn`, 8 `CreateIndex`, 8 `AddForeignKey`, nothing else |
| Bugs the sweep caught | **3** in C#, **1 class** (7 of 8 forms) in TypeScript |
| Tests | Domain **703** (+15), Application **1266** (+54), Infrastructure 12, Angular **585** (+9) |
| `ng build` | clean, **643.68 kB** against the 680 kB budget |

**The value type is the phase's real mechanism.** `PrimaryQuantity` is a `readonly record struct`
with no implicit conversion from `decimal`, so `IStockLedgerService` cannot be called with a raw line
quantity. That made the compiler enumerate **17** ledger call sites across 12 files and then, via
required `AddLine` parameters, **16** Create/Update handlers — and it caught three real bugs that no
test saw. Phase 51's lesson was *a sweep driven by the compiler stops exactly where the compiler
stops*; this phase's refinement is that **you can choose where the compiler stops.**

---

## Decision A — the live read was taken, and it changed the phase

Phase 51 invoked the phase-8f rule and designed from the product tabs. This phase asked first, and
the answer was yes — so the design is observed, not derived. The full record is an appendix in
`docs/erp-module-scan.md` dated 2026-09-17; the four findings that changed decisions:

1. **The control is inside the Qty cell**, not a column. The grid is still
   `Product / service | Qty | Item Batch | Rate | Discount | Tax | Amount`. For a product with only
   its primary unit the vendor renders a greyed `cursor: not-allowed` **label**; for one with
   secondary units, a select listing the **whole** matrix, primary included.
2. **The factor never touches the money** (Decision B).
3. **Eight line types carry it**, including `warehouse-transfers`, which has no price at all — so the
   rule is *every line naming a product and a quantity*, not *every priced line*. A list sampled from
   the sales and purchase forms would have been wrong (phase 30).
4. **`secondary_units[]` is the whole matrix including the primary**, flagged `is_primary: true` at
   rate 1 — the payload-side mirror of phase 45's observation.

**The falsification.** The roadmap's `100BTL` is a **paraphrase**; that string appears nowhere in
`erp-module-scan.md`. What the scan records is `Qty+unit` on the Invoice detail panel, `Qty 1 cyl`
in the hands-on pass, and `unit` on the `QuotationLine`/`InvoiceLine` data model.

---

## Decision B — store the factor, derive the quantity (a deliberate divergence)

**The experiment.** A Purchase Bill was approved on the reference tenant for `2 BTL` of a product
whose Carton-to-Piece rate was 12. Its movement row read:

```
quantity 2  unit BTL  rate 1200  total 2400
primary_quantity 24   primary_in_quantity 24   ValuationRate 100
```

The rate was then changed to 6. The vendor offers **no edit** — the Secondary Unit tab's `Action`
column is a **delete icon only** — so changing it meant deleting the row outright and re-adding it.
The delete was **not guarded**: it succeeded with a plain "Are you sure?" while an approved bill
referenced that row. Afterwards, and again after re-adding at 6:

```
bill line     2 BTL @ 1200 = 2400                                   unchanged
movement      primary_quantity 24, ValuationRate 100                unchanged
Overview in BTL   0.333 BTL   <- was 0.167 BTL over the same 2 PIS   CHANGED
```

A live lookup would have said 12. **The conversion's effect is part of the document's history; only
the display re-expression is live.**

**Why the factor and not the result.** The vendor stores `primary_quantity`. Storing
`ConversionFactor` instead gets identical immutability — both inputs are frozen on the same row, so
the derivation reads nothing that can change underneath it — for one fewer column, and avoids a
second quantity that `Quantity` and the factor can contradict. The factor is also the half a human
reads on a printed bill ("1 CTN = 12 PIS"); the product is not. Recorded in `UnitConversion`'s doc
comment and asserted in both directions by `UnitSweepGuardTests`.

**Why the line names the unit lookup, not the product's row.** That is what makes a document survive
its own catalogue: on the reference tenant the BTL row was deleted and the bill still read `2 BTL`,
because the line had never pointed at it. Reproduced here as
`Deleting_the_products_unit_row_does_not_move_a_line_already_written`.

**Scales, decided once.** `UnitConversion.QuantityScale = 4`, because that is exactly the precision
of the columns it lands in (`QuantityIn`, `QuantityRemaining`, `StockMovement.Quantity` are all
`decimal(18,4)`) — so rounding happens once at the boundary rather than invisibly in the database.
`FactorScale = 6`, for `ExchangeRates.RateScale`'s reason. A factor **below one is ordinary**:
`Maida 50 kg` on the reference tenant has primary `bag` and secondary `NOS` at **0.02**, so a
`factor >= 1` rule would reject real data.

---

## Decision C — the factor is snapshotted at Create/Update, not at Approve

Phase 51 minted batches at Create/Update so a draft reads back what it will post; document numbers
go the other way because they are a statutory sequence a discarded draft must not consume. A factor
is neither, but the draft argument applies unchanged. An Update re-resolves, because the line rebuild
is already wholesale.

**Stated honestly:** the live evidence settles what a *post-approval* edit does. It does not
distinguish create-time from approve-time freezing. This is our own precedent applied, not a reading.

---

## Decision D — `StockLedgerEntry` gains nothing, and neither does `StockMovement`

The ledger half is live-confirmed: the Inventory Ledger report has **no unit column at all**, and the
vendor's movement carries `primary_in_quantity` in primary units.

The `StockMovement` half is a **decision**, and the kickoff asked for it to be said rather than left
to omission. The vendor's movement row carries both representations — but phase 51 made ours one row
**per relief**, so a serialised line of five is five rows and an entered quantity would have to be
apportioned across them. That is a second quantity by another name. The entered unit is presented
from the **line**, which every document view already joins to.

---

## Decision E — which line types carry a unit

**Eight, from the read:** Quotation, Sales Order, Invoice, Credit Note, Purchase Order, Purchase
Bill, Debit Note, Warehouse Transfer.

**Four deferred, and they were _unread_ rather than excluded:** Opening Stock, Inventory Adjustment,
Production Journal, Bill of Materials. The reference tenant had none of those documents — the list
endpoint 404'd on its first row — so this is the phase-8f rule again, at a smaller scale. Asserted in
the negative by `UnitSweepGuardTests.The_deferred_line_types_still_carry_no_unit`, so a later phase
that gives one of them a unit has to delete a line and re-read the reason.

**Phase 51's three refusals are not a precedent here, and the asymmetry has a reason.** Those paths
were refused because they create or destroy a layer with no source to inherit a batch from, *and a
batch changes which FIFO layer is chosen*. A unit changes nothing about layer selection — it is a
pure multiply on the way in. There is no unanswered question, only more sweep.

**BOM is the one with a real open question**, recorded rather than waved through: the scan shows a
BOM's *Raw Materials* table carrying `Qty` **and** `Qty/Unit`, where `Qty/Unit` is a per-output-unit
ratio. How an entered unit interacts with that ratio is an interaction nobody has posed, which is
phase 45's rule for not deciding it.

---

## Decision F — the sweep mechanism, and the three bugs it caught

`IStockLedgerService.IncrementAsync` / `ConsumeAsync` / `PreviewConsumptionCostAsync` take
`PrimaryQuantity`, not `decimal`. There is no implicit conversion and no public constructor: the only
ways in are `FromEntered(quantity, factor)` and `AlreadyPrimary(value)`, and each forces the caller
to say which kind of quantity it holds.

**Three bugs, none of which any existing test saw:**

1. **`VoidInvoice` restocked `line.Quantity`** — the *entered* quantity. A voided invoice for 2
   cartons would have put 2 pieces back instead of 24. Phase 43's shape exactly: *changing what a
   document does to the stock ledger changes what its Void owes.*
2. **`VoidDebitNote`** had the same defect in the same shape.
3. **`ApprovePurchaseBill` computed the FIFO layer's unit cost as `(Amount + allocated) / line.Quantity`.**
   It now divides by the primary quantity — which is what produces the `ValuationRate 100` the live
   experiment showed for `2 BTL @ 1200`, and what the E2E then reproduced exactly.

Plus three **COGS accumulators** (`ApproveInvoice`, `ApproveCreditNote`, `ApproveDebitNote`) that
multiplied a per-primary-unit FIFO cost by the entered quantity, and `ApproveDebitNote`'s
landed-cost release proportion, which compared a note quantity against a bill quantity that could be
in another unit.

**Required parameters alone would have caught the first group and none of the arithmetic.** That is
the argument for the type over the cheaper option.

### The TypeScript half repeated the lesson from the other side

Making `EditableLine.unitId` **required** let `ng build` enumerate the line mappings, and it found
**eleven** — the Invoice form alone has three (load, conversion prefill, inbox prefill).

Then the **save** path proved the converse. `unitId` is *optional* on the `*LineInput` records, so
nothing failed to compile — and a hand check found **7 of the 8 forms would have saved without it**,
passing all 576 tests while writing a carton line as pieces. That is phase 51's un-swept
`IncrementAsync` in another language, and only a written checklist catches it.

---

## Decision G — the conversion-cap key gains the unit

`GetInvoiceRemainingByLineAsync` and its purchasing twin key a line by
`(ProductId, Rate, VatRate, DiscountPct)` and cap a return against the remaining quantity. Without
the unit in that key, an invoice line of `2 CTN` and one of `5 PIS` at the same rate would be summed
into `7` of nothing. The key is now a 5-tuple, in all **nine** grouping sites plus the two declared
return types, and the two return-conversion templates read `kv.Key.UnitId` so the prefilled quantity
is denominated in the unit it was capped in.

---

## Decision H — no new permission keys

A unit rides the document's own key, as variants did (24) and batches did (51). Demonstrated in the
E2E: a custom role holding only `Purchasing.PurchaseBill.View` gets **403** on
`POST /purchase-bills` naming `Purchasing.PurchaseBill.Create` — the write path the unit rides on.

---

## Decision I — no importer, and the inbox prefill carries no unit

**No importer changes, and the reason is not "no time":** no importer imports a document line. The
nine master-data importers import master data, and the two migrated register importers carry **no
`Quantity` at all** (verified).

**The document inbox deliberately carries no unit.** The five document-to-document conversion
templates carry it because their source is a line in this database. The inbox's source is a
supplier's scanned paper bill read by an extractor whose schema has no unit field, so carrying one
would mean inventing it or widening what tenant data goes to a third party (phase 22) for a field
the user can set in one click. Recorded on `InboxPrefillLineDto` with a re-entry condition, because
phase 35a's rule would otherwise make it look forgotten.

---

## What the manual E2E proved

A fresh Organization, master data seeded entirely by curl, browser clicks reserved for this phase's
own UI. Every status code printed.

### The two laws

```
--- what the LINES stored (entered unit x frozen factor) ---
Document      Quantity  ConversionFactor  PrimaryQuantity  Amount
PurchaseBill    2.0000         12.000000          24.0000  2400.00     <- curl, in CARTONS
PurchaseBill    3.0000          6.000000          18.0000  3600.00     <- BROWSER, after the edit
PurchaseBill    1.0000          6.000000           6.0000   600.00
Invoice         6.0000          1.000000           6.0000  1200.00     <- curl, in PIECES

--- what the LEDGER received (always the primary unit) ---
QuantityIn  QuantityRemaining  UnitCost
    6.0000             6.0000  100.0000
   18.0000            18.0000  200.0000
   24.0000            18.0000  100.0000     <- 2400 / 24, the live ValuationRate reproduced

FifoLayers  InventoryAccount  MovementHistory  Law
   6000.00           6000.00          6000.00  HOLDS

EnteredTimesFactor  LedgerQuantityIn  Law
           48.0000           48.0000  HOLDS
```

### The rate edit — the test the first decision exists for

The product's Carton rate was edited `12 → 6` through the API, and **every number above was
re-read**. The approved bill still said `conversionFactor 12`, the ledger still held `24 @ 100`, and
both laws still held. A **new** bill created afterwards took factor **6** and produced 6 primary
units. The catalogue governs the future and never the past.

### The refusal

```
400  POST /invoices naming a unit the product lacks
     {"errors":{"Lines[0]":["That unit is not on this product's unit list, so there is no
      conversion rate to apply. Choose the product's primary unit or one of its secondary units."]}}
```

### The permission proof, with its control

```
404  POST /purchase-bills/{nonexistent}/approve   as Admin      "Purchase bill not found."
403  POST /purchase-bills/{same id}/approve       restricted    (Purchasing.PurchaseBill.Approve)
200  GET  /purchase-bills                         restricted    same user, same run
403  POST /purchase-bills                         restricted    (Purchasing.PurchaseBill.Create)
```

The 404-as-Admin is the control that makes the 403 mean the behavior fired *before* the handler.

---

## The browser pass, and the two things it caught

Driven on the phase-25 Step 3 recipe (dev cert, `erp-web-ssl`, curl's `erp_auth` transplanted via
`document.cookie`), against the same organization the curl half built.

A whole Purchase Bill was authored through the UI: supplier, warehouse, product, unit **CTN**, Qty 3,
Rate auto-filled **1200** from the carton's purchase price, Amount **3,600.00**. It saved, **read the
unit back after a reload**, approved, and landed in the ledger as 18 primary units at 200 — with both
laws still holding.

| Checked | Result |
|---|---|
| Control position | between `Qty` and `Item Batch` in the a11y tree, inside the Qty cell |
| Accessible name | `combobox "PIS"` with `aria-label="Unit"` |
| Options offered | `PIS`, `CTN` — the whole matrix, primary first |
| Rate prefill | `CTN` → 1200; Amount `3 × 1200 = 3,600.00`, **not** `× 12` |
| Read-back after reload | `CTN` |
| Mobile (375 px) | no page horizontal scroll (375 = 375); the table scrolls inside `.table-responsive` |
| Console | clean (the one error was my own mistyped URL, not the app) |

**Two defects the pass caught, both mine:**

1. **The read-only branch showed a bare quantity.** My wiring script wrapped only the draft `<input>`
   and never touched the `@else` branch, so an approved line rendered `3` where it should read
   `3 CTN`. Seven forms; the Invoice, which I had wired by hand, was correct. **A scripted sweep
   covers exactly the shape its pattern names**, and the read-only branch is a different shape.
2. **The Qty input was crushed to a few pixels.** The column is 100–110 px and now had to hold a
   number input *and* a select. I noticed the symptom during the pass, mis-read it as a bad click,
   and moved on; **the user hit it and reported it.** The column is now 190 px with a `min-width:
   5rem` floor on the input, verified at `1234.56` fully visible. The screenshot was ground truth
   and I did not look hard enough at it — phase 34b's rule, failed and then relearned.

A third, smaller: my wiring script silently renamed Warehouse Transfer's input label from
`"Quantity"` to `"Qty"` while its visible header still said Quantity — a label-in-name break (WCAG
2.5.3) that I introduced and reverted.

---

## Review pass — two defects the phase did not go looking for

1. **Seven header "Discount %" inputs carried `aria-label="Qty"`** — on every sales and purchase
   form, since those forms were written, surviving phase 40's accessibility pass. CLAUDE.md's own
   line is that *a wrong accessible name is worse than none*. Fixed in all seven.
2. **`ListProductsQueryHandler` never loaded `SecondaryUnits`**, so the Angular `Product.secondaryUnits`
   field was declared and permanently empty — the picker could not have known a product's units.
   Now `.Include`d, matching the vendor's own picker call (`products-minimized?…&unit=true`).
   **No performance number is claimed for this**, and the code comment says so: it is a join over the
   page, but `listAllProducts` asks for a very large page (phase 34c's rule).

---

## Known limitations / carried items

1. **Four line types have no unit** (Decision E) — Opening Stock, Inventory Adjustment, Production
   Journal, BOM. Unread, not excluded. BOM additionally has a real open question (`Qty/Unit`).
2. **Reports mean the primary unit, always.** The vendor's product Overview has a `Unit:` selector
   that re-expresses stock live (`2 PIS` → `0.167 BTL`); this phase did **not** build it. Every
   quantity-bearing report continues to read the primary unit, which is now a stated answer rather
   than an accident.
3. **No performance measurement was taken and none is claimed.** The `.Include` on the product list
   is the one plan change; phase 51's `StockLedgerEntry` index is still unmeasured.
4. **A restricted role cannot restore itself.** The E2E moved its own user onto a custom role with
   no `Tenancy.Role.Manage`, which made the restore a **403** — the only way back was
   `UPDATE tenancy.OrganizationMemberships`. Phase 51's lesson was "restore the role"; the sharper
   version is that on this path you *cannot*, through the API.
5. **The reference tenant carries one voided Purchase Bill** (`UOM-TEST-52`) from the write pass,
   with its reason recorded. The unit matrix and stock were restored and verified identical.

---

## Tests

| Suite | Before | After |
|---|---|---|
| Domain.UnitTests | 688 | **703** |
| Application.UnitTests | 1212 | **1266** |
| Infrastructure.UnitTests | 12 | 12 |
| Api.IntegrationTests | 30 | 30 (unchanged; needs Docker) |
| Angular | 576 | **585** |

`dotnet build` / `dotnet test` / `ng build` / `ng test` all clean. `ng build` does not warn; the
initial bundle is **643.68 kB** against phase 42's 680 kB budget — unchanged, because the new shared
control lands in a lazy chunk.

The guard was proven to bite: a `ConvertedQuantity` property injected into `InvoiceLine` produced
exactly one failure naming that type, and the file was restored **by hash** (verified identical) and
`touch`ed so MSBuild rebuilt.
