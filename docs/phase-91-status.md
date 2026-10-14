# Phase 91 — serials at the till, returns that carry their serials, the till refund's owed read, and one guest declining the service charge

## TL;DR

**Three items the user chose (2026-10-13), from phase 62 § 5, phase 90 § 5 and phase 83 § 5.** All three were read or
measured before they were built.

- **Serial-tracked products sell at the till.** Phase 62 left a gap: the cart sent no serials, so selling such a product
  was the server's 400.
  - **Tap or scan.** Tapping a serial product opens its cart line's picker: a box to scan or type a serial into, and a
    checklist of the units in stock at the till's warehouse. A scan in the main box that matches no product code is
    tried as an in-stock serial and adds the phone with that unit named.
  - **One line, gated payment.** A serial product keeps one line. Payment is refused while its count of serials
    differs from its quantity.
  - **Not on restaurant orders.** They are left off the restaurant's grid, and an order naming one is a 409.
- **Returns carry their serials, a phase-51 gap fixed.** `CreditNoteApprovalPosting` restocked a serial-tracked product
  as one layer of the whole quantity with **no serial**. The returned phone became stock that no serial could ever be
  sold from, on every ERP credit note since phase 51.
  - **The rule.** A note's lines now carry serials, checked by `CreditNoteSerials`: each must be a unit the invoice
    sold and no approved note has brought back (a void gives it up). Approve needs one serial per unit, and each comes
    back as its own layer of one.
  - **Where they come from.** The till refund ticks the sale line's units. An ERP note converted from an invoice is
    prefilled with the units still out, in a serial field like the invoice form's.
- **The till refund's owed read: 56,566 → 59 logical reads.** `PosRefundPlanner.OwedOnSaleAsync` asked
  `OutstandingDocumentReader` about the sale's customer. At a till that is the walk-in, who owns every counter sale.
  - **Measured** on `tools/scale` after `seed-phase91.sql` (20,000 walk-in till sales, 2,000 refunds): one load of the
    refund screen cost 56,566 reads and 1.3 s of CPU.
  - **Narrowed:** the reader now takes an `invoiceId` and reads that sale through itself (819 reads).
  - **Indexed:** a measured index on `CreditNotes (OrganizationId, ReferrerId)` finished it (59). The customer picker
    and Suggest also fell 588 → 138, and no phase-90 path read more.
- **One guest may decline the service charge on their own bill** (phase 83's part discount, same shape). The part carries
  none, and each line records what it declined (`InvoiceLine.PartServiceChargeWaived`).
  - **Never charged back.** The order's planner prices a line's whole less every waiver its bills not voided recorded,
    so the last part never charges a later guest what an earlier one declined. A twin-tab test holds the next guest's
    bill to the paisa.
  - **Key:** the order waiver's own, `Sales.Invoice.TillRemoveServiceCharge` at the order's location.
  - **Printing.** The receipt, the ERP PDF and the 80 mm roll print the guest's discount apart from the order's
    (phase 83 § 5), and a declined service charge as "Waived", never as a figure.

| | |
|---|---|
| Live read taken | **yes**, abcagro1 (12 trial days left). 7 writes, all through the vendor's own screens (listed at the end) |
| Migrations | `Phase91CreditNoteReferrerIndex`: an index only. `Phase91PartServiceChargeWaived`: `sales.InvoiceLines.PartServiceChargeWaived` decimal(18,4) NOT NULL DEFAULT 0, true of every row. Both applied to the dev DB |
| New permission keys | **none**. The serial list rides `Pos.Session.Operate` like the grid; the waiver rides `Sales.Invoice.TillRemoveServiceCharge` |
| New endpoint | `GET /pos/tills/{locationId}/products/{productId}/serials?search=`, the in-stock units at the till's warehouse, oldest first, at most 200 |
| Changed endpoints | **Till grid:** `GET /pos/tills/{id}/products?code=` rows gain `scannedSerialNo`. **Sales and refunds:** `POST /pos/sales` lines carry `serialNumbers` (accepted since phase 51; the till sends them now); `POST /pos/refunds` and `/refunds/preview` lines take `serialNumbers`; `GET .../refundable` lines gain `serialNumbers` and `returnableSerialNumbers`. **Credit notes:** `POST`/`PUT /credit-notes` lines take `serialNumbers`; `GET /credit-notes/{id}` lines and the conversion template carry them. **Restaurant bills:** `POST /pos/orders/{id}/bill/preview` and `/invoices` take `waiveServiceCharge`; the preview answers `partServiceChargeWaived` and each line's `serviceChargeWaived`; `GET /pos/orders/{id}` answers `partServiceChargeWaived`. **Receipt:** gains `partDiscountAmount` and `serviceChargeWaived` |
| Changed screens | **Retail till:** serial picker, scan by serial, the serial refusal before payment. **Refund screen:** a serial line is refunded by ticking its units. **ERP credit note:** a serial field on a converted note's serial lines. **ERP invoice, purchase bill and credit note (§ 6):** a scanner's Enter ends a serial in the box, with a count under it. **Restaurant:** the bill page's *Waive service charge on this bill*; the order page's *Service charge waived on bills* |
| Changed print output | **ERP invoice PDF / roll:** a bill carrying the order's Amount discount and a guest's prints *Discount Applied* and *Bill Discount* apart; a declined service charge prints *Service Charge: Waived*. **Till receipt:** the same two |
| Initial bundle | **653.05 kB** (unchanged) against 680 kB |

Tests: Domain **1040** (+8), Application.UnitTests **1832** (+15), Infrastructure.UnitTests 14, Api.IntegrationTests
76, Angular **900** (+19, of which 7 are § 6's follow-up). All green with Docker up.

## 0. Live read, 2026-10-13 (abcagro1, writes allowed through the vendor's own screens)

Every write was clicked through the vendor's screens. The list is at the end.

- **Serial tracking is two switches.** The tenant's comes first (Configurations > General > *Batch and Serial No
  Tracking*, `POST general-settings {type: "serial_no_tracking_enabled", value: true}`). Only then does the product
  form show a per-product *Serial Number Tracking* switch. Ours has the product switch only; the tenant switch is not
  built (§ 5).
- **Receiving.** The purchase bill's expanded line holds a *SERIAL NUMBERS* tag input ("0 / 3 serial numbers added").
  Each serial is validated as it is typed (`POST item-serial-numbers/validate {source, product_id, serial_number,
  in_movement, batch_id, warehouse_id}`), and the bill stores `serial_numbers` on the item.
- **Selling at the till.** Tapping a serial product's tile (badged *SERIAL*) opens the line editor with "Serial
  Numbers (0/1)". The box says "Scan or type, then press Enter". **There is no list of units in stock.**
  - **Validation.** Each serial is validated against the location's default warehouse (`source: "Invoice"`), and the
    box is disabled with "This location has no default warehouse set" until the location has one. POS Retail had none,
    so write 5 below gave it one.
  - **The cart.** The line shows "SN IMEI91A". The cart never merges a serial line with a second tap of the product,
    and it refuses checkout while a line's count differs from its quantity (from the client bundle).
- **The main scan box does not know serials.** IMEI91A typed there gives "Barcode not found!". Ours finding the phone
  by its serial is the user's choice (Decision B).
- **Vendor defect.** Its validator answered `is_valid: true` for **NOTREAL1**, a serial never received, on a sale.
  Ours refuses an issue of a unit not in stock (phase 51; the E2E's 409).
- **Refunding at the till.** *Initiate Refund* lists the sale line's units as checkboxes ("Serial Numbers (1/1
  selected)"). Lowering the quantity trims the ticks, and each tick is validated (`source: "Credit Note"`,
  `in_movement: true`). The credit note stores `serial_numbers: ["IMEI91A"]`, and the vendor's Batch / Serial Numbers
  list shows IMEI91A **AVAILABLE** again. That is the restock this phase gave ours.
- **Not read:** the per-part service charge waiver. The vendor has no per-part term of any kind (phase 74 read its
  split, phase 83 Decision C), so ours is a decision, not a copy.

## 1. What shipped

### Serials at the Retail till

- `PosSellableProducts.ListAsync` gains two options.
  - **`serialWarehouseId` (the Retail till).** An exact code that matches no product's Barcode/Code/SKU is looked up
    as an in-stock serial at the till's warehouse. The row then carries `ScannedSerialNo`, in the casing the unit was
    received under (the E2E's `imei-4` → `IMEI-4`). The ledger is read through a subquery, so the phase-51 filtered
    index (in-stock serial layers only) is all it reads; no index was added.
  - **`excludeSerialTracked` (the restaurant's grid).** `PosOrderPricing` refuses a serial-tracked product on any
    order (409).
- **`ListPosProductSerialsQuery`.** The units of one product in stock at the till's warehouse, oldest first, capped at
  200 and narrowed by `search`. It rides `Pos.Session.Operate`, so a cashier needs no catalogue key; empty when the
  product is untracked or the till has no warehouse.
- **The cart (`PosCart`).**
  - Lines carry `serialNumbers` and products `serialTracking`; carts stored before the phase read as untracked.
  - A serial product has one line. `addSerial` names a unit (refused if it is already in the cart, ignoring case) and
    raises the quantity when the units named outnumber it.
  - A serial line counts whole primary units and stays in the primary unit; lowering its quantity drops the last
    serials named.
  - `serialShortfall` names a line whose count is off. The till says so in the cart and before Complete, and
    `canComplete` waits for it.
- **The till page.** A tap opens the line's panel on its picker (focus in the box), and Enter in the box checks the
  serial against the stock there before naming it. A scan that matched a serial adds the unit straight in.

### Returns carry their serials

- **`CreditNoteSerials`** (Application/Sales) holds the rule. A serial is named only on a serial-tracked line, once per
  note, and only on a note converted from its invoice, the only kind that restocks. Each must be a unit the invoice
  sold and none an approved note of it already returned (`ReturnedAsync`, `Status == Approved`, so a void gives it
  up). At Approve, a serial-tracked Goods line names exactly its primary quantity.
  - **Where it is enforced.** An ERP draft's save gets a 400 naming the line, with the count deferred to Approve as the
    invoice's draft does. Approve gets a 409. The till's plan gets a 400 on `Lines`, in the preview as in the refund.
- **`CreditNoteApprovalPosting`** restocks through `LineStockAllocator.IncrementLineAsync`, one layer per serial.
  - The till refund hands its serials in, because they are written in the same request and cannot be read back until
    it saves, as the till's sale already does. An ERP Approve loads them.
- **ERP credit notes.**
  - `CreditNoteLineInput.SerialNumbers` is written through `DocumentLineAllocationWriter.ReplaceSerialsAsync`
    (`CreditNoteLine`, a parent type phase 51 declared and nothing wrote).
  - `GetCreditNote` reads the serials back, and the conversion template prefills the units each key sold and no
    approved note returned.
  - The form shows a comma-separated serial field on a converted note's serial-tracked lines (the invoice form's
    control) and "SN …" once approved.
- **The till refund.**
  - `PosRefundLineInput.SerialNumbers` comes through `PosRefundPlanner.PlanSerialsAsync`.
  - `GetPosRefundableSale` lines gain the units sold and those still returnable.
  - The refund screen replaces a serial line's quantity box with a checklist of returnable units; the quantity is
    how many are ticked, and *Refund everything left* ticks every unit still out.

### The till refund's owed read

- **The reader.** `OutstandingDocumentReader.LoadAsync`/`LoadInDocumentCurrencyAsync` take `invoiceId` (customers
  only; a supplier with one is an `ArgumentException`).
  - The candidate invoices, the line totals, the tenders and change, the allocations and the credit notes are each
    narrowed through `db.Invoices.Where(Id == invoiceId).Select(Id)`, phase 90's subquery shape.
  - Opening balances and journal vouchers are not read, since they are no invoice.
  - The answer is the row the contact-wide read gives that invoice, by construction: `PosRefundOwedTests` holds the
    refund screen's `Owed` to Invoice Age's balance on a customer with tenders, credit, a refund and a paid sale.
- **The caller.** `PosRefundPlanner.OwedOnSaleAsync` passes the sale.
- **The index.** `CreditNotes (OrganizationId, ReferrerId)`, configured in `CreditNoteConfiguration` (`TenantIndexConvention` already
  covered the table by its date; this is a second, measured index, § 4).

### One guest declining the service charge

- **Domain.**
  - `PosLineArithmetic.Figures.LessServiceCharge(off, vatPercent)` takes declined service charge off a figure and
    takes VAT again.
  - `PosOrder.FiguresOf(line, qty, partDiscount, serviceChargeWaived)` and `Estimate(billed)` price the whole less
    every waiver.
  - `PosOrderBill.Plan(…, waiveServiceCharge)`: a waived part carries no service charge and records it per line
    (`PosOrderBillLine.ServiceChargeWaived`). The last of a line declines what is left of the line's, priced from the
    whole (`WhatIsLeft` with the waived total).
  - `PosOrderLineBilled.PartServiceChargeWaived` is the sum over bills not voided (`PosOrderBilling`).
  - `InvoiceLine.PartServiceChargeWaived` is stored by `CreatePosOrderPart`, which refuses a waived line still
    carrying a service charge or a rate.
- **Application.**
  - `CreatePosOrderInvoiceCommand.WaiveServiceCharge` checks the key at the order's location in the handler (the loaded
    order decides the location). The line is billed at service charge rate 0, which is what refunds and the sales
    readers rebuild from.
  - `PreviewPosOrderBillQuery.WaiveServiceCharge` needs no key, like phase 83's discount preview.
  - The Api's two request records carry `waiveServiceCharge`.
- **Screens.**
  - The bill page has *Waive service charge on this bill*: offered on a dine-in order that has not waived it as a
    whole, disabled with the key named for a cashier without it, and cleared after each payment.
  - The order page shows *Service charge waived on bills*.
- **Export.** `TillExportTests` names `PartServiceChargeWaived` as never charged (no figure of the bill holds it).

### Printing (phase 83 § 5)

- **The PDF summary** (A4 and the roll share it). When a bill carries both the order's Amount discount and a guest's,
  with no percentage, it prints *Before Discount*, *Discount Applied* (the order's), *Bill Discount* (the guest's), then
  *Sub Total*. A bill with a declined service charge prints *Service Charge: Waived* where the figure would be.
- **`PosReceiptDto`** gains `PartDiscountAmount` and `ServiceChargeWaived`. The till receipt prints *Discount* and
  *Bill Discount* apart above Sub Total, and *Service Charge: Waived*.

## 2. Scope decisions

- **A — All three items in one phase** (the user).
- **B — Scan or pick at the till** (the user, the recommended option). The vendor offers scan-or-type only.
  - **The picker** spares a cashier with no scanner reading an IMEI off a sticker.
  - **The scan by serial** saves a second step: the vendor's main box says "Barcode not found".
  - Both are additions to the vendor's behaviour, never a different rule: the sale is checked by the server as before.
- **C — Serial products are kept off restaurant orders** (the user). A kitchen ticket, a split, a transfer and a parcel
  have nowhere to name a unit. They sell at a Retail till.
- **D — Serials on till refunds and ERP credit notes** (the user). The restock bug was phase 51's and hit ERP notes
  first. A standalone note (no invoice) restocks nothing, so it names no serials, and naming them is a 400 rather than
  present-and-ignored.
- **E — The waiver's key is the order waiver's** (`Sales.Invoice.TillRemoveServiceCharge`, not asked: it is the same
  power on one bill, as phase 83 reused `TillDiscount`). A waiver where there is nothing to decline (a take-away, an
  order already waived) records nothing and is not offered on screen.
- **F — No serial on the printed receipt or the PDF.** The vendor's invoice view shows "SN …" under the line; its
  printed receipt was not read. Left for a phase that reads it (§ 5).
- **G — The owed read is narrowed by the sale, not cached.** The reader stays the one answer for every caller, and the
  equality test holds the narrowed read to the organization-wide one.
- **H — The index, measured both ways.** `CreditNotes (OrganizationId, ReferrerId)` was created by hand on the scale
  tenant and every phase-90 path probed with and without it (§ 4). It lowered two paths and raised none, so it shipped.

## 3. Bugs found and fixed

1. **A returned serial-tracked unit came back with no serial (phase 51).** `CreditNoteApprovalPosting` passed only
   `BatchId` to `IncrementAsync`.
   - **The damage.** Every return of a serialised product made one layer of the whole quantity with `SerialNo` null.
     That broke phase 51's "a serialised layer is of size one" invariant, and the unit could not be sold by its serial
     again; an unnamed FIFO issue could take it.
   - **Why nothing caught it.** Phase 51's own status doc said a return "re-enters stock as a new layer carrying the
     same SerialNo", and nothing wrote one; `DocumentLineParentType.CreditNoteLine` existed and nothing wrote it.
   - **Fixed** (§ 1). Existing rows are not repaired: no approved note on the dev tenants names a serial, and a note's
     units cannot be recovered from a layer that never recorded them.
2. **A scanned serial kept the cashier's casing.** The first E2E pass returned `scannedSerialNo: "imei-2"` for a
   unit received as `IMEI-2`, because the lookup matches by the collation (case-insensitive) and handed back the
   typed string. The sale would have named the unit in a casing its layer does not carry. It now returns the layer's
   own `SerialNo`.
   - **Why no test saw it.** InMemory is case-sensitive, so a handler test that searches with the stored casing passes
     either way (phase 34b's gotcha); the E2E is its proof.

## 4. Evidence

- **`tools/scale`, the till refund's read** (`seed-phase91.sql` on the phase 42/50/90 tenant: the 20,000 tendered
  invoices made walk-in till sales of a Retail head office, 2,000 of them refunded with a cash payout;
  `probe-phase91-io.sh`):

  | | refundable (fresh sale) | refundable (refunded sale) |
  |---|---|---|
  | before (`probe-phase91-before.csv`) | 56,566 reads, 1,279 ms CPU | 56,574, 1,351 ms |
  | narrowed by the invoice (`-after.csv`) | 819, 7.7 ms | 844, 12.0 ms |
  | + `IX_CreditNotes_OrganizationId_ReferrerId` (`-shipped.csv`) | **59**, 5.6 ms | **94**, 16.9 ms |

  - **Where the reads went.** Before: the walk-in's 20,000 sales, four times (candidates 11,086, line totals 18,927,
    tenders 11,481, change 11,086). After narrowing: five scans of the 2,000 credit notes by `ReferrerId`, about 160
    each.
- **Phase 90's paths with and without the index, on today's data** (`probe-phase90-p91-with-referrer-index.csv`,
  `-without-…`):
  - the customer picker and Suggest: 588 → 138;
  - the supplier picker 286 and the VAT return 40,844, either way;
  - the ageing summaries: 90,561 / 42,985 either way.
- **Fresh-org E2E** (organization *P91 E2E 135000*, seeded from nothing through the API, checked in SQL):
  - **Sale refusals.** A phone with no serial is 400 naming `Lines[0]`. NOTREAL1 is 409 "not in stock in this
    warehouse".
  - **The sale.** IMEI-1 and IMEI-2 for 22,600.
  - **The refund.** The refundable read lists both. The preview of IMEI-2 is 11,300, and the refund pays out 11,300.
    The same unit again is 400 "already been returned by another credit note".
  - **Stock after.** In stock afterwards: IMEI-3, IMEI-4, IMEI-2.
  - **The ERP note.** An ERP invoice of IMEI-3 converts to a note prefilled `["IMEI-3"]`, which approves and reads
    back with it.
  - **In SQL, the layers.** IMEI-2 and IMEI-3 each have a `CreditNote` layer of 1 at 8,000. The serial rows sit on
    `InvoiceLine`, `CreditNoteLine` and `PurchaseBillLine` as expected. Layers equal the Inventory account, 24,720.
  - **The restaurant.** A phone on a take-away tab is 409 naming serial tracking. A guest's momo and Coke with the
    service charge declined preview at 294 (waived 20, service charge 0) and bill at 294. The order reads total
    610.20, waived on bills 20. The rest previews at 316.40 → 316 with 20 of service charge, and settles.
  - **In SQL, the waived bill.** The momo line has `ServiceChargeRate .00`, `ServiceChargeAmount .0000`,
    `PartServiceChargeWaived 20.0000`.
- **403-not-404 pairs.**
  - **The serial list.** A nonexistent product is 404 "Product not found." as Admin. It is 403 naming
    `Pos.Session.Operate` from a role holding every Admin key but that one, beside its 200 on `GET
    /billing-locations`.
  - **The waiver.** A nonexistent order is 404 as Admin. A real order's waived bill is 403 naming
    `RS.Sales.Invoice.TillRemoveServiceCharge` from a role without that key, beside the same bill charged, which is 200.
  - The membership was moved back to Admin afterwards (checked).
- **Keyboard check** (the browser pane, `erp-web-ssl`, the cookie transplanted through `web/public` and deleted at
  once). On the till, typing `imei-4` in the scan box and pressing Enter added *P91 Phone, SN IMEI-4 (1/1)*. F9, Enter
  and Enter completed sale 0008. The request carried `serialNumbers: ["IMEI-4"]`, and SQL holds the row with IMEI-4's
  layer issued.
- **PDFs looked at** (`tools/pdf/pdf2png.ps1`).
  - **The waived bill:** *Sub Total 260.00, Service Charge Waived, VAT 33.80, Round Off 0.20, Grand Total 294.00*.
  - **A bill with the order's Rs 52 and a guest's Rs 26:** *Before Discount 260.00, Discount Applied (26.00), Bill
    Discount (26.00), Sub Total 208.00, Service Charge Waived, VAT 27.04, Round Off −0.04, Grand Total 235.00*.
  - Both read straight down.
- **Tests added.**
  - `PosSerialTests` (9): the picker, scan, refund layer, refusals, void, restaurant and ERP note.
  - `PosRefundOwedTests` (1).
  - `PosPartServiceChargeWaiverTests` (Domain, 8): the twin tab, last part, void, total, discount first, no-op and
    `LessServiceCharge`.
  - `PosPartServiceChargeWaiverHandlerTests` (4): preview/bill/line/order, refund, key and print.
  - Angular: pos-cart (5), pos-till-page (3), pos-refund-page (1), pos-bill-page (3).

## 5. Left open, and for later phases

- **The tenant switch for batch and serial tracking.** The vendor gates the product switches behind Configurations >
  General; ours has the product switches only. Not asked.
- **A serial on the printed receipt and the PDF.** The vendor's invoice view prints "SN …" under the line; its printed
  receipt was not read. A tax invoice naming the IMEI is common in phone retail; read the vendor's print first.
- **Serials on a debit note.** `ApproveDebitNoteCommandHandler` reads `DebitNoteLine` serials nothing writes, so a
  return to the supplier relieves FIFO units, not the ones named. Same shape as this phase's credit-note fix.
- **Batch-tracked products at the till** still sell un-narrowed (phase 62: the batch is optional on an issue).
- **Existing returns of serialised products** (if any on a real tenant) keep their un-serialled layer; no backfill is
  possible.
- **The vendor's validator accepting a never-received serial on a sale** (NOTREAL1) is a vendor defect. Ours refuses.
- **Receiving a serial the product already holds in stock** is refused only by phase 51's filtered unique index
  (`OrganizationId, ProductId, SerialNo` where in stock). Nothing checks it first or catches the `DbUpdateException` on
  the purchase bill's approval, so it is most likely a 500, not a 409 naming the serial. Read from the code, not run.
- **One serial on two products is allowed** (the index is per product), so the till's scan of a serial both hold at
  the warehouse offers both products. Arguably right, since the cashier must choose, but untested.
- **Carried unchanged:**
  - phase 90 § 5's other items (Annex 13's balances, the tax advisor's word);
  - phase 89 § 5; phase 88 § 5; phase 87 § 5 / Decision G; phase 84 § 5's money receipt on the roll;
  - phase 81 § 5; phase 80 § 5; phase 79 § 5; phase 77 § 5; phase 76 § 5; phase 75 § 5;
  - phase 72 § 5; phase 71 § 5; phase 70 § 5; phase 69 § 5; phase 67 § 5; phase 66 § 5;
  - phase 62 § 5's barcode index.

## 6. Follow-up: a barcode scanner in the ERP serial boxes

Asked after the close: does a scanner fill the purchase bill's serial box? Only once.
- **What was wrong.** A scanner types the code and presses Enter. The serial box on the invoice, purchase bill and
  credit note forms is a one-line input that ignored Enter, so a second scan ran on into the first (`IMEI91AIMEI91B`).
  The server then refused the line (two units, one serial). Its comment said "comma- or newline-separated", but a
  one-line input cannot hold a newline.
- **The fix.** `web/src/app/shared/catalog/serial-entry.ts` is now the forms' one parser.
  - Enter ends a serial: repeats are taken out (trimmed, case-insensitive, as `StockTrackingRules` compares them) and
    `, ` is written, so scan, scan, scan reads `A, B, C, `.
  - An always-present `aria-live` hint under the box says "2 of 3 serial numbers", and names a repeat Enter took out
    until the next keystroke.
  - Frontend only; the server's rules are unchanged.
- **The bug its test found.** After a repeat, the tidied text can equal what the `[value]` binding last wrote. Angular
  then writes nothing and the repeat stays on screen. Phase 64's radio, in a text box. The handler writes the box by
  hand as well.
- **Evidence.**
  - `serial-entry.spec.ts` (5 tests). Two page tests in `purchase-bill-additional-cost.spec.ts` fire input then Enter
    events on the focused box; the repeat test failed before the hand write.
  - A browser pass on the P91 E2E org: a draft bill's box was cleared and SCAN-A ⏎ SCAN-B ⏎ scan-a ⏎ SCAN-C ⏎ was typed.
    It read `SCAN-A, SCAN-B, SCAN-C, ` with "3 of 3 serial numbers" and kept focus. *Save Draft* stored exactly those
    three in `inventory.DocumentLineSerials`, read in SQL.
  - Angular 900 passed; ng build unchanged at 653.05 kB.

## Writes made to the vendor tenant

All seven through the vendor's own screens, with the user's permission given for this phase; none by replaying its
headers.

1. `general-settings serial_no_tracking_enabled = true` (Configurations > General).
2. Product P0004 *P91 Phone Serial* (serial tracking on, 10,000, 13% VAT, Box).
3. A purchase bill draft, then 4. its approval as **PB0002/HO/83-84**: 3 × 8,000 from Ram Hari Sharma, serials IMEI91A,
   IMEI91B, IMEI91C, into Main Warehouse.
5. Location POS Retail (1003): address Biratnagar, warehouse Main Warehouse (Configurations > Organization > Features).
6. Till sale **INV0004/1003/83-84** at POS Retail: IMEI91A, 11,300 cash.
7. Till refund **CN0003/1003/83-84** of it: IMEI91A, 11,300 cash.
