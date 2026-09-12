# Phase 37 — inventory policy: negative stock, returns at cost, the clearing unwind

## TL;DR

The first phase since 7 to change a Domain invariant. Until now `StockLedgerService.ConsumeAsync`
**threw** whenever a document would take more than the FIFO layers hold, so the tenant setting the
reference product has always had — *Negative Item Balance: Reject / Warn / Do Nothing* — had exactly
one real branch. Phase 31 built the setting; phase 26c pinned the throw and wrote a report guard for
the day it went away. This is that day.

1. **Negative stock is real, and it is a layer.** An oversell now leaves a **shortfall layer** — a
   `StockLedgerEntry` whose `QuantityIn` and `QuantityRemaining` are both negative — carried at the
   product's last known cost in that warehouse (zero when it has never been received there). The
   next receipt **fills** it before the goods become stock on hand.
2. **The cost catch-up is the phase's central number.** A shortfall is issued at an assumed cost and
   covered at the real one, and `filled × (real − assumed)` has to reach three places at once or two
   of them drift quietly: the FIFO layers, the Inventory account, and the movement history every
   dated stock report is reconstructed from. It posts as its **own GL entry** against the covering
   document, and rides the movement table as a **value-only row** (`StockMovement.ValueAdjustment`,
   quantity zero).
3. **A purchase return relieves Inventory at the cost the layers gave up**, not at the price the
   supplier credits — the phase-6 modelling choice phase 29 stopped widening but did not fix. The
   difference between the two is a real gain or loss on the return, derived as the plug that
   balances the entry, and posted to the tenant's Inventory Adjustment account.
4. **The clearing unwind is stated in both directions**: a full return takes Landed Cost Clearing
   back to zero, a partial one leaves exactly the share of the accrual the unreturned units carry.
   Its Inventory leg is *gone*, because the single Inventory credit now already carries the freight.
5. **The Credit Note needed nothing**, and the phase says why rather than leaving it unanswered: a
   sales return *adds* stock at the cost its source invoice recorded, so what it puts into the
   ledger and what it credits COGS are the same number by construction. The Debit Note's problem was
   specific to *relieving*, where FIFO chooses the layer.
6. **`GlJournalEntry.PostReversalOf` is deleted.** Phase 36 showed "one entry per approved document"
   was a habit; phase 37 made it routinely false. Every void in the codebase now reverses what is
   **outstanding** through `SourceDocumentGlEntries`, and every detail query reads every entry.

Proven live end to end: Reject 409, Warn 422-then-200, a covering bill posting two balanced entries,
and `FifoLayers = InventoryAccount = MovementHistory = 128.00` read out of SQL Server.

---

## What changed

### 1. The shortfall layer (`Domain/Inventory/StockLedgerEntry`)

`CreateShortfall` takes a positive magnitude and stores it negated, so the sum of
`QuantityRemaining` — which is what `GetAvailableQuantityAsync` and every stock figure derive from —
goes negative by exactly the amount owed. `Fill(quantity)` walks it back towards zero and never
past; `QuantityIn` is left alone, the same rule `Consume` follows and for the same reason (it is the
layer's original size, and the kardex reconstruction depends on it).

`LoadLayersOldestFirstAsync` already filtered `QuantityRemaining > 0`, so a shortfall layer is
invisible to consumption — you cannot sell out of a debt. Filling has its own query, ordered oldest
first by the same `TransactionDate` then `CreatedAt` tie-break the FIFO walk uses.

**The assumed cost** is the newest positive layer's unit cost for that (product, warehouse), and
zero when the product has never been received there. Shortfall layers are excluded from that
lookup: chaining an assumption off an assumption would compound it.

### 2. `allowNegative`, and where the decision is made

`ConsumeAsync` gained `bool allowNegative = false`. It deliberately does **not** read
`TenantSettings.NegativeStockBalanceAction` itself: a Warn verdict also has to have been *confirmed*
by the user, and only the command handler knows whether it was. So the handler passes the verdict
`IStockAvailabilityPolicy` reached, which is the one place that setting is consulted (phase 25
Decision F).

Two callers pass `true`: **Invoice** and **Production Journal** — exactly the two that already
consult the policy. The other three stay hard rejects, with reasons recorded rather than inherited:

| Path | Verdict | Why |
|---|---|---|
| Invoice | honours the setting | The case the setting exists for: selling goods not yet booked in. |
| Production Journal | honours the setting | Phase 25 already routed it through the same policy; a Reject tenant that warned on production would be the inconsistency. |
| Warehouse Transfer | hard reject | Moving goods between your own shelves is not selling goods you have not received. A transfer out of nothing would create a shortfall in one warehouse and value in another out of thin air. |
| Inventory Adjustment (Decrease) | hard reject | An adjustment *is* the correction; driving one negative is a data-entry error, not a policy choice. |
| Debit Note | hard reject | You cannot return to a supplier what is not on the shelf. Phase 6's direct-reject precedent, unchanged. |

### 3. The cost catch-up

`IncrementAsync` changed from `Task` to `Task<decimal>` — it returns the catch-up, and **a caller
must post a non-zero result**. Eleven call sites were swept; the interface's doc comment says so in
as many words, because a silently-ignored return here is the Inventory account drifting for ever.

**Why a second GL entry rather than a leg on the receipt's own.** Those eleven call sites build
their entries through six different `IGlPostingRule`s with six different input records. Threading
one more optional amount through all of them would have put the same two lines in six places —
phase 33's copy-N+1 trap. Phase 36 removed the only reason not to do it the other way: it had
already replaced every `SingleAsync` over `(SourceDocumentType, SourceDocumentId)` with
`SourceDocumentGlEntries`, so a second entry against the receipt is reversed with it and displayed
with it. **The tool phase 36 built for its own problem is what made this phase's shape available.**

**Why the Inventory Adjustment account.** Charging the catch-up back to whichever account originally
took the wrong figure would mean remembering, per shortfall layer, which document consumed it and
which account that document's rule debited — and one shortfall can be filled by several receipts,
across periods. The tenant's Inventory Adjustment account already exists for this shape of entry, is
seeded, is on the Accounting Defaults screen, and lands in the Income Statement next to the cost it
corrects. A *new* tenant-default account would have owed a migration, a screen and a backfill for a
figure that is zero on every tenant that never oversells — and phase 29's lesson is that an
unscreened default account is an unreachable one.

### 4. The value-only movement (`StockMovement.ValueAdjustment`)

Phase 26c established that every dated stock figure is reconstructed from `StockMovement`, never
from the FIFO table, because `QuantityRemaining` mutates in place. So a correction the layers and
the ledger both make has to appear there too, or a dated report drifts from both from the fill
onwards.

Two alternatives were tried on paper and rejected:

- **A matched In/Out pair of the filled quantity** (In at the assumed cost, Out at the real one)
  nets to the right value with no quantity change and needs no schema at all — but it inflates the
  **In and Out quantity columns** of every movement report. A wrong quantity is more visible, and
  much less defensible, than a wrong value.
- **Folding the catch-up into the receipt's own unit cost** keeps one row, and misstates what the
  receipt cost.

So: a nullable-free `decimal ValueAdjustment` defaulting to zero, `Value => (Quantity × UnitCost) +
ValueAdjustment`, and `Direction` carrying the sign the same way it does for every other row. A
positive catch-up (the usual case — the assumption is a *previous* purchase price) is an Out row.
`CreateCostAdjustment` refuses a zero amount: a row that moves nothing is noise on a kardex, and
`decimal` keeps the sign bit of a negative zero (phase-26c bug #1).

The migration adds the column NOT NULL with a default of zero and **no hand-written backfill**,
which is the opposite call from phase 31's stored `DueDate` — and the rule that separates them is
that *a default is safe exactly when it is the truth about the rows already there*. Every movement
written before this phase carried its whole value in quantity times unit cost.

### 5. The purchase return at consumed cost

`DebitNotePostingRule` used to credit each Goods line its **return price** and, since phase 29, add
a second Inventory credit for the released freight. Both legs are replaced by one: Inventory is
credited `RelievedInventoryCost` — the sum of quantity times what `ConsumeAsync` actually returned,
landed cost already inside it. Landed Cost Clearing is still debited its released share (that is the
carrier's accrual unwinding, and has nothing to do with what the goods cost).

The difference is then derived as **the plug that balances the entry** — never as a separately
computed figure. That is the only construction that keeps `sum(Debit) == sum(Credit)` under the
currency fold (phase 28's rule) and absorbs the rounding residue in the same place. It is posted to
the Inventory Adjustment account, and the rule throws a friendly 409 naming the missing account
**only when the plug is non-zero**, so a return whose price happens to equal its FIFO cost — the
ordinary case — does not start demanding an account every such return has managed without.

`PurchaseBillPostingLineInput` gained `RelievesStock`, set by `PurchaseBillAccountResolver` (the only
place that knows a line's `Product.Type`). Comparing the resolved account against the Inventory
account instead would have been a guess on any tenant pointing two defaults at one account.

A **standalone** debit note — no source bill — consumes no stock, so it has no relieved cost and
posts exactly what it always did. `RelievedInventoryCost` is nullable for that reason: null means
"unchanged", never "zero", and a test pins the distinction.

### 6. The reversal sweep, and a deleted Domain method

Nine void handlers and seven detail queries read GL entries with `SingleAsync`/`SingleOrDefaultAsync`
over `(SourceDocumentType, SourceDocumentId)`. Phase 36 fixed the three it had broken; this phase
broke four more (Purchase Bill, Credit Note, Inventory Adjustment, Production Journal all post a
catch-up) and swept **all** of them, including the two — Cash Transfer and Expense — that genuinely
post only one entry and could have been left. One rule with no exceptions beats a rule with an
allow-list, and there is now no handler left for a second entry to surprise.

That left `GlJournalEntry.PostReversalOf` with zero callers, and it is **deleted** rather than kept
for a future caller to reach for. `SourceDocumentGlEntries.BuildReversals` produces identical output
for the single-entry case it used to serve, and is the only form also correct for the others. The
one-entry assumption is exactly what kept coming back; CLAUDE.md's phase-33 gotcha is that an
extraction is not done until the copies it replaced are gone.

`ReverseIncrementAsync` also gained `QuantityIn > 0`. A single document can now own layers of both
kinds — a Production Journal consumes raw materials and receives its output against the same pair —
and without the filter a shortfall layer sails past the already-consumed guard (its
`QuantityRemaining` equals its `QuantityIn`, both negative) and reaches `Consume(negative)`, which
throws out of the Domain as a 500.

### 7. Two document types that had never posted

`WarehouseTransfer` and `OpeningStock` post nothing in the ordinary case, and either can be the
receipt that covers a shortfall. `GlSourceDocumentResolver` gained both — the transfer by its code,
the opening stock line by a literal label, exactly as an Opening Balance line already was. The
resolver degrades an unknown type to a null code rather than throwing, so nothing would have told
us: the Journal report and both general ledgers would simply have printed an empty Txn No.

---

## Scope decisions

**A. The shortfall cost is the last known cost, not zero and not the selling price.** Zero would
have made the whole of a sale's cost land as a catch-up months later, in the wrong period, on every
oversell. The selling price is not a cost at all. The last receipt into that warehouse is the best
available estimate and is what the catch-up then corrects. Zero survives as the fallback for a
product never received there — there is no price to guess from, and a test pins that the conservation
law makes it safe: nothing is lost, only recognised later.

**B. The catch-up posts to the Inventory Adjustment account, not COGS.** COGS is arguably more
precise when the oversell was a sale, which it usually is — but not when it was a production run or
a transfer, and knowing which would mean a back-pointer per shortfall layer. Recorded as a
deliberate approximation rather than left implicit.

**C. Warn and Do Nothing differ only in whether the user is interrupted.** The ledger records the
same thing either way. That is what the `IStockAvailabilityPolicy` verdict already expressed
(`DoNothing → Ok`), and phase 37 did not change it — it made the `Ok` verdict mean something.

**D. Three consuming paths stay hard rejects.** Table in §2 above. The reference product's setting
is global; this codebase's is global too, but only two paths can produce a shortfall, and the reasons
are recorded per path rather than derived from "the setting is global".

**E. The Credit Note is answered, not skipped.** §5 of the TL;DR, with a test over a two-price
ledger — a single-cost ledger would have passed whatever the rule was.

**F. No new tenant-default GL account.** §3 above. The corollary is that `DebitNotePostingRule` can
now 409 for a missing Inventory Adjustment account on a tenant that never configured one; it does so
only when the plug is non-zero, and the message names the screen.

---

## Bugs and traps hit

1. **`ReverseIncrementAsync` would have 500'd on a Production Journal** whose raw material ran short
   — `Consume(negative)` out of the Domain. Caught by reasoning about which documents own rows of
   both kinds before writing the fill, not by a test.
2. **The phase-35b enum trap, again.** `PUT /general-settings` answered
   `400 "Failed to read parameter … as JSON"`, naming no field, because three of the six enum values
   in the E2E script were guesses (`LastSellingPrice`, `WithoutTax`, `None` against the real
   `RecentSellingPrice`, `ExclusiveOfVat`, `AccountingMovement`). The fix is to `GET` the resource
   first and echo its own values back.
3. **The prints-and-returns bash trap, walked into after writing a comment warning about it.**
   `mkacct()` both printed its progress line and returned the id through `$( )`, so every account id
   was the whole progress line. The fix is the one CLAUDE.md already records: set a global.
4. **A heredoc in the Bash tool mis-parsed** a Python patch script containing triple quotes, exactly
   as the known gotcha says. Written to a file with the Write tool instead.
5. **Two integration tests failed once and passed on re-run** (Testcontainers under load) — the
   nondeterminism phase 36 recorded for the Angular suite, in the .NET one.
6. **A stale `ErpApp.Api` process from a previous session held the build output** (`MSB3027`, "the
   file is locked by ErpApp.Api"). Stopped before rebuilding.

---

## What the E2E proved

A fresh organization, seeded entirely by direct API calls (12 accounts, 5 account groups, unit,
category, Goods product, warehouse, customer, supplier, accounting defaults in one `PUT`).

| Step | Expected | Got |
|---|---|---|
| Reject tenant, oversell with the override flag set | 409 | 409 "Insufficient stock to approve this invoice." |
| Warn tenant, first approve | 422 | 422, `warningKind: "StockAvailability"` |
| Warn tenant, second approve with override | 200 | 200, document number assigned |
| Covering bill at a different cost | 200 | 200; its detail panel returns **4 GL lines** — two entries, which `SingleOrDefaultAsync` would have 500'd on |
| Purchase return, FIFO relieving an older cheaper layer | Inventory at cost | Dr AP 100, **Cr Inventory 70**, **Cr Inventory Adjustment 30** |
| Nonexistent purchase bill, custom role with no grants | 403 | 403 "(Purchasing.PurchaseBill.Approve)" |
| Nonexistent invoice, same user | 403 | 403 "(Sales.Invoice.Approve)" |

And in SQL Server directly, after the oversell, the fill and the return:

```
FifoLayers   InventoryAccount   MovementHistory   InventoryAdjustment   UnbalancedEntries
128.00       128.00             128.00            -18.00                0
```

The shortfall layer is visible in the FIFO table as `Invoice  -3.0000  0.0000  10.0000` — issued at
10.00, filled by the 14.00 receipt — and the catch-up as the movement row
`PurchaseBill  Out  0.0000  0.0000  12.00`.

The negative-permission proof used a **custom role with no grants at all** rather than a Member,
because Member holds `Purchasing.PurchaseBill.Approve` legitimately; system roles cannot be edited
(409), so a custom role is the only way to prove a document-scoped 403 for a key Member has.

---

## Known limitations

1. **A standalone Debit Note with a Goods line still credits Inventory without touching the FIFO
   ledger** — a pre-existing divergence, not introduced here, and out of scope because `DebitNote`
   carries no `WarehouseId` of its own, so there is nowhere to consume *from*. Closing it means
   either a warehouse on the aggregate or crediting the Purchase account instead; both are a
   modelling decision, not a fix.
2. **The catch-up's period is the fill's, not the sale's.** A sale in a closed period whose cost
   turns out to be wrong is corrected when the receipt lands, which is the only answer that does not
   restate a closed period — but it does mean a margin report for the earlier period keeps the
   assumed cost.
3. **Roadmap 37's other two items are not built** and are carried forward, named: Inventory Master
   gaining WarehouseTransfer and OpeningStock rows (needs a live re-check of the reference report's
   Txn Type filter, 26c Decision D), and multi-UOM × variants (24). Neither was in this session's
   brief, which scoped the phase to the three inventory-policy items plus the Credit Note question.
4. **Nothing in `web/` changed, and that is the finding, not an omission.** The Warn-and-confirm
   dialog has been wired since phase 7 and the `warningKind` split since phase 31; the setting's
   radio group has been on Configurations > General since phase 31. The screen was complete and the
   server was the lie — the mirror of phase 31's rule that a tenant field is only reachable if you
   can name the command that writes it *and* the screen that calls it. Here you could name both, and
   the engine three layers down refused anyway.

---

## Tests

Domain 452 (+9), Application.UnitTests 1013 (+13 net: +14 new, −1 replaced), Api.IntegrationTests 18,
Angular 301. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean.

New: `Domain.UnitTests/Inventory/ShortfallLayerTests.cs`,
`Application.UnitTests/Inventory/NegativeStockTests.cs`,
`Application.UnitTests/TestSupport/StockConservation.cs` (the shared three-view assertion), and three
tests added to `PurchaseBillLandedCostTests`.

**Phase 26c's pinned oversell test was replaced deliberately.** It pinned a *fact* — `ConsumeAsync`
throws — rather than a requirement, and existed to stop the unreachable report guard being tidied
away. What changed is the fact. `NegativeStockTests` is strictly stronger: Reject still throws (same
assertion, now conditional on the setting), Warn and Do Nothing reach the guard, and the guard's own
output is asserted rather than merely protected. `InventoryReportQueryHandlerTests` carries a comment
at the old test's position saying so.

The acceptance test is `StockConservation.AssertHoldsAsync`, which asserts all **three** views
together. Any two of them can be made to agree by patching one; only the third catches a drift —
phase 36's lesson that two things patched into agreement agree by coincidence.
