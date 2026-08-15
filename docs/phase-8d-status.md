# Phase 8d status — TDS Report

**Status: COMPLETE.** One pure-read query handler (`TdsReportQuery` under
`Application.Purchasing.Queries.TdsReport`) produces a deductee-wise TDS register: one row per
Approved PurchaseBill/Expense/DebitNote carrying a non-null `TdsTypeId` (rows with `TdsTypeId ==
null` mean no deduction happened and are excluded, not shown as a zero-TDS row), filtered on each
document's own business `Date` field within `[FromDate, ToDate]` — the same non-GL reasoning
Phase 8b/8c established. No new commands, aggregates, or schema tables beyond a
permission-seed-only migration (`AddPhase8dReportPermissions`), matching Phase 8a–8c's "pure read"
framing. Unlike VAT Summary Report's bucketed rollup, this is a flat register — a real TDS return
is filed deductee-wise, so the report needs each Contact's name/PAN and each document's own
withheld amount, not a netted total (scope decision below). It lives under
`Application.Purchasing`, not `Application.Accounting` like `VatSummaryReportQuery` — TDS in this
codebase only ever originates on the purchase side (`PurchaseBill`/`Expense`/`DebitNote`), never
Sales, so there's no cross-module straddle to resolve the way VAT Summary had.

Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below): a fresh
Admin set up a Chart of Accounts (VAT Receivable, Accounts Payable, TDS Payable, Purchase Expense,
Office Expense), a Warehouse, a Supplier carrying a PAN, a Service Product (VAT-rated, no
Inventory/COGS accounts needed since this phase deliberately used a Service product, unlike
Phase 8c's Goods scenario), and a `TDS-15` TDS Type at 15%. They approved a PurchaseBill (10 ×
100 @ 13% VAT, gross 1130, TDS 150), approved an Expense (500, TDS 75), and converted 3 of the
PurchaseBill's 10 units to a DebitNote (gross 339, TDS 45) and approved it. The TDS Report page
showed exactly three rows — the PurchaseBill and Expense as positive entries, the DebitNote as its
own **negative-signed** row (`-339.00` / `-45.00` / `-294.00`), never netted into the PurchaseBill's
row — and a totals footer of Gross 1291.00 / TDS 180.00 / Net 1111.00, matching hand arithmetic
exactly (`1130 - 339 + 500 = 1291`, `150 - 45 + 75 = 180`). A second user invited as Member hit the
same report through the real UI and got the real API's `403` with `Reports.TdsReport.View` in the
error message, confirming the Admin-only grant.

## Roadmap Phase 8d exit criteria — final status

- [x] `TdsReportQuery(OrganizationId, FromDate, ToDate)` — one Application-layer query handler
      under `Application.Purchasing.Queries.TdsReport`, no new commands/aggregates/migrations
      beyond a permission-seed one
- [x] One row per Approved PurchaseBill/Expense/DebitNote with a non-null `TdsTypeId`, filtered on
      each document's own `Date` within `[FromDate, ToDate]` — rows with `TdsTypeId == null` are
      excluded entirely, not shown zero-filled
- [x] Row fields: `ContactId`/`ContactCode`/`ContactName`/`ContactPan`, `DocumentType`, `EntryNo`
      (Approve-time `Code`), `EntryDate`, `TdsTypeCode`/`TdsTypeName`/`TdsRatePct`, `GrossAmount`
      (`GrandTotal` before TDS — the sum of each line's `Amount + VatAmount`), `TdsAmount`,
      `NetPayableAmount` (computed property, `GrossAmount - TdsAmount`)
- [x] DebitNote rows carry a **negative-signed** `GrossAmount`/`TdsAmount` (and thus a negative
      `NetPayableAmount`) rather than being netted into their source PurchaseBill's row — explicit
      call made and documented (scope decision #1)
- [x] Totals footer: `TotalGrossAmount`, `TotalTdsAmount` (both computed properties, `Rows.Sum(...)`
      — negative DebitNote rows sum straight in, so the footer nets correctly without special-casing)
- [x] Permission key `Reports.TdsReport.View`, **Admin-only** (Member gets an explicit
      `IsGranted=false` denial row) — explicit judgment call made and documented (scope decision #2)
- [x] Angular: `tds-report-page` under `organizations/:id/reports/tds-report`, date-range picker
      only (no Contact/TDS-Type filters — a filing-period register, same shape decision as VAT
      Summary Report), one flat table plus a Gross/TDS/Net totals footer, dashboard nav link next to
      VAT Summary Report's
- [x] Unit tests: `TdsReportQueryHandlerTests` (2), against the InMemory `TestAppDbContext`, seeding
      real Contact/Warehouse/ProductCategory/UnitOfMeasurement/Product/Account/TenantSettings/TdsType
      rows and real PurchaseBill/Expense/DebitNote documents through their real Create/Approve
      command handlers (same pattern as Phase 8b/8c's report tests, including the
      shared-`FakeDocumentNumberGenerator`-per-test discipline) — covers date-range filtering,
      Approved-only filtering (a Draft PurchaseBill with TDS is excluded), a PurchaseBill with
      `TdsTypeId == null` being excluded entirely rather than shown as a zero row, and the
      DebitNote-reversal negative-signed-row behavior netting the totals footer correctly
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged — no Domain changes this phase;
      Application.UnitTests 124 — 2 new + 122 pre-existing, all green) and `ng build`/`ng test`
      (7 pre-existing specs green, no new Angular specs — matching every prior Phase 8 report page,
      none of which have their own spec file in this codebase) both pass. `Api.IntegrationTests` was
      **not** re-run this phase — Docker Desktop wasn't running in this session, the same situation
      Phase 5 hit; the migration is purely additive (`InsertData` only) and the new endpoint follows
      an established minimal-API pattern, so this is a documented gap, not a skipped check performed
      silently.
- [x] Manual E2E against real API/DB/browser (see summary above), including the Admin-only
      permission gate confirmed via a real `403` for an invited Member

## Scope decisions

1. **DebitNote rows are listed as their own negative-signed row, not netted into their source
   PurchaseBill's row.** The brief flagged this explicitly as a call to make and document. Phase 8c's
   VAT Summary Report nets CreditNote/DebitNote lines into their source document's `VatRate` bucket
   because that report's whole point is a rate-bucket rollup — no individual document ever appears in
   it. A TDS register is different: a real Nepal TDS return is filed against specific deductee
   entries, and the tax authority needs to see that a reversal actually happened in the filing period,
   not have it silently absorbed into an earlier row a filer might already have referenced by its own
   `EntryNo`. Silently reducing the PurchaseBill's row instead would also make the PurchaseBill's own
   `TdsAmount` field (150) disagree with what the report shows for it (105) with no visible
   explanation — a correctness-adjacent readability problem, not just a stylistic one. Listing the
   reversal as its own row keeps every row's numbers matching the source document's own persisted
   `TdsAmount` exactly, and the totals footer still nets correctly because
   `Rows.Sum(r => r.GrossAmount)`/`Rows.Sum(r => r.TdsAmount)` simply add the negative row in.
2. **`Reports.TdsReport.View` is Admin-only.** The brief asked to weigh two separate factors rather
   than default to either precedent: (a) this is a flat per-contact fact table, the same shape
   distinction that made Phase 8b's Master Reports Admin-only over Phase 8a's Admin+Member rollups
   (see `PermissionKeys.SalesMasterReportView`'s doc comment); and (b) this is the **first** report in
   this codebase to surface a Contact's PAN — a real government tax-ID field, categorically different
   from Rate/margin-adjacent business data. Phase 8c's VAT Summary Report earned its Admin+Member
   grant specifically *because* its rollup shape meant neither factor applied — no per-transaction
   fact table, no PAN. Here, unlike Phase 8c, both factors independently argue the same direction:
   either one alone would justify Admin-only under the Phase 8b precedent, and there's no rollup
   argument pulling the other way to create the kind of tension Phase 8c had to resolve. So Admin-only
   was the only defensible call, not a coin flip.
3. **Lives under `Application.Purchasing`, not `Application.Accounting`.** `VatSummaryReportQuery`
   lives under `Application.Accounting` specifically because it straddles both Sales and Purchasing
   equally, with no more-natural single owning module (see its own doc comment / phase-8c-status.md).
   TDS has no such straddle in this codebase — `TdsTypeId`/`TdsAmount` exist only on
   `PurchaseBill`/`Expense`/`DebitNote`, never on any Sales document (`Invoice`/`CreditNote` carry no
   TDS fields at all, confirmed by `docs/phase-6-status.md`'s TDS scope decisions) — so
   `Application.Purchasing` is the correct home, the same way `PurchaseMasterReportQuery` lives under
   `Application.Purchasing` rather than a shared reports namespace.

## Bugs hit and fixed along the way

None in the shipped query handler, endpoint, permission wiring, or EF Core migration —
`dotnet build`/`dotnet test` passed clean on the first attempt for the Application-layer
query/handler and both `TdsReportQueryHandlerTests`, and the migration scaffolded as a clean
`InsertData`/`DeleteData` pair with no manual reordering needed (a permission-seed-only migration
carries none of the column-ordering risk `docs/phase-1c-status.md`'s bug #1 or
`docs/phase-2-status.md`'s bug #6 describe).

The **Manual E2E pass surfaced one browser-automation mistake worth recording**, not a codebase
defect: `Contact.Type` is immutable once created (`docs/phase-3-status.md`'s scope decision), and
the New Contact form's Type radio group defaults to Customer. A first attempt at creating the test
Supplier clicked the Supplier radio at stale on-screen coordinates before the click actually
registered against the live DOM, silently leaving Type on its Customer default — nothing client- or
server-side flags this, since Customer is a perfectly legitimate value, and the mistake was only
caught by directly querying `contacts.Contacts` and seeing `Type = Customer` on a row named "Global
Supplies Pvt Ltd". The fix was to create a second Contact rather than try to edit Type after the
fact (immutable, per Phase 3). Worth remembering for whoever next drives Contact creation through
browser automation: verify a radio-group selection actually landed (e.g. via a JS
`querySelectorAll('input[type=radio]')` check) before submitting, rather than trusting the click
coordinate alone, especially right after page navigation when the DOM may still be settling.

## What's next

**Phase 8+** (see `roadmap.md`): the two Annex reports (Annex 13, Annex 5) remain the last
Nepal-specific statutory reports in the Reports sequence. Annex 13 in particular still carries its
own unresolved open item (the Capital-vs-Others purchase-expenditure classification's UI location,
last flagged unresolved in `erp-module-scan.md`/`architecture-spec.md` §4.5, and specifically *not*
touched by this phase even though `PurchaseBillLine.ExpenditureClassification` — the very field
Annex 13 will need — was visible and exercised in this phase's own Purchase Bill form). After the
statutory reports, Customer/Supplier Ageing & Statement are next in the Reports sequence per the
roadmap.
