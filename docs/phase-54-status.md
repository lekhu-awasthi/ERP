# Phase 54 — the four line types phase 52 deferred, and the two measurement debts

## TL;DR

**The phase's first decision dissolved when the screen was opened.** Phase 53 found
`fg_measurement_unit_id` on an approved Production Order's *header* and called "is the header unit
the same mechanism?" phase 54's first and hardest question. It is not a unit mechanism at all: the
Output Quantity field renders a static `ant-input-suffix` holding the product's primary unit short
name, and it stays static text for a product carrying a secondary unit. There is nothing to choose,
so there is nothing to freeze.

**The four types split three-to-one, and the split is a reading rather than a deferral.**

| Type | Line unit | Header unit |
|---|---|---|
| **Inventory Adjustment** | **yes** — a real select, markup identical to the Invoice grid's | — |
| Bill of Materials | no — a display element | no — static suffix |
| Production Order | no — a display element | no — static suffix |
| Production Journal | no — a display element | no — static suffix |
| Opening Stock | no line grid at all | — |

| | |
|---|---|
| Live read taken | **yes** — 2026-09-20, Cadehi + Moonbeam, read + one reverted write |
| The conservation law, in SQL Server | `FifoLayers = InventoryAccount = MovementHistory = 2400.00`, **HOLDS** |
| The unit law, in SQL Server | `EnteredTimesFactor 30 = LedgerQuantityIn 30`, **HOLDS** |
| Line types now carrying a unit | **9** (Inventory Adjustment joins phase 52's eight) |
| BOM's `Qty/Unit` | **deleted, not carried** — the column does not exist in this build |
| New permission keys | **0** — the unit rides the document's own |
| Migration | 1, purely additive: 2 `AddColumn`, 1 `CreateIndex`, 1 `AddForeignKey`, nothing else |
| Measurement debts paid | **2 of 2**, with numbers (`tools/scale/comparison-phase54.md`) |
| Tests | Domain 703, Application **1286** (+20), Infrastructure 12, Angular **591** (+6) |
| `ng build` | clean, **643.68 kB** against the 680 kB budget |

**The reusable part is how the read was taken.** Phase 52's own finding — the vendor renders the
unit control *inside* the Qty cell — is what made phase 53 refuse to conclude from an empty grid.
Phase 54 added a line to each form and compared the markup. But that was still not enough, because
with a primary-only product the *disabled* control and a *display element* look the same on screen.
The reading only became decisive once a product with a secondary unit existed, and neither tenant
had one that the manufacturing forms could reach. **So the phase wrote to the reference tenant and
reverted it** — phase 52's precedent, used deliberately and asked about first.

---

## Decision A — the read, and why an empty grid and a full one were both insufficient

Phase 52 deferred four types **unread** because the reference tenant held none of those documents.
Phase 53 opened three of the four add forms and found a bare `Product | Quantity` header — and
correctly refused to conclude anything, because the vendor renders the unit control *in the Qty
cell*, so an empty grid proves nothing.

Phase 54 added one line to each form. That settled the *position* question and not the *kind*
question: with a primary-only product the vendor renders the control as a greyed, `cursor:
not-allowed` label, which on screen is indistinguishable from a plain display element. The two are
distinguishable only in markup, and only reliably so beside a product that has something to choose.

**The byte-for-byte comparison, one tenant, one product (`Momo large`, primary `PLT`):**

| Form | Qty-cell markup |
|---|---|
| Invoice (the phase-52 baseline) | `<div style="color: rgba(0,0,0,0.25); cursor: not-allowed; margin-left: 6px;">PLT</div>` **inside** `ant-input-group-compact` |
| **Inventory Adjustment** | the identical element — same style string, same parent |
| BOM / Production Order / Production Journal | `<div>PLT</div>`, `cursor: auto`, **outside** the input group |

**The write that made it decisive.** Every Cadehi product carried only its primary unit, and
Moonbeam's one multi-unit product had had its second unit row deleted — by phase 52's own reverted
experiment. Moonbeam's account also cannot open Inventory Adjustment or Production Journal at all.
So a `Carton` secondary unit (rate 12) was added to Cadehi's `Momo large`, all four forms were
re-read, and the row was deleted again:

- **Inventory Adjustment**: `.ant-select` present, offering `PLT` and `CT` — the whole matrix
  including the primary, exactly phase 52's Invoice shape.
- **BOM / Production Order / Production Journal**: `.ant-select` absent; still `<div>PLT</div>`.
  Same product, same tenant, same session, minutes apart.

No document was saved on either tenant; every form was abandoned without Save, and `Momo large` was
confirmed back to `plate 1 100 0` afterwards. The full record is `docs/erp-module-scan.md`,
"Row-present read (2026-09-20, phase 54)".

---

## Decision B — the header unit is not a unit mechanism, and the kickoff's first decision dissolves

The kickoff called this "the first decision, and it is not a detail", and offered two answers: the
same mechanism (a `UnitId` + `ConversionFactor` pair) or a different one. Both are wrong, because
there is no mechanism to choose between.

On all three manufacturing forms the header's Output Quantity renders
`<span class="ant-input-suffix">PLT</span>` — static text holding the chosen product's primary unit.
It stays static text when the finished good is the multi-unit product, tested across all eight of
Moonbeam's BOM-selectable products and again on Cadehi with the Carton row added.

**So `fg_measurement_unit_id` is the product's primary unit id, stored by the vendor and never
chosen by a user.** Modelling it here would be a stored field no user could ever set to a second
value — phase 43's *present-and-ignored* shape, which that phase deleted from a request record for
exactly this reason.

`UnitSweepGuardTests.UnitlessOutputHeaders` asserts it in both directions: that `BillOfMaterials`,
`ProductionOrder` and `ProductionJournal` really do name a product and an output quantity (the
premise — the shape phase 52's rule could not reach), and that none of them carries a unit
(the conclusion). Without the first half a rename would make the test vacuous and it would keep
passing.

---

## Decision C — BOM's `Qty/Unit` is deleted, not carried

The roadmap recorded BOM's Raw Materials table as carrying `Qty` **and** `Qty/Unit`, and phase 52's
Decision E called the interaction between an entered unit and that ratio "a real open question" —
phase 45's rule for not deciding an interaction nobody has posed. Phase 53 downgraded it from *open*
to *unconfirmed*, because both BOM lists were empty and a column might render only with a row.

With a row present, on both tenants, the Raw Material (Input) header reads exactly
`Product | Quantity` and the row has exactly three cells. There is no second ratio column. Per the
kickoff's own instruction the question is **deleted** rather than carried forward.

This is the same class as phase 53's Recurring Invoices and phase 47's Decision G: recorded as *read
and absent*, with the evidence, so that no future session re-opens it from a list.

---

## Decision D — what Inventory Adjustment gains, and what it does not

The same mechanism as phase 52's eight, with nothing new invented:

- `InventoryAdjustmentLine` gains `UnitId` (the **unit lookup**'s id, never the product's
  secondary-unit row) and `ConversionFactor`, frozen at Create/Update by
  `DocumentLineUnitResolver`. `PrimaryQuantity` is derived and never a column.
- `InventoryAdjustmentLineInput` gains an optional trailing `UnitId`; the factor is never accepted
  from the client.
- The detail DTO carries `UnitId`, `UnitName` and `ConversionFactor` back — phase 35a's rule, since
  a detail query that drops a field leaves the form posting its own default over the stored value.
- **No new permission key.** The unit rides `Inventory.InventoryAdjustment.*`, exactly as phase 52
  decided for its eight.
- **No importer.** No importer imports a document line (phase 52's Decision I, unchanged).

### The money, and the one place it is easy to get wrong

Phase 52's law is that a unit converts the **quantity** and never the money. For a cost-bearing
inventory line that has one consequence the sales-side types do not have:

```
Amount        = Quantity x UnitCost        (in the ENTERED unit)
PrimaryQty    = Quantity x ConversionFactor
LayerUnitCost = Amount / PrimaryQty        (rounded once, at ExchangeRates.UnitCostScale)
```

An Increase line for `2 CT @ 1,200` with a factor of 12 is an Amount of **2,400** — not 28,800 — and
it creates **24** primary units at **100** each. Proven live and in SQL.

The GL debits `LayerUnitCost x PrimaryQty`, **not** the Amount the user typed. Without a unit the
two are identical; with one they differ by the rounding residue of that division, and phase 29's
rule is that the account has to receive what the ledger received or the two drift apart silently.

A Decrease line is the mirror: `ConsumedUnitCost` is recorded **per primary unit**, which is what
lets the Void restock at it without converting a second time (`ExchangeRates`' never-convert-twice
rule).

---

## Decision E — where the compiler stops, chosen again

Phase 52's refinement of phase 51's lesson is that *you can choose where the compiler stops*. This
phase chose it twice:

1. **`AddLine`'s new parameters are required, not optional.** Optional ones would have let both
   Create/Update handlers keep compiling while writing a factor of zero into every line — which is
   the shape `IncrementAsync` shipped un-swept in phase 51.
2. **`PrimaryQuantity` was already a distinct type**, so every ledger call site on this document had
   to say which kind of quantity it held.

**It caught one real bug, and it is the same bug in the same place as phase 52's.**
`VoidInventoryAdjustmentCommandHandler` restocked `PrimaryQuantity.AlreadyPrimary(line.Quantity)` —
the **entered** quantity. Voiding a one-carton write-off would have put back one piece where twelve
had left, and valued it at the per-piece cost. Phase 52 caught exactly this in `VoidInvoice` and
`VoidDebitNote`; this is the third instance, and nothing but the type made the compiler walk back
through it.

`InventoryAdjustmentUnitTests.Voiding_a_decrease_restocks_the_primary_quantity_and_not_the_entered_one`
pins it, and was verified to bite by injecting the regression and restoring the file **by hash**.

**The weak side is still the client, and now it has a guard.** `unitId` is optional on the request
records, so a form that drops it compiles and passes everything — 7 of 8 Angular forms did exactly
that in phase 52, and the browser pass was what found it. `line-unit-sweep-guard.spec.ts` now asserts,
over all nine unit-bearing forms, that each renders the control, names the unit in its **read-only**
branch, and actually puts `unitId` on the wire — and that the three manufacturing forms do none of
those things.

---

## Decision F — no conversion cap, and why the manufacturing chain does not need one

Phase 52's Decision G gave the conversion-cap key the unit, so a return could not add cartons to
pieces. The kickoff asked whether any of this phase's types has a cap of its own, noting that
BOM → Production Order → Production Journal is a conversion chain.

**None does.** An Inventory Adjustment is not a conversion of another document — it has no
`ReferrerType`/`ReferrerId` and no quantity cap to compare against. The manufacturing chain does
have one, and it is unaffected: none of its three types carries a unit, so every quantity in that
chain is already in the product's primary unit and the existing cap compares like with like.

---

## The two measurement debts, paid

Full method, fixture and numbers: `tools/scale/comparison-phase54.md`. Logical reads, not the wall
clock (phase 50). The phase-34c dataset has neither stock nor secondary units — its products are
Service-typed on purpose — so `tools/scale/seed-phase54.sql` adds 200,000 FIFO layers (1 in 20
serialised, concentrated over 2,000 products) and 20,000 product secondary units.

### Phase 51's filtered unique index on `StockLedgerEntry` — **no cost to any other path**

| path | with the index | without |
|---|---:|---:|
| `stock.walk.serial` (its own path) | **5** | **319** |
| `stock.walk` | 319 | 319 |
| `stock.available` | 319 | 319 |
| `stock.shortfalls` | 319 | 319 |

It makes a serialised issue **64× cheaper** and leaves the three paths it did not target reading
319 **to the read**. That is the question phase 34c's rule actually asks, and phase 50's own refusal
came from the same comparison going the other way.

### Phase 52's `.Include(x => x.SecondaryUnits)` — **small, flat, and the premise was wrong**

| path | with the Include | without | delta |
|---|---:|---:|---:|
| `products.picker200` | 5,901 | 5,524 | +377 (+6.8 %) |
| `products.grid50` | 779 | 443 | +336 |
| `products.search.hit` / `.miss` | 452 | 452 | 0 |

**The phase-52 comment's premise was wrong and is now corrected in the source.** It said
`listAllProducts` "asks for a very large page"; `MAX_PAGE_SIZE` is **200**, so the join is over 200
rows, not 20,001. The cost is ~340–380 reads whether the page is 50 rows or 200 — the shape of a
seek into the child table rather than a per-row lookup.

**Kept.** 377 reads on a page already costing 5,901 is 6.8 %, and it buys the thing the unit control
cannot work without: a product's unit matrix the moment the product is chosen, against a round trip
per line.

---

## What the manual E2E proved

A fresh organization (`a455d441…`), master data seeded by curl + cookie jar, browser clicks reserved
for this phase's own screen.

| Claim | Evidence |
|---|---|
| The unit law | `EnteredTimesFactor 30 = LedgerQuantityIn 30`; `DecreaseTimesFactor 6 = LedgerQuantityOut 6` |
| The conservation law | `FifoLayers = InventoryAccount = MovementHistory = 2400.00` |
| The arithmetic | `2 CTN @ 1200, factor 12` → `QuantityIn 24, LayerUnitCost 100`; `1 CTN @ 600, factor 6` → `6 @ 100` |
| The freeze | the approved line still reads `ConversionFactor 12` while `CatalogueRateNow` is `6` |
| An edit re-resolves | a draft raised after the rate change carries `6` |
| Per-primary cost | `ConsumedUnitCost 100` (not 600, the per-carton figure) |
| **The Void** | the voided Decrease restocked a layer of **6 @ 100** — the primary quantity, matching the 6 that left; on hand back to 30 and the conservation law re-checked at 3000 |
| A bad unit | **400** naming `Lines[0]`, with the message about the product's unit list |
| The primary unit named explicitly | **201** — legal, and not a secondary-unit lookup |

**The negative path, proved last** (phase 52's carried item 4: a restricted role cannot restore
itself through the API):

- **404** as Admin on `PUT /inventory-adjustments/{nonexistent}` — the control
- **200** on `GET /inventory-adjustments/{real}` from the same user, same organization, same run
- **403** on the same nonexistent id naming **`Inventory.InventoryAdjustment.Edit`** — 403-not-404
  proves `AuthorizationBehavior` fired before the handler
- the run user restored to Admin by `UPDATE tenancy.OrganizationMemberships`, and a 200 afterwards,
  so the app is not left broken for the browser pass

## The browser pass

Against the same organization, through `erp-web-ssl` with curl's cookie transplanted (phase 25's
recipe). Nothing was wrong, which is the honest result and worth stating given phase 52's browser
pass caught two defects the author had shipped:

- The Qty cell renders the input at **97 px** beside a **73 px** select — phase 52's crushed-input
  defect, checked for rather than waited for.
- The select offers `PIS` and `CTN` — the whole matrix, primary included.
- Choosing `CTN` prefills Unit Cost from the carton's purchase price (1200) and Amount reads
  **1,200.00**, i.e. `Quantity x Rate` in the entered unit. The money is not multiplied.
- The **read-only** branch of an approved document renders **`2 CTN`** — the branch phase 52's
  scripted sweep never matched, written with the control here rather than after a bug report.
- The GL panel shows Inventory 2,400.00 / Inventory Adjustment 2,400.00.

---

## Known limitations / carried items

1. **The manufacturing refusals rest on a UI reading, not on a server one.** The vendor's BOM and
   Production Order payloads do carry `measurement_unit_id`; the claim is that the value is always
   the product's primary unit, which was established from the forms rather than by saving a document
   with a multi-unit raw material and reading it back. Re-entry condition: a tenant whose
   manufacturing forms are reachable *and* whose products carry secondary units — Cadehi has the
   first, Moonbeam had the second.
2. **The print pipeline still renders a quantity with no unit**, for all nine unit-bearing types.
   That predates this phase (phase 52 did not touch it either) and is deliberately left alone rather
   than fixed for one type: `PrintDocumentQueryHandler` builds a `Quantity(l.Quantity)` cell for
   every type, and changing one would make the nine inconsistent.
3. **The scale fixture is a read-path fixture.** `seed-phase54.sql` writes layers and secondary
   units by direct INSERT with no documents, GL or movements, so the phase-34c organization must not
   be used for a conservation-law claim. It is re-runnable and removable by its sentinel
   `SourceDocumentId`.
4. **Phase 51's index measurement is on synthetic layers.** 200,000 rows one warehouse deep with a
   uniform date spread; a real tenant's distribution differs. The comparison is A/B on the same
   fixture, which is what the debt asked for, but the absolute 319 is not a production figure.

---

## Tests

| suite | before | after |
|---|---:|---:|
| Domain.UnitTests | 703 | **703** |
| Application.UnitTests | 1266 | **1286** (+20) |
| Infrastructure.UnitTests | 12 | **12** |
| Api.IntegrationTests | 30 | **30** (needs Docker) |
| Angular | 585 | **591** (+6) |

`dotnet build`, `dotnet test`, `ng build` and `ng test` all clean. `ng build` does not warn; the
bundle is **643.68 kB** against phase 42's measured 680 kB budget.
