# Phase 25 — Manufacturing (FR-8.8, FR-8.9, FR-9.5's manufacturing slice)

## TL;DR

**The confirm-live pass answered the phase's biggest open question, and the answer was "nothing".**
`erp-module-scan.md` §10 left it open — a Production Journal "likely emits GL Transactions
(**unconfirmed which accounts — open item**)". On 2026-09-02 that was closed **by experiment**: a
Production Journal was created and approved in the reference tenant (PJ0008, 02-09-2026) and it does
not appear anywhere in a **199-row Journal report covering that exact date**, while its stock moved
(the raw material's on-hand went 8896.5 → 8899.5, being −12 consumed +15 by-product). Production is
also absent from the Transaction list report's own type list, which *does* include Inventory
Adjustment and Warehouse Transfer. That tenant runs **periodic** inventory — its Purchase Bills debit
"Purchase Goods", a Direct Expense — under which a production journal genuinely has nothing to post.

**We post anyway, and that is Decision A.** This codebase is *perpetual*: since the post-Phase-19 fix
a Goods PurchaseBill debits `DefaultInventoryAccountId`, so that account is a real asset balance
meant to track the FIFO ledger. Posting nothing would leave it understating stock by exactly the
production expenses capitalised into finished goods, silently and forever. So the divergence is
reasoned, not accidental — the same shape as phase-16b's discount finding, where the live answer was
also "no GL account at all".

**The rest of the live pass was high-yield and changed three more decisions.** A Production Order's
native lifecycle **is** Draft → Approved (the scan's "Planned/InProgress/Completed" turned out to be
Phase 20b's **Custom Status** in the list grid, orthogonal to the lifecycle — Decision E); the
by-product "% of Cost" is a percentage **of the Total Cost of Production**, verified to the penny
against two real journals (Decision C); and "LOAD BOM" is an explicit, user-invoked template load
that scales by output ratio and leaves the percentage alone, confirmed by driving it (Decision D).
The reference product also **lets the same order convert to a journal repeatedly** — PRO0011 still
offered the button after PJ0013 was made from it — which is phase-6 bug #4 in the wild, and which we
refuse.

**The conservation law is the phase, and it is proven in the database, not asserted in prose:**

```
raw-material FIFO cost consumed  +  production expenses
        =  finished-goods stock value created  +  by-product stock value created  ( + rounding residue )
```

Against real SQL Server: 15 units of a raw material held in **two layers at different costs** were
consumed for 2000.0000 (a weighted average of 133.3333 — not 200, not 100, not the BOM's rate); with
300 of expenses and a 20% by-product the finished good entered stock at **184.0000/unit**; value in
2300.0000, stock value created 2299.99990000, residue **0.00010000** — exactly the figure the
document reports. Inventory's **net** GL movement was **+299.9999**, which is the production expenses
and nothing else; only two accounts were touched; the org's trial balance difference is **.0000**. A
second, clean-numbered run conserved **exactly, to the cent, with a zero residue**.

**Two bugs found, both by things unit tests structurally cannot reach.** A shared FluentValidation
helper took `Func` selectors instead of `Expression`, so **every one of the six manufacturing
endpoints returned a 500** the first time it was called against the real API — invisible to handler
tests, which never run `ValidationBehavior`. And the browser pass found the cost roll-up's
**"Rounding Adjustment" row rendering `0.00`**, because the residue is smaller than a cent and
`AmountPipe` is fixed at two decimals — a row that looks like a defect rather than a disclosure.

**Step 3's four carried screens are now browser-passed and the debt is closed**: `Import / Export`'s
Export half (Queued → Completed → Download, with the Download control correctly gated on *an
artifact exists*), `Organization > Migration`, and both Migrated Register reports (the "never posted
to the General Ledger" banner renders, and the footer totals match the SQL sums exactly). **No
defects were found in any of the four.** The three carried *confirm-live* items on the reference
product are also closed — see the end of Step 2.

Tests: Domain **249** (+19), Application.UnitTests **571** (+31), Api.IntegrationTests **18**
(unchanged), Angular **128** (+9). `dotnet build` / `dotnet test` / `ng build` / `ng test` /
`tsc --noEmit` all clean.

---

## Step 2 — what the live pass actually showed

Read on 2026-09-02 against the Tigg UAT tenant (Inventory > Bills Of Materials / Production Order /
Production Journal, and Reports > Inventory Report). The user signed in; no credentials were entered
or committed.

### Bill of Materials
Master data, no Draft/Approve lifecycle, no document number. List columns: Product Name, Finished
Goods (output quantity + unit), Raw Materials (item count), By Products (item count) — 23 rows.
Detail: Product, Output Quantity, a **"Manufacture on every sales."** checkbox, Notes, then three
tables — Raw Materials (Product, Qty, **Qty/Unit**), By Product (Product, **% of Cost**, Qty,
Qty/Unit), Expense (Expense Term, Amount, **Amount/Unit**). The per-unit columns are pure
derivations: BOTTLEE's output is 12, its raw material 12 (Qty/Unit 1), its by-product 15
(Qty/Unit 1.25), its expense 500 (Amount/Unit 41.67).

**No BOM picker exists anywhere.** "LOAD BOM" resolves the recipe from the chosen product alone,
which is what makes *at most one BOM per finished product* a real constraint rather than a guess.

### Production Order
List has **Approved / Draft** tabs, plus a **separate STATUS column in the grid** whose values are
"Completed" / "Select Status" — Phase 20b's Custom Status shape exactly (a per-row control in the
list, saving instantly). So the scan's "status lifecycle independent of Approved/Draft" was real but
misread: it is a *second, orthogonal* pipeline layered over the house lifecycle, not a replacement.

Detail: Date, code, Product, Output Quantity, Reference, Notes; **Raw Materials with Qty only**; a By
Product table with % of Cost and Qty; an Expense table **with Amounts**. No Warehouse field. Banner:
"You can convert this transaction to Production Journal".

Create form: Code reads **DRAFT** until approval, and a **LOAD BOM** button appears only once Product
*and* Output Quantity are both set. Driving it for an output of 24 against BOTTLEE's BOM (output 12)
returned raw 24, by-product 30 at **12%** and expense 1000 — quantities and amounts scaled by the
output ratio, **the percentage untouched**.

### Production Journal
List has Approved / Draft tabs (25 approved). Create form: Date, Reference, Code, Product Name,
Output Quantity, **Warehouse** (required — unlike the Order), Raw Material (Product, Quantity,
**Rate**, Amount), Production Expenses ("Production Cost Terms"), By product, Notes, a **live cost
roll-up box**, Custom Fields and Reporting Tags. Approving stamps the number (PJ0008). OPTION menu:
Edit / Make Duplicate / **Void this Production Journal** / Print.

The raw-material **Rate is pre-filled from stock cost and editable** — observed as `0.097341`, six
decimals, clearly derived rather than typed.

**The roll-up, read off a live draft with real numbers** (output 12, raw 12 @ 0.097341, one 500
expense, a by-product at 12% over 15 units):

| Line | Value | Check |
|---|---|---|
| Raw Material Cost | 1.17 | 12 × 0.097341 = 1.168092 |
| Production Expenses | 500 | |
| Total Cost of Production | 501.17 | raw + expenses |
| Cost Allocated to By-product | 60.14 | **12% × 501.168092 = 60.140171** |
| Finished Goods Cost | 441.03 | total − by-product |
| Cost Per Unit | 36.75 | 441.027921 ÷ 12 |
| (by-product Rate) | 4.01 | 60.140171 ÷ 15 |

Cross-checked against approved journals in the same tenant: PJ0001 (raw 400,000 + expenses 400,000 =
800,000; by-product at 5% = 40,000; finished 760,000 over 500 units = 1,520/unit) and PJ0004
(raw 6,800 + expenses 416.67 = 7,216.67; by-product 12% = **866.0004**; finished 6,350.67 over 10 =
635.07). Both conserve exactly. **The percentage is of the Total Cost of Production**, confirmed
twice.

Persisted precision is worth noting: the saved draft stored `Amount 1.168092` and
`AllocatedAmount 60.14017104` at full precision while displaying 2 dp, and stored the by-product's
`Rate` as the rounded 4.01. **The amount is the fact; the unit rate is a rounded derivation.** Its
own PJ0006 rolled 3250 into 240 units at a displayed 13.54, which multiplies back to 3249.60 — the
reference product has the same rounding residue and simply does not show it.

### The GL open item, closed by experiment
Journal report over 17-07-2026 → 02-09-2026, **all 199 rows** (rows-per-page raised to 200 so nothing
was hidden): six Production Journals fall inside that window and **not one GL entry** references any
of them. Creating and approving PJ0008 on 02-09-2026 — a date the report covers, with two other
02-09-2026 transactions listed above it — added **nothing**. Its stock moved: the Planning report's
availability for the raw material went 8896.5 → 8899.5 (−12 consumed, +15 by-product).

The Transaction list report's Txn Type set is Quotation, Sales Order, Invoice, Customer Payment,
Credit Note, Purchase Order, Purchase Bill, Expense, Supplier Payment, Debit Note, Journal Voucher,
Cash Transfer, Quick Payment, Quick Receipt, **Inventory Adjustment, Warehouse Transfer** — production
is not in it.

Corroborating detail: the **Production Summary Report carries DR Account and CR Account columns on
its Production Expenses block, empty for every row.** The product models the idea and that tenant
posts nothing.

### The three manufacturing reports — all three are real, and all three are computable from our data
- **Production Summary** — filters Period + Product Category + Product. Column blocks: Date, Voucher
  No, Reference No | *Finished Goods Produced* (Item, Quantity Produced, Rate, Amount) | *Raw
  Material Consumed* (Item, Quantity, Rate, Amount) | *By Product Produced* (same four) | *Production
  Expenses* (Cost Term, Amount, DR Account, CR Account).
- **Production Variance** — same filters. Columns: Date, Voucher No, Ref No, Item Name, Quantity
  Produced | Item Name, **Voucher Quantity, BOM Quantity, Variance Quantity, Variance %**. Only
  journals carrying a BOM appear (2 of the 7 in the window). Variance = BOM − Voucher; % = Variance ÷
  BOM.
- **Production Planning** — **not a period report**: Product + Quantity. Header shows Product,
  Quantity to be produced, **Multiple Level: No**. Columns: Item Name, Unit Of Measurement, Quantity
  Required, Quantity Available, Surplus/(Deficiency). Raw materials only.

So the Phase 8f Annex 5 trap did **not** apply here: none of the three needed guessing, and all three
shipped.

### Two-minute items
- **Finding 8 refuted.** A Production Journal does not appear in Allocate Supplier Payment's Type
  column: production is absent from the Transaction list's type set, no GL or payable is created, and
  there is no supplier field anywhere on the Journal's form or detail. (The Allocate Supplier
  Payments screen itself errored server-side on the day, so this is refuted on the weight of the
  other evidence rather than by reading that column directly.)
- **"Manufacture on every sales"** is a checkbox on the BOM with no observable effect in the tenant
  (unchecked on every BOM opened). Stored, not honoured — see Decision D.

### The three carried confirm-live screens (21b/21c) — all closed
- **`Organization > Developer Mode`** — API credential management: "Generate API Keys", a Client ID
  with a Secret Key, a Revoke control, and a "Go to API Playground" link. **Real substance, but a
  *platform* feature** (machine-to-machine API credentials), not an ERP one: this rebuild has no
  public API programme and no OAuth client model. Recommend dropping it from the roadmap rather than
  carrying it further.
- **`Organization > Documents`** — a bare "Drop your files or Click to upload new document" zone at
  `#/config/organization/attachments`. That is Phase 18's polymorphic `Attachment` with
  ParentType=Organization and nothing else. Thin; no FR asks for it. Recommend dropping.
- **`Organization > Migration`** — "Migrated Reports" with an IMPORT button and two links, Sales
  Register and Purchase Register. **Exactly what Phase 21c built.** Confirms the design; nothing
  outstanding.

---

## Decision A — what a Production Journal does to the General Ledger

**Alternatives considered.**

1. **Post nothing** (parity with the reference product). Correct under *periodic* inventory, wrong
   here: our Inventory account is a perpetual asset balance, so it would understate stock by the
   capitalised expenses, permanently and silently.
2. **A WIP control account.** Debit WIP the raw materials and the expenses, credit WIP the output.
   A Production Journal is atomic — one document is one complete production event — so WIP is
   debited and credited inside the same entry and **always nets to zero**. An eleventh account for
   no information. Rejected.
3. **Credit each Cost Term's own expense account.** `CostTerm` has no `AccountId`, and the
   semantics would be identical to (4) with more moving parts. Rejected.
4. **Inventory → Inventory, with the difference on one absorption account.** Chosen.

**The entry, posted gross rather than netted** so the Journal report shows the real transformation:

| Leg | Debit | Credit |
|---|---|---|
| Inventory | finished value + by-product value | |
| Inventory | | raw-material cost consumed |
| Production Cost Applied | | the difference, when non-zero |

**Net effect, traced per account (phase-6 bug #3's discipline).**
- **Inventory** nets to `(finished + by-product) − raw`, which is exactly the production expenses
  capitalised into stock. **Not zero, and not the full raw-material value.**
- **Production Cost Applied** nets to a credit of that same figure — a contra-expense absorbing into
  inventory the labour/overhead the tenant already booked to real expense accounts through
  Expense/PurchaseBill documents.
- **Nothing else is touched.** No COGS, no purchase expense, no payable.
- Downstream, when the finished good sells, `InvoicePostingRule`'s COGS leg debits COGS and credits
  Inventory at the FIFO cost computed here — so the expense reaches the P&L at the point of sale
  rather than the point of production, which is the entire point of capitalising it. **No double
  count**: the raw material was capitalised at purchase, not expensed.
- `sum(Debit) = finished + by-product`; `sum(Credit) = raw + (finished + by-product − raw)`.
  **Balanced by construction, for any inputs**, including the expense-free case where the two
  Inventory legs are equal and the third line is omitted entirely.

**The eleventh account.** `TenantSettings.DefaultProductionCostAccountId` — the only new one, read
solely by `ProductionJournalPostingRule`. Deliberately **not** a reuse of
`DefaultInventoryAdjustmentAccountId`: production is not an adjustment, and folding them together
would make that account's balance unreadable as either. It is required at Approve
**unconditionally**, like `DefaultInventoryAccountId`, because the posting leg can be non-zero even
with no expense lines (see Decision B's residue).

Per the phase-7 addendum's lesson, every account field this rule reads was grepped for other
readers: `DefaultProductionCostAccountId` has exactly one (`ApproveProductionJournalCommandHandler`),
and `DefaultInventoryAccountId`'s existing readers (`PurchaseBillPostingRule`, `InvoicePostingRule`,
`InventoryAdjustmentPostingRule`) are unchanged by this phase.

---

## Decision B — where the cost arithmetic lives, and what is stored

**Where.** In the Domain, `ProductionJournal.ComputeAndRecordRollUp()`, not the handler. The handler
supplies the one thing Domain cannot know — what `ConsumeAsync` actually returned per raw line — and
then creates the stock layers and the GL entry from the figures recorded. That is what makes the
conservation law provable **without a database**
(`Domain.UnitTests/Manufacturing/ProductionJournalCostRollUpTests`).

**What is stored.** Per raw line, `ConsumedUnitCost` **and** `Amount`; per by-product line,
`AllocatedUnitCost` and `AllocatedAmount`; on the header, `RawMaterialCost`,
`ProductionExpenseCost`, `CostAllocatedToByProduct`, `FinishedGoodsCost` and
`FinishedGoodsUnitCost`. `TotalCostOfProduction` and `CostRoundingAdjustment` are **derived** and
`Ignore`d in EF, exactly like `Invoice.GrandTotal`: both are pure functions of stored figures, so
neither can drift.

Storing a line's `Amount` as well as its unit cost looks redundant and is not: `ConsumeAsync` returns
an **unrounded** weighted average whose stored form is rounded to the column's scale, so the
multiplication has to happen on the unrounded value for the journal's Raw Material Cost to equal, to
the cent, what the ledger gave up. This is the same split `ApproveInventoryAdjustmentCommandHandler`
already uses.

### The rounding residue, and why it is named rather than hidden

A FIFO layer stores a **unit cost**, not a value, so the value it represents is
`Quantity × Round(cost, 4)`. Whenever the finished-goods cost does not divide evenly by the output
quantity at four decimals, the layer is worth a fraction of a cent more or less than the cost that
went in. That residue is structural — the reference product has it too and does not show it.

The design:
- Every unit cost is rounded to **exactly `StockLedgerEntry.UnitCost`'s own scale** (4), so the
  document and the ledger can never disagree. `ProductionJournal.UnitCostScale` exists to say so.
- By-products are costed **first**; the finished good gets the remainder.
- The GL is built from the values **actually created**, so it balances by construction.
- `CostRoundingAdjustment` reports the difference, bounded by `OutputQuantity × 0.00005`.

It is zero for any ordinary whole-quantity run, and was zero in the clean E2E run.

---

## Decision C — by-product cost allocation

**Percentage of the Total Cost of Production** (raw material cost + production expenses), not of the
raw material alone. **Observed, not inferred**: 12% of 501.168092 gave 60.14017104 and 5% of 800,000
gave 40,000, both to the penny, on real reference-tenant journals.

- Each by-product's allocated cost is `Total × Pct ÷ 100`; its unit cost is that divided by its
  quantity, rounded to 4 dp; **its FIFO layer is created at that unit cost.**
- The finished good is allocated `Total − (the by-product layers' actual value)`. Subtracting the
  *actual* value rather than the theoretical allocation is what keeps the two sides equal.
- **The percentages must total strictly less than 100**, enforced in the Domain
  (`EnsureByProductAllocationIsSane`) and echoed in both editors before save. At exactly 100 the
  finished good enters stock at zero cost, and a zero-cost FIFO layer makes every future sale of it
  100% margin — a worse outcome than a refusal, so it is refused with the real total named
  (phase-24 Decision F's precedent).

`ProductionJournalCostRollUpTests.Allocating_to_a_by_product_never_creates_value_from_nothing` is the
test that exists specifically for the failure this decision is about.

---

## Decision D — the BOM's authority

**A template that defaults, never a constraint that binds.** Live-confirmed: "LOAD BOM" is an
explicit button the user presses, it fills editable rows scaled by output ratio, and nothing
afterwards re-checks a document against its BOM. `ManufacturingValidation.EnsureBillOfMaterialsExists`
deliberately does *not* require the BOM's product to match the document's, because re-checking would
turn a default into a constraint.

`BillOfMaterialsId` is **nullable** on both the Order and the Journal: a production run typed by hand
is perfectly legitimate, and the only consequence is that it does not appear in the Variance report —
there is nothing to vary against.

**"Manufacture on every sales" is stored and deliberately not honoured.** Auto-raising a Production
Journal inside `ApproveInvoiceCommandHandler` would create a costed document that consumes FIFO stock
and posts GL, with no human on a form, no permission check of its own and no warehouse chosen — the
exact shape phase-22's Decision B rejected. It is kept as a field so a BOM edited here round-trips
it, and the editor **says so on the control**: *"Recorded for reference only. This build does not
raise a production journal automatically."* That is phase-21b's Decision A precedent (say it on the
control rather than ship the word).

One BOM per finished product, enforced by a unique index on `(OrganizationId, ProductId)` and a 409
in both Create and Update. Not a filtered index — `ProductId` is non-nullable here, so phase-24's
nullable-column caveat does not apply.

---

## Decision E — the two lifecycles

**Production Order: the house Draft → Approved → Converted/Void.** The scan's
"Planned/InProgress/Completed" is Phase 20b's Custom Status in the list grid, orthogonal to the
lifecycle — and phase-20b's own test for whether a candidate pipeline is genuinely orthogonal comes
out the *other* way here from Cheque's: these values do not mirror the native lifecycle, they sit
beside it. So the native lifecycle is the house one, and the observation was of a second pipeline.
(Adding Production Order to the Custom Status document-type list is a clean follow-up; it is not
built here.)

**`Converted` is our addition, not parity.** The reference product still offered "Convert to
Production Journal" on PRO0011 *after* PJ0013 had been created from it a minute later — phase-6
bug #4 exactly. `ProductionOrder.MarkConverted()` refuses anything but Approved, and
`CreateProductionJournalCommandHandler` calls it whenever the request names an order as its referrer.
Setting `ReferrerType`/`ReferrerId` still enforces nothing by itself; `MarkConverted` is the gate.
Proved twice: a second conversion is a 409, and the conversion *template* refuses too (a courtesy —
declining to prefill stops nobody posting the command directly).

**Production Journal gets a Void, and it is the first one that unwinds in both directions.**
- Stock **created** (finished good + every by-product) is reversed by `ReverseIncrementAsync`, which
  refuses the whole void with a 409 if any of those layers has been consumed onward. That is exactly
  right here and is the interesting case: once some finished goods have been sold, the run cannot be
  pretended away, because the sale's COGS was computed from the very cost this would erase.
- Stock **consumed** is put back at each line's recorded `ConsumedUnitCost`, mirroring
  `VoidInventoryAdjustmentCommandHandler`'s restock of a Decrease line.
- The GL is reversed by `PostReversalOf`, which mirrors the original's own posted lines rather than
  re-deriving them (phase-16a's guarantee against phase-6 bug #3).
- **Order matters**: `ReverseIncrementAsync` runs first, so a partly-consumed run fails before
  anything at all has been mutated. Verified: after a refused void the run was still `Approved` with
  its three GL lines and the raw material still consumed.

Production Order gets a Void too (mirroring PurchaseOrder), and a Converted order cannot be voided —
`Void` only accepts `Approved`, so no extra check was needed.

---

## Decision F — raw-material availability

`IStockAvailabilityPolicy.CheckAsync` takes a concrete `Invoice`. Options were to generalise it over
a new `IStockConsumingDocument` abstraction (a new abstraction for two callers), add a parallel
policy (two homes for one rule — the way a Reject tenant ends up rejecting invoices but warning on
production), or **add one document-agnostic method to the existing interface**. The last was chosen:
`CheckRequirementsAsync(organizationId, warehouseId, requirements)`, with `CheckAsync(Invoice)`
becoming a thin adapter over it. One implementation, one place where `NegativeStockBalanceAction` is
consulted.

The namespace stays `Application.Sales.Stock` deliberately — moving it to `Inventory.Stock`, where it
now arguably belongs, would touch about twenty files for a rename, so Manufacturing takes the one
odd-looking `using` instead. Recorded as a wart, not an oversight.

A Journal short of raw stock therefore hits the tenant's **real** policy: Reject is a hard 409, Warn
is a confirmable 422 that a second Approve with `overrideWarning` proceeds through, DoNothing passes.
Both branches are covered by handler tests.

---

## Decision G — feature gating, permissions, reports

**Gate.** Every manufacturing command and query declares
`[TenantFeature.Manufacturing, TenantFeature.TrackInventory]` — two entitlements, because production
is entirely a stock operation and a tenant without inventory tracking has no ledger to consume from.
WarehouseTransfer set the two-feature precedent. Unlike `MultipleWarehouses` (phase-20f), a **hard
block is right here**: a Manufacturing-off tenant loses nothing it could otherwise do, so there is no
risk of wedging it. The dashboard hides the whole manufacturing surface behind the same two flags.

Proved both ways against real tenants: a Manufacturing-off organization gets 403 on reads, on writes,
and **on a read against a nonexistent id** (so the gate fired before the handler); a Manufacturing-on
organization gets 200 on the identical call.

**Permissions — three shapes, derived per subject rather than defaulted.**

| Subject | Keys | Member |
|---|---|---|
| `BillOfMaterials` | View / Manage | View ✔, Manage ✘ |
| `ProductionOrder` | View / Create / Edit / Approve / Void | View, Create, Edit ✔; Approve, Void ✘ |
| `ProductionJournal` | View / Create / Edit / Approve / Void | View, Create, Edit ✔; Approve, Void ✘ |
| `ProductionReport` | View | ✔ |

A BOM is master data — a recipe, curated rarely and read constantly, with no lifecycle and no
document number — so it takes `ProductCategory`/`UnitOfMeasurement`'s exact View/Manage split. The two
documents take `InventoryAdjustment`'s transactional split, which matters more here than elsewhere:
approving a Journal permanently consumes FIFO layers and writes the cost every future sale of the
finished good will read. `ProductionReportView` is one shared View-only key for all three reports, on
`InventoryLedgerView`'s precedent.

13 keys, 26 seed rows, ids `…-0002-000000000155` through `…-0002-00000000016e`, added through
`RolePermissionConfiguration.HasData` before the migration was scaffolded.

**Reports: all three shipped.** The Phase 8f trap did not apply because the live pass answered every
one of them (see Step 2). One deliberate correction to what was observed, in the Variance report — see
below.

---

## The one deliberate divergence in the Variance report

The live report's **BOM Quantity appears not to be scaled to the journal's own output**: a run
producing 10 against BOTTLEE's BOM (output 12, raw material 12 — a 1:1 ratio) reported a BOM Quantity
of 12.5 and a **36% variance**, which compares a plan for one batch size against a run of another.
Here the BOM quantity is scaled by `journal output ÷ BOM output` first, so the same run reports a plan
of 10 against an actual of 8: a variance of 2, or 20%. Anything else labels a correctly-sized run as
variant purely because the batch sizes differ.

`ManufacturingReportQueryHandlerTests.The_variance_report_scales_the_bom_plan_to_the_runs_own_output_before_comparing`
uses those exact numbers.

`ProductionPlanning` adds an **optional WarehouseId** the live report does not have. Null gives the
reference product's all-warehouses figure; supplying one narrows it, because our FIFO layers are keyed
`(ProductId, WarehouseId)` and a Journal consumes from exactly one warehouse — a planner who already
knows where the run will happen would otherwise be shown a number that cannot be consumed.

---

## Bugs found

### 1. Every manufacturing endpoint returned 500 — a FluentValidation helper taking `Func`, not `Expression`

`ProductionLineValidation.ValidateProductionLines` started life taking plain `Func` selectors so the
four validators could share one set of line rules. It compiled, `dotnet build` was clean, and **all
566 unit tests passed** — because handler tests call handlers directly and never run
`ValidationBehavior`. The first real API call returned:

```
InvalidOperationException: Could not infer property name for expression:
x => Invoke(value(...ProductionLineValidation+<>c__DisplayClass0_0`1[...]).rawMaterials, x).
```

FluentValidation derives a rule's property name by walking an **expression tree**; a compiled
delegate has none. Fixed by taking `Expression<Func<T, IEnumerable<...>>>` (note `IEnumerable`, not
`IReadOnlyList` — `RuleForEach`'s type inference needs it).

This is the FluentValidation twin of phase-9 bug #1's captured-`Func`-in-`Where`, and the remedy is
the same shape. `ProductionValidatorTests` now **executes each of the six validators**, which is the
one thing the handler tests structurally could not, plus asserts that a failing line rule names the
collection it failed on — because "it did not throw" is not the property that matters.

### 2. The cost roll-up's "Rounding Adjustment" row rendered `0.00`

Found in the browser pass. The residue is smaller than a cent **by construction**, and `AmountPipe`
is fixed at two decimals, so the row appeared with a value of `0.00` — strictly worse than omitting
it, because it reads as a defect rather than a disclosure.

`AmountPipe` gained an **optional decimals argument defaulting to 2**, so none of phase-23's 324 call
sites changed, and the roll-up renders the residue as `| amount: 4` → `0.0001`. Its `-0.00`
normalisation was generalised to any negative that rounds to all zeros at the requested precision.
Two new pipe tests pin both.

This is phase-23's own lesson in a new costume: a figure can flow correctly all the way to the
template and still tell the user nothing.

---

## Manual E2E — the exit criterion, verified with `sqlcmd`

Fresh organizations (Manufacturing on and off), master data seeded through the real API by
curl + cookie jar, browser clicks reserved for the new screens.

**Raw material received in two layers at different costs**: 10 @ 100 (10-01-2026), then 10 @ 200
(20-01-2026).

**Run 1 — BOM → Production Order → Production Journal**, 15 raw, 300 of labour, a by-product at 20%
over 3 units, output 10:

```
=== Raw-material line: the real FIFO cost consumed ===
Steel Sheet | 15.0000 | ConsumedUnitCost 133.3333 | Amount 2000.0000

=== FIFO layers this journal CREATED ===
Steel Offcut    | 3.0000  | UnitCost 153.3333 | LayerValue  459.99990000
Finished Widget | 10.0000 | UnitCost 184.0000 | LayerValue 1840.00000000

=== THE CONSERVATION LAW, computed in the database ===
RawConsumed 2000.0000 | Expenses 300.0000 | ValueIn 2300.0000
StockValueCreated 2299.99990000 | RoundingResidue .00010000

=== Raw-material layers AFTER the run ===
Steel Sheet | QuantityIn 10 | QuantityRemaining .0000 | UnitCost 100.0000   <- consumed in full
Steel Sheet | QuantityIn 10 | QuantityRemaining 5.0000 | UnitCost 200.0000  <- 5 taken

=== Kardex ===
Finished Widget | In  | 10.0000 | 184.0000
Steel Offcut    | In  |  3.0000 | 153.3333
Steel Sheet     | Out | 15.0000 | 133.3333

=== GL, net movement per account ===
Inventory               |  299.9999
Production Cost Applied | -299.9999

=== Trial balance, whole organization ===
TotalDebit 6799.9999 | TotalCredit 6799.9999 | Difference .0000
```

Every clause of the exit criterion, in order: the raw-material layers decreased by exactly the
consumed quantity at exactly their FIFO cost (the 100-layer emptied, the 200-layer gave 5); the
finished good's new layer carries a `UnitCost` equal to the computed cost per unit; **raw cost
consumed + expenses == finished value + by-product value**, to the residue the document itself
reports; the kardex reconciles; the GL balances **and** Inventory's net movement equals exactly the
production expenses added; the trial balance is zero.

The weighted average is the load-bearing number: **133.3333**, not 200 (latest), not 100 (oldest),
not the BOM's planned rate. A test using one layer at one cost would pass under three wrong
implementations.

**Run 4 — clean numbers** (every remaining raw unit at 60): raw 1200 + expenses 300 = **in 1500**;
by-product 300 + finished 1200 = **out 1500**; rounding adjustment **0.0**. *Conservation exact, to
the cent.*

**Other proofs, all green:**
- **LOAD BOM scaling** — BOM output 10 asked for 20: raw 15 → 30, by-product 3 → 6 at an unchanged
  20%, expense 300 → 600.
- **Conversion replay refused** — one order → one journal; the second attempt 409s, and so does the
  conversion template.
- **A second BOM for the same product** — 409. **A landed-cost term as a production expense** — 409.
- **Void unwinds both directions** — raw material restored to 100 on hand, finished goods and
  by-product gone, status `Void`.
- **Void refused once sold** — after an Invoice consumed 4 of run 1's finished units:
  *"Cannot void this document — some of the stock it added has already been consumed by a later
  document."* (409), and the run was still `Approved` with its three GL lines — nothing half-unwound.
- **Feature gate both ways** — Manufacturing-off: 403 on the list, 403 on a write, and 403 on a read
  **against a nonexistent id**; Manufacturing-on: 200.
- **Negative permission proof** — a custom role with all 13 manufacturing keys denied (Admin/Member
  are system roles whose grants cannot be edited), membership moved onto it:

  ```
  403  /bills-of-materials/<nonexistent>   ... (Manufacturing.BillOfMaterials.View).
  403  /production-orders/<nonexistent>    ... (Manufacturing.ProductionOrder.View).
  403  /production-journals/<nonexistent>  ... (Manufacturing.ProductionJournal.View).
  403  /reports/production-planning        ... (Manufacturing.ProductionReport.View).
  403  /production-orders/<nonexistent>/approve
  403  /production-journals/<nonexistent>/approve
  403  /production-journals/<nonexistent>/void
  ```

  Then, with Admin restored, **the identical ids return 404** — which is what proves the 403 was the
  authorization behavior firing before the handler, not a not-found in disguise.

### The migration was additive, and it was checked rather than assumed
One `AddColumn` (`TenantSettings.DefaultProductionCostAccountId`), one `EnsureSchema`
(`manufacturing`), 12 `CreateTable`, 25 `CreateIndex`, one `InsertData` (26 permission rows).
**Not one `DropColumn`, `DropIndex`, `DropTable`, `AlterColumn` or `RenameColumn` in `Up`**, and
**zero operations touch the stock or GL tables** — both verified by grepping the scaffolded file
before applying it, per the scaffold-ordering gotcha.

---

## Step 3 — the four carried browser passes, now done

All four were driven in a browser for the first time. **No defects were found in any of them.**

1. **`Configurations > Import / Export`, the Export half (21b).** Start Export → the row appeared
   **Queued**, "0/5 sheets", with a **Cancel** control → polled through to **Completed**, "5/5 sheets
   · 64 rows", a 15 KB `DataExport_…xlsx` and a **Download** button. The honesty banner renders on
   the page: *"This is a readable export, not a restorable backup — the file cannot be uploaded back
   to recreate this organization. Generated files are deleted automatically after 7 days."* Code
   checks: the Download control gates on `job.hasArtifact`, **not** `status === 'Completed'` (with a
   comment saying why); the page uses plain signals written by `(change)` handlers rather than a
   `computed()` over a `FormControl`; and its selects use `[selected]` per option, never `[value]` on
   the select. All three hold.
2. **`Configurations > Organization > Migration` (21c).** The statutory-data banner renders in full
   ("never posted to the General Ledger… import only"), the upload-type list offers both **Sales
   Register** and **Purchase Register**, and the create-only rule is stated on the control:
   *"Migrated rows can only be created, never updated."* Same two trap checks pass.
3. **`Reports > Migrated Sales Register` (21c).** The **"not posted to the General Ledger" banner
   renders** — the whole reason the report is safe to ship. With three seeded rows including a
   negative return row, the table showed every column (including the four Export columns phase-23's
   bug #1 was about) and the footer read **1,58,200.00 / 0.00 / 1,40,000.00 / 18,200.00**, matching
   `SUM()` over the table exactly.
4. **`Reports > Migrated Purchase Register` (21c).** Same banner; all Annex-13-shaped columns render;
   footer **0.00 / 2,00,000.00 / 26,000.00 / 1,50,000.00 / 19,500.00 / 4,00,000.00 / 52,000.00**,
   again matching `SUM()` exactly.

The page-versus-full-set half of phase-16c bug #1 is proven mechanically rather than visually, by
`migrated-sales-register-page.spec.ts`, whose stub deliberately returns a page summing to *less* than
the report totals — a client-side reduce fails it rather than accidentally passing.

### How the browser pass was made possible non-interactively

This is the thing that kept the debt alive through 21b, 21c and 22, so it is worth writing down. The
app's auth cookie is `HttpOnly; Secure; SameSite=None`, and the session is established by curl per
CLAUDE.md's manual-E2E rule — so the browser needs that same session **without anyone typing a
password into the login form**:

1. Export the ASP.NET dev certificate, which the browser already trusts:
   `dotnet dev-certs https --export-path .certs/dev.pem --format PEM --no-password`
2. Serve the SPA over HTTPS with it — `.claude/launch.json`'s **`erp-web-ssl`** entry does exactly
   this. A self-signed `ng serve --ssl` certificate is *not* trusted and the pane refuses to load it;
   the ASP.NET one is.
3. From `https://localhost:4200`, set the cookie curl already obtained:
   `document.cookie = "erp_auth=<token>; path=/; secure; samesite=none"`. Cookies ignore port, so it
   is sent to `https://localhost:7104`; both origins are HTTPS, so it is same-site.

`.certs/` is gitignored. Any future phase can browser-pass its own screens in a couple of minutes.

---

## Shape as built

`Domain/Manufacturing`: `BillOfMaterials` (+ `BomRawMaterialLine`, `BomByProductLine`,
`BomExpenseLine`), `ProductionOrder` (+ three line types), `ProductionJournal` (+ three line types),
`ProductionOrderStatus`, `ProductionJournalStatus`, `ProductionLineRules`.

`Application/Manufacturing`: 12 command handlers, 8 query handlers, `ManufacturingValidation`,
`ProductionLineWriter`, `ProductionRequestProducts`, `ProductionLineValidation`, `ProductLabels`,
`Posting/ProductionJournalPostingRule`.

`Api`: `ManufacturingEndpoints` — 6 BOM routes (including `/bom-template`), 7 Production Order routes
(including the conversion template), 6 Production Journal routes, 3 report routes.

`web`: `core/manufacturing` (models + service), six feature pages under `features/manufacturing`,
three report pages, nine routes, and six dashboard links behind the two feature flags.

**Nine encapsulated child collections**, every one restated in `TestAppDbContext.OnModelCreating` —
which has no `ApplyConfigurationsFromAssembly`, and whose omission presents as the same unhelpful
`DbUpdateConcurrencyException` as phase-24 bug #1.

---

## Found in passing and fixed: `GetAccountingDefaultsQuery` had no authorization at all

While adding `DefaultProductionCostAccountId` it turned out
`GetAccountingDefaultsQuery` implemented **neither** `IRequirePermission` nor `IOrganizationScoped`.
`AuthorizationBehavior` is the only org-membership check in the pipeline (phase-12), so **any
authenticated user could read any tenant's accounting defaults by passing its id**. Fixed in place —
it now carries `PermissionKeys.AccountingDefaultsManage`, the same key its own Update command uses.
One line, in a file already open; left unfixed it would have been a real cross-tenant read.

---

## Deliberately out of scope, decided not forgotten

- **Custom Status on Production Order.** Live-confirmed to exist (the STATUS column in the reference
  product's list grid). Phase 20b's machinery already exists; adding the document type to it is a
  clean, small follow-up rather than part of the costing engine.
- **Multi-level BOM explosion.** The live Planning report states **"Multiple Level: No"**, so
  single-level is parity, and our report says the same on its face.
- **Reporting Tags / Custom Fields on production documents.** Present in the reference product's
  forms; both are cross-cutting mechanisms with their own phases, and neither affects costing.
- **Print templates / PDF for production documents.** Phase 20d's machinery would take them; nothing
  in FR-8.8/8.9 asks.

## Carried forward

- Server-rendered PDFs and `.xlsx` still print dates in AD (phase-23's Decision A limitation).
- The manufacturing reports have no `.xlsx` export; the two Migrated Register reports' exports were
  not exercised in this pass.
- `Organization > Developer Mode` and `> Documents` were confirm-lived this phase and **recommended
  for dropping** from the roadmap (see Step 2) — they are platform/attachment features no FR asks
  for. That is a recommendation; the roadmap now records it rather than continuing to carry them as
  unlooked-at debt.
