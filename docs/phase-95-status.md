# Phase 95 — clearing the backlog: seven leftovers fixed in one session

## TL;DR

**In plain words, what changed for someone using the app.** On 2026-10-20 the user asked why every phase left work
behind, and chose to clear the list in one session. All seven items were done; nothing from them is carried.

1. **Keyboard focus no longer disappears.** After a failed Save, focus returns to the Save button. After *Remove* on a
   line, it moves to the next line's Remove (or the previous one's, or the list's *Add Line* when the list is empty).
   One fix covers every screen, about 70 of them.
2. **Restaurant orders name their batch**, as Tigg's order screen does. A take-away split or a table transfer keeps
   the batch, and the bill issues stock from it.
3. **Scanning a barcode at the counter is fast in a big shop**: about 115 ms of database work became about 2 ms on
   20,000 products. The measurement also found the counter's product grid itself slow (about 210 ms), now about 40 ms.
4. **A batch has its own selling price and a required expiry**, entered when a purchase bill first receives it. Picking
   the batch at the counter, on a restaurant order or on an ERP invoice prices the line at it, and that price needs no
   "change price" permission.
5. **Serial numbers can print on the bill and the counter receipt.** It's a switch on the bill's printing template, off
   until a company turns it on. No statute requires them; a warranty uses them.
6. **Two company-wide switches, Batch Tracking and Serial No Tracking** (Configurations > General). Off overrides every
   product's own setting; each product keeps its own, so switching back on restores it. A new company starts with both
   off, like Tigg; every existing company that already tracked products got its switch on.
7. **A purchase return's price-to-cost difference posts to the account the bill used**, not to whatever the default
   is by the time of the return.

**Found on the way and fixed:** on a 1,024-pixel screen the purchase bill's Rate box was 30 pixels wide and hid its
number. Every typed box in a line table now keeps 5 rem and the table scrolls sideways.

| | |
|---|---|
| Live read taken | **no** (Tigg was read in phase 94 for the batch picker, its price, its expiry and its restaurant order) |
| Migrations | `Phase95PosOrderLineBatch` (`pos.PosOrderLines.BatchId`, FK), `Phase95ProductScanIndexes` (`catalog.Products (OrganizationId, Barcode)` and `(OrganizationId, Sku)`), `Phase95StockTrackingSwitches` (two bits on `tenancy.TenantSettings`, **hand-written backfill**: on for a company with a tracked product), `Phase95BatchSellingPrice` (`catalog.ProductBatches.SellingPrice` decimal(18,4) null), `Phase95PrintSerialNumbers` (bit on `configuration.PrintingTemplates`, default 0), `Phase95BillInventoryAdjustmentAccount` (`purchasing.PurchaseBills.InventoryAdjustmentAccountId` null, no backfill). All applied to the dev DB |
| New permission keys | **none**. The switches read rides `Catalog.Product.View`; the switches' write is the General settings' `Configurations.GeneralSettings.Manage` |
| New endpoint | `GET /stock-tracking`: `{batchTrackingEnabled, serialTrackingEnabled}` |
| Changed endpoints | `PUT /general-settings` takes both switches, **required** (a body without one is a 400 naming it). `POST /products`, `PUT /products/{id}`: switching tracking on while the company's switch is off is a 400 naming the field. `POST`/`PUT /purchase-bills` lines take `batchSellingPrice`; a line naming a **new** batch without `expiryDate` and `batchSellingPrice` is a 400. `POST /pos/orders` and `/items`: items take `batchNo`, required for a batch-tracked product (400 `Items[i]`). `GET .../products/{id}/suggested-rate` takes `batchNo`. Batch lists carry `rate` (and `sellingPrice` on the product's Batch tab). `PUT .../printing-templates/{id}/layout` and the preview take `printSerialNumbers` (left out = unchanged). The till receipt's lines carry `serialNumbers` when printed |
| Changed screens | Every screen (focus); restaurant order (batch picker); General settings (two switches, a warning before switching one off); product form (its switches hidden while the company's is off); credit/debit notes (boxes follow the switches); purchase bill (*New batch* expiry and price); invoice (a batch's price on naming it); till (picking a priced batch prices the line); layout editor (*Print serial numbers*); product Batch tab (Selling Price) |
| Changed print output | An Invoice's or Credit Note's PDF (every layout) and the till receipt name each line's serials as "SN: ..." under the product, when the template says so. Looked at: `tools/pdf` render of the E2E invoice |
| Initial bundle | **656.01 kB** (+2.96 kB: the app-wide focus fix) against 680 kB |

Tests: Domain 1040, Application.UnitTests **1894** (+10), Infrastructure.UnitTests 14, Api.IntegrationTests 79,
Angular **928** (+16). All green with Docker up.

## 1. What shipped, item by item

### Item 1 — focus (`shared/a11y/focus-keeper.ts`)

One `MutationObserver` started by the app root. It remembers the focused control. When that control is disabled
(Save while saving) and later enabled with focus still on `<body>`, it focuses it again. When it is removed with its
row, it focuses the same-labelled control on the next row, else the previous row, else the nearest *Add ...* button
around the list, else the next control, else the previous one. It acts **only** when focus is on `<body>`: a screen
that moves focus itself (an error, a dialog, the till's own rules since phase 85) does so in Angular's render, before
the observer runs, and is left alone.

- **Checked in a real browser** (invoice form, keyboard only). Enter on the middle line's Remove moved focus to the
  next line's Remove, then to the previous line's, and then, with no line left, to *Add Line*. A server-refused Save
  (a batch product with no batch) left focus on *Save Draft* with the error shown.

### Item 2 — a batch on a restaurant order line

`PosOrderLine.BatchId`, set from the item's `batchNo` (resolved as an issue's: required, must exist), copied by a
take-away split and a transfer (`CopyOf`), part of `HasSameTermsAs` (two batches never merge), and passed to the
bill's line, so the ledger issues from it. A line ordered before this phase has none and walks, as before; its void
and refund still restock per batch (phase 93). The order screen shows a radio picker of the batches in stock, refuses
*Send to Kitchen* until each batch item names one, and shows *Batch B2* on sent lines.

### Item 3 — the scan and the grid (`tools/scale/probe-phase95-*.csv`, `seed-phase95.sql`)

Measured on the phase 34c tenant (20,001 products), each product given a barcode:

| path | before | after |
|---|---|---|
| scan by barcode / code | 11,189 reads, ~115 ms CPU | 50 reads, ~2 ms |
| scan that matches nothing | 8,922 | 18 |
| till grid, first page | 85,606 reads, ~210 ms | 5,413 reads, ~40 ms |
| ERP product picker filtered by location | 80,995 | 794 |
| product list / searches / till open | unchanged or better | |

- The scan's `Barcode == x OR Code == x OR Sku == x` could seek only `Code`. Two indexes fix it.
- The grid's page count ran the "available at this location" rule as two subqueries per product.
  `ProductLocationAvailability` states it as a set computed once.
- **The two category-tab queries keep the per-product form**: their `EXISTS` stops at the first product (10 reads).
  The set form read every product (4,465), so they were restored after measuring.
- Text "contains" search (about 250 ms on 20,000 products) is unchanged: a contains search reads every name, and no
  index helps it.

### Item 4 — a batch's price and expiry

`ProductBatch.SellingPrice` (per primary unit, in the tenant's price basis like a product's own price). A purchase-bill
line naming a **new** batch must carry `expiryDate` and `batchSellingPrice` (Tigg's New Batch form requires both). A
later bill of the same batch fills a missing price but never changes one: a new price is a new batch.
`ProductPrices.BatchRate` turns the price into a line's rate (a secondary unit multiplies by its conversion), and it is
read by:

- the till's batch list and picker (the line takes it);
- the restaurant's order pricing (the order line is frozen at it);
- the ERP invoice's suggested rate (`suggested-rate?batchNo=`), set when the batch box is committed;
- **all three price gates**: the till's `TillChangeRate`, the ERP's `Sales.Invoice.ChangeRate`, and the till page's own
  warning. A batch's own price is never a changed price.

### Item 5 — serials on the printed bill

`PrintingTemplate.PrintSerialNumbers` (off; its own mutator). An Invoice's and a Credit Note's printed rows carry their
serials, and `PrintTemplateApplier` writes them under the product ("SN: a, b") when the template prints them, and drops
them otherwise. No layout's drawing code changed. The till's sale and refund receipts read the location's Invoice or
Credit Note template, the same switch. The layout editor offers the box on those two types only.

### Item 6 — the company switches

`TenantSettings.BatchTrackingEnabled` / `SerialTrackingEnabled`. Every rule that asked `Product.BatchTracking` now asks
`StockTrackingSwitches` (company switch **and** product flag). That covers:

- the line allocation rules and their two methods (now take the switches, so the compiler listed every caller);
- the refused paths, both notes' batch and serial rules, and both conversion templates (they prefill nothing a switch
  turns off);
- the till grid and scan, the restaurant's serial refusal, and both pickers.

A product cannot be switched on while the company's switch is off (400). One that already tracks keeps its flag through
an edit. A company with no settings row (only test fixtures) reads as "each product decides".

### Item 7 — a return's difference

`PurchaseBill.InventoryAdjustmentAccountId`, recorded at Approve for a bill that stocked goods (phase 87's
`RecordPostingAccounts`). A debit note returning it books its price-to-cost difference there. A standalone note, or a
bill approved before this phase, uses today's default, as phase 87 left every un-recorded account. Debit notes post no
rounding, so that half of Decision G had nothing to move.

## 2. Scope decisions (the user's answers, 2026-10-20)

- **A — fix items 1–3 now, and build 4–7 on the answers:**
  - 4: yes;
  - 5: a setting, since no statute asks for it;
  - 6: yes, and the company switch overrides the product's;
  - 7: yes, the bill's account.
- **B — the switches start off for a new company and on for one already tracking** (the backfill). Nothing an existing
  company does changes.
- **C — serials print by template, off by default**, and the till receipt follows its location's bill template rather
  than a second switch.
- **D — a batch's price is set when it is first received**, never changed by a later bill (a new price is a new batch),
  matching phase 51's rule for its dates.
- **E — the category tabs keep their per-product query**, measured.

## 3. Bugs found and fixed

- **A layout flaw:** the purchase bill's Rate box was 30 px at 1,024 px wide. A floor on number boxes alone moved the
  squeeze to the Item Batch box, so the floor is on every typed box in a `.table-responsive` line table.
- **Item 1's first version** sent an emptied list's focus to the footer's Discount box: *Add Line* sits above the
  table. It now looks for the list's *Add* button first.
- **The till grid's page count** (item 3) was 98% of the grid's reads, found only because the scan was measured beside
  it.

## 4. Evidence

- **Unit and component tests.**
  - `FocusKeeper` (7 tests).
  - `BatchIntegrityTests`:
    - restaurant batch rules, a parcel and a transfer keeping the batch;
    - a line from before the phase walking;
    - the switches overriding and restoring;
    - the product guard;
    - a new batch's requirements;
    - batch pricing through the till, the ERP gate and the suggestion;
    - the restaurant's batch price.
  - `RecordedPostingAccountsTests`: the return's difference after the default moved. **Proven to bite**: with the fix
    disabled it fails, and the file was restored by hash.
  - `PrintingTemplateLayoutTests`: the applier with the switch on and off.
  - `PosSerialTests`: the receipt follows the template.
  - Specs: the order picker, the till's batch price, the cart's unit scaling, the bill form's new-batch fields, the
    switches on General settings, the debit note's hidden batch box, the layout editor's box.
- **Fresh-org E2E on SQL Server** (`P95 Leftovers ...`, `ba40e8a8-...`):
  - **The switches.** A new company had both switches off, and a batch product was a 400 naming `BatchTracking`. A
    settings body without the switch was a 400 naming it. With the switches on:
    - a new batch with no expiry or price: 400 naming `Lines[0]`;
    - B1 (150) and B2 (170): the till list showed both with their rates and expiries;
    - `suggested-rate?batchNo=B1` returned 150, `BatchSellingPrice`.
  - **The sales.**
    - Till sale of B1 at 150 → line 150, B1.
    - Restaurant order with no batch: 400 `Items[0]`; with B2 → order line 170, B2, and its bill's line 170, B2,
      stock out of B2.
    - Batch switch off: a sale without a batch was 200 and walked to B1; naming one was a 400 (*not batch-tracked*).
  - **The print.** Template *Print serial numbers* on → the phone invoice's PDF prints `SN: IMEI-95A` under the phone
    (looked at). In SQL the bill recorded the tenant's Inventory Adjustment account.
- **403-not-404.** A role copied from Admin without `Catalog.Product.View` (membership switched in SQL and restored)
  is 403 naming it on `GET /stock-tracking`, beside a 200 on `GET /billing-locations`. Admin gets 200 again.
- **In the browser:** the focus checks in § 1; the bill's *New batch B9* with Expiry date and Selling price; every box
  in the bill's line 80 px at 1,024 px.

## 5. Left open, and why

The user's rule (2026-10-20) is that nothing found here is carried without a reason that is not ours to settle. What
remains is only that:

- **Needs a tax advisor** (phases 89 and 90 § 5): carrying VAT credit across the fiscal year's end, a domestic zero-rated
  supply, and Annex 13's before-VAT basis.
- **Differences from Tigg that nobody asked for**, offered to the user as droppable (2026-10-20; not yet answered):
  - phase 79 § 5: moving print blocks, renaming non-statutory labels, the delivery-note/GRN/transfer HTML templates,
    lakh grouping for other currencies;
  - phase 88 § 5: Tigg's per-row allocation table.
- **Not fixable without new information:**
  - a partial refund of a sale from before phase 94/95 that walked its batches returns into the oldest-taken batch:
    no record says which units came back;
- **Not asked:** a sale return's (credit note's) rounding still posts to today's Rounding account. Item 7 was about
  purchase returns; whether sale returns should follow their invoice's rounding account is a question for the user,
  asked in the session's closing message.
