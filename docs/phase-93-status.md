# Phase 93 — batch integrity: the batch half of phase 92's serials

## TL;DR

**The user chose batch integrity (2026-10-15), from phase 92 § 5.** Phase 92 made a serial follow its unit through
every movement. This phase did the same for the batch, and found the vendor's rule was stricter than ours.

- **An ERP issue names its batch.** Read live today: the vendor refuses an invoice, credit-note or debit-note line of a
  batch-tracked product with no batch (*"Batch is required for 'P93 Batch Item'"*). Ours let an invoice line leave it
  blank and walk every batch oldest-first.
  - **The rule** (the user's Decision A). An invoice or debit-note line issuing a batch-tracked product must name its
    batch, a 400 naming the line.
  - **The till keeps the walk.** It has no batch picker yet (phase 91 § 5).
  - **The mechanism.** `StockLineKind` (`Receipt`, `Issue`, `TillIssue`) replaced phase 51's `bool isReceipt`, so the
    compiler listed all five callers.
- **Nothing wrote either note's batch.** Phase 51 gave `DebitNoteLine` and `CreditNoteLine` a `BatchId`, Approve read
  it, and no request carried it. A debit note relieved whichever batch FIFO reached, and an ERP return came back into
  no batch at all. Both lines now take `batchNo`, prefilled by their conversion templates.
  - **Debit note** (`DebitNoteBatches`, Decision C). Required on a batch-tracked goods line. A converted note names
    only a batch its bill received; a standalone one any batch.
  - **Credit note** (`CreditNoteBatches`, Decision B). Only a note converted from its invoice restocks, so only it
    names one, and must. Each batch must be one the invoice issued, and no more of it than the invoice issued less
    what other notes brought back. A note that moves no stock naming a batch is a 400.
  - **Both** are a 400 at the draft and a 409 at Approve, as phase 91/92's serial rules are.
- **A walked issue goes back into the batches it took (phase 92 § 5).**
  - **Voids.** Voiding a till sale, or an older ERP invoice or debit note that walked its batches, restocked the whole
    quantity as one un-batched layer.
  - **Till refunds.** A refund of such a sale did the same.
  - **The fix.** `IssuedStockRestock` reads what the document itself took, from its own `StockMovement` rows per batch
    and serial, and divides it among the lines. A named batch is taken first; a walked line takes what is left, in the
    order it was taken. A refund reads the sale's issue less earlier refunds'.
  - **Costs** stay the line's own, so **the GL does not move**.
- **Two more 500s are now answers.**
  - **Over-long numbers.** A serial or batch number over 100 characters, or a blank serial, is a 400 naming the field on
    all ten line-bearing commands (`StockLineValidationRules`).
  - **The serial race.** Two approvals racing for one serial now get a **409** (`AppDbContext.SaveChangesAsync` maps
    phase 51's unique index). It was a 500. Proved on SQL Server.
- **Found by the browser pass: the credit note named the wrong customer.** Its customer `<select>` bound `[value]`
  (phase 5's race), so every converted or saved note showed the walk-in. Only the display was wrong (SQL held Hari
  Customer). It now binds `[selected]` per option. A census found four more selects of that shape on two screens.
  They were fixed the same way, and `select-binding-sweep-guard.spec.ts` now fails any `<select>` binding `[value]`.

| | |
|---|---|
| Live read taken | **yes**, abcagro1 (10 trial days left on 2026-10-15). **Writes, through the vendor's own screens:** Batch Tracking switched on in Configurations > General; product P0006 *P93 Batch Item* (FIFO); batches B93A, B93B; PB0004/HO/83-84 and INV0005/HO/83-84 approved. Two Saves were refused by the vendor and wrote nothing (§ 0) |
| Migrations | **none** |
| New permission keys | **none**. A note's batch rides its own Create/Edit/Approve keys, as its serials do |
| Changed endpoints | `POST`/`PUT /invoices`: a batch-tracked line without `batchNo` is a 400. `POST`/`PUT /debit-notes` and `/credit-notes` lines take `batchNo`; `GET` of both reads it back; both conversion templates prefill it. All ten line-bearing commands: a serial over 100 characters, a blank serial or a batch number over 100 is a 400 naming the field. Any save putting a serial into stock twice at once is a 409 |
| Changed screens | **ERP debit note** and **ERP credit note (converted)**: a *Batch no* box on a batch-tracked line. **Credit note:** the Customer select shows the note's own customer. **Cash Transfer** (From and To account) and **Opening Balances** (Side, Warehouse): each select shows the stored value |
| Changed print output | none |
| Initial bundle | **653.05 kB** (unchanged) against 680 kB |

Tests: Domain 1040, Application.UnitTests **1879** (+39), Infrastructure.UnitTests 14, Api.IntegrationTests **79** (+3),
Angular **907** (+5). All green with Docker up.

## 0. Live read, 2026-10-15 (abcagro1, writes through its own screens)

The tenant had no batch-tracked product: **Batch Tracking was off at tenant level** (Configurations > General, beside
Serial No Tracking, which was on). The product form shows its *Batch Tracking* toggle only once the tenant switch is on
(`POST general-settings {type: "batch_tracking_enabled", value: true}`).

- **A batch is created from the line.** On a bill, typing a new number in *Item Batch* offers *Add "B93A"*, which opens
  a **New Batch** modal: Batch No., Manufacture Date, **Expiry Date (required)**, **Selling Price (required)**, Purchase
  Price. Save is `POST item-batches` at once, not at the bill's save, which is ours (minted at the draft, phase 51).
  - **Divergences, not built:** the vendor requires an expiry and a price on every batch. Picking a batch on an
    invoice sets the line's rate to that batch's selling price.
- **An issue must name its batch.** An invoice line of P93 with no batch: `400 "Batch is required for \"P93 Batch
  Item\"."`. The invoice's batch picker lists the product's batches with their expiry, plus *Add New*.
- **Both notes prefill the batch and refuse a blank one.**
  - **Credit note.** *Create Credit Note* from INV0005 (B93A × 3, B93B × 2) carried each line's `batch_id` and
    `batch_number` in its `form_data`. The picker is editable and lists every batch of the product. Clearing one and
    saving: `400 "Bad Request."`.
  - **Debit note.** *Create Debit Note* from PB0004 prefilled B93A and B93B at the bill's **full** quantities (5 each,
    though 3 and 2 had since been sold). A blank batch was the same 400.
- **Writes:** the switch, the product, two batches, an approved bill and an approved invoice, all clicked through the
  vendor's screens. The two refused saves wrote nothing. Batch Tracking was left on.

## 1. What shipped

### The issue rule

`StockLineKind` says what a line does to stock. `StockTrackingRules.ValidateLineAllocation` takes it:

- **`Receipt`** must name a batch (unchanged).
- **`Issue`** (an ERP invoice or debit note) must name one (new; *"…so a line issuing it must name a batch."*).
- **`TillIssue`** may leave it blank and walk.

`DocumentLineAllocationWriter.ResolveBatchesAsync` passes it through. The new `ResolveNoteBatchesAsync` is its batch-only
half for the two notes, whose serials have their own rules. It never mints a batch: a return names one that exists.

### The notes

- **`DebitNoteBatches.FindProblemAsync`.** Refuses a batch-tracked goods line with no batch (a draft saved before this
  phase meets this at Approve), and a converted note's batch that its bill did not receive. Called on Create and Update
  (400) and in Approve (409).
- **`CreditNoteBatches.FindProblemAsync`.** Applies only to a note converted from its invoice. It refuses a tracked
  goods line with no batch, and a batch beyond what the invoice issued less what other notes brought back. That
  outstanding figure is read through `IssuedStockRestock.OutstandingOnInvoiceAsync`, the same reader the approval's
  restock and the template use, so the three cannot disagree. Called on Create and Update (400) and in the ERP's
  Approve handler (409). It is not called in the shared posting: a till refund copies its sale line's batch, and the
  refund planner already caps it by the line.
- **Templates.**
  - **Debit note:** a key's remaining quantity is offered batch by batch, each at most what the bill received of it
    less what approved notes sent back under it, in batch-number order.
  - **Credit note:** offered batch by batch from the outstanding reader, in the order the sale took them. A key the
    movements do not cover (stock issued before phase 51) is offered with no batch, for the user to name.
- **Detail DTOs** carry `BatchNo` as an init property, read through `LineAllocationReader.LoadBatchesAsync`.

### Putting a walked issue back

`IssuedStockRestock`:

- `OfDocumentAsync` reads a document's net movements (Out less In) per product, batch and serial.
- `OutstandingOnInvoiceAsync` reads the sale's net issue less every note returning it. A voided note's restock is
  reversed by its void's own movements, so it gives its share up.
- `Plan` is pure, and divides those rows among the lines:
  1. a serial goes back under the batch it left from;
  2. a line that named a batch takes it;
  3. a line that named none walks what is left of its product in the order it was taken;
  4. what no movement covers goes back un-batched, as it always did.
- `PutBackAsync` makes the calls.

The callers are the invoice void (a till sale included), the debit-note void and `CreditNoteApprovalPosting` (ERP
returns and till refunds). A product without batches has one un-batched pool, so it takes one part per line: the path
every line took before.

### The two 500s

- **`StockLineValidationRules`.** `SerialNumberRules` (blank or over 100 after trimming) and `BatchNoRule` (over 100).
  Each member is registered from one expression, phase 64's rule, on the invoice, purchase bill, credit note and debit
  note (Create and Update each), the till sale and the till refund. The till sale's own `MaximumLength(100)` on
  `BatchNo` was replaced, not duplicated.
- **`AppDbContext.SaveChangesAsync`.** A `DbUpdateException` whose `SqlException` (2601/2627) names
  `IX_StockLedgerEntries_OrganizationId_ProductId_SerialNo` becomes a `ConflictException` (`SerialInStockRaceMessage`).
  Any other unique index still surfaces as the database error it is (a test holds that).

### The screens

- **Debit note and converted credit note.** A *Batch no* box under the product on a batch-tracked line, beside the
  serial box (free text, `maxlength="100"`). Read back from the detail, prefilled from the template, sent trimmed or
  null. A read-only note shows *Batch B2*.
- **Credit note's Customer.** Bound per option.

## 2. Scope decisions

- **A — An ERP issue must name its batch; the till may walk** (the user, recommended). This is the vendor's rule, read
  live. The till keeps its walk until it has a picker (phase 91 § 5). Voids and refunds of what walked are handled by
  reading the movements, which covers the till and every ERP document saved before this phase.
- **B — A converted credit note's batch is capped per batch** (the user, recommended). It must be one the invoice
  issued, within what is left. This is phase 91's serial rule for batches. The vendor's picker offers every batch; its
  cap was not tested (a save would have been a write of a return).
- **C — A converted debit note names only a batch its bill received** (the user, recommended). Stock is the ledger's
  question at Approve, as for an invoice. A standalone note names any batch. Phase 92's rule for serials.
- **D — The race is a 409** (the user, recommended). Mapped at the one save every path shares, by the index's name.
- **E — A walked line returns into the first batches it took.** The movements say which batches a document took, not
  which line took which. When a till refund returns part of a walked sale, the oldest-taken batches come back first.
  Nobody can know which physical units came back without a picker; this rule is at least never outside what the sale
  took, and two refunds of one sale cannot both claim one batch.
- **F — Every select bound by `[value]` is fixed in this phase.** The credit note's Customer was fixed unasked: a defect
  the browser pass hit on a form this phase changes. The census then found four more (Cash Transfer's From and To
  account, Opening Balances' Side and Warehouse). The user had them done in this session, with a guard.
- **G — No new keys, no migration.** A note's batch is a field of the note.

## 3. Bugs found and fixed

1. **Nothing wrote a debit note's or an ERP credit note's batch** (phase 51). Fixed above. `StockTrackingRules`' class
   comment had called both notes' batches "derived from the source"; nothing derived them. Grep for the write, not the
   comment (phase 91's lesson again).
2. **A walked issue's void and refund restocked into no batch** (phase 92 § 5). Fixed by `IssuedStockRestock`.
3. **Over-long serial or batch numbers were 500s on every path** (phase 92 § 5). Now 400s naming the field.
4. **The serial race was a 500** (phase 92 § 5). Now a 409.
5. **The credit note showed the walk-in as every converted note's customer** (found by the browser pass). Phase 5's
   `[value]`-on-a-select race; it reproduced after a reload, on a saved note whose SQL row named Hari Customer. Fixed
   per option.
6. **The credit-note template's batch order followed the invoice's line load order**, which is not fixed. The first E2E
   run read B2 before B1. It now follows the order the sale took them, and the debit-note template follows the batch
   number.
7. **Four more selects bound `[value]`** (the census after bug 5): Cash Transfer's From and To account, Opening Balances'
   Side and Warehouse. Each was bound per option, and the shape is now guarded (§ 4).

## 4. Evidence

- **Unit tests** (Application, +39):
  - **`BatchIntegrityTests`** (7):
    - the ERP 400 and the till's walk;
    - a walked till sale's void;
    - two refunds of a walked sale;
    - a debit note's prefill, named return, the next template and its void;
    - the debit note's three 400s (no batch, a batch from another bill, a typo) and a standalone note;
    - the credit note's prefill, its blank and over-cap 400s, the approval and the next template;
    - a note that returns no goods.
  - **`IssuedStockRestockPlanTests`** (3): named-first sharing; one part per line for an untracked product; a serial
    under its batch, and uncovered stock un-batched.
  - **`StockLineValidationRulesTests`** (29): each of the ten validators built and run on a 101-character serial, a
    blank serial and a 101-character batch, plus the 100-character boundary.
  - **They bite.** Emptying the movements the planner reads (phase 51's behaviour) failed both till tests and both
    planner tests; the file was restored by hash.
- **Integration** (+3, SQL Server): `SerialStockRaceTests`. Two contexts read IMEI-93 absent and both write it: the
  first saves, the second is a 409 with the message, and one row remains. A duplicate batch number is still a
  `DbUpdateException`. A unit sold and bought back is not a conflict.
- **Page spec** `debit-note-batches.spec.ts` (2): the box only on the batch line, holding what was saved; typing ` B1 `
  with focus kept, then Save sends `['B1', null]`.
- **Guard** `select-binding-sweep-guard.spec.ts` (3). It covers every template and inline `template:`, with comments
  stripped, and fails any `<select>` opening tag binding `[value]`. It bites: putting the binding back on Cash Transfer's
  To Account failed it, naming the file, and the file was restored by hash.
- **The four selects on screen**, all keyboard-driven, focus kept throughout:
  - **Cash Transfer.** A draft seeded Cash → Nabil Bank. Both selects showed those accounts. Down on To Account showed
    Petty Cash; Enter on *Save Draft* stored Cash → Petty Cash in SQL, and a reload showed Petty Cash.
  - **Opening Balances, Side.** Payable at 750 CR: its edit row opened on CR. Up showed DR; Save stored Debit 750,
    Credit 0, and a reload opened the row on DR.
  - **Opening Balances, Warehouse.** Phase 92's org, which has two warehouses (phase 93's has one by design: a second
    is a 403 without Multiple Warehouses). Down from Branch showed Main Warehouse, and the list's reload asked for Main's
    id, checked in SQL.
- **Fresh-org E2E on SQL Server** (org `P93 Batch 211002`, `38867e9d-…`; an earlier run's org `3b09e88b-…` stopped at
  the template-order bug and was left).
  - **Receipts.** PB1 received B1 5 @ 100 and B2 5 @ 120; PB2 received B3 4.
  - **Issue rule.** An invoice line with no batch: **400** `Lines[0]` *"…a line issuing it must name a batch."* INV1
    sold B1 × 3 and B2 × 2.
  - **Credit notes.**
    - The template offered `[B1 3, B2 2]`.
    - B2 × 3: **400** *"issued 2 of batch 'B2' … this note returns 3"*.
    - A blank batch: **400** `Lines[0]`. A standalone note naming a batch: **400** *"returns no goods"*.
    - CN1 returned B1 × 2, read back B1 and approved. The next template offered `[B1 1, B2 2]`.
  - **Debit notes.**
    - The template offered B1 5 and B2 5.
    - B3: **400** *"not received on the purchase bill"*. A blank batch: **400**.
    - DN1 returned B2 × 2, read back B2, approved and voided.
  - **Lengths.** A 101-character batch: **400** `Lines[0].BatchNo`. A 101-character serial: **400**
    `Lines[0].SerialNumbers[0]`.
  - **Till.**
    - Sale 1 walked 4 and was voided: its movements were Out B1 2, B2 1, B3 1, and In the same per batch.
    - Sale 2 walked 4 (Out B1 1, B3 3). The refund of 3 came back as In B3 3, a batch the sale took.
  - **In SQL.** The item's `inventory.StockLedgerEntries` hold **zero** layers with `BatchId IS NULL`. The shelf reads
    B1 3, B2 3, B3 4, which is 14 received − 5 invoiced + 2 returned − 4 till-sold + 3 refunded. The credit-note and
    debit-note lines store their batches.
- **403-not-404.** A role copied from Admin without `Sales.CreditNote.Approve`, the membership switched in SQL and
  restored:
  - approving a nonexistent note was a **403 naming the key**;
  - the same user got a 200 on `GET /credit-notes` and on CN1;
  - as Admin the same request was a **404** *"Credit note not found."*
- **Keyboard and browser pass** (cookie through `web/public`, file deleted at once).
  - **The debit note.** From PB1, Enter on *Convert to Debit Note* opened the form prefilled B2/B1.
    - **The refusal.** In line 1's batch box: Ctrl+A, `B3` (Enter in the box submitted nothing). Enter on *Save Draft*
      showed *"Line 1: batch 'B3' was not received on the purchase bill this note returns."*
    - **The save.** Then `B2`, quantity 1, line 2 removed and *Save Draft*. SQL holds one line, B2 × 1, `ReferrerType
      PurchaseBill`, and the page read B2 back.
  - **The credit note.** Converted from INV1, it showed B1 × 1 and B2 × 2. Saved, SQL holds Hari Customer, B2 × 2 and
    B1 × 1. The Customer read *WALKIN* until bug 5's fix, then *0001 — Hari Customer* after a reload. A new note opens
    on *Select a customer*, and Down arrow chose a customer with focus kept.
  - **Two focus losses seen, both older than this phase** (§ 5). A failed Save leaves focus on `<body>`, because the
    button disables itself. Removing a line does the same.
- **PDFs:** none changed.

## 5. Left open, and for later phases

- **Focus falls to `<body>` after a failed Save** on every form whose button is `[disabled]="saving()"`, and after
  *Remove line* on the line forms (about 15). Both are older than this phase and need an app-wide pass, not one form.
- **The till still sells batch-tracked products un-narrowed** (phase 91 § 5). Its void and refund now put each batch
  back, so what is left is the picker itself.
- **The vendor's batch carries an expiry and a selling price, both required**, and picking a batch sets the line's
  rate from it. Ours has optional dates and no price. Read today, not built; a product question.
- **A refund of part of a walked sale returns into the oldest-taken batch** (Decision E). Only a picker would know which
  physical units came back.
- **The vendor's credit-note cap per batch was not tested** (a save would have written a return). The vendor's debit
  note prefills the bill's full quantities, not what is on the shelf.
- **abcagro1 now has Batch Tracking on** and holds P0006, B93A, B93B, PB0004/HO/83-84 and INV0005/HO/83-84.
- **Carried unchanged:** phase 91 § 5's tenant switch (ours has none; the vendor's is now read) and the serial on the
  printed bill; phase 90 § 5; phase 89 § 5; phase 88 § 5; phase 87 § 5 / Decision G; phase 84 § 5's money receipt on
  the roll; phase 81 § 5; phase 80 § 5; phase 79 § 5; phase 77 § 5; phase 76 § 5; phase 75 § 5; phase 72 § 5; phase 71
  § 5; phase 70 § 5; phase 69 § 5; phase 67 § 5; phase 66 § 5; phase 62 § 5's barcode index.
