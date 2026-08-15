# Phase 8c status — VAT Summary Report

**Status: COMPLETE.** One pure-read query handler (`VatSummaryReportQuery` under
`Application.Accounting.Queries.VatSummaryReport`) produces a standard Nepal VAT-return-style
summary — net Sales/Purchase and Output/Input VAT bucketed by the three `VatRate` values
(`NoVat`/`ZeroVat`/`ThirteenPercentVat`), netting each side's reversal document (CreditNote against
Invoice, DebitNote against PurchaseBill) into the same bucket rather than listing them as their own
rows. No new commands, aggregates, or schema tables beyond a permission-seed-only migration,
matching Phase 8a/8b's "pure read" framing. Like Phase 8b's Master Reports (and unlike Phase 8a's
three GL reports), both sides filter on each document's own business `Date` field, not
`GlJournalEntry.PostedAt` (scope decision #1). The new `Reports.VatSummary.View` permission key is
**Admin+Member**, a deliberate return to Phase 8a's precedent rather than Phase 8b's Admin-only one
— this report's output is a rollup (six numbers total), not a flat per-transaction fact table (scope
decision #2). erp-module-scan.md never opened this screen in the hands-on pass — VAT Summary Report
is only named in the Tax Report category list — so this shape is a designed-not-observed standard
VAT-return structure, documented explicitly rather than presented as a reproduction of Tigg's actual
screen (scope decision #3).

Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below): a fresh Admin
set up a Chart of Accounts (including Inventory/COGS accounts for Phase 7's Goods-line requirement),
a Customer and Supplier, a Warehouse, and a VAT-rated Goods Product, then approved two PurchaseBills
(one `ThirteenPercentVat`, one `NoVat`), two Invoices (one of each rate), a standalone CreditNote
against the `ThirteenPercentVat` bucket, and a standalone DebitNote against the same bucket. The VAT
Summary Report page showed all three `VatRate` buckets on both sides, correctly netted
(`ThirteenPercentVat` sales: 300 Invoice − 100 CreditNote = 200 net / 39 − 13 = 26 Output VAT;
`ThirteenPercentVat` purchases: 400 PurchaseBill − 100 DebitNote = 300 net / 52 − 13 = 39 Input VAT;
`NoVat` sales 100/0, `NoVat` purchases 300/0), Total Output VAT 26.00, Total Input VAT 39.00, and a
**Net VAT Refundable: 13.00** badge — the negative `NetVatPayable` (26 − 39 = −13) surfaced as a
refund, not clamped to zero, confirmed both in the API response and the UI's conditional badge.
Hand arithmetic matched the report exactly.

## Roadmap Phase 8c exit criteria — final status

- [x] `VatSummaryReportQuery(OrganizationId, FromDate, ToDate)` — one Application-layer query
      handler, no new commands/aggregates/migrations beyond a permission-seed one
- [x] Sales side: net Approved Invoice lines minus Approved CreditNote lines within
      `[FromDate, ToDate]` (each document's own `Date`), grouped by `VatRate`, giving
      `NetSalesAmount`/`OutputVatAmount` per bucket — all three `VatRate` values always present,
      zero-filled when there's no activity, matching a real VAT-return form's fixed row structure
- [x] Purchase side: mirror shape, Approved PurchaseBill lines minus Approved DebitNote lines,
      giving `NetPurchaseAmount`/`InputVatAmount` per bucket
- [x] Totals: `TotalOutputVat`, `TotalInputVat`, `NetVatPayable` (computed property,
      `TotalOutputVat - TotalInputVat`) — sign surfaced as-is, confirmed negative
      (refundable/carried-forward credit) in the Manual E2E pass, not clamped
- [x] Permission key `Reports.VatSummary.View`, **Admin+Member** (both `IsGranted=true`) — explicit
      judgment call made and documented (scope decision #2), diverging from Phase 8b's Admin-only
      Master Reports
- [x] Angular: `vat-summary-report-page` under `organizations/:id/reports/vat-summary`, date-range
      picker only (no Contact/Product/Warehouse filters — a filing-period summary, not a
      transaction register), two small tables (Sales-by-VatRate, Purchase-by-VatRate) plus a Net
      VAT Payable/Refundable summary line, dashboard nav link
- [x] Unit tests: `VatSummaryReportQueryHandlerTests` (2), against the InMemory `TestAppDbContext`,
      seeding real Contact/Warehouse/ProductCategory/UnitOfMeasurement/Product/Account/
      TenantSettings rows and real Invoice/CreditNote/PurchaseBill/DebitNote documents through
      their real Create/Approve command handlers (same pattern as Phase 8b's Master Report tests,
      including the shared-`FakeDocumentNumberGenerator`-per-test discipline) — covers date-range
      filtering, Approved-only filtering (a Draft invoice and an out-of-range invoice both
      excluded), CreditNote/DebitNote netting into their source's `VatRate` bucket rather than
      needing their own bucket, and a negative `NetVatPayable` surfacing correctly when Input VAT
      exceeds Output VAT
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67, Application.UnitTests 122 — 2 new + 120
      pre-existing, all green; Api.IntegrationTests 4, re-run this phase since Docker Desktop was
      already running — no failures, confirming the purely-additive permission-seed migration and
      new minimal-API GET endpoint don't disturb anything) and `ng build`/`ng test` (7 pre-existing
      specs green) all pass
- [x] Manual E2E against real API/DB/browser (see summary above)

## Scope decisions

1. **Filters on each document's own business `Date` field (`Invoice.Date`, `CreditNote.Date`,
   `PurchaseBill.Date`, `DebitNote.Date`), not `GlJournalEntry.PostedAt`** — the same call Phase 8b
   made for its Master Reports, and the same reasoning: this is a document-register aggregate ("what
   did the Sales/Purchase team record as having happened in this filing period"), not a GL report,
   and every relevant aggregate already carries the field, so there's no cross-cutting change needed
   to use it (unlike Phase 8a's GL reports, which would need a new field threaded through every
   `IGlPostingRule<TDocument>` call site to get the same result). A VAT return filed against the
   wrong period because a document posted late is a real filing-accuracy problem, so keying off the
   date a user actually entered — not an Approve-time posting timestamp — is the correct choice here,
   not just the convenient one.
2. **`Reports.VatSummary.View` is Admin+Member** (both roles get an explicit `IsGranted=true` row),
   returning to Phase 8a's precedent rather than continuing Phase 8b's Admin-only one. Explicit
   judgment call per the brief's own request to weigh this rather than default silently either way.
   The discriminator Phase 8b actually used wasn't "this data touches Sales/Purchase documents" — it
   was "flat unaggregated fact table, sliceable by Contact/Product/Warehouse, exposing every
   individual transaction's Rate in bulk" versus "rollup." `VatSummaryReportQuery`'s output is
   unambiguously the latter: six numbers for the entire query period (three `VatRate` buckets ×
   Net/VAT amount, per side, plus three totals), with no way to recover any single transaction's
   Rate, Customer, Supplier, or Product from it — the same shape distinction that earned Trial
   Balance/Balance Sheet/Income Statement their Admin+Member grant in Phase 8a. A Member with
   `Sales.Invoice.View`/`Purchasing.PurchaseBill.View` can already see every line that feeds this
   report's totals one document at a time; this report only makes the *sum* faster to read, it
   doesn't expose anything new. The brief itself noted this report is "closer to Phase 8a's rollup
   shape than 8b's flat-fact-table shape" — that similarity, not the fact that it happens to be
   tax-filing-adjacent, is what should drive the permission bar. (A tenant that genuinely wants VAT
   filing numbers restricted to Admin-only can still do so once the Role Reference full editor from
   the roadmap's later phases exists — same deferral Phase 8a/8b both used for their own
   per-tenant-granularity questions.)
3. **VAT Summary Report's row/column shape is a designed standard Nepal VAT-return structure, not a
   reproduction of a confirmed Tigg screen.** The brief flagged this explicitly: unlike Sales/
   Purchase Master Report, whose exact live shape Phase 8b's hands-on pass confirmed field-by-field,
   `erp-module-scan.md`'s Reports Module section only *names* "VAT Summary Report" in the Tax Report
   category card list (line 264) — it was never opened and walked through in the scan's hands-on
   pass, unlike Annex 13 (which at least got a partial field list before hitting its own open
   question, per architecture-spec.md §4.5). Inventing a shape and presenting it as if it matched a
   screen nobody actually looked at would be worse than admitting the gap, so this phase designs a
   standard structure instead: three `VatRate` buckets per side (the enum this codebase already has,
   not a Nepali tax-code-specific rate table), Net Amount + VAT Amount per bucket, and
   Output/Input/Net totals — the minimum a Nepal VAT return actually needs (Schedule 3/9-style
   output-vs-input netting), deliberately not extended with return-specific fields (Tax Period
   dropdown, PAN number header, prior-period-carried-forward-credit line) that would imply a fidelity
   to a real filing form this phase never attempted. If Tigg's actual VAT Summary Report screen is
   ever confirmed (a future scan pass opening it), reconcile this shape against it then rather than
   guessing further now.
4. **All three `VatRate` buckets are always present on both sides, zero-filled when a rate has no
   activity in the period**, rather than only emitting buckets that have data (the shape Phase 8b's
   Master Reports use for their rows, since a document register's row count is meaningful signal, not
   noise). A VAT-return-style summary is read against a fixed mental model — "what's my 13% VAT,
   what's my 0%-rated activity, what's fully exempt" — so a bucket silently missing because it
   happened to be zero this period would read as a display bug rather than "no activity," the same
   reasoning Trial Balance's `Handle_lists_every_active_account_even_with_a_zero_balance` test already
   established for a different report in Phase 8a. Implemented via `Enum.GetValues<VatRate>()` driving
   the bucket list rather than a `GROUP BY` over only the rows present.

## Bugs hit and fixed along the way

None in the shipped query handler, endpoint, or permission wiring — `dotnet build`/`dotnet test`
passed clean on the first attempt for all three (Application-layer query/handler, permission-seed
migration, minimal-API endpoint), and both `VatSummaryReportQueryHandlerTests` passed without
needing a fix once the `Seed` helper compiled (a straightforward combination of
`SalesMasterReportQueryHandlerTests`' and `PurchaseMasterReportQueryHandlerTests`' existing seed
patterns into one org with both sides' accounts, not a new pattern).

The **Manual E2E pass surfaced two pieces of pre-existing Phase 7 product behavior** worth recording
for whoever next seeds test data by hand or by script against a fresh organization — neither is a
defect, both are working as designed, but both cost a retry during setup:

1. Approving an Invoice for a Goods-type, `TrackInventory=true` Product against a warehouse with
   insufficient stock returns HTTP 422 (`FifoStockAvailabilityPolicy`'s Warn path, not this phase's
   code) rather than silently succeeding — the fix is to approve a PurchaseBill first (or pass
   `overrideWarning: true`), not to treat the 422 as an error in the new report.
2. Approving an Invoice for a Goods-type, `TrackInventory=true` Product without
   `TenantSettings.DefaultInventoryAccountId`/`DefaultCogsAccountId` set returns HTTP 409 (Phase 7's
   COGS-leg posting requirement) — the fix is to add Inventory/COGS accounts and set them via the
   Accounting Defaults endpoint before approving any Goods invoice, which a Services-only test
   scenario (like Phase 8a/8b's own seed data, which used `ProductType.Service`) never needs to hit.
   This phase's own manual E2E deliberately used a Goods Product (closer to a real Nepali retail SME's
   VAT-return scenario than a Service would be), which is why it surfaced here for the first time in
   the Reports sequence.

## What's next

**Phase 8+** (see `roadmap.md`): TDS Report and the two Annex reports (Annex 13, Annex 5) remain the
next Nepal-specific statutory reports, explicitly kept out of this phase's scope per the brief — in
particular, Annex 13 still carries its own unresolved open item (the Capital-vs-Others
purchase-expenditure classification's UI location, last flagged unresolved in
`erp-module-scan.md`/`architecture-spec.md` §4.5) that this phase deliberately did not touch or try
to resolve as a side effect. After the statutory reports, Customer/Supplier Ageing & Statement are
next in the Reports sequence per the roadmap. One open item carried forward from this phase: if
Tigg's actual VAT Summary Report screen is ever confirmed via a future scan pass, reconcile this
phase's designed shape (scope decision #3) against it and adjust if they diverge in a way that
matters (e.g., a Tax Period selector instead of a raw date range, or additional statutory columns).
