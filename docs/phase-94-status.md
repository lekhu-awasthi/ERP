# Phase 94 — the Retail till's batch picker

## TL;DR

**The user chose the till's batch picker (2026-10-20), from phase 91 § 5 and phase 93 § 5.** Phase 93 made an ERP
issue name its batch and left the till walking, because it had nowhere to name one. This phase gave it somewhere.

- **The vendor's till was read live first** (abcagro1, nothing written; its one sale was refused in the browser before it
  left). Tapping a batch-tracked product opens a line editor whose *Batch* picker is required ("Select a batch to
  continue"). The restaurant's order screen asks the same at order time. Picking a batch sets the line's price to the
  batch's own selling price, and two batches of one product are two cart lines.
- **A Retail till line names its batch** (Decision A, the vendor's rule). A batch-tracked line with no batch is a 400
  naming the line, as an ERP invoice's has been since phase 93. Phase 93's `StockLineKind.TillIssue`, which let the till
  walk, is gone: a till sale is an `Issue`.
- **The picker.** `GET /pos/tills/{loc}/products/{productId}/batches` lists the batches with stock left at the till's
  warehouse, with what is left of each (summed over its layers, a shortfall included) and its expiry, soonest expiring
  first. It rides the cashier's `Pos.Session.Operate`, as phase 91's serial list does.
  - **On the till.** A tap or a scanned code opens the line on a radio group of those batches, focus on the first. A tap
    grows only a line still waiting for its batch, so each chosen batch is a line of its own. Payment is refused while a
    line names none.
- **Restaurant bills still walk** (Decision B). An order line carries no batch, and carrying one through kitchen
  tickets, parcels, transfers and part bills is its own phase. Their voids and refunds already restock per batch
  (phase 93's `IssuedStockRestock`).
- **A batch has no price of its own here** (Decision C). Picking one changes nothing about the line's price.

| | |
|---|---|
| Live read taken | **yes**, abcagro1 (5 trial days left on 2026-10-20). **Nothing written**: every non-GET was refused by a logger in the page, and the one sale it would have made (`POST /pos/invoices`) was read from the refused request's body (§ 0) |
| Migrations | **none** |
| New permission keys | **none**. The batch list rides `Pos.Session.Operate`, as the serial list and the grid do |
| New endpoint | `GET /pos/tills/{locationId}/products/{productId}/batches?search=`: `[{batchNo, expiryDate, quantity}]`, the batches with stock left at the till's warehouse, soonest expiring first (none last), then first received; at most 200 |
| Changed endpoints | `POST /pos/sales`: a batch-tracked line without `batchNo` is a 400 naming `Lines[i]` (it walked) |
| Changed screens | **Retail till:** a batch-tracked product opens its line on a *Batch* radio group (what is left, expiry); the line shows *Batch B1* or *Batch not chosen yet*; payment is refused until each line names one |
| Changed print output | none |
| Initial bundle | **653.05 kB** (unchanged) against 680 kB; the till is lazy |

Tests: Domain 1040, Application.UnitTests **1884** (+5), Infrastructure.UnitTests 14, Api.IntegrationTests 79,
Angular **912** (+5). All green with Docker up.

## 0. Live read, 2026-10-20 (abcagro1, read only)

The vendor's login had lapsed; the user signed in. Entered as in phases 74–93 (Open Pos's captured URL in the same
tab). A logger wrapped XHR and fetch and refused every non-GET, so the cart could be filled and paid without a write.

- **The Retail till.** P0006 *P93 Batch Item* shows a *BATCH* tag on its tile. A tap opens a line editor before
  anything reaches the cart: Quantity, Unit, Price, **Batch** (a search box over a list, plus *Add New Batch*), Note.
  - **The list** is `GET item-batches?product_id=…`, every batch of the product, **not narrowed by warehouse or
    stock**, shown soonest-expiring first with *Expires dd-mm-yyyy*. Each row says *Balance: 0*, but the payload carries
    no balance field at all: B93A really held 2 and B93B 3 (phase 93's bill of 5 each, less INV0005's 3 and 2).
  - **Required.** *Save* with no batch: *"Select a batch to continue."* and a toast, *"Fill in the required
    batch/serial number details before saving the order."*
  - **The price.** Picking B93A filled Price with 150 and B93B with 170, each batch's `selling_price`. The product's
    own price is 0.
  - **Two batches, two lines**: *P93 Batch Item / Batch B93A* and *… / Batch B93B*.
  - **The sale's body** (refused, so nothing was written): each item carries `batch_id`, `batch_no`, `batch_expiry` and
    `rate` (the batch's price), with `original_rate: 0`.
  - **Scan.** *B93A* typed into the scan box is `GET products-minimized?search_phrase=B93A&source=barcode`, which answers
    `[]`: a batch number does not find its product.
- **The restaurant** (*POS Restaurant*, which has no tables, so its *Take Away* tab) offers P0006 on its grid (and
  the serial phone, which ours keeps off) and opens the same editor with the same required *Batch*: the vendor names
  the batch when the order is taken.

## 1. What shipped

### The rule

`CreatePosSaleCommandHandler` resolves its lines' batches as an `Issue`, the ERP invoice's kind. A batch-tracked line
with no batch is phase 93's 400 (*"…a line issuing it must name a batch."*), and one naming a batch that does not exist
is the existing 400 (*"There is no batch 'B9' for this product…"*). `StockLineKind` is down to `Receipt` and `Issue`,
and the two `!= TillIssue` branches are gone.

### The list

`ListPosProductBatchesQuery` loads the till (its warehouse), refuses an unknown product with a 404, and answers an empty
list for an untracked product or a till with no warehouse, as the serial list does. It reads the product's non-zero
layers at that warehouse through a subquery on its batches, sums them per batch in memory, and keeps those above zero.
A shortfall layer counts, so an oversold batch reads as what it holds. A search narrows by batch number.

### The till

- **The cart.** `CartLine.batchNo` and `CartProduct.batchTracking` (absent on a stored cart, which reads as untracked).
  `add()` grows a batch-tracked product's line only while it waits for its batch. `setBatch()` names one and never merges
  lines, so a radio a keyboard is arrowing through never vanishes under it. `unbatchedLine` gates payment, and
  `toSaleLines()` sends `batchNo`.
- **The page.** A batch product's tap or scan opens its line on the picker and focuses its first radio (or the chosen
  one). Opening a line's *Edit* reads the batches again. The picker is a fieldset of native radios with a legend
  *Batch*: *B1 · 5 JR left · expires …*. The line reads *Batch B1* or *Batch not chosen yet*. The refusal, *"P94 Honey
  is batch-tracked: choose the batch it is sold from."*, shows in the cart and on the payment screen.

## 2. Scope decisions

- **A — The Retail till requires the batch** (the user, recommended). The vendor's rule, and the ERP invoice's since
  phase 93. A cart saved before this phase that holds a batch line shows it as not chosen and refuses payment until one
  is.
- **B — Restaurant bills keep walking** (the user, recommended). The vendor asks at order time. Ours would need a batch
  on `PosOrderLine` and every path that moves one (kitchen tickets, *Mark as Take Away*, item transfer, discard, part
  bills), which is a phase of its own. Their voids and refunds already put each batch back.
- **C — No batch price** (the user, recommended). The vendor's batch carries a required selling price and expiry, and
  picking it reprices the line. Ours carries neither requirement. The line keeps the catalogue price, and the till's
  price key still governs a changed one.
- **D — The list is what this warehouse holds, soonest expiring first.** The vendor lists every batch with a balance it
  never fills. A cashier choosing what to hand over wants what is on this shelf and what to sell first. A batch with
  no expiry sorts last, then first received.
- **E — A tap grows only an unbatched line; lines are never merged.** The vendor asks for a batch on every add. Ours
  lets *+* grow a chosen line, and a second tap starts a line that may come from another batch. Merging two lines of one
  batch on a pick would remove the line being picked on.
- **F — No scan by batch number.** The vendor's scan does not find one, and a batch number is not unique across
  products.

## 3. Bugs found and fixed

- None in the shipped code. The walked-sale tests (phase 93's void and refund) sold at the Retail till, which now
  refuses an unbatched line. They moved to a restaurant bill, the one sale left that walks, and assert the same shelf.

## 4. Evidence

- **Unit and component tests.** `BatchIntegrityTests`:
  - The Retail till's 400s, no batch and an unknown one.
  - A named batch taking only that batch.
  - A restaurant bill still walking.
  - The walked void and refund, on a restaurant bill.
  - The list's order (expiry first), stock and search, its empty answer and its 404.

  `pos-cart.spec.ts`: the line rules, holds and an old stored cart. `pos-till-page.spec.ts`: the picker's focus,
  labels and refusal, and the batch sent, including a second tap.
- **Fresh-org E2E on SQL Server** (`P94 Till Batch …`, `3521b92a-…`).
  - **Setup.** B1 ×5 (expires +30 days), B2 ×5 (no expiry) and B3 ×4 (+10 days, a later bill).
  - **The list.** `[B3 4, B1 5, B2 5]`; `?search=2` gives `[B2]`; an untracked product `[]`; a nonexistent product 404
    *"Product not found."*
  - **Sales.**
    - No batch: 400 `Lines[0]`. Batch `B9`: 400.
    - B2 ×2: 200, and the list shows B2 3. Refunding one: B2 4.
    - B3 ×3, then voided: B3 back to 4.
  - **In SQL.**
    - Each sale's `InvoiceLines.BatchId` names its batch.
    - `StockMovements`: Invoice Out B2 2, CreditNote In B2 1, Invoice Out B3 3, Invoice In B3 3.
    - No P94 Honey layer has `BatchId IS NULL`.
- **403-not-404.** A role copied from Admin without `Pos.Session.Operate`, the run's own membership switched in SQL
  and restored.
  - The batch list of a nonexistent product is **403** *"…(Pos.Session.Operate)."*, beside a 200 on
    `GET /billing-locations`.
  - As Admin again it is 404.
- **Keyboard, in the browser** (erp-web-ssl, the cookie read from `web/public` and deleted).
  - The scan box took *0001* and Enter; focus landed on the first radio (B3, soonest expiring, *4 JR left · expires
    30-10-2026*) with none chosen.
  - **Down** chose B1, focus stayed on it, and the line read *Batch B1*.
  - F9, Enter, Enter completed it. SQL holds the sale's line with batch B1, and B1 fell 5 → 4.

## 5. Left open, and for later phases

- **A batch on a restaurant order line** (Decision B): the vendor asks at order time. It touches `PosOrderLine`,
  kitchen tickets, take-away splits, transfers, discards and part bills.
- **The vendor's batch price and expiry** (Decision C, phase 93 § 5): required there, and picking a batch reprices the
  line. Still a product question.
- **Carried unchanged:**
  - Phase 93 § 5: focus after a failed Save and after *Remove line*, the vendor's per-batch credit-note cap (untested),
    and a partial refund of a walked sale returning the oldest-taken batch, which now applies to restaurant bills only.
  - Phase 91 § 5: the tenant switch, and a serial on the printed bill.
  - Phase 90 § 5, phase 89 § 5, phase 88 § 5, and phase 87 § 5 / Decision G.
  - Phase 84 § 5's money receipt on the roll, phase 81 § 5, phase 80 § 5 and phase 79 § 5.
  - Phase 77 § 5, phase 76 § 5, phase 75 § 5, phase 72 § 5, phase 71 § 5, phase 70 § 5, phase 69 § 5, phase 67 § 5 and
    phase 66 § 5.
  - Phase 62 § 5's barcode index.
- **abcagro1** is unchanged by this phase. It has 5 trial days left (2026-10-25).
