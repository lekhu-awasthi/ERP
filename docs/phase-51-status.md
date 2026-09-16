# Phase 51 — Batch and serial tracking

## TL;DR

**The modelling question had one answer that makes the phase-25 conservation law hold by
construction rather than by reconciliation, and it is the same answer for both dimensions: a batch
and a serial are both *keys on the FIFO layer*, and neither carries a quantity of its own.**

- A **batch keys the layer**. `StockLedgerEntry` and `StockMovement` gain a nullable `BatchId`.
  `ProductBatch` is an *identity* row — batch number, manufacture date, expiry date — and stores **no
  quantity at all**. The Batch tab's QUANTITY column is `SUM(QuantityRemaining)` over that batch's
  layers, grouped by warehouse. The alternative (a batch-quantity ledger reconciled against the
  layers) creates a second quantity that can disagree with the first, which is precisely the failure
  mode phase 37's "three views or two drift silently" describes.
- A **serial is a layer of quantity one**. `StockLedgerEntry` and `StockMovement` gain a nullable
  `SerialNo`. The Serial Number tab's three columns are the layer's own — SERIAL NO. (`SerialNo`),
  WAREHOUSE (`WarehouseId`), CREATED AT (`CreatedAt`) — and the report's **Status** filter, which the
  tab's three columns do not show and which the kickoff warned against inventing, is
  `QuantityRemaining` : `1` is *In Stock* and `0` is *Issued*. The lifecycle was already in the
  ledger; it did not have to be modelled.

Everything else in this phase is a consequence of those two sentences.

| | |
|---|---|
| The phase's acceptance test, in SQL Server | `FifoLayers = InventoryAccount = MovementHistory = 2780.0000`, **HOLDS** |
| The batch dimension as a GROUP BY | `Total 17 = SumOfBatches 16 + UnBatched 1`, **HOLDS** |
| Serialised layers not of size one | **0** |
| Batch narrowing beat FIFO | issue named BATCH999 at 130; older, cheaper BATCH123 untouched at 10 |
| Specific identification beat FIFO | issued J9; older A1 still in stock |
| New permission keys | 2, both Admin+Member, derived |
| Migrations | 2, both purely additive — **no** `DropColumn`/`DropIndex`/`DropTable` in either `Up` |
| Stock paths refusing a tracked product | 3, each named with its reason and guard-tested |
| Tests | Domain **688** (+14), Application **1212** (+24), Infrastructure **12** (+4), Angular **576** (+15) |
| `ng build` | clean, **643.68 kB** against the 680 kB budget |

**The live read was not taken.** The two reports' column sets have never been read, and they are
designed from the product tabs — the phase-8f rule, invoked explicitly. That is Decision A, and it
is the first thing this doc says because it is the constraint everything downstream inherits.

**One bug reached the E2E and nothing else could have caught it**: `ApprovePurchaseBillCommandHandler`
never passed the line's batch to the ledger, so every receipt created an un-batched layer while every
handler test passed. The general lesson is at the bottom, and it is sharper than the bug: *a sweep
driven by the compiler stops exactly where the compiler stops.*

---

## Decision A — the live read was **not** taken; both reports are designed from the product tabs

The phase-8f rule, invoked explicitly rather than silently, and this is the paragraph that records
it.

The 2026-09-16 pass got `permission denied` on `#/reports/new/batch-tracking-report` and
`#/reports/new/serial-number-tracking-report`, because the vendor gates them behind new permission
keys the demo Admin does not hold. Phase 49 listed the re-read as a cheap extra and did not take it;
phase 50 did not take it either. **Phase 51 asked, and the answer was to design from the tabs.**

So, stated plainly and without hedging: **the column sets of the Product Batch Report and the
Product Serial No Report have never been read.** What this phase builds is derived from

- the **Batch tab** — BATCH NO. / MANUFACTURE DATE / EXPIRY DATE / QUANTITY, a *Select Warehouse*
  filter and a batch search box, with the observed row `BATCH123 | 01-09-2026 | 03-09-2026 | 2 CTN`;
- the **Serial Number tab** — SERIAL NO. / WAREHOUSE / CREATED AT, with eight serials observed;
- the **catalogue's own filter lists**, which were readable even though the reports were not:
  Period / Group By / Warehouse for the batch report, Period / Group By / Status for the serial
  report, with Group By offering None (default) / Product / Warehouse.

That is a real constraint on what follows and it is not smoothed over: the two report pages are this
codebase's design, consistent with its own report conventions, and they may well differ in column
order, grouping behaviour and totals from the vendor's. What they are *not* is invented — every
column on each report traces to a column on the corresponding tab or to a filter the catalogue
showed. The one place a filter demanded something the tabs did not show is the serial report's
**Status**, and Decision C explains why the ledger already answered it rather than this phase making
something up.

**Re-entry condition.** If an account or tenant holding those two keys ever becomes available, the
read is worth one page load each, and the answer belongs in `docs/erp-module-scan.md` beside the
2026-09-16 section rather than here.

---

## Decision B — a batch keys the FIFO layer, and `ProductBatch` stores no quantity

The kickoff put the fork precisely: does a batch **key** the layer, or **hang off** it?

**It keys it.** `StockLedgerEntry` is already scoped `(ProductId, WarehouseId)`; the scan reads a
batch as *quantity per product per warehouse per batch, with dates*. That is the same key with one
more column, which is the whole argument. Concretely:

- `ProductBatch` — `(OrganizationId, ProductId, BatchNo, ManufactureDate?, ExpiryDate?)` — is a
  tenant-scoped **identity**, unique on `(OrganizationId, ProductId, BatchNo)`. It is created on
  receipt (a Purchase Bill line naming a batch number that does not exist yet mints one) and is
  reusable by later receipts of the same batch.
- **It has no `Quantity` column, deliberately.** Every quantity a batch has is derived:
  on-hand per warehouse is `SUM(QuantityRemaining)` over `StockLedgerEntry` filtered to that
  `BatchId`; dated movement is `StockMovement` filtered the same way. There is exactly one quantity
  in the system and the batch dimension is a `GROUP BY` over it.
- **FIFO within a batch is automatic.** `ConsumeAsync` already walks layers
  oldest-`TransactionDate`-first; a named batch adds one predicate to `LoadLayersOldestFirstAsync`
  and changes nothing else about the walk.
- **Landed cost needs nothing** (phase 29). Capitalisation rides the layer's `UnitCost`, and the
  layer is still the layer — it just also carries a `BatchId` now. The conservation law
  `goods + allocated = layer value + residue` is untouched because no term in it moved.

The rejected option deserves its sentence: a separate batch-quantity ledger would have had to be
kept in step with the FIFO layers by every one of the nine `Increment`/`Consume` call sites, and
phase 37's central finding is that any two of three views can be patched into agreement while the
third drifts. A dimension that is a `GROUP BY` cannot drift from the thing it groups.

### Dates: both nullable, on purpose, and the phase-50 trap

`ProductBatch.ManufactureDate` and `ProductBatch.ExpiryDate` are both **nullable `DateOnly`**.

The read does not establish that either is required — one observed row carried both, which is one
sample — and a lot number with no expiry is an ordinary thing in the trades this product serves.
Nullable is therefore the honest model, not a dodge.

It is worth saying out loud that this also means the phase-50 trap **does not fire**, and that this
is a consequence rather than the reason. `TenantIndexConvention.RequiredDateProperty` looks only at
**required** `DateOnly` properties, precisely because "a nullable one is a secondary date a document
carries beside its own". `ProductBatch` has no required date, so it classifies as master data — and
it classifies as master data *by rule*, because it carries a per-tenant unique index
`(OrganizationId, ProductId, BatchNo)` whose leading column is `OrganizationId`, exactly like every
other master-data table in the model.

Phase 50's actual lesson is the one about being right **by luck** — `StockLedgerEntry` and
`StockMovement` passed the convention only because a hand-written composite happened to lead with
`OrganizationId`. So this phase does not leave it to luck: `tests/Infrastructure.UnitTests` gains an
assertion that `ProductBatch` carries no required `DateOnly` **and** does carry a leading-
`OrganizationId` index, so that a later phase making either date required is told, by a failing
test, that it now owes the convention a declaration.

---

## Decision C — a serial is a layer of quantity one, and its Status is `QuantityRemaining`

The kickoff's fork: a layer of quantity one, or its own aggregate pointing at the layer that carries
its cost?

**A layer of quantity one**, and the deciding argument is the same one as Decision B: the
alternative stores a second truth. A `ProductSerial` aggregate with a `Status` column is a status
that can disagree with the ledger, and the count of its *InStock* rows is a second quantity that can
disagree with `SUM(QuantityRemaining)`. Under a layer of quantity one, the count **is** the quantity.

What falls out, none of it invented:

| The tab / report wants | Where it comes from |
|---|---|
| SERIAL NO. | `StockLedgerEntry.SerialNo` |
| WAREHOUSE | `StockLedgerEntry.WarehouseId` |
| CREATED AT | `StockLedgerEntry.CreatedAt` |
| **Status** (the filter the tab's columns do not explain) | `QuantityRemaining`: `1` → In Stock, `0` → Issued |
| the unit's cost | `StockLedgerEntry.UnitCost`, for free |

**Serial tracking is specific identification, and this codebase already has the word for it.**
Issuing serial `J9` must relieve `J9`'s layer even when an older layer exists, which is not FIFO.
Under a layer of quantity one those are the same walk with a different selector — `ConsumeAsync`
filters to the named serial and there is exactly one layer to take. No second engine.

### Two consequences that are decisions in their own right

1. **A serial-tracked product cannot go negative, whatever the tenant's Negative Item Balance
   setting says.** A shortfall layer is the record of *units issued before they were received*; a
   serial is a named physical unit, so "issue serial `A1`, which has never been received" is not an
   oversell, it is a typo. `ConsumeAsync` therefore never takes the shortfall branch for a named
   serial and raises a `ConflictException` naming the serial instead. This is the same reasoning
   `ApproveWarehouseTransferCommandHandler` already applies to its source side, quoted in its own
   comment: the setting exists so a business can sell goods it has not booked in yet, and this is
   not that.
2. **Serial uniqueness is over *in-stock* layers, not over all of them.** A serial that is issued and
   later returned by a Credit Note re-enters stock as a **new** layer carrying the same `SerialNo`,
   because the old layer's `QuantityRemaining` is `0` and its row is history the kardex still needs.
   So the unique index is filtered — at most one *in-stock* layer per `(OrganizationId, ProductId,
   SerialNo)` — and CLAUDE.md's rule about nullable unique indexes applies twice over, since
   `SerialNo` is nullable for every non-serialised product in the table. InMemory enforces neither
   half, so the race is verified against SQL Server (phase 20e Decision C's discipline).

---

## Decision D — one batch per line, N serials per line, and the asymmetry is forced by the data

The scan shows **one** `Item Batch` column on both grids, between Qty and Rate. So a line carries
**one nullable `BatchId`**, not a child collection: a delivery drawn from two batches is two lines,
which is what the vendor's own grid shape says.

Serials cannot follow that, and the asymmetry is not a choice. A line of quantity 5 of a serialised
product names **five** serial numbers, because the tab is one row per physical unit. So a line
carries a **child collection of serial numbers**, constrained to exactly `Quantity` entries when the
product is serial-tracked (and `Quantity` must then be a whole number — you cannot have 2.5 serials).

**The serial capture control is itself unread**, and that goes in the same box as Decision A: the
scan records the `Item Batch` column and says nothing about where serials are entered. This phase
designs it and says so.

### Required, optional, or refused — per side, because they are not the same question

- **On receipt** (a path that *creates* a layer), a batch-tracked product's line **must** name a
  batch and a serial-tracked product's line **must** name its serials. Stock cannot come into
  existence belonging to no batch when the product's whole point is that it does — that is phase
  24's variant-parent argument exactly: a bucket nothing ever receives into, reconciling against
  nothing.
- **On issue** (a path that *consumes*), naming a batch is **optional**: it narrows the FIFO walk to
  that batch, and leaving it blank walks every batch oldest-first. This is both faithful to the read
  (the control is there on the Invoice grid, so it can be used) and what makes the seven document
  types the scan never showed a control on keep working. A **serial**, by contrast, is required on
  issue — FIFO cannot choose a physical unit on the user's behalf.
- **When the product's flag is off**, the fields are **refused**, not ignored: a `BatchId` or a
  serial list on a line whose product is not tracked is a `409` naming the product. Phase 43's rule
  is that a field the aggregate refuses should be *absent* from the request record rather than
  present-and-ignored — here the field must exist on the record (the same record serves tracked and
  untracked products), so the refusal is explicit and named instead of silent.

### Where the control is, and where the dimension goes without one — stated as a rule

Phase 30's lesson is that **a list sampled from a few screens becomes a wrong list; find the rule**.
The rule here is:

> The allocation control is **where the 2026-09-16 read put it** — the Invoice and Purchase Bill line
> grids. Every other stock path either **derives** the allocation from its source or **refuses** a
> tracked product with a named 409.

That settles the paths the read never showed a control on without guessing at each one, and it is
asserted in both directions by `StockTrackingSweepGuardTests`.

| Path | Batch / serial comes from |
|---|---|
| Purchase Bill line | **the control** — the read shows it |
| Invoice line | **the control** — the read shows it |
| Credit Note line (sales return) | **derived**: the line carries a `BatchId` prefilled from the Invoice line it returns — phase 19's `ExpenditureClassification` precedent |
| Debit Note line (purchase return) | **derived**: the same, from the Purchase Bill line it returns |
| Warehouse Transfer | **derived, and it needs no field at all** — it re-creates, relief by relief, exactly what the source warehouse gave up (Decision E) |
| Opening Stock | **refused**, with its reason |
| Inventory Adjustment | **refused**, with its reason |
| Production Journal | **refused**, with its reason |

The three refusals live in `StockTrackingRules.RefusedPaths` with a reason each and a recorded
re-entry condition, because phase 46's lesson is that an exclusion has to say what it is excluding
and why, and be asserted to still exist. Their reasons, in brief: an Opening Stock line is keyed
`(Product, Warehouse)` and unique on it, so a batch would have to join that key — a unique-index
change on a populated table, with a nullable column in a unique index, which is the
at-most-one-sentinel trap phase 32 hit; an Inventory Adjustment creates a layer at a user-entered
cost with no source to inherit from, and the read showed no control (phase 27a found it has no
Custom Fields section either, so nothing says how the vendor handles it); a Production Journal needs
*two* controls plus a by-product rule, and the reference tenant runs periodic inventory so its
journals post nothing to compare against.

**A refusal is a real limitation and is named as one.** A tenant that turns batch tracking on cannot
give that product opening stock through the Opening Stock screen, adjust it, or manufacture with it
until a later phase adds those controls. The ordinary answer for opening stock is a back-dated
Purchase Bill, which carries the batch today. The alternative — letting those paths through — would
create un-batched layers of a batch-tracked product, which is exactly the bucket that reconciles
against nothing that phase 24 refuses on a variant parent, and it would be invisible until a batch
report disagreed with the product's own on-hand.

Phase 35a's rule is attached to all of this and is the thing most likely to be got wrong: adding a
field to many aggregates owes **three** assertions — write, read, and *every prefill between*. That
phase found 14 of 15 detail DTOs and all 5 conversion templates silently dropping `LocationId`. Here
it bit four times, and the compiler caught all four only because the editable line type is
exhaustive: `Invoice.SetExport`'s line rebuild, both conversion-template prefills, and both
document-inbox prefills.

---

## Decision E — `ConsumeAsync` returns what it took, and Warehouse Transfer stops collapsing batches

`ConsumeAsync` returns a single weighted-average `decimal` today. That is enough for a COGS leg and
not enough for anything that has to *re-create* what it consumed.

`ApproveWarehouseTransferCommandHandler` consumes from the source warehouse and increments the
destination at the returned average. For a batch-tracked product that silently **destroys the batch
identity** — two batches go out of Kathmandu and one un-batched layer arrives in Pokhara — and, as a
pre-existing simplification nobody had cause to notice, it also collapses two genuinely different
unit costs into one averaged layer.

So `ConsumeAsync` now returns a `StockConsumption` record: the `AverageUnitCost` every existing
caller already reads, plus the **reliefs** — one entry per `(BatchId, SerialNo, UnitCost)` it
actually took, with the quantity taken from each. The transfer then re-creates the destination side
relief by relief, preserving batch, serial and cost.

It is one return type rather than a second `ConsumeDetailedAsync` method, because CLAUDE.md's rule
is to retire copies 1..N before writing copy N+1 — two methods answering one question is how
`GrantedPermissionReader` ended up with two inlined joins that both missed a filter.

**`StockMovement` follows the same logic**: one row per distinct relief rather than one row per
call. For an untracked product that is exactly one row and nothing changes; for a batch-tracked
product it is one row per batch, which is what a kardex for a batch-tracked product should show; for
a serialised product it is one row per unit, which is what "a row per physical unit" means. The
existing doc comment on `StockMovement` says one row per call and will be corrected rather than left
to contradict the code.

---

## Decision F — what an oversell, a return and a Void each owe

The three questions the kickoff said to settle in the same sitting as the modelling, because all
three are consequences of what the layer's key became.

**An oversell.** A shortfall layer carries **whatever key the request named**, which is the rule
that keeps the model uniform:

- Batch named, batch short → the shortfall layer carries that `BatchId`. The batch's derived
  quantity goes negative, which is exactly as honest as the product's own quantity going negative,
  and `FillShortfallsAsync` later matches it by batch.
- No batch named (a FIFO walk across every batch came up short) → the shortfall layer has **no**
  batch, which is correct: the units owed were never received, so they belong to no batch. A
  batch-tracked product's on-hand is then `SUM(batches) + un-batched shortfall`, and it still sums
  to one number because it is all one column.
- Serial named → **no shortfall is possible at all**, per Decision C.

`FillShortfallsAsync` pays **same-batch shortfalls first, then un-batched ones**. The second half is
load-bearing: without it an un-batched debt on a batch-tracked product could never be repaid by any
receipt, because every receipt of such a product carries a batch.

**A return.** Phase 37 settled the *cost* question — a return relieves at the cost the layers give
up, and the difference from the price the supplier credits is the balancing plug. This phase settles
only the *key*, and it does not disturb that: the batch **narrows which layers FIFO may choose**,
and the cost is still whatever those layers give up. A Credit Note restocks into the batch its
source Invoice line named, prefilled and editable; a Debit Note relieves the batch its source
Purchase Bill line named, prefilled and editable.

**A Void.** Phase 43's lesson — *changing what a document does to the stock ledger changes what its
Void owes* — and the good news is that the existing shape defuses it. `ReverseIncrementAsync`
already reads `WarehouseId` and `LocationId` **off the layer it is reversing**, never from a fresh
argument, with phase 35b's comment explaining why. `BatchId` and `SerialNo` join them by the same
rule and for the same reason: a release that landed in a different batch would leave that batch's
derived quantity permanently wrong while the product-wide total still reconciled — which is phase
43's failure exactly, and the reason it was found in production shape rather than in a test.

---

## Decision G — the parent/variant rule, stated once

A variant **parent** may never reach a document line (`ProductVariantRules`, phase 24), so it never
holds a layer, so it can never hold a batch or a serial. That is the whole rule, and it splits into
two halves that are genuinely different:

- **The two flags are catalog metadata and are copied down**, exactly as `TrackInventory` already is
  by `Product.GenerateVariant`. A parent carrying `BatchTracking = true` is a *template* for the
  variants it generates, which is the behaviour the one pre-existing flag of this kind already has.
  Inventing a different answer for the two new flags would make three flags behave two ways.
- **Creating a `ProductBatch` for a variant parent is refused**, with a `409` naming the reason.
  This is phase 45's shape precisely — a secondary unit on a parent "reconciles against nothing",
  and phase 45's own lesson is that this is the argument for *refusing* it rather than excusing it.

The two halves are reached by **reading** `ProductVariantRules` rather than merging a new rule into
it, per the `ILockDateSensitiveDocument` gotcha: reuse a marker by reading it when its member set is
narrower than the new grant's.

---

## Decision H — permission keys, derived

Two new keys, both **Admin + Member**, and the derivation is CLAUDE.md's own sentence rather than a
default:

- `Reports.ProductBatch.View`
- `Reports.ProductSerial.View`

The rule is *"flat per-transaction registers and anything exposing PAN/contact identity →
Admin-only; bounded rollups and routine daily-use working data → Admin+Member"*, and the nearest
decided neighbours are the phase-26c inventory keys, whose recorded reasoning reads: *"quantity, rate
and value per product, with no contact anywhere … the working data a stock operator needs hourly"*.
Both new reports are strictly narrower than that — batch number, two dates, a warehouse and a
quantity; serial number, a warehouse and a status. **No contact, no rate, no margin, no document
number.** They cannot warrant more protection than `Reports.InventoryPosition.View`, which a Member
already holds.

**The vendor gates its own two reports behind keys its demo Admin lacks.** That is evidence about
*their* split and not automatically ours — the kickoff says so and it is worth repeating here,
because it is the one piece of contrary evidence and it is being consciously set aside rather than
missed.

**No key for batches or serials themselves.** A batch is created by approving a Purchase Bill and
read on the product detail page, so it rides `Catalog.Product.View` and the document's own approve
key — the same reasoning phase 24 used for variants ("creating a variant is creating a product").
There is no batch-management screen to gate.

New `PermissionKeys` constants are auto-discovered by `PermissionKeyCatalog`, but the seed migration
must go through `RolePermissionConfiguration.HasData` first or the scaffold is silently empty
(CLAUDE.md, phase 9).

---

## Decision I — the custom-field migration path is **recorded and out of scope**

Moonbeam still shows tenant-defined Custom Fields named **Batch NO / Manufacturing Date / Expiry
Date / Lot** on the same forms that now carry the native `Item Batch` column. Phase 35b declined to
model those as traceability and was right to.

A real tenant that faked traceability with custom fields **will** want to migrate, and this phase
does not build it. That is a decision, not an omission, and the reason is that a migration needs to
know things this phase cannot: which of a tenant's custom fields mean what (they are free text and
the four names above are one tenant's spelling), what to do with rows whose values do not parse as
dates, and — the one that actually settles it — what to do about **history**, since a custom field
sits on a document while a batch sits on a *layer*, and there are no layers to retro-fit for
documents approved before the feature existed.

The honest shape when it is built is phase 38's: an importer with `PlanAsync` / `ImportRowPlan` and
a pre-commit dry run, reading the tenant's own custom-field values and *proposing* batches, with the
review step showing what it would create before anything is written.

---

## Decision J — no importer this phase, for a reason that is not "no time"

Batches and serials get **no upload type**, and this is worth a decision rather than a silence
because phase 38 established eight of them and phase 45 added a ninth.

A `ProductBatch` row with no layer behind it is an identity with no stock — importing one creates
exactly the bucket that reconciles against nothing which Decision D refuses on a document line. The
way stock of a batch comes into existence is a **receipt**, and receipts already have importers
(Purchase Bill) and an Opening Stock path. So the importable thing is the *document line's batch
column*, which is where a later phase should put it, rather than a standalone batch sheet.

Stated as the rule: an entity whose quantity is derived has nothing to import that its source
document does not already carry.

---

## What the manual E2E proved, and the one bug only it could find

A fresh Organization, master data seeded entirely by curl, browser clicks reserved for this phase's
own new UI — CLAUDE.md's bar, met in both halves. Every status code printed.

### The conservation law, read out of SQL Server

```
FifoLayers   InventoryAccount   MovementHistory   Law
2780.0000    2780.0000          2780.0000         HOLDS
```

Phase 37's rule is that a stock value has to reach **three** views or two drift silently, and that
any two can be patched into agreement while the third does not. All three are asserted, on data
that exercises both dimensions at once.

And the phase's own law — that a batch is a `GROUP BY` and therefore cannot lose or invent quantity:

```
Total     SumOfBatches   UnBatched   Law
17.0000   16.0000        1.0000      HOLDS
```

(The un-batched 1 is the serialised laptop still in stock: a serial carries no batch, because the
product is serial-tracked and not batch-tracked. The two dimensions are independent, and this is
what that looks like in the data.)

### The two behaviours that would be invisible in a handler test

**Batch narrowing beat FIFO**, which is the whole point of the control:

```
BatchNo    Direction   Quantity   UnitCost
BATCH123   In          10.0000    100.0000     <- older AND cheaper
BATCH999   In          10.0000    130.0000
BATCH999   Out          4.0000    130.0000     <- the issue named BATCH999
```

An unnarrowed FIFO walk would have taken BATCH123 at 100. The invoice named BATCH999 and got 130,
and BATCH123 stayed whole at 10.

**Specific identification beat FIFO** on the serial side, for the same reason:

```
SerialNo   CreatedAt                          QuantityRemaining
A1         2026-09-16 17:12:42.6845427        1.0000      <- older
J9         2026-09-16 17:12:42.6859292        0.0000      <- the one issued
```

Plus: `SerialisedLayersNotOfSizeOne = 0`, which is the invariant that makes "a serial is a layer of
quantity one" a fact rather than a convention every caller has to remember.

### Every refusal, with the message it actually produced

| Attempt | Result |
|---|---|
| Batch Tracking on a Service product | `400` — *"Batch Tracking applies to the stock ledger, so it can only be set on a Goods product."* |
| A batch-tracked line receiving with no batch named | `400` on `Lines[0]` — *"'Paracetamol' is batch-tracked, so a line receiving it must name a batch."* |
| A serialised line of quantity 3 naming 1 serial | `400` on `Lines[0]` — *"…needs exactly 3 serial number(s), and 1 were given."* |
| Issuing serial `NOPE`, never received | `409` — *"Serial number 'NOPE' is not in stock in this warehouse, so it cannot be issued."* |
| Opening Stock for a batch-tracked product | `409` — *"'Paracetamol' is batch-tracked, and Opening Stock cannot record which batch it moves."* |

The `NOPE` case is worth its own sentence: the tenant's Negative Item Balance setting **allowed** an
oversell on this organization (Invoice 1's earlier run produced a shortfall layer when the batch was
short), and the serialised issue was refused anyway. That is Decision C's claim, demonstrated
against the setting rather than asserted around it.


### The browser pass

Driven through the Browser pane on the phase-25 Step 3 recipe (dev cert, the `erp-web-ssl` profile,
curl's `erp_auth` transplanted via `document.cookie`), against the same organization the curl E2E
built — so the two halves are looking at one ledger rather than two.

**A whole Purchase Bill was created and approved through the UI**, not through curl: supplier,
warehouse, product, Qty 7, `Item Batch` = `BROWSERBATCH`, Rate 150. It saved (amount computed
1,050.00), **read the batch back into the control after a reload** — which is phase 35a's rule
demonstrated on screen rather than inferred from a DTO — approved, and then appeared on the product's
Batch tab as `BROWSERBATCH | — | — | 7 Carton`.

Then the conservation law was re-read in SQL, and it still holds with a UI-authored document in it:

```
FifoLayers   InventoryAccount   MovementHistory   Law
3830.0000    3830.0000          3830.0000         HOLDS     (2780 + 7 x 150)
Total 24.0000 = SumOfBatches 23.0000 + UnBatched 1.0000     HOLDS
```

What the pass checked, and what it found:

| Checked | Result |
|---|---|
| `Item Batch` column position on both grids | between Qty and Rate, as the read shows |
| Column width, Purchase Bill / Invoice | 167 px / 162 px, table 1118 px in an 1118 px wrapper — **no overflow** |
| Accessible names on both new controls | `Item Batch` and `Serial numbers` resolve via `find` |
| Batch tab | `BATCH123 10 Carton`, `BATCH999 6`, `BROWSERBATCH 7` — matches SQL exactly |
| Serial tab | `A1 / Kathmandu / 16-09-2026 22:57`; J9 correctly absent (issued) |
| `NepaliDatePipe` `'datetime'` mode | 17:12 UTC rendered 22:57 — the Nepal day and time, phase 48's rule working |
| Product Batch Report | three rows, server-computed footer **23.00** |
| Product Serial No Report | `In Stock` / `Issued` badges as words, footer `1 in stock, 1 issued` |
| Status filter actually filters | `Issued` → one row, footer recomputes to `0 in stock, 1 issued` |
| Reports index | both entries present under **Inventory** |
| Mobile (375 px) | **no page horizontal scroll** (375 = 375); the wide table scrolls inside `.table-responsive` |
| Network on the new pages | all 200 |

**Two things the pass corrected, and both were mine rather than the code's.** A scaled-down first
screenshot made the `Item Batch` column look crushed; measuring it said 167 px and no overflow, which
is phase 34b's rule working in the direction nobody expects — *the picture can mislead as well as
correct, so measure the thing the claim is about.* And `Category` and `Primary Unit` rendered as `—`
on the product page, which looked like a phase-51 regression and was not: the E2E's last step leaves
the test user on a custom role holding only `Reports.ProductSerial.View` and `Catalog.Product.View`,
so the lookup calls legitimately 403. Restoring Admin restored the labels. **An E2E that ends by
restricting its own user leaves the browser pass looking at a broken app** — worth knowing before the
next phase copies the script.

### The permission proof

```
403  GET …/reports/product-batch    {"title":"You do not have permission to perform this action (Reports.ProductBatch.View)."}
200  GET …/reports/product-serial
```

Same user, same organization, same run, after being moved onto a custom role granting
`Reports.ProductSerial.View` and not `Reports.ProductBatch.View` — and a `200` on the batch report
from the same user *before* the role change, which is the control that makes the 403 mean something
(phase 41's rule).

### The bug only the E2E could find

**`ApprovePurchaseBillCommandHandler` never passed the line's batch or serials to the ledger.**

Every handler test passed. `dotnet build` was clean. The Create path resolved the batch, stored the
`BatchId` on the line, minted the `ProductBatch` row, and the detail DTO read it all back. What the
*approve* path did was call `IncrementAsync` with the arguments it had always called it with — so
every receipt created an **un-batched** layer, and the first symptom was three steps later and in
another subsystem: the Batch tab showed `BATCH123: 0` and `BATCH999: -4`, because the batch had no
stock and the invoice's narrowed consume had therefore left a shortfall against it.

It is the same shape as the thing this codebase keeps rediscovering — a field that reaches the
aggregate and not the side effect — and it is worth naming precisely: **a sweep driven by the
compiler stops exactly where the compiler stops.** Changing `ConsumeAsync`'s return type made the
compiler enumerate all five consume sites, which is why the consume half was complete and correct
from the first build. `IncrementAsync` only gained *optional* parameters, so it enumerated nothing,
and the one increment site that needed them was the one nobody was forced to look at.

---

## Carried items, named rather than left implicit

1. **The two reports' real column sets are still unread** (Decision A). One page load each, from an
   account holding the two keys.
2. **Three stock paths refuse a tracked product** rather than carrying the allocation: Opening
   Stock, Inventory Adjustment, Production Journal (Decision D, `StockTrackingRules.RefusedPaths`).
   Each is a real limitation with a recorded reason and re-entry condition.
3. **The custom-field migration path is out of scope** and is a plausible "not now" rather than an
   omission (Decision I).
4. **No importer** for batches or serials, for a reason rather than for time (Decision J).
5. **The serial capture control was never read.** The scan records the `Item Batch` column on both
   grids and says nothing about where serials are entered; this phase designed a comma-separated
   list beside the batch control and says so.
6. **No performance measurement was taken, and none is claimed.** The two new indexes
   (`ProductBatches`' unique key, `DocumentLineSerials`' two) serve their own reads only, and
   `StockLedgerEntry`'s filtered unique index is a correctness constraint rather than an access
   path. Phase 34c's rule is that an index added for one path changes the plan for every other path
   on the same table — `StockLedgerEntry` did gain an index, so **a later phase touching stock
   performance should re-measure the FIFO walk**, and this phase makes no claim that it did not
   change.
