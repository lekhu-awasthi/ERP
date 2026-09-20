# Phase 54 kickoff — the four line types phase 52 deferred, and the two measurement debts

Phase 53 is complete and **uncommitted** (a ready commit message is in the session hand-off). It was a
re-planning phase and wrote no code; it rebuilt `docs/roadmap.md`'s forward plan. Start here; do not
continue its thread.

This is an ordinary feature phase again.

---

## What this phase is

Phase 52 put a unit on eight document line types and deferred four **unread** — Opening Stock,
Inventory Adjustment, Production Journal and Bill of Materials — asserted in the negative so that a
later phase has to delete a line and re-read the reason. This is that phase. Phase 53's 2026-09-20
read supplies most of the evidence; it does not supply all of it, and the gap is the first thing to
close.

It also **owns the two outstanding measurement debts**, because it touches both areas and phase 34c's
rule is that an unmeasured plan change is re-measured by whoever next touches that area.

## Read first

- `docs/roadmap.md` — **Forward plan (54–56)**, specifically the `### 54.` entry, and the **Ordering
  rule** above it.
- `docs/erp-module-scan.md`, the **2026-09-20 appendix** ("Re-planning read") — the table of what each
  of the four types shows, and the caveat under it.
- `docs/phase-52-status.md` — **TL;DR, Decision D and Decision E**. E is the list of which line types
  carry a unit and why; D is why `StockLedgerEntry` and `StockMovement` gain nothing.
- `docs/phase-53-status.md` — **Decision D** (the evidence and the caveat) and carried items 1, 3, 4.
- `docs/phase-lessons.md`: the **51, 52 and 53** paragraphs.
- `CLAUDE.md`'s Current status and the gotchas under *GL posting, documents and domain invariants*
  and *Testing and manual E2E*.

## The read to take first, before writing anything

**An empty grid settles nothing here.** The vendor renders the unit control *inside the Qty cell*,
not as a column (phase 52's own finding). The Production Order add form shows a bare
`Product | Quantity` header while its approved documents demonstrably carry `measurement_unit_id`.

So: **add one line to each form and read the cell.** No start condition beyond a browser — the user
logs in, never enter credentials.

- `/inventory/inventory-adjustment/add` — Cadehi (Moonbeam's account lacks the key and renders
  *not authorized*).
- `/inventory/manufacturing/add` (Production Journal) — Cadehi, same reason.
- `/inventory/bom/add` — either tenant. **Also settle `Qty/Unit` here.** The roadmap recorded BOM's
  Raw Materials table as carrying `Qty` **and** `Qty/Unit`, and named the interaction between an
  entered unit and that ratio as a real open question. Phase 53 found neither column on either tenant
  and no saved BOM to read. If it genuinely does not exist, **delete the question** rather than carry
  it forward; if it does, it is this phase's hardest decision.

**Already settled, do not re-read:** Opening Stock has **no** unit (Opening Balances → Product is
`NAME | CATEGORY | QUANTITY | RATE | AMOUNT`, one row per product with no line grid — the only one of
the four an empty screen *can* settle, because there is no cell to hide a control in). Production
Order/Journal **does** carry one (live payload of approved `PRO0014`).

## The scope decisions to make

1. **The header unit is the first decision, and it is not a detail.** An approved Production Order
   carries `measurement_unit_id` per `raw_materials[]` line **and `fg_measurement_unit_id` on the
   header**, for the finished good. Phase 52's rule was *every line naming a product and a quantity* —
   a finished good named on the **header** is the first thing that rule does not reach. Decide whether
   the header unit is the same mechanism (a `UnitId` + `ConversionFactor` pair, frozen at
   Create/Update, with the primary quantity derived) or a different one, and record the reasoning.
   Phase 52's argument against a third column applies unchanged: a stored primary quantity is a second
   quantity able to contradict the other two.
2. **What each of the four types does, stated in both directions.** Phase 51 and 52 both wrote their
   refusals as asserted guard tests rather than as omissions; keep that. Opening Stock's *no* is a
   decision now, not an absence.
3. **Production Journal is value-transforming** (phase 25), so a unit on a raw-material line reaches
   the FIFO consumption *and* the finished good's unit cost. Phase 52's law is that a unit converts
   the **quantity** and never the money: `Amount = Quantity × Rate` in the entered unit, and the
   layer's unit cost is `Amount / primary quantity`. Re-derive the conservation law for a production
   journal before changing a posting rule.
4. **The conversion-cap key** gained the unit in phase 52 (Decision G) so a return cannot add cartons
   to pieces. Ask whether any of this phase's types has a cap of its own — BOM → Production Order →
   Production Journal is a conversion chain.
5. **Where the compiler stops** is the phase-52 method and it should be chosen deliberately again.
   `PrimaryQuantity` is already a distinct type with no implicit conversion from `decimal`, which is
   what enumerated 17 ledger call sites last time. The client half is the weak side: `unitId` is
   **optional** on the request records, so every save path compiles while a form silently drops it —
   7 of 8 Angular forms did exactly that in phase 52.

## The two measurement debts

Neither is a claim; both are plan changes shipped without a number.

- **Phase 51's index on `StockLedgerEntry`.** An index added for one path changes the plan for every
  other path on the table, and the FIFO walk is the other path — which this phase exercises directly.
- **Phase 52's `.Include(x => x.SecondaryUnits)` on `ListProductsQueryHandler`.** A join over the page
  rather than the table, but `listAllProducts` asks for a very large page, and every document form
  this phase touches calls it.

`tools/scale/` is the harness. The number to trust is **logical reads and CPU from
`sys.dm_exec_query_stats`**, never the wall clock (phase 50), and `UPDATE STATISTICS ... WITH FULLSCAN`
on both sides before comparing anything (phase 34c). Re-measure the paths you did **not** touch
(phase 50's own refusal came from exactly that).

## Exit bar

The roadmap's standard bar, plus this phase's specifics:

- `dotnet build` / `dotnet test` / `ng build` / `ng test` all green (`ng test` from `web/`, Node 24).
- A hand-driven E2E on a **fresh organization**: seed master data by curl + cookie jar
  (`docs/e2e-recipes.md` first — a wrong field name is usually a 400 naming no field), reserve browser
  clicks for this phase's own screens.
- **The conservation law in SQL Server** — `FifoLayers = InventoryAccount = MovementHistory` — and the
  unit law, `EnteredQuantity × Factor = LedgerQuantity`, on whichever of the four types ends up
  carrying a unit.
- At least one **negative** path: a 403 naming the exact key against a **nonexistent id** (so
  403-not-404 proves the behaviour fired before the handler), beside a 200 from the same user on the
  same organization in the same run. Phase 52's carried item 4 is the warning: a restricted role
  cannot restore itself through the API — prove the 403 last, or against a throwaway user.
- A number for each measurement debt, or an explicit statement that it was measured and refused.
- `docs/phase-54-status.md` with a TL;DR block, and the usual refresh of `CLAUDE.md`'s Current status,
  `docs/phase-lessons.md` and `docs/roadmap.md`'s index row.
- **Do not commit** — hand over a ready commit message, and end by generating the phase 55 kickoff.
