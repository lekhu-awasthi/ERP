# Phase 78 — The export finished: every part of a purchase, and every rupee in and out of a drawer

## TL;DR

**Phase 77 § 5's export items, finished, plus a defect on the export screen that the keyboard check found.**

1. **The Purchase Documents sheet now carries the rest of a purchase's money.** It gains twelve columns, appended
   after phase 38's fourteen, and three new kinds of row:
   - **Expenses are on the sheet.** Their lines name an *Account* instead of a product.
   - **TDS is a row of its own** after a document's lines. It is the amount withheld, so what a supplier is owed is
     Amount + VAT Amount − TDS.
   - **An additional cost (landed cost) is a row of its own.** It names its *Cost Term* and, when it has one, its
     product. It sits in an *Additional Cost* column, never in Amount: the supplier is credited only the goods.
   - **A debit note names its bill** (*Against Bill*). Its line takes the bill line's *Expenditure Classification* and
     *Import*, matched exactly as the statutory registers match them.
   - *Supplier Invoice Ref* and *Document Discount %* are added too.
2. **Unit, Exchange Rate and Location on both document sheets.** Since phase 52 a Quantity was in a unit the sheet
   never named. A dollar bill's figures could not be turned into rupees, and a branch's documents could not be told
   apart. Payments gains *Exchange Rate* and *Location* too.
3. **A drawer's Cash In and Cash Out are rows of the Payments sheet**, numbered by the session, in the location's cash
   mode, with an appended *Account* column naming what the cash moved against.
4. **The export screen exported every category whatever was unticked** (since phase 38). Its selection started empty,
   meaning "all", and unticking a box removed it from an empty list. The state stayed "all" while the box showed
   unticked. Now the selection is the ticked list, "all" is only what the request sends when every box is ticked, and
   Start Export is disabled, with a reason, when none is.

The user's decisions (2026-10-11), each the recommended option:

- **A:** Phase 77 § 5's export items are the phase (over print layouts, discount controls and discard-with-a-share).
- **B:** Expenses go on the Purchase Documents sheet, with an Account column.
- **C:** The drawer's Cash In / Cash Out only, not Cash Transfers.
- **D:** Unit, Exchange Rate and Location on every sheet they apply to.

| | |
|---|---|
| Live read taken | **none needed.** The export is our own feature (FR-2.8); phase 21a confirmed the reference product has no working backup screen to compare against |
| Migration | **none** (no schema change) |
| New permission keys | **none**. The export keeps `Configuration.ExportJob.Manage` / `.View` |
| Changed endpoints | none in shape. `POST /export-jobs` produces wider *Purchase Documents*, *Sales Documents* and *Payments* sheets |
| Changed screens | Configurations > Import / Export: the category checkboxes now mean what they show; Start Export is disabled with a reason when nothing is ticked |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged |

**Three things worth carrying forward.**

1. **A checkbox list whose empty state means "all" cannot be unticked.** Unticking from the empty list leaves it empty,
   and so still "all". The box shows unticked anyway, because its `[checked]` binding never changed (phase 64's radio,
   as a checkbox). The rule: hold the explicit list, and translate to the API's "omitted means all" only when sending.
   This was phase 38's "choose what to export silently exports everything" coming through a second door, the browser
   rather than the binder. No guard caught it, because no test ever unticked a box.
2. **A document figure true of no line is a row of its own**, not a column repeated on every line. Phase 77's
   round-off was the first; TDS and an additional cost are the next two. A reader sums a column and gets the
   document's figure exactly once.
3. **A purchase document carries a location only under *All Transactions*.** The default scope (*Sales Transactions
   Only*) stores none (phase 32). So an empty Location column on a purchase row is the truth, not a reader bug. The
   E2E had to switch the scope to see the column filled.

Tests: Domain 995, Application.UnitTests **1722** (+8), Infrastructure.UnitTests 13, Api.IntegrationTests 57, Angular
**836** (+3, 101 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and `ng test` are green.

---

## 1. What shipped

- **`PurchaseDocumentExportReader`** (rewritten).
  - Seven queries, each ordered and capped on its own, then merged in memory as phase 77's sales reader does:
    bill lines, debit-note lines, expense lines, additional costs, and bill / debit-note / expense TDS rows.
  - A document's rows print as its lines, then its additional costs, then its TDS (`RowKind`).
  - A debit note's bill is phase 6's referrer (`ReferrerType = PurchaseBill`). Its line's classification is a
    correlated subquery on the bill's lines, keyed by product, rate and VAT rate as in `PurchaseReturnReader`. It
    coalesces to *Others* and its *Import* to *No* when nothing matches, which is what the registers do.
  - Debit-note figures are negated, including its TDS. Rates, percentages and the exchange rate are not.
  - `ExportedMoney` lists, by type and property, every `decimal` the sheet carries.
- **`SalesDocumentExportReader`**: *Unit* (the line's, else the product's primary unit), *Exchange Rate* and
  *Location* appended. `ExportedDocumentMoney` names the invoice's and credit note's own figures the sheet carries.
- **`PaymentExportReader`**: *Exchange Rate*, *Location* and *Account* appended, plus the drawer movements:
  - A fifth query reads the drawer's movements, bounded by Nepal midnights (each bound its own `Where`).
  - The drawer's mode is the location's Cash-kind mode whose account is the session's drawer, resolved in memory.
  - Status is *Approved*: a movement posts when it is recorded, and nothing voids it.
  - Source is *Till Cash In* / *Till Cash Out*.
- **`ExportLookups`** (new): units, locations, accounts (code and name) and cost terms, each loaded once per tenant
  and resolved after the page is read. It replaces a join in every query, and avoids handing the page's ids back to
  SQL (phase 34c's `OPENJSON`).
- **The import page**: `selectedExportCategories` starts with every category; toggling rebuilds it in catalogue
  order; `startExport` sends `[]` only when every box is ticked. Start Export is
  `[disabled]` with nothing ticked, and is `aria-describedby` an always-present `aria-live` hint.

## 2. Scope decisions

- **Decision A: the export, not printing or discount controls.** The print layouts would have needed live reads
  while the vendor tenant still lives (about 14 days). The export is fully ours and closed in scope.
- **Decision B: expenses on the purchase sheet, with an Account column.** Annex 13 and the TDS report already treat an
  expense as a purchase. Its supplier, per-line VAT and TDS were otherwise on no sheet.
  - Its rows leave Product empty and carry zero Quantity and Rate, as the round-off row does.
  - *Rejected: leave expenses to Ledger Transactions.* The ledger has the accounts but not the supplier's invoice
    reference or the TDS withheld.
- **Decision C: the drawer's own moves only.** A Cash In / Cash Out is money a till handled (phase 77's promise). A
  Cash Transfer is account to account inside the business. Adding it would change what the sheet means.
  - Not rows: the opening float (it moves no money) and a close's over/short (a correction of the drawer's account;
    Ledger Transactions has it).
- **Decision D: Unit, Exchange Rate and Location everywhere they apply.** Without Unit a phase-52 quantity is
  ambiguous. Without the rate a foreign row cannot be read in rupees. Without Location a branch's rows mix.

Decided in the session, not asked:

- **Decision E: TDS and an additional cost are rows of their own** (phase 77 Decision F's reason). TDS is a figure of
  the document; on every line it would sum wrong.
  - An additional cost is owed to whoever carried the goods, not the supplier (confirmed live 2026-09-04). So it is
    its own column, never folded into Amount.
  - What it added to stock is base currency and already on Stock Movements. `CapitalisedAdditionalCost` and the
    rounding adjustment are named in the guard, not exported.
- **Decision F: a debit note's classification is its bill's, with the registers' fallback.** A debit-note line stores
  none (phase 19). The export agrees with Annex 13 and the Purchase Register for each return, by reading the same key.
- **Decision G: names are resolved in memory** from tenant-sized lists (`ExportLookups`). A unit, location, account or
  cost term is configuration, bounded by the tenant, never by the period.
- **Decision H: the guards cover headers now.** Phase 77's guard held only the sales *lines*.
  - The purchase guard holds eight types: three headers, three line types, the additional cost and its allocation.
  - A second sales guard holds the invoice's and credit note's own decimals.
  - Each `decimal` is exported or named with a reason (derived, base-currency stock value, another sheet's row).
- **Decision I: the export-screen fix ships in this phase.** It is the only way to *choose* what the new sheets
  export, and the keyboard check found it.

## 3. Bugs found and fixed

- **Unticking an export category did nothing (since phase 38).** Found in the keyboard pass. Six boxes were unticked
  and Start Export pressed, and the history showed **8/8 sheets**; SQL's `exports.ExportJobs.Categories` held all eight.
  The cause is § TL;DR's first lesson. After the fix the same keys queued **0/2 sheets**, and SQL holds
  `PurchaseDocuments,Payments`. Three new specs pin it: what is left ticked is sent, every box ticked sends none,
  and nothing ticked disables the button and says why.
- Test-only: the purchase test's column constants `Account` and `CostTerm` shadowed the domain types of the same name
  (`Account.Create` resolved to an `int`), so they live in a nested `Col` class.
- Test-only: a Cash In from the bank account tripped the negative-cash guard (409) on a tenant whose bank holds
  nothing. The test takes it from an Owner's Capital account, which is what a float top-up is.

## 4. Evidence

**Tests.**
- Application (`PurchaseExportTests`, new, six tests):
  - An imported dollar bill at a branch: 4 cartons at 100, 5% and 10% off. Its line reads 342 / 44.46, *Carton*,
    133.5, *Capital*, *Yes*, 10 and *SUP-INV-9*, then a *Freight* row of 50 with Amount 0, then a TDS row of 15.
  - The debit note against it: −1, −85.50, *Capital*, *Yes*, *Carton*, TDS −3. A standalone note: *Others*, *No*,
    the product's own unit.
  - An expense: the account by code and name, 2000 / 260, the branch; then its TDS row of 20.
  - Σ(Amount + VAT − TDS) equals the documents' own totals less TDS, and Σ Additional Cost is 50.
  - The period filter, and the guard over eight types (proved to bite: removing `Expense.TdsAmount` fails it, and
    the file was restored by hash).
- Application (`TillExportTests`, two more tests, one extended):
  - A till line's Unit, rate 1 and location; the round-off row has no unit.
  - The drawer's In 300 / Out 100 rows: session code, Approved, cash mode, note, location and account. A period
    ending yesterday holds none of today's moves; today's holds both.
  - The sales-document guard.
- Angular (`import-page.spec.ts`, three more specs): see § 3.

**E2E on SQL Server** (fresh organization *Phase 78 Purchase Export*, multi-currency, two locations, a Goods product
with *Carton* = 12 *Piece*, TDS 1.5%, cost term *Freight 78*):

- **Approved:**
  - Bill `0001`: 5 pieces at 80, rupees.
  - Bill `0002`: imported, USD at 133.5, 4 cartons at 100, 5% + 10% off, TDS 5.13, freight 50.
  - Debit note `0001` against `0002`: one carton, TDS 1.2825.
  - Standalone debit note `0002`: 2 pieces.
  - Expense `0001`: rent 2000 + VAT, TDS 30.
  - The till: Cash In 300 from Owner Capital, Cash Out 100 to Vegetables, one 113 sale.
- **Purchase Documents** (read from the downloaded .xlsx's XML):
  - The debit note's line reads *Against Bill* `0002`, *Capital*, *Yes*, *Carton*, 133.5, and its TDS row −1.2825.
    SQL's join of the note's line to the bill's line gives *Capital*, so the correlated subquery and its coalesce
    translate on SQL Server.
  - The standalone note reads *Others* / *No* / *Piece*; SQL has no matching bill line.
  - Every Location was empty, and SQL's `LocationId` is NULL on all five documents. The tenant was on *Sales
    Transactions Only* (scope 0). With the scope switched to *All Transactions*, bill `0003` (2 cartons, product-wise
    freight 40, TDS 36) and expense `0002` (USD) were raised at the branch. Their rows read *Pokhara Branch 78*, the
    freight row names *Cashew 78*, and the expense carries 133.5.
  - **Σ(Amount + VAT − TDS) = 5574.6975**, and SQL's lines less TDS (debit notes negated) = **5574.6975**.
    **Σ Additional Cost = 90**, and SQL = **90.0000**.
- **Payments:** the till sale's tender, then `SES0001` Received 300 *Float top-up* (*Till Cash In*, *Cash 78*,
  *HeadOffice*, *0009 Owner Capital 78*) and Paid 100 *Vegetables* (*Till Cash Out*, *0014 Vegetables 78*). SQL's
  `pos.PosCashMovements` holds the same two.
- **Sales Documents:** the sale's line reads *Piece*, 1, *HeadOffice*.
- **The 403-not-404 pair.** `GET /export-jobs/{nonexistent}/download` was **404** "Export job not found." as Admin.
  From a role holding only `Tenancy.BillingLocation.View` it was **403** "…(Configuration.ExportJob.View)", beside
  that role's 200 on `GET /billing-locations`. Admin was restored afterwards.
- **Keyboard (browser pass, https dev server, cookie transplanted through `web/public` and the file deleted at once).**
  - On Configurations > Import / Export, focus started on *Start Export*. Tab moved through the categories, Space
    unticked six, and the hint read "Untick any category you do not need." Shift+Tab ×6 returned to *Start Export*,
    with its ring visible, and Enter queued **0/2 sheets**.
  - Before the fix, the same keys queued 8/8 (§ 3).
- No PDF changed.

## 5. Left open, and for later phases

- **Cash Transfers on Payments** (Decision C). They stay on Ledger Transactions.
- **The drawer's over/short** is not a Payments row. It is a correction of the drawer's own account.
- **An additional cost's *Method*** (Value / Quantity) is not exported. The allocation it produced is
  (`PurchaseBillAdditionalCostAllocation`, named in the guard), and the stock value it added is on Stock Movements.
- **Purchase Orders, GRNs, Delivery Notes and the manufacturing documents** are on no sheet. Each has quantities but no
  money owed to anyone, except a Purchase Order, which is a commitment, not a purchase.
- **Carried unchanged:** phase 77 § 5's discard-with-a-share and the vendor's transfer handling, phase 76 § 5, phase
  75 § 5 (and the phase 73 § 5 it carries), phase 74 § 5, phase 72 § 5, phase 71 § 5, phase 70 § 5, phase 69 § 5,
  phase 67 § 5, phase 66 § 5, phase 63 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. The vendor tenant was not opened.
