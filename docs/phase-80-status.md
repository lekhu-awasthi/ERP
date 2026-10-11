# Phase 80 — The export's remaining documents: four new sheets, and a document number on both ledger sheets

## TL;DR

**Phase 78 § 5's export items, finished by a census rather than by the list.**

1. **A census of every document type** found eight on no sheet: Quotation, Sales Order, Purchase Order, Delivery Note,
   GRN, Bill of Materials, Production Order and Production Journal. Phase 78 § 5 named Purchase Orders. Quotations and
   Sales Orders are uncovered for the same reason (a commitment posts nothing and moves no stock), and so were
   missing from the list, not from the problem.
   - Four more types (Journal Voucher, Cash Transfer, Warehouse Transfer, Inventory Adjustment) reached the export only
     as a GUID. Ledger Transactions and Stock Movements printed `Source Document Id` and no number, since phase 21b.
2. **Four new sheets** (twelve categories, from eight):
   - ***Bills of Materials*** (master data, not date-filtered, after the Chart of Accounts). Each BOM has an *Output* row,
     then its *Raw Material*, *By-Product* and *Expense* rows.
   - ***Orders***: Quotation, Sales Order and Purchase Order lines. Figures are as written and positive, with *Valid
     Until*, *Delivery Date* and *Delivered / Received At*.
   - ***Goods Notes***: Delivery Note and GRN lines. A *Direction* column (Out / In) works as on Stock Movements. Each
     row names its *Warehouse* and the order it was raised *Against*, plus its tracking number, expected date and
     shipping address.
   - ***Production***: Production Order and Production Journal, using the same row kinds as a BOM. A journal's row
     carries its unit cost and amount. A plan's costs are empty cells, never zeros.
3. **Document No and Reference on both ledger sheets**, appended after their existing columns. They come from phase 26a's
   `GlSourceDocumentResolver`, which gained a whole-tenant mode (`LoadEveryAsync`) because a 50,000-row page is too long
   to send back to SQL as ids. Cash Transfers stay off Payments (phase 78 Decision C stands). They are now readable
   where they already were.
4. **Allocation Method** (Value / Quantity) closes the Purchase Documents sheet, filled on an additional cost's row.

The user's decisions (2026-10-11), each the recommended option:

- **A:** The export's remaining documents are the phase (over discount gating, discard-with-a-share and print positioning).
- **B:** Four new sheets (Orders, Goods Notes, Production, Bills of Materials), not § 5's list literally and not folded
  into the existing document sheets.
- **C:** Document No and Reference on both ledger sheets; Cash Transfers stay off Payments.

| | |
|---|---|
| Live read taken | **none needed.** The export is our own feature (FR-2.8); the reference product's Backup covers five categories and none of these documents (phase 78's precedent). The vendor tenant was not opened |
| Migration | **none**. `ExportCategory` is stored by name; the four members are appended. The full selection is 155 characters of the column's 200, now held by a test |
| New permission keys | **none**. The export keeps `Configuration.ExportJob.Manage` / `.View` (Admin-only by phase 21b's derivation; four more sheets of the same tenant's data change nothing about who may take it out) |
| Changed endpoints | none in shape. `POST /export-jobs` accepts four more `categories` values and produces four more sheets; *Ledger Transactions*, *Stock Movements* and *Purchase Documents* are wider |
| Changed screens | Configurations > Import / Export: twelve category checkboxes in the workbook's order (*Bills of Materials (all dates)*, *Orders*, *Goods Notes*, *Production* added) |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged |

**Three things worth carrying forward.**

1. **Census where each type lands, not which list names it.** § 5 said "Purchase Orders"; the mirror question (phase 30's
   *find the rule*) added Quotations and Sales Orders. The census of every `DocumentType` against every sheet had
   three answers, not two: on a sheet, **on a sheet as a GUID**, and nowhere. Four types had been in the middle bucket
   since phase 21b, and nothing reported it because a GUID is a non-empty cell.
2. **A sheet's columns must mean one thing when summed.** That rule decided the grouping. An order on *Sales Documents*
   would be counted again by its invoice, so commitments get their own sheet. A delivery is *Out* with positive figures,
   not a negated row that reads as a refund. A plan's empty cost stays empty, so it never reads as free.
3. **A recorded capacity is a claim until a test reads it.** `ExportJobConfiguration` said 200 characters was "roughly six
   times the longest possible full selection". It was 110 then (under twice), and it is 155 now. The width is
   Infrastructure's and the names are the Domain's, so the check is a cross-assembly test in
   `Infrastructure.UnitTests` (phase 50's rule). An overflow would fail only on SQL Server, as a truncation error on the
   job insert.

Tests: Domain 1014, Application.UnitTests **1739** (+10), Infrastructure.UnitTests **14** (+1), Api.IntegrationTests 71,
Angular **839** (+1, 101 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and `ng test`
are green.

---

## 1. What shipped

- **`ExportCategory`** gains `BillsOfMaterials`, `Orders`, `GoodsNotes` and `Production`, appended (stored by name).
  `ExportJobProcessor.CategoryOrder` puts Bills of Materials after the Chart of Accounts, with the bounded master data.
  The other three go last: they are what a workbook over its 150,000-row budget truncates first.
- **`OrderExportReader`** (*Orders*): three queries (quotation, sales-order and purchase-order lines) merged in memory,
  phase 77's shape.
  - Columns: phase 38's fourteen through `DocumentLineRow` with *Party*, then *Document Discount %*, *Valid Until*,
    *Delivery Date* and *Delivered / Received At* (the order's `DeliveredAt` / `ReceivedAt`, on the Nepal clock).
    Then *Unit*, *Exchange Rate* and *Location*.
  - A line's Amount is already net of the document's discount, as on every document sheet.
- **`GoodsNoteExportReader`** (*Goods Notes*): two queries.
  - Columns: the fourteen, then *Direction* (`StockMovementDirection`, Out / In), *Warehouse* and *Against Order* (the
    referrer: Sales Order for a delivery, Purchase Order for a receipt). Then *Tracking No*, *Expected Delivery Date* and
    *Shipping Address* (a delivery's own fields), *Document Discount %*, *Unit*, *Exchange Rate* and *Location*.
- **`ProductionExportReader`** (*Production*): eight queries, four row kinds per document type.
  - Columns: Document Type / No, Date, Status, Reference, *Against Order* (the journal's production order), *Row*,
    Product Code / Name, *Cost Term*, Quantity, *Unit* (the product's primary unit, the only unit a manufacturing line
    has), *Cost Allocation %*, *Unit Cost*, *Amount*, *Warehouse* and *Location*.
  - The journal's referrer join is composed once (`journalHeaders`) and joined by its four queries. It translated on
    SQL Server (§ 4).
- **`BillOfMaterialsExportReader`** (*Bills of Materials*): four queries, ordered by the finished product's code in SQL
  as well as in memory, so a capped sheet keeps the rows the merged order prints first.
  - Columns: Finished Product Code / Name, *Active*, *Manufacture On Every Sale*, *Row*, Product Code / Name, Cost Term,
    Quantity, Unit, Cost Allocation %, Amount, Notes.
- **`PartKind`** (Output, Raw Material, By-Product, Expense) and **`ManufacturingNames`** are shared by the two
  manufacturing readers. `ManufacturingNames` holds the tenant's products (code, name, primary unit) projected before
  `ToDictionaryAsync`, with units, cost terms, warehouses and locations. `ExportLookups` gains `WarehousesAsync`.
- **`GlSourceDocumentResolver`**: one body (`LoadCoreAsync`) behind two entry points.
  - `LoadAsync(keys)` is unchanged for the three GL reports.
  - `LoadEveryAsync(types)` loads every document of the named types for the tenant.
  - Each type composes its id filter as a second `Where` only when it has one (phase 33's rule).
  - Opening Balance and Opening Stock now answer their labels in `For()` itself, whichever way the resolver was loaded.
- **`LedgerTransactionExportReader`** and **`StockMovementExportReader`**: *Document No* and *Reference* appended,
  resolved for the types the page contains.
- **`PurchaseDocumentExportReader`**: *Allocation Method* appended, filled on an additional cost's row and empty on
  every other row.
- **Guards**: `OrderExportReader.ExportedMoney`, `GoodsNoteExportReader.ExportedMoney`,
  `ProductionExportReader.ExportedFigures` and `BillOfMaterialsExportReader.ExportedFigures`. Each new type's every
  `decimal` is on its sheet or named with a reason, as phase 78 holds the purchase types.
- **Angular**: `ExportCategory` and `EXPORT_CATEGORIES` gain the four, in sheet order; the page needed no change.

## 2. Scope decisions

- **Decision A: the export's remaining documents.** Chosen by the user over phase 76 § 5 (ERP discount gating; its
  maximum-per-role half has no live evidence), phase 77 § 5 (discard with a share) and phase 79 § 5 (positioning and
  renames).
- **Decision B: four sheets, grouped by what their columns mean when summed.**
  - *Rejected: § 5's list literally.* Quotations and Sales Orders are uncovered for exactly the reason Purchase Orders
    are.
  - *Rejected: fold into Sales / Purchase Documents.* Those sheets sum to net sales and net purchases. An order there is
    counted again by its invoice, and a delivery note again by the invoice that bills it.
- **Decision C: Document No on the ledger sheets; Cash Transfers stay off Payments.** Phase 78's reason stands: Payments
  is money that moved against a contact or through a till. The real gap was that the ledger named a cash transfer only
  by GUID.
  - *Rejected: a sheet for Journal Vouchers, Cash Transfers, Warehouse Transfers and Inventory Adjustments.* Their rows
    are reconstructable from the ledger sheets once a number is printed (phase 38's rule).

Decided in the session, not asked:

- **Decision D: positive figures and a Direction column on Goods Notes**, as Stock Movements prints them. A negated
  delivery would read as a refund. The notes' rates and VAT are the form's own; nobody is owed them, because the Invoice
  or Purchase Bill does that.
- **Decision E: the output is a row**, on both manufacturing sheets. On a journal it carries figures of its own (the
  finished goods' unit cost and total), and the BOM sheet follows so the two read alike. A BOM with no lines still has
  its Output row.
- **Decision F: a plan's costs are empty, not zero**: a Production Order's rows and a draft journal's. A journal's three
  cost totals and its two computed figures (`TotalCostOfProduction`, `CostRoundingAdjustment`) are sums of its rows,
  named in the guard and not repeated.
- **Decision G: the whole tenant's documents for the ledger numbers**, not the page's ids. A page can be 50,000 rows
  (phase 42's OPENJSON finding). A type's list is one (Id, Code, Reference) row per document, read once per sheet, and
  only for the types on the page.
- **Decision H: no new permission key.** The export is one Admin-only power over all of a tenant's data (phase 21b).
  More sheets of the same data do not change who may take it out.

## 3. Bugs found and fixed

- **The guard found two computed figures the reader had not decided about.** `ProductionJournal.TotalCostOfProduction`
  and `.CostRoundingAdjustment` are expression-bodied getters, which reflection sees like stored properties. Both are
  sums of the sheet's rows, so they are named, with that reason. The guard did what it is for: it made the decision
  explicit.
- **`ExportJobConfiguration`'s width comment was false** before this phase (§ TL;DR item 3). It now states the measured
  figure and names the test.
- No defects in shipped behaviour were found.

## 4. Evidence

**Tests.**
- Application (`OrderAndGoodsNoteExportTests`, new, four tests):
  - A dollar quotation at a branch in cartons, 5% off the document: 2 at 50 reads 95 / 12.35, *Carton*, 133.5, *Valid
    Until* 2026-09-30.
  - A sales order's delivery date and its *Delivered At* stamp. A purchase order's *Received At*.
  - Nothing is negated: the column sums to 220 + 95 + 480 + 400.
  - A delivery is *Out* 4 against `SO-0001`, with its tracking number, expected date and shipping address. A receipt is
    *In* 6 against `PO-0001` in cartons. The period filter works, and the seed's note names no order.
  - Another tenant's orders and notes never appear. The guard over ten types.
- Application (`ManufacturingExportTests`, new, four tests):
  - Juice from fruit. 20 kg at 20 plus 100 of labour gives 500. The pulp takes 10% (2 kg at 25 = 50) and the juice the
    rest (10 l at 45 = 450). Raw + expense = output + by-product, exactly.
  - The order carries the same rows with no costs (the expense's planned 100 aside).
  - A draft journal prints empty costs. The BOM sheet ignores a period. The guard over twelve types.
- Application (`LedgerDocumentNumberExportTests`, new, two tests):
  - A cash transfer's two GL lines read `CT-0001` / *Float to bank*. An opening balance reads *Opening Balance*. An id
    that exists nowhere keeps its id and an empty number.
  - A warehouse transfer's two movements read `WT-0001` / *To the godown*. *Opening Stock* is labelled. A movement
    naming **another tenant's** transfer prints its id and nothing else.
- Application (existing, updated): `PurchaseExportTests` reads *Value* on the freight row and nothing on the line;
  `ExportJobProcessorTests` asserts the thirteen sheets in order and one row on each new one; the shared seed gained a
  quotation, a delivery note, a BOM and a production order, so the tenant-isolation test covers the new sheets.
- Infrastructure (`ExportJobModelTests`, new): twelve categories, joined, fit `ExportJob.Categories`.
- Angular (`import-page.spec.ts`, one more spec): twelve boxes in the workbook's order; unticking down to *Bills of
  Materials* and *Goods Notes* sends exactly those two.

**E2E on SQL Server** (fresh organization *Phase 80 Export Breadth*: manufacturing, multiple warehouses, Litre and
Kilogram, juice / mango / pulp, cost term *Labour 80*, 100 kg of mango as opening stock at 20):

- **Approved:** Quotation `0001` (10 l at 120, 5% off), Sales Order `0001` (6 l), Purchase Order `0001` (50 kg at 20),
  GRN `0001` against it, a BOM, Production Order `0001`, Production Journal `0001` against it, and Delivery Note `0001`
  against the sales order. Also Journal Voucher `0001` (capital into the cash account), Cash Transfer `0001` (250 to
  petty cash) and Warehouse Transfer `0001` (5 kg to the godown).
- **Full export** (`POST /export-jobs`, all twelve): 13 sheets, 57 rows, read from the downloaded .xlsx's XML.
  - *Orders*: 1000 / 1140 / 720, which matches SQL's line sums (1000.0000, 1140.0000, 720.0000). *Received At* and
    *Delivered At* match the orders' stamps on the Nepal clock.
  - *Goods Notes*: Out 6 against `0001`, In 50 against `0001`. SQL's physical ledger has In 50 (GRN) and Out 6 (DN).
  - *Production*: the journal reads 45 / 450, 20 / 400, 25 / 50 (10%) and 100. SQL has `RawMaterialCost` 400,
    `ProductionExpenseCost` 100, `CostAllocatedToByProduct` 50, `FinishedGoodsUnitCost` 45 and `FinishedGoodsCost` 450.
    The order reads *Converted* with empty costs. The composed referrer join translated on SQL Server.
  - *Bills of Materials*: Output 10 l, Raw Material 20 kg, By-Product 2 kg at 10%, Expense 100, *Ripe fruit only*.
  - *Ledger Transactions*: seven lines, each named: `0001` *RUN-80* ×3, `0001` *Capital in* ×2, `0001` *Float to petty
    cash* ×2. SQL has 3 + 2 + 2 GL lines across the three types.
  - *Stock Movements*: *Opening Stock*, the journal's three movements, and `0001` *To the godown* Out / In.
  - Location was filled only on the sales order. In SQL, the quotation, purchase order and delivery note have NULL
    `LocationId` on a *Sales Transactions Only* tenant, so the empty cell is the truth (phase 78's lesson).
  - `exports.ExportJobs.Categories` stored the full 155-character list.
- **The 403-not-404 pair.**
  - `GET /export-jobs/{nonexistent}/download` was **404** "Export job not found." as Admin.
  - `POST /export-jobs?categories=Orders&categories=Bogus` was **400** for the Admin (binding runs first).
  - From a role holding only `Tenancy.BillingLocation.View`: the download was **403** "…(Configuration.ExportJob.View)",
    and `POST /export-jobs?categories=Orders&categories=Production` (a valid body) was **403**
    "…(Configuration.ExportJob.Manage)". Both sat beside that role's 200 on `GET /billing-locations`.
  - Admin was restored in SQL afterwards.
- **Keyboard (browser pass, `erp-web-ssl`, the cookie moved in through a `web/public` file written by a script and
  deleted at once).**
  - Configurations > Import / Export showed twelve boxes. *Bills of Materials* reads "(all dates)".
  - From *Products*, Tab and Space unticked nine boxes, leaving *Bills of Materials*, *Goods Notes* and *Production*.
    Shift+Tab ×12 reached *Start Export*, with its ring visible, and Enter sent
    `POST /export-jobs?categories=BillsOfMaterials&categories=GoodsNotes&categories=Production` (201).
  - The history read **3/3 sheets · 14 rows** (4 + 2 + 8). SQL's row is `BillsOfMaterials,GoodsNotes,Production`, 3
    categories, 14 rows.
- No PDF changed.

## 5. Left open, and for later phases

- **POS Orders** (`PosOrder`) are on no sheet, by phase 77's Decision D: a bill names its order and its lines carry
  the order's terms, and an unbilled tab is not exported.
- **Cash Transfers on Payments, and the drawer's over/short**, stay on Ledger Transactions (phase 78 Decision C),
  now named by number.
- **Inventory Variance** (phase 58) is a report over the two stock ledgers, not a document; nothing to export.
- **An Inventory Adjustment's entered unit** is not on Stock Movements, which is primary-unit by design (phase 52). The
  adjustment is identifiable there by number now.
- **Carried unchanged:** phase 79 § 5, phase 77 § 5's discard-with-a-share and the vendor's transfer handling, phase 76
  § 5, phase 75 § 5, phase 74 § 5, phase 72 § 5, phase 71 § 5, phase 70 § 5, phase 69 § 5, phase 67 § 5, phase 66 § 5,
  phase 63 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. The vendor tenant was not opened.
