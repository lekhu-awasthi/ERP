# Phase 92 — serial integrity: debit notes, duplicate receipts, voids, and the till scan across products

## TL;DR

**The user chose serial integrity (2026-10-14), from phase 91 § 5.** It covers three gaps phase 91 named and a fourth
this phase found. All four meant the same thing: phase 51 promised a unit is followed by its serial through every
movement, and some paths still moved stock with no serial.

- **A debit note names the units it sends back.** `ApproveDebitNoteCommandHandler` had read `DebitNoteLine` serials
  since phase 51, but nothing wrote them, so a return to the supplier relieved whichever units FIFO chose.
  - **The rule** is `DebitNoteSerials`, phase 91's `CreditNoteSerials` in mirror:
    - only a serial-tracked line names serials, each once on the note;
    - every unit named is in stock at the note's warehouse;
    - a note converted from a bill names only units that bill received and no approved note of it already returned;
    - Approve needs one serial per unit.
  - **Prefill.** The conversion template offers the bill's units still on the shelf.
  - **The form.** It gains the scanner-friendly serial box.
- **Voids put each unit back under its serial (found this phase).** Phase 51's bug had three more copies. Voiding an
  invoice (a till sale included), a debit note, or a warehouse transfer restocked a serialised line as one nameless layer.
  - **The fix.** Each now restocks one layer per serial, with the line's named batch, at the cost it always used.
  - **The GL** does not move.
- **Receiving a unit already in stock is refused by name.** Phase 51's filtered unique index was the only guard, and it
  answered as a 500.
  - **Draft save.** A 400 naming the line and the serial (stored casing). A serial on two lines of one document is also
    a 400.
  - **Every restock path.** The stock ledger itself answers a 409. That covers a bill's approval, a credit note's
    return, every void and a transfer.
  - **Same rule as the vendor's,** read live today: *"is already available in inventory and cannot be added again"*.
- **One serial on two products** stays allowed, because serials are unique per product (the user's Decision D). The
  till's scan offers both, and each row now names its own product's stored unit, where before both carried the first
  one found.
- **The debit-note form had no warehouse (found by the browser pass).** Phase 43 made the server require one for a
  goods line, and no screen ever sent it.
  - **Converted notes** worked only through the server's fallback to the bill's warehouse.
  - **A standalone goods return** could not be saved from the screen at all.
  - **Now** the form has the vendor's Warehouse field: seeded from the location's default, prefilled from the bill,
    read back and sent.

| | |
|---|---|
| Live read taken | **yes**, abcagro1 (11 trial days left on 2026-10-14). **No writes**: two forms opened and left unsaved, two serials typed into their boxes (§ 0) |
| Migrations | **none** |
| New permission keys | **none**. Debit-note serials ride the note's own Create/Edit/Approve keys |
| Changed endpoints | **Debit notes:** `POST`/`PUT /debit-notes` lines take `serialNumbers`. `GET /debit-notes/{id}` lines carry them. `GET /purchase-bills/{id}/debit-note-conversion-template` lines carry the units still on the shelf. **Receipts:** `POST`/`PUT /purchase-bills` answer a 400 for a unit already in stock or named on two lines, and every approve or void that restocks a unit already in stock answers a 409. **Till:** `GET /pos/tills/{loc}/products?code=` rows each carry their own `scannedSerialNo` |
| Changed screens | **ERP debit note:** a serial box on a serial line (scan with Enter, repeats out, a count); a **Warehouse** select |
| Changed print output | none |
| Initial bundle | **653.05 kB** (unchanged) against 680 kB |

Tests: Domain 1040, Application.UnitTests **1840** (+8), Infrastructure.UnitTests 14, Api.IntegrationTests 76, Angular
**902** (+2). All green with Docker up.

## 0. Live read, 2026-10-14 (abcagro1, read only)

- **The vendor's debit note carries serials.** *Create Debit Note* from PB0002 opens a form whose `form_data` items
  carry `serial_numbers`, and the expanded line shows the same *SERIAL NUMBERS* tag input as the bill.
  - **Prefill.** It had all three units the bill received (IMEI91A, B, C) and read "3 / 3 serial numbers added". All
    three were in stock, so whether the vendor leaves out a unit that was sold could not be seen.
  - **The warehouse.** The form has a required header *Warehouse* and a per-line WAREHOUSE.
- **A debit note's serial is checked against the warehouse's stock.** Typing NOTREAL1 sent `POST
  item-serial-numbers/validate {source: "Debit Note", in_movement: false, warehouse_id}` and got a 400: "serial number
  NOTREAL1 is not available in the selected warehouse". That is ours exactly (Decision B).
- **A receipt of a unit already in stock is refused.** On *Make Duplicate* of PB0002, IMEI91C was removed and typed
  back. The request was `{source: "Purchase Bill", in_movement: true}` and the answer a 400: *'Serial Number "IMEI91C" is
  already available in inventory and cannot be added again.'* That is ours (Decision C), product-wide like the vendor's
  message.
- **Vendor defect.** *Make Duplicate* copies the bill's serials into the new form, all of them already in stock, with
  no check. Its Save was not tried (it would be a write).
- **Writes:** none. The logger refused every non-GET except the two `validate` calls the boxes made; nothing else was
  sent.

## 1. What shipped

### Debit notes carry their serials

- **The rule.** `src/Application/Purchasing/DebitNoteSerials.cs`:
  - `FindProblemAsync` checks a note's serials: serial-tracked only, each once, the bill's own and not yet returned on a
    converted note, in stock at the note's warehouse, and a whole quantity of them at Approve.
  - `SaveDraftAsync` writes them positionally, as the credit note's draft does.
  - `ReceivedAsync` and `ReturnedAsync` answer per bill. A void gives its units up: a sum over approved notes, never a
    counter.
- **The command paths.**
  - Create and Update call `SaveDraftAsync` (a 400 naming the line).
  - Approve checks with `requireCount: true` before the number is drawn (a 409).
  - `GetDebitNote` reads the serials back.
- **The template.** `GetDebitNoteConversionTemplate` prefills, per line key, the bill's units not returned and still in
  the bill's warehouse. A unit sold since is left out: it is with a customer, and the box's count shows how many are
  still to name.
- **The form.** `debit-note-detail-page` gains the box on any draft serial line, converted or standalone. It reuses
  phase 91's `serial-entry.ts`: Enter ends a serial, a repeat comes out and is named, focus stays, and the box is written
  by hand as well.

### Voids restock under the serial

- **The three handlers.**
  - **VoidInvoice:** each line's serials from `DocumentLineSerials` and its `BatchId`, through `LineStockAllocator.
    IncrementLineAsync` at the line's `CogsUnitCost`.
  - **VoidDebitNote:** the same, at `ConsumedUnitCost`.
  - **VoidWarehouseTransfer:** each destination layer's own `BatchId` and `SerialNo`.
- **The GL.** Quantities and costs are unchanged, so it does not move.
- **Unchanged.** A line naming neither key takes exactly the old path.

### A unit is received once

- **`SerialStock`** (`src/Application/Inventory/Stock/SerialStock.cs`) is the one question: which of a product's serials
  are in stock. It reads through phase 51's filtered index and answers with the stored value.
- **The ledger's check.** `StockLedgerService.IncrementAsync` refuses a serial already in stock with a 409. It asks the
  change tracker too, because a transfer takes a unit out and puts it back in within one save: the tracked source layer
  is at zero while the stored one still says one.
- **The draft check.** `DocumentLineAllocationWriter.ResolveBatchesAsync`, which invoice, bill and till sale share,
  refuses:
  - a serial on two lines of one document;
  - on a receipt, a unit already in stock.

### The till scan across products

`PosSellableProducts` keeps a scanned serial per product. A serial both products hold in stock lists both, and the
cashier chooses through phase 24's existing "choose one" list. Each row names its own product's unit as received.

## 2. Scope decisions

- **A — Voids are fixed in this phase** (the user, recommended). All three: invoice and till sale, debit note,
  transfer. A batch-tracked line that named no batch (an issue walked across batches) still restocks un-batched (§ 5).
- **B — A standalone debit note names any unit on the shelf** (the user, recommended). A converted one names only its
  bill's units, and both need one per unit at Approve. The vendor checks the same warehouse stock (§ 0).
- **C — A duplicate receipt is refused at the draft and by the ledger** (the user, recommended). The draft check
  matches the vendor's validator. The ledger's 409 covers whatever changes between save and approve, and every
  non-bill restock.
- **D — Serials stay unique per product, and the till offers both** (the user, recommended). Two makers' numbers can
  collide, so a serial is not refused because another product holds it.
- **E — The debit note's warehouse is fixed in this phase** (not asked: a defect the E2E hit). Decision B cannot be
  used on screen without it. It mirrors the purchase bill's control (phase 35a's seed). The label reads *Warehouse (for
  goods)* because the server requires it only then. The a11y guard holds the three field-error bindings to their
  standard shape, which left no room for a separate hint.
- **F — No new keys.** Serials are a field of the note, gated by its own Create, Edit and Approve keys, as the credit
  note's are.

## 3. Bugs found and fixed

1. **Debit-note serials were read and never written** (phase 51). A return relieved FIFO's choice of units.
2. **Three voids restocked a serial line with no serial or batch** (phase 51 and phase 43). Voided sales, debit notes
   and transfers made stock no serial could sell. Found by grepping the restock calls, per phase 91's lesson.
3. **A duplicate receipt reached the unique index** (phase 51). It was read from the code at phase 91's close as
   "likely a 500" and not run before the fix; it is now a 400 or 409 naming the unit.
4. **The till scan named every row's unit after the first product found** (phase 91). A tablet's `imei-2` would have
   sold as the phone's `IMEI-2`.
5. **The debit-note form had no warehouse** (phase 43). A standalone goods return could not be saved from the screen;
   the PUT answered "A debit note with a Goods line needs a warehouse".
A design note rather than a bug: the ledger's check reads the change tracker as well as the database from the start.
A database-only read would refuse every serialised transfer, whose source layer is consumed in memory before the
destination layer is written. `A_unit_restocked_and_moved_in_one_save_is_not_mistaken_for_a_second_copy` pins it.

## 4. Evidence

- **Unit tests.** `tests/Application.UnitTests/Inventory/SerialIntegrityTests.cs`, 8 tests:
  - the converted note's prefill and its named return;
  - the 400s (a unit not received, a unit sold) and the short Approve's 409;
  - a standalone note and its void;
  - voids of a sale and of a transfer;
  - the 400 and 409 on duplicate receipts;
  - the change-tracker case;
  - the scan across two products.

  Each restock test asserts that every layer is of one and carries a serial. The old voids wrote one nameless layer
  of the whole quantity, which cannot pass that (reasoned from the code; the old handlers were not re-run). The scan
  test uses one casing on both products, because InMemory compares case-sensitively; the E2E shows the per-row casing.
- **Page spec.** `debit-note-serials.spec.ts` (2 tests): the box only on the serial line, holding what was saved; a scan
  by Enter keeps focus, and Save sends exactly the units and the warehouse.
- **Fresh-org E2E on SQL Server** (org `P92 Serial 771416`, `fea606fa-…`; an empty org from a failed first seed run was
  also left behind).
  - **Receipts.** PB1 received IMEI-1..4. A draft naming `imei-2` was a **400** naming `'IMEI-2' of 'P92 Phone'`. NEW-1
    on two lines was a **400** naming `Lines[1]`. Two drafts naming NEW-1: the first approved, the second a **409**
    ("Serial number 'NEW-1' is already in stock").
  - **Debit notes.** IMEI-1 was sold on an ERP invoice, so the template offered IMEI-2/3/4 and, after the return,
    IMEI-2/4. Naming IMEI-1 was a **400** "not in stock in this note's warehouse"; naming NEW-1 a **400** "not received
    on the purchase bill". A short Approve was a **409**. A standalone note of NEW-1 was approved and voided.
  - **Voids.** The invoice was voided, and a transfer of two phones was approved and voided.
  - **In SQL:** `inventory.StockLedgerEntries` for the phone hold **zero nameless layers**.
    - Every restock is a layer of 1 under its serial: IMEI-1 `Invoice`, IMEI-2 and IMEI-4 `WarehouseTransfer`, NEW-1
      `DebitNote`. IMEI-3 is out.
    - `StockMovements` name each unit in and out.
  - **Till scan.** The tablet received `imei-2` (allowed, per product). `GET /pos/tills/{loc}/products?code=IMEI-2`
    (and `Imei-2`) answered `[('P92 Phone', 'IMEI-2'), ('P92 Tablet', 'imei-2')]`.
- **403-not-404.** A role copied from Admin without `Purchasing.DebitNote.Approve` (membership switched in SQL, then
  restored) got a **403 naming the key** on approving a nonexistent note. The same user got a 200 on `GET /debit-notes`
  and on the note itself. As Admin, the same request was a **404** "Debit note not found."
- **Keyboard and browser pass** (cookie through `web/public`, file deleted at once):
  - On a standalone draft, the box took IMEI-2 ⏎, imei-2 ⏎ (taken out and named: "imei-2 is already on this line. 1 of
    2 serial numbers") and IMEI-4 ⏎. It read `IMEI-2, IMEI-4, `, "2 of 2", with focus kept.
  - The first Save (Enter on the focused button) was the phase-43 400 above: bug 5, fixed, then repeated.
  - After the fix the Warehouse read back as *Main Warehouse*, and SQL holds exactly IMEI-2 and IMEI-4 on the line with
    that warehouse.
- **PDFs:** none changed.

## 5. Left open, and for later phases

- **A batch-tracked line that named no batch** (an issue walked across batches) still restocks un-batched on a void.
  The fix is to read the document's own Out movements per batch and share them across lines of one product.
- **`DebitNoteLine.BatchId` is never written either.** Approve reads it and no request carries it: the batch half of
  this phase's serial bug. The phase-51 comment says it is "prefilled from the bill line".
- **A serial longer than 100 characters** reaches `DocumentLineSerial.Create`'s invariant as a 500 on every serial path.
  It needs a validator rule (phase 39's shape).
- **Two approvals racing for one serial** still meet only the unique index (a 500). The ledger's check is
  read-then-write.
- **The vendor's *Make Duplicate*** copies in-stock serials unchecked (a vendor defect; we have no duplicate action).
- **Carried unchanged:** phase 91 § 5's tenant switch and the serial on the printed bill; batch-tracked products at the
  till; phase 90 § 5; phase 89 § 5; phase 88 § 5; phase 87 § 5 / Decision G; phase 84 § 5's money receipt on the roll;
  phase 81 § 5; phase 80 § 5; phase 79 § 5; phase 77 § 5; phase 76 § 5; phase 75 § 5; phase 72 § 5; phase 71 § 5;
  phase 70 § 5; phase 69 § 5; phase 67 § 5; phase 66 § 5; phase 62 § 5's barcode index.
