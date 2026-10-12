# Phase 89 — the VAT return in BS months, the annexes in BS, and the Nepal day everywhere

## TL;DR

**The VAT Summary is now the IRD VAT return: a BS fiscal year, twelve month columns, the return's lines.** The vendor's
screen was opened for the first time (phase 8c designed ours blind). It is not three VAT-rate buckets over a date range.
It is a fiscal-year picker, Shrawan to Asar, one row per line of the return in the IRD form's Nepali wording, with a
month's credit carried into the next. Ours now has the same shape:

- Each line is in English, with the form's Nepali beside it.
- It reads the four readers the Sales and Purchase Registers read, so a month of the return equals the registers over
  that month by construction.
- Its `.xlsx` is in BS whatever the toggle says.

**Also shipped:**

- **Expenses are purchases now**, in the Purchase Register and on the return: their VAT is input tax.
- **Annex 5 and Annex 13 exports** carry their dates and period in BS with AD beside it, as phase 71's registers do.
- **Annex 5 gains the vendor's columns we can now back:** Fiscal Year, Discount (with Amount before discount), Print
  Count, the first print's time and printer, and Entered By.
- **Both annexes open on this fiscal year to date.**
- **An ERP return's own page names its warehouse** (phase 86 § 5).
- **A client-wide fix of the calendar day.** 67 sources took "today" as the UTC date, which is yesterday from 00:00 to
  05:45 in Kathmandu. 29 report defaults opened on the last day of the previous month. The invoice and purchase-bill
  forms suggested every due date **a day early**, all day.

The user's decisions (2026-10-12):

- **The candidate:** phase 71 § 5's annex exports in BS, plus phase 86 § 5.
- **The VAT Summary becomes the IRD return grid.**
- **Labels:** English with the Nepali beside it.
- **Calendar:** phase 71's rule, BS with AD beside it, whatever the toggle says. The vendor follows the toggle.
- **All four extras:** the date sweep, fiscal-year-to-date defaults, Annex 5's backed columns, and the credit note's
  warehouse.
- **The date sweep covers all 67 files**, not only the 18 report pages first scoped.
- **Expenses go in both** the register and the return.
- **Credit carries across the fiscal year's end.**

| | |
|---|---|
| Live read taken | **yes**, abcagro1 (13 trial days left): VAT Summary, Annex 5, the shared export drawer, the client bundle. The only non-GET was each report's own *Generate* (`POST /api/v1/erp/report`, a read by meaning, clicked through the vendor's screen); every other non-GET was refused in the page. Nothing written |
| Migration | **none** |
| New permission keys | **none**. The return keeps `Reports.VatSummary.View` (Admin+Member); the annexes keep theirs |
| Changed endpoints | `GET /reports/vat-summary` and `/export` take **`fiscalYear`** (400 without it, 404 outside the BS table) instead of `fromDate`/`toDate`, and answer `{fiscalYear, fromDate, toDate, columns, lines, netVat}`. `GET /reports/annex-five` rows gain `fiscalYear`, `discount`, `printCount`, `printedAt`, `printedBy`, `enteredBy`, and **`amount` is now before discount**. `GET /reports/purchase-register` lists approved expenses (`documentType: "Expense"`). `GET /credit-notes/{id}` answers `returnWarehouseName`, and `posRefund.warehouseName` is gone |
| Changed screens | VAT Summary (rebuilt), Annex 5 (columns, default period), Annex 13 (default period), Purchase Register (an Expense badge), the credit note page (*Goods returned into*), and every form and report that defaults a date |
| Changed print output | none (no PDF changed) |
| Initial bundle | **653.05 kB** (+0.47) against 680 kB |

**Three things worth carrying forward.**

1. **A screen designed before anyone opened the reference product is a hypothesis, however long it has shipped.**
   Phase 8c said so in its own status doc: "reconcile against the live screen if it is ever opened". Eighty phases
   later, opening it replaced the report's input (a date range became a fiscal year), its shape (rate buckets became the
   return's lines) and its arithmetic (a month's credit carries forward).
2. **`new Date(y, m, 1).toISOString()` is the previous day east of Greenwich.** Local midnight, printed in UTC, is
   18:15 the day before in Kathmandu. It was in 18 report defaults, and in the two due-date suggestions as a
   local-midnight parse (`T00:00:00` with no `Z`). Neither shows on a UTC build machine or in a test without a faked
   clock. `nepal-day-sweep-guard.spec.ts` bans the four shapes, and `nepal-time.spec.ts` fakes the clock at the boundary.
3. **A new reader of a shared reader is the moment to ask what the reader leaves out.** Building the return on the
   Purchase Register's reader surfaced that the register had never listed an expense. Its VAT had been missing from both
   the purchase book and the old VAT Summary since phase 8c.

Tests: Domain 1032, Application.UnitTests **1813** (+8), Infrastructure.UnitTests 14, Api.IntegrationTests **76** (+5),
Angular **881** (+17), 110 files. `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and
`ng test` (alone) are green.

---

## 0. Live read, 2026-10-12 (abcagro1), read only

**VAT Summary** (`#/reports/new/vat-summary`):

- The filter is one **fiscal year** picker ("2083 - 2084", defaulting to the current one). There is no date range.
- *Generate* posts `{report_id: "report-vat-summary-report", fiscal_year: 2083}`. The answer is `sales[12]`,
  `purchase[12]`, `monthly_tax_balance[12]`, `prev_month_credit_tax_balance[12]` and `payable_tax_balance[12]`, one
  entry per BS month in fiscal order.
- The screen heads the twelve columns Shrawan … Ashad. Its rows, labelled only in Nepali, are:
  - **Sales:** taxable sales and their VAT, exports, local exempt sales, the total, and the returns of each (exempt,
    taxable and its VAT, the total).
  - **Purchases:** taxable purchases and their VAT, taxable imports and their VAT, capital purchases and imports and
    their VAT, exempt purchases and imports, the total, and the returns of each.
  - **The settlement:** the month's tax credit/(debit), the credit unreconciled from last month, and tax payable/(credit
    left).
- On the tenant, Asoj held 1,647.50 of taxable sales (VAT 214.18), 940.45 of taxable returns (VAT 122.26) and 162 of
  exempt purchases. The month read 91.92 and the payable 91.92, so the month is output VAT net of returns, less input
  VAT net of returns. The 162 is PB0001/HO/83-84, a purchase bill. The tenant has no expense, so how the vendor treats an
  expense's VAT is unread. Its client names the purchase-side series `purchase_expenses`.

**Annex 5** (`#/reports/new/annex-5-report`):

- It takes `from_date`/`to_date`, defaulting to **this fiscal year to date** (17-07-2026 to today), and so does Annex 13
  (with `amount: 100000`).
- Each row carries `fiscal_year: "2083.084"` and `discount`, and `amount` **before** it: 1,006.50 − 50 = taxable
  956.50.
- Rows also carry `print_count`, `printed_time`, `printed_by`, `entered_by`, `payment_method`, `vat_refund_amount`,
  `is_ird_sync`, `is_realtime` and `transaction_id`.
- The row's `entered_by` names a user with an email and phone, which this doc does not record.

**The export.** Every report's *Export* opens one drawer ("Export Current View | Export Full List", ".xlsx") and posts
the filters with **`is_date_bs`, taken from the viewer's AD/BS toggle** (`localStorage.date`). So the vendor's exports
follow the toggle. Ours do not, by phase 71's rule (Decision C).

## 1. What shipped

**Backend**

- `SalesBookReader` (Application/Sales/Reports) is the Sales Register's invoice half, moved out unchanged: folded to
  base per line, fees inside the VAT base, joined to the invoice query, with every filter the register applied.
- `PurchaseBookReader` (Application/Purchasing/Reports) is the Purchase Register's bill half plus approved **expenses**.
  An expense line is Others and local, bucketed by `PurchaseReturnReader.Bucket`. It also takes phase 42's join to the
  query in place of the id list it used to send.
- The Sales and Purchase Registers read them. With `SalesReturnReader` and `PurchaseReturnReader`, they are the four
  readers the return reads.
- `VatSummaryReportQuery(OrganizationId, FiscalYear)` returns `VatSummaryReportDto(FiscalYear, FromDate, ToDate,
  Columns, Lines)`.
  - `VatReturnLineDto` is `(Key, Section, Label, LabelNepali, IsTotal, Monthly[12], Total?)`.
  - `VatReturnLines` holds the 28 keys and their labels in the form's order, without the vendor's two typos.
  - A validator gives the 400. A year outside the table is a 404, as on the Monthly reports.
- `ReportSpreadsheetExporter`:
  - The VAT export is the grid: the period in BS (AD) and each month's AD range under its BS name. A credit prints in
    parentheses and a zero is written unsigned. It is named `VatSummaryReport_BS2083-2084.xlsx` with no calendar marker.
  - Annex 5 gains Fiscal Year, Date (BS), Date (AD), Discount, Print Count, Printed At (BS (AD) and Nepal time),
    Printed By and Entered By.
  - Annex 13 gets the period above its header.
  - `ExportTable` takes an optional `periodLine`, and sizes columns from the header down.
- `BillDate.FormatRange(from, to)`: `2083-04-01 to 2083-06-26 BS (2026-07-17 to 2026-10-12 AD)`.
- `AnnexFiveReportRowDto` gains six **required** parameters, so no caller can stop short. Discount is
  Σ(quantity × rate − Amount); Amount is the stored value plus it.
  - The print columns come from `InvoicePrints`/`CreditNotePrints`. The first print is the original bill.
  - Entered By is the user on the earliest `Create` audit row.
- `CreditNoteDetailDto.ReturnWarehouseName` (init) is read through `CreditNoteReturnWarehouses`, and
  `CreditNotePosRefundDto.WarehouseName` is retired.

**Client**

- **VAT Summary page:**
  - A fiscal-year select.
  - A grid per block (`tbody` per section, `th scope="rowgroup"`), each line `th scope="row"` with its Nepali in a
    `lang="ne"` span.
  - A credit in parentheses, a nil month shown as "–" with a visually hidden "nil", and a caption naming the year.
  - Each month header is its BS name in bold over its from/to dates in small type on two lines. The user found the
    one-line range hard to tell from the label.
- **Annex 5:** six columns, and the fiscal-year-to-date default (`fiscalYearBounds(currentFiscalYear()).from` to
  `nepalToday()`). **Annex 13:** the same default.
- **Purchase Register:** an *Expense* badge. **Credit note page:** *Goods returned into …* under *Against invoice* on
  every approved note. The till panel's copy is gone.
- **`nepal-time.ts`** gains `nepalDayOf`, `nepalFirstOfMonth` and `shiftDay`, and the sweep moved 67 files onto them:
  - 72 UTC "today"s, 29 first-of-month defaults (18 local-midnight, 11 `Date.UTC` built from the browser's fields) and
    2 two-years-ago defaults.
  - By hand: the two due-date suggestions, the delivery note's "tomorrow", `currentFiscalYear()`, the date-range
    presets' `iso()`, and the two calendar-shift copies (`shiftDate`, the dashboard's `addDays`), which now delegate.

## 2. Scope decisions

**A–C are the user's** (TL;DR): the return grid, labels in both languages, BS whatever the toggle says.

**D. The return reads the registers' readers, not its own queries.** Phase 8c's handler had four queries of its own,
and phase 36's rule is that two reports agree only through one reader. So the registers' halves were extracted rather
than copied. `A_months_return_equals_the_sales_and_purchase_registers_over_that_month` holds every line to its
register column on a month carrying every kind of figure. It was proven to bite with an injected regression (exempt
purchases zeroed), and the file was restored by hash.

**E. Expenses are in the book (the user).** An expense line names an account, carries VAT and has no classification
or import flag, so it is taxable non-capital local, or exempt at no VAT. Annex 13 has classed it the same way since
phase 8e. This changes the Purchase Register's totals on any tenant with approved expenses. That is a correction: their
input VAT was missing from the purchase book.

**F. Exports vs exempt.** The register files an export invoice's zero-rated value under Tax-Exempt *and* as Export
Value. The return lists it once, as an export, and "local exempt" is the tax-exempt value of the invoices that are not
exports. A domestic zero-rated line stays in local exempt, as in the register.

**G. The carry (the user).** The handler reads the readers from the beginning of the books to the year's end in one
pass. Months before the year are bucketed by their own BS month. A month's net VAT first uses the credit it inherited;
an excess credit carries on. Shrawan therefore opens with what every earlier month left. A date before BS 2000 has no
VAT period and carries nothing.

**H. No location filter.** An IRD return is filed once per PAN, and `ReportLocationSweepGuardTests`' census exemption
stands. The return reads the whole organization, every channel.

**I. Annex 5's columns: what is backed, and what is not.**
- **Backed now:** Fiscal Year (the server has had a BS calendar since phase 26b), Discount (phase 16b), the counted
  prints (phase 67) and Entered By (the audit trail, phase 16d).
- **Still out:** Payment Method, VAT Refund and the IRD-sync columns, which have no backing, as phase 8f found.
- **Amount moved to before discount**, as the vendor's is, so Amount − Discount reads true. That is the old Amount, so
  no figure was lost.
- **The print columns show the first print**, the original bill, with Print Count beside it. Before the build the
  user was told "Last Printed". The first print was chosen because later copies are what Print Count counts.

**J. The screen keeps the toggle; the file does not.** The grid's month ranges follow the viewer's toggle, as every
on-screen register does (phase 71 Decision E). The `.xlsx` is BS with AD beside it.

**K. The sweep is all 67 files (the user), with a guard.** It bans four shapes outside `nepal-time.ts`:
`new Date().toISOString().slice(0, 10)`, a date built from the browser's calendar fields and printed in UTC, a
local-midnight parse, and `setDate`. A whole instant (`new Date().toISOString()`, the POS estimate's timestamp) is
not a day and is allowed. The guard asserts it read over 300 sources, and that each pattern matches the code it
replaced.

**L. No new keys.** The return exposes nothing the old summary did not (sales and purchase totals for a period) under
the same `Reports.VatSummary.View`. Annex 5's new columns sit under its Admin-only key. The prints and Entered By name
users, which is the reason Annex 5 is Admin-only.

## 3. Bugs found and fixed

1. **Every report opened a day early in Nepal** (`new Date(y, m, 1).toISOString()`), and "today" was yesterday before
   05:45. This was in 67 sources, since each page was written.
2. **The invoice and purchase-bill forms suggested every due date a day early**, all day in Nepal (a local-midnight
   parse printed in UTC). Phase 36 put due dates on the server, so the stored date was right; the suggestion on the form
   was wrong.
3. **Expenses' VAT was in no purchase book** and on no VAT summary since phase 8c.
4. **An ERP return's page did not name its warehouse** (phase 86 § 5).
5. **First version: the VAT grid's label column collapsed** to a word per line at 800 px. It now has a minimum width,
   and the months scroll instead.

## 4. Evidence

**Unit and export tests.**

- `VatSummaryReportQueryHandlerTests` (9):
  - The grid's shape.
  - A month net of returns and drafts, with the next year left out.
  - The carry within a year and across the year's end.
  - Expenses, the base-currency fold, and the 404.
  - The register agreement, and expenses in the Purchase Register.
- `AnnexFiveReportQueryHandlerTests` adds a discounted, twice-printed bill entered by another user.
- `CreditNoteReturnWarehousePageTests` covers an ERP return and a standalone note.
- `StatutoryExportBsTests` (5) covers the VAT grid and Annex 5 under both toggles, and Annex 13's period. 18:30 UTC
  prints as the next Nepal day.
- Angular adds `vat-summary-report-page.spec`, `annex-five-report-page.spec`, `nepal-time.spec` (faked clock at
  18:15 UTC) and `nepal-day-sweep-guard.spec`.

**Fresh-org E2E, run 2026-10-12.** The organization is *Phase 89 VAT Return Traders*: VAT-registered, tracking
inventory, a chart of accounts and twelve defaults.

- **Shrawan 2083:** a local bill (10 × 100), an import bill (500), a capital bill (1,000), expenses of 200 at 13% and
  50 at no VAT, and a sale of 400.
- **Bhadra 2083:** a sale of 1,500, an export of 250, an exempt sale of 100, a return of 300 against the sale, and a
  debit note of 100.

| | Shrawan | Bhadra | Aswin |
|---|---|---|---|
| Taxable sales / VAT | 400 / 52 | 1,500 / 195 | – |
| Exports / local exempt | – | 250 / 100 | – |
| Sales returns / VAT | – | 300 / 39 | – |
| Taxable purchases / VAT | 1,200 / 156 (bill 1,000 + expense 200) | – | – |
| Imports / VAT, capital / VAT, exempt | 500 / 65, 1,000 / 130, 50 | – | – |
| Purchase returns / VAT | – | 100 / 13 | – |
| VAT for the month | (299) | 169 | – |
| Brought forward | – | 299 | 130 |
| Payable / (credit) | (299) | (130) | (130) |

- **Every line equals its register column** over that month: sixteen identities, all true. In SQL, Shrawan's input VAT
  is bill lines 325 + expense lines 26 = 351, and Bhadra's figures are 195, 39 and 13.
- **Annex 5:** rows carry fiscal year 2083, Discount 0, Entered By the test user, and on the printed bill Print Count 1
  and Printed At 16:59 UTC, exported as `2083-06-26 BS (2026-10-12 AD) 22:44`.
- **Export files:**
  - The VAT `.xlsx` is `VatSummaryReport_BS2083-2084.xlsx`, headed `2083-04-01 to 2084-03-31 BS (2026-07-17 to
    2027-07-16 AD)`, with a row `Taxable sales | करयोग्य बिक्री | 400 | 1500 | 0 …`.
  - The Annex 5 and Annex 13 `.xlsx` open with their period in BS (AD).
  - Annex 5's first columns are `Fiscal Year | Date (BS) | Date (AD)`.
- **The credit note's page** answers `returnWarehouseName: "Main Store"` with `posRefund: null`.
- **Negative paths:**
  - No `fiscalYear` is a **400**. `fiscalYear=2095` is a **404** as Admin.
  - A role copied from Member minus `Reports.VatSummary.View` gets **403 naming the key**, on the return and on its
    export.
  - The same user in the same run gets a **200** on the Trial Balance, with a 200 on the return as Admin before. The
    membership was restored to Admin in SQL.
- **Browser pass**, via the cookie transplant through `web/public`, with the file deleted at once:
  - The VAT page shows the table above, with the Nepali glyphs rendered.
  - **Keyboard:** the Fiscal Year select focused, ArrowDown, focus stays on the select, value 2082, caption *2082 -
    2083*, and the request sent `fiscalYear=2082`.
  - Annex 5 opened on `fromDate=2026-07-17&toDate=2026-10-12` with its six new columns. The credit note page reads
    *Goods returned into Main Store* once.
  - The Purchase Register's Expense badge was not seen in the browser: its default period (this AD month) holds no
    document on this tenant. The API run shows the register's types `Expense, PurchaseBill` for Shrawan.

## 5. Left open, and for later phases

- **How the vendor treats an expense's VAT** is unread (Decision E): the tenant has none.
- **A tax advisor's word**:
  - whether a credit carries across the fiscal year's end, or is refunded or reset;
  - whether a zero-rated domestic supply belongs in local exempt.
- **The vendor's export follows the toggle**; ours is BS whatever it says (phase 71's divergence, now on five statutory
  exports).
- **Annex 5's Payment Method, VAT Refund and IRD-sync columns** have no backing. A till sale's tenders could back
  Payment Method, but an ERP invoice has none.
- **The return's history read is from the beginning of the books.** It is one pass per reader, but on a long-lived
  tenant it is linear in history (phase 42's ledger lesson). Measure on `tools/scale` before narrowing it, for example
  to the last month with a non-zero payable.
- **Carried unchanged:**
  - phase 88 § 5 and phase 87 § 5 / Decision G;
  - phase 84 § 5's money receipt on the roll; phase 83 § 5; phase 81 § 5; phase 80 § 5; phase 79 § 5's other items;
  - phase 77 § 5; phase 76 § 5; phase 75 § 5; phase 72 § 5; phase 71 § 5's other items (the email body's `$[DATE]$`,
    the on-screen register grids);
  - phase 70 § 5; phase 69 § 5; phase 67 § 5; phase 66 § 5; phase 62 § 5.

## Writes made to the vendor tenant

None. Each report's *Generate* is a POST that reads, clicked through the vendor's own screen. Every other non-GET was
refused in the page, and none was attempted.
