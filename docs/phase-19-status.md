# Phase 19 — Reporting Tags + remaining reports

## TL;DR
`TransactionReportingTag` (document-level, many-to-many) ships as the write-side prerequisite, with
attach/detach on Quotation/Invoice (the two document types confirmed live to carry the field) and a
`ReportingTagFilter` threaded through the GL-derived reports. Six new reports close the catalog:
Cash Flow Summary (direct-method Bank/Cash movement summary, **not** an indirect-method statement —
live-confirmed against the real Tigg screen, which has no Operating/Investing/Financing buckets at
all, just a Bank Accounts filter), Sales Register / Purchase Register (Nepal IRD statutory
Sales-Book/Purchase-Book format, live-confirmed column-by-column, reusing PurchaseBill's existing
`IsImport`/`ExpenditureClassification` fields for the Capital/Others+Import split), Stock Ageing
(same 1-30/31-60/61-90/91+ buckets as Customer/Supplier Ageing, live-confirmed), Product
Profitability (a per-product-per-period aggregate, **not** a per-line fact table — live-confirmed),
and Ratio Analysis (no live check needed — erp-module-scan.md already fully specifies the ratio
list). All 6 live screens were opened hands-on before any handler code was written, per this
codebase's confirm-live discipline.

## Decisions

### 1. Reporting Tag attachment granularity — document-level
Live-confirmed against a real Approved Quotation (Q0012/83-84): "REPORTING TAGS" appears once in
the document's left sidebar (`No reporting tags` / `Add/Edit`), not once per line. The Invoice
create form (via Convert-to-Invoice) carries no Reporting Tags field on the create form itself —
same as Quotation, the control only appears on the saved document's detail view, confirming this is
a post-creation attach action, not a create-time field. `TransactionReportingTag { DocumentType,
DocumentId, TagOptionId }` models this directly (architecture-spec.md §3.8's shape, unchanged) — no
line-level variant is built. The reference product's own multi-select semantics for >1 tag weren't
observable in this session (the sample document had zero tags attached and the Add/Edit dialog did
not visibly open in the automated browser pass), so filter semantics for multiple selected tags
default to **OR** (any selected tag matches) — the common tracking-category convention (Xero/
QuickBooks) — recorded as a judgment call, not independently live-verified.

### 2. Cash Flow Summary — direct-method Bank/Cash movement summary
Live-confirmed: the real screen's only filters are Period + **Bank Accounts** (a picker over
Account rows, defaulting to "All") + a Compare toggle — there is no Operating/Investing/Financing
classification anywhere in the UI, and no such field exists anywhere in this codebase's Account/
AccountGroup model (grepped: no `Direct`/`Indirect`/classification field). The generated report
shows: **Starting Balance** (as of From Date) → **Received From Customer** (Cash In/Cash Out) →
**Other Receipts** → **Paid To Supplier** → **Other Payments** → **Ending Balance** (as of To Date).
This is a direct-method summary of actual Bank/Cash account movements, computed the same way as
Phase 8a's three statements (`GlLine`/`GlJournalEntry`, this time filtered to `Account.Kind ==
Bank || Cash` — the Phase 17 field) — not a new AccountGroup classification. Bucketing:
`GlJournalEntry.SourceDocumentType == Payment` with `Payment.Direction == Received` and
`Contact.Type == Customer` → Received From Customer; `Direction == Paid` and `Contact.Type ==
Supplier` → Paid To Supplier; every other GL line touching a Bank/Cash account (JournalVoucher,
CashTransfer, a Payment that doesn't match either pairing e.g. Quick Payment/Receipt with no
Customer/Supplier contact) → Other Receipts (net debit) or Other Payments (net credit). Starting
Balance = net Bank/Cash position from all GL activity before the period; Ending Balance = Starting +
period movements. "Compare" (period-over-period) is not built — the existing Trial Balance/Balance
Sheet/Income Statement handlers don't have it either (FR-9.1 names it but Phase 8a never built it),
so this isn't new Phase 19 scope; noted as a pre-existing gap, not fixed here.

### 3. Sales Register / Purchase Register — Nepal IRD statutory register columns
Live-confirmed against the real screens — Devanagari headers, translated:

**Sales Register** (one row per Approved Invoice or CreditNote — CreditNotes appear as negative
rows in the *same* register, not a separate "Sales Return Register"; FR-9.4 names only "Sales
Register"/"Purchase Register", not the Return variants, so those stay out of scope this phase):
Date, Invoice/CreditNote No, Buyer Name, Buyer PAN, Total Sales Value, Tax-exempt Sales Value,
Taxable Sales Value, VAT Amount, plus 4 Export columns (Export Value, Country, Declaration No,
Declaration Date) that the live screen has but this codebase's Invoice has no way to populate yet —
`IsImport`'s sales-side mirror ("This is export sales" checkbox, also seen live on the Invoice
create form) is explicitly deferred to Phase 23 (roadmap item 5). Those 4 columns ship in the DTO,
always empty/zero, with the limitation recorded here rather than silently omitted.

**Purchase Register** (one row per Approved PurchaseBill; DebitNotes as negative rows in the same
register, same reasoning as above): Date, Bill No, Import Declaration No, Supplier Name, Supplier
PAN, then 4 value/tax column-pairs — Tax-Exempt, Taxable-NonCapital-Local, Taxable-NonCapital-Import,
Taxable-Capital(combined local+import). Unlike Sales Register, **no domain gap** — PurchaseBill
already has `IsImport`/`ImportCountry`/`ImportDate`/`ImportDocumentNo` (Phase 6) and
`ExpenditureClassification` (Capital/Others, Phase 8e) on each line, exactly the fields this split
needs. Both registers explicitly exclude Draft/Void documents (FR-9.10 — confirmed live the register
only ever showed Approved-looking codes, no literal "DRAFT" rows).

### 4. Stock Ageing buckets — same as Contact Ageing (1-30/31-60/61-90/91+)
Live-confirmed against the real "Inventory Ageing Summary Report": columns are Product Name,
Category Name, 1-30/31-60/61-90/91+ Days (quantity, unit-labeled), Total, Rate, Amount — identical
bucket boundaries to `ContactAgeingSummaryQueryHandler`'s `age <= 30 ? 0 : age <= 60 ? 1 : age <= 90
? 2 : 3` (Phase 9 precedent), reused directly. Age = asOfDate − `StockLedgerEntry.TransactionDate`,
weighted by `QuantityRemaining`; Rate/Amount are the overall (not per-bucket) weighted-average
valuation, matching the live screen showing only one Rate/Amount pair per product row, not one per
bucket.

### 5. Product Profitability — per-product-per-period aggregate, not a per-line fact table
Live-confirmed: one row per Product (Code, Name, Category), columns Opening Balance, Purchase,
Production Cost, Additional Cost, Closing Balance, Cost Of Sales, Sales, Consumption, Gross Profit,
Gross Margin(%) — an aggregate, not Sales Master Report's per-transaction-line shape. Sales = sum of
`InvoiceLine.Amount` for Approved Invoices in the period; Cost Of Sales = sum of
`InvoiceLine.CogsUnitCost × Quantity` (both already stored at Approve time, confirmed by
`InvoiceLine.cs`/Phase 7 — no new write-side work). Gross Profit = Sales − Cost Of Sales; Gross
Margin% = GrossProfit/Sales×100. "Production Cost" and "Consumption" (Manufacturing-sourced) and
"Additional Cost" (Cost Terms/landed cost, Phase 20) ship as always-zero columns — the underlying
write-side features don't exist in this codebase yet (Manufacturing is an open scope question per
architecture-spec.md §6; Cost Terms is Phase 20 scope) — recorded as a known limitation, not silently
dropped from the DTO shape.

### 6. Ratio Analysis — no live check needed
erp-module-scan.md's Reports Module section already fully specifies every ratio (Liquidity: Current/
Quick/Cash Ratio; Solvency: Debt-to-Equity/Debt Ratio; Efficiency: Inventory Turnover/Receivables
Turnover/Asset Turnover/AR-AP Days/Inventory Holding Period/Cash Conversion Cycle; Profitability:
Gross/Net Profit Margin/ROA/ROE), all derived from Balance Sheet/Income Statement figures — computed
directly from `BalanceSheetQueryHandler`/`IncomeStatementQueryHandler`'s own internals (reused, not
re-derived from raw GL), per the kickoff's own instruction.

### 7. Permission-key derivation
- **CashFlowSummaryView** — Admin+Member, same bar as Phase 8a's three statements: a Bank/Cash
  rollup, no PAN/per-transaction exposure a Member can't already piece together from documents they
  can already view.
- **SalesRegisterView / PurchaseRegisterView** — Admin-only, same bar as every flat per-transaction
  register with PAN exposure (Phase 8b/8d/8e/8f precedent) — both factors independently justify it
  here (flat fact table, PAN column) with no tension to resolve.
- **StockAgeingView** — weighed against `InventoryLedgerView` (Phase 8a-era, Admin+Member): no PAN/
  contact exposure, a per-product rollup by ageing bucket, not a per-transaction fact table (each row
  is a Product×bucket aggregate, not one row per stock movement) — same shape class as
  `InventoryLedgerView`'s Stock Position screen, not Sales Master Report's flat shape. Admin+Member.
- **ProductProfitabilityView** — Admin-only. The one genuine judgment call: this report exposes
  per-product Cost Of Sales next to Sales in the same row — a direct margin/markup readout a Member
  with ordinary Sales.Invoice.View/Inventory.InventoryLedger.View access cannot reconstruct today
  (Invoice screens show Rate, not COGS; COGS unit cost is nowhere else user-visible). That's a
  meaningfully more sensitive exposure than Stock Ageing's quantity-only rollup, closer to Sales
  Master Report's "bulk margin-adjacent data" reasoning (Phase 8b) than InventoryLedgerView's — Admin-
  only.
- **RatioAnalysisView** — Admin+Member, same as Phase 8a: derived purely from Balance Sheet/Income
  Statement rollups already Admin+Member-visible, no new exposure.
- **Reporting Tags** — no new permission key. `ReportingTagCategoryView/Manage` and
  `ReportingTagOptionView/Manage` (Phase 2) already gate the category/option CRUD; attaching a tag
  to a transaction rides on that document type's own existing Edit permission (`QuotationEdit`/
  `InvoiceEdit`) — attaching a tag is a document-detail edit action, not a separate capability, and
  giving it its own key would let a user attach tags to a document they can't otherwise edit, which
  makes no sense.

## What shipped

**Backend.** `TransactionReportingTag` (document-level join, `configuration.TransactionReportingTags`,
unique on `(DocumentType, DocumentId, TagOptionId)`) plus `SetTransactionReportingTagsCommand`
(replace-the-whole-set, riding on that document type's own Edit permission) and
`GetTransactionReportingTagsQuery`, wired to `PUT`/`GET
/api/organizations/{id}/configuration/reporting-tags/{documentType}/{documentId}`. A shared
`ReportingTagFilter.ResolveMatchingDocumentIdsAsync` helper (OR semantics) threads through
`SalesRegisterQuery` only — the one report whose source documents (Invoice) are confirmed to carry
tags. Six new report query handlers, all `IRequirePermission`/`IOrganizationScoped` from the start:
`CashFlowSummaryQuery`, `SalesRegisterQuery`, `PurchaseRegisterQuery`, `StockAgeingQuery`,
`ProductProfitabilityQuery`, `RatioAnalysisQuery` — plus 6 new `Reports.*.View` permission keys
seeded through `RolePermissionConfiguration.HasData` (GUID tail `...119`–`...124`) and matching
`ReportSpreadsheetExporter` export methods, all wired to Minimal API endpoints across
`AccountingEndpoints`/`SalesEndpoints`/`PurchasingEndpoints`/`InventoryEndpoints`. One migration
(`Phase19ReportingTagsAndReports`), applied to the local dev DB.

**Frontend.** Six new Angular report pages (`cash-flow-summary-page`, `sales-register-page`,
`purchase-register-page`, `stock-ageing-page`, `product-profitability-page`, `ratio-analysis-page`),
routed and linked from the Organization Dashboard's report button grid. A shared
`ReportingTagsEditor` standalone component (`shared/reporting-tags/`) wired into both
Quotation-detail-page and Invoice-detail-page, matching the live Tigg reference's exact shape
("REPORTING TAGS ... Add/Edit ... No reporting tags"). `ConfigurationService` grew read-only
`listReportingTagCategories`/`listReportingTagOptions` methods (the category/option management CRUD
screen itself is a pre-existing gap this phase didn't build — see known limitations).

**Tests.** 15 new Application.UnitTests (242 → 257), all seeded through real Create/Approve command
handlers with hand-computable expected numbers (Phase 8b's report-test-suite discipline), covering:
Reporting Tag replace-not-append semantics + validation + permission-key resolution; Sales Register's
Invoice/CreditNote combination, tax-exempt/taxable split, and tag-filter narrowing (both the match
and the exclude-untagged-CreditNotes cases); Purchase Register's 4-bucket Capital/Import
classification including DebitNote-resolved-from-source-line; Stock Ageing's bucket placement and
exact reconciliation against `ProductStockPositionQuery`; Cash Flow Summary's document-classification
bucketing (Customer/Supplier Payment vs. JournalVoucher) and Starting-Balance roll-up; Ratio
Analysis's full 16-ratio computation against a hand-built Balance Sheet/Income Statement scenario;
Product Profitability's Sales/CostOfSales/GrossMargin from real FIFO-consumed Invoice lines. Domain.UnitTests
unchanged (125). `tsc --noEmit`, `ng build`, `ng test --watch=false` (7 specs, unchanged) all clean.

**Manual E2E.** Full curl + cookie-jar pass against a fresh Organization (seeded: Warehouse, Product
Category/Unit, Contact Group, Customer/Supplier contacts, a Goods Product, full Chart of Accounts
with TenantSettings accounting/inventory defaults, a ReportingTagCategory with two Options) — real
Approved PurchaseBill (10 units @ 40) and Invoice (5 units @ 100), a Customer Payment, two Journal
Vouchers (capital injection, rent-on-credit), one tag attached to the Invoice. Every report's numbers
independently hand-verified against this seed (see exit-criteria checklist below); Sales Register's
tag filter narrowed to exactly the tagged Invoice and excluded the untagged one; Stock Ageing's 5.00
total reconciled exactly against Stock Position's Balance; all 6 exports produced valid, non-corrupt
`.xlsx` files (verified via `file` magic-byte detection) with zero exceptions in the server console.
A second real user (registered, DB-activated, invited as Member) proved all 6 new permission keys
live: **403** naming the exact key for the three Admin-only reports (`Reports.SalesRegister.View`,
`Reports.PurchaseRegister.View`, `Reports.ProductProfitability.View`) and **200** for the three
Admin+Member reports (`Reports.CashFlowSummary.View`, `Reports.StockAgeing.View`,
`Reports.RatioAnalysis.View`) — six separate proofs, not one representative sample. Live browser
click-through (Sales Register, Stock Ageing, Quotation's Reporting Tags editor) confirmed pixel-correct
rendering against the same real data with zero console errors.

## Bugs hit and fixed

1. **Ratio Analysis's Inventory figure used the wrong GL account balance.** The first design used
   `TenantSettings.DefaultInventoryAccountId`'s GL balance for Current Ratio/Quick Ratio/Inventory
   Turnover's Inventory figure. Manual E2E caught it immediately: `PurchaseBillPostingRule` debits a
   *Purchase* (Expense) account, not Inventory (see that class's own doc comment, a Phase 6 design
   decision predating this phase) — the Inventory GL account only ever receives Invoice's own
   COGS-relief *credit*, so it runs permanently negative and never reflects real stock value. Fixed
   to sum `StockLedgerEntry.QuantityRemaining × UnitCost` instead, the same FIFO-layer valuation
   Stock Ageing/Product Profitability already use. Caught only by a live Trial Balance cross-check
   against a freshly seeded organization, not by the unit test (which used a Service-only scenario
   with no PurchaseBill/Inventory activity at all, so it never touched this code path) — worth
   noting as its own lesson: a report-handler unit test that avoids the exact GL account a live
   E2E pass happens to touch can pass green while the handler is still wrong.
2. **Report-handler unit tests using fixed calendar dates against `GlLine`/`GlJournalEntry` fail
   silently-in-spirit (return all-zero results, not an exception).** `GlJournalEntry.PostedAt` is
   stamped from the real clock at Approve() time (`GlDateBoundary`'s own doc comment, a Phase 8a
   decision), not a document's own business `Date` — every existing GL-report test already works
   within this constraint by using `DateOnly.FromDateTime(DateTime.UtcNow)`-relative query windows,
   not fixed years-in-the-future dates. `CashFlowSummaryQueryHandlerTests` was first written with
   fixed `2026-01`/`2026-02` dates (matching the seed's own business dates) and every assertion came
   back zero — not a build error, not a thrown exception, just quietly wrong numbers, since the
   query's `[fromUtc, toUtc]` window and every posted entry's real `PostedAt` never overlapped.
   Fixed by rewriting the test to bracket `DateTime.UtcNow`, matching every prior GL-report test's
   own convention (`TrialBalanceQueryHandlerTests` et al.) — a convention this phase should have
   grepped for before writing new GL-report tests, not rediscovered by a failing assertion.

## Known limitations
- Sales Register's 4 Export columns are always empty — Invoice has no Export flag until Phase 23.
- Product Profitability's Production Cost/Consumption columns are always zero — Manufacturing is
  unbuilt (architecture-spec.md §6, open scope question). Additional Cost is always zero — Cost
  Terms/landed cost is Phase 20 scope.
- Cash Flow Summary has no period-over-period Compare, matching the pre-existing gap in Trial
  Balance/Balance Sheet/Income Statement (FR-9.1 names it, Phase 8a never built it) — not new scope.
- Reporting Tag multi-select filter semantics (AND vs OR) default to OR without independent live
  confirmation of the reference product's own drawer behavior — see Decision #1.
- Sales/Purchase "Return Register" variants and "Migrated" variants are out of scope (see Decisions
  #3 and the roadmap's own Phase 21 deferral for Migrated).
- **The Reporting Tag Category/Option management screen (Configurations > Reporting Tags) doesn't
  exist in the Angular frontend** — discovered mid-phase. `ReportingTagCategory`/`ReportingTagOption`
  CRUD commands and permission keys have existed since Phase 2, and CLAUDE.md's own Phase 2 summary
  lists "ReportingTags" among the lookups that got Angular screens, but `configuration.service.ts`
  never actually grew the corresponding methods — only this phase's read-only
  `listReportingTagCategories`/`listReportingTagOptions` exist now. Without that screen, a tenant
  can only create tags via direct API calls, not through the UI; flagged via `spawn_task`, not fixed
  here (out of this phase's explicit scope, which is attach/detach + report filtering).
- ~~**Net Profit Margin/ROA/ROE can read confusingly negative for a tenant that uses both a
  "Purchase" Expense account and a separate "COGS" Expense account.**~~ **Fixed** by
  `PurchaseBillAccountResolver`'s post-Phase-19 rework (see `docs/phase-7-status.md`'s addendum and
  CLAUDE.md's "Known gotchas" `DefaultInventoryAccountId` entry): a Goods line now debits
  `TenantSettings.DefaultInventoryAccountId` (Asset), not a Purchase Expense account, so the only
  Expense recognition left for a sold Goods unit is Invoice's existing COGS relief — recognised
  exactly once. `IncomeStatementQueryHandler`/`RatioAnalysisQueryHandler` needed no changes.
  Regression coverage: `IncomeStatementQueryHandlerTests.Handle_recognises_goods_cost_as_cogs_only_once_not_also_as_purchase_expense`
  (buy 10 @ 40, sell 5, asserts Net Income is the correct 300, not the double-counted -100) plus
  `ApprovePurchaseBillCommandHandlerTests`'s three GL-posting-level cases.
