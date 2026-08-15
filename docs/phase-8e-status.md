# Phase 8e status — Annex 13 Report

**Status: COMPLETE.** One pure-read query handler (`AnnexThirteenReportQuery` under
`Application.Purchasing.Queries.AnnexThirteenReport`) produces a per-Contact Annex 13 rollup: one
row per Contact with any Approved Sales or Purchase activity in the period, netting Invoice/CreditNote
(Sales) and PurchaseBill/Expense/DebitNote (Purchase) activity into six buckets — Service Purchase
Capital/Others, Goods Purchase Capital/Others, Service Sales, Goods Sales — the confirmed field list
from `erp-module-scan.md` line 279's Tax Report category card, filtered to rows whose total activity
is `>= ThresholdAmount` (100,000 NPR default, editable per query). No new commands, aggregates, or
schema tables beyond a permission-seed-only migration (`AddPhase8eReportPermissions`), matching every
prior Phase 8 report's "pure read" framing. It lives under `Application.Purchasing`, not
`Application.Accounting` — same reasoning as `TdsReportQuery` (`ExpenditureClassification`, the
Capital-vs-Others split this report needs, only exists on the purchase side).

Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below): a fresh Admin
set up a Chart of Accounts (Accounts Receivable/VAT Receivable/Inventory, Accounts Payable/VAT
Payable, Sales Revenue, Purchase Expense/Cost of Goods Sold/Office Expense), a Warehouse, a Goods
Product and a Service Product, and three Contacts (a Customer, and two Suppliers — one with a 5,000
Opening Balance, one deliberately kept small). They approved a PurchaseBill with four lines (one per
bucket: Goods Capital 2,000, Goods Others 500, Service Capital 1,000, Service Others 500), a DebitNote
reversing 500 of the Goods-Capital line, an Expense of 300 (bucketed as Service Others), a small
PurchaseBill on the second Supplier (200, deliberately under any reasonable threshold), an Invoice
with a Goods line (1,000) and a Service line (500), and a standalone CreditNote reversing 200 of the
Goods line. Querying the Annex 13 Report through the real UI against the real API/DB with
`ThresholdAmount=1000` returned exactly two rows matching hand arithmetic exactly — Acme Traders
(Service Sales 500.00, Goods Sales 800.00, Total Activity 1,300.00, Closing Balance 1,300.00) and
Global Supplies Pvt Ltd (Service Purchase Capital 1,000.00, Service Purchase Others 800.00, Goods
Purchase Capital 1,500.00, Goods Purchase Others 500.00, Total Activity 3,800.00, Closing Balance
8,800.00 = 5,000 Opening + 3,800) — with the third, small-activity Supplier correctly excluded by the
threshold. A second user invited as Member hit the same report through the real UI and got the real
API's `403` with `Reports.AnnexThirteen.View` in the error message, confirming the Admin-only grant.

## Roadmap Phase 8e exit criteria — final status

- [x] `AnnexThirteenReportQuery(OrganizationId, FromDate, ToDate, ThresholdAmount = 100000m)` — one
      Application-layer query handler under `Application.Purchasing.Queries.AnnexThirteenReport`, no
      new commands/aggregates/migrations beyond a permission-seed one
- [x] One row per Contact with any Approved Sales/Purchase activity in `[FromDate, ToDate]`, filtered
      on each document's own business `Date` field, not `GlJournalEntry.PostedAt` — same
      document-register reasoning as every Phase 8 report since Phase 8b
- [x] Row fields: `ContactId`/`ContactCode`/`ContactPan`/`ContactName`/`ContactType`,
      `OpeningBalance`, `ServicePurchaseCapital`, `ServicePurchaseOthers`, `GoodsPurchaseCapital`,
      `GoodsPurchaseOthers`, `ServiceSales`, `GoodsSales`, plus two computed properties —
      `TotalActivity` (the six buckets summed) and `ClosingBalance` (`OpeningBalance + TotalActivity`)
- [x] Threshold filter applied last, against `TotalActivity`, after every bucket has already netted
      each document type against its own reversal (Invoice−CreditNote, PurchaseBill+Expense−DebitNote)
- [x] Capital-vs-Others bucketing resolved from `PurchaseBillLine.ExpenditureClassification`
      directly for PurchaseBill lines, and via the source PurchaseBill line's own classification
      (matched by the established `(ProductId, Rate, VatRate)` triple) for a DebitNote reversing one
      — explicit call made and documented (scope decision #1)
- [x] Expense activity (no `ProductId`, no `ExpenditureClassification`) bucketed as
      `ServicePurchaseOthers` — explicit call made and documented (scope decision #2)
- [x] Opening/Closing Balance kept as an explicitly-documented approximation, not a real ledger
      balance — `Contact.OpeningBalance` as-is, `ClosingBalance = OpeningBalance + TotalActivity`,
      Payment allocations excluded — explicit call made and documented (scope decision #3)
- [x] Permission key `Reports.AnnexThirteen.View`, **Admin-only** (Member gets an explicit
      `IsGranted=false` denial row) — explicit judgment call made and documented (scope decision #4)
- [x] Angular: `annex-thirteen-report-page` under `organizations/:id/reports/annex-thirteen`,
      date-range picker plus an editable `ThresholdAmount` input (default 100,000), one flat table,
      no totals footer (each row is already a per-Contact total), dashboard nav link next to TDS
      Report's
- [x] Unit tests: `AnnexThirteenReportQueryHandlerTests` (2), against the InMemory `TestAppDbContext`,
      seeding real Contact/Warehouse/ProductCategory/UnitOfMeasurement/Product(Goods+Service)/
      Account/TenantSettings rows and real Invoice/CreditNote/PurchaseBill/Expense/DebitNote
      documents through their real Create/Approve command handlers (same pattern as Phase 8b–8d) —
      covers Goods-vs-Service and Capital-vs-Others bucketing including a DebitNote reversal resolved
      back to its source line's classification, Expense's Service-Others bucketing, the Closing
      Balance formula, the threshold filter (a Contact exactly at threshold included, one just under
      excluded entirely), Approved-only filtering (a Draft PurchaseBill excluded), and date-range
      filtering
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged — no Domain changes this phase;
      Application.UnitTests 126 — 2 new + 124 pre-existing, all green; `Api.IntegrationTests` 4,
      re-run this session since Docker Desktop was running — all green, closing the gap Phase 8d
      left open) and `ng build`/`ng test` (7 pre-existing specs green, no new Angular specs — matching
      every prior Phase 8 report page) all pass
- [x] Manual E2E against real API/DB/browser (see summary above), including the Admin-only permission
      gate confirmed via a real `403` for an invited Member, both via direct API call and through the
      actual report page's error banner

## Scope decisions

1. **Capital-vs-Others bucketing for a DebitNote line resolves from its source PurchaseBill line's
   own classification, matched by the exact `(ProductId, Rate, VatRate)` triple.** `DebitNoteLine`
   carries no `ExpenditureClassification` of its own (per that type's own doc comment — "a reversal
   doesn't need its own Annex 13 classification"), but a DebitNote reversing a PurchaseBill still
   needs to reduce the *correct* bucket, not an arbitrary default — reversing a Capital purchase and
   bucketing the reversal as Others would silently overstate both Capital and Others in the same
   period. This reuses the exact matching key `PurchasingValidation.GetPurchaseBillRemainingByLineAsync`
   already established for the conversion-cap enforcement (`docs/phase-6-status.md`'s bug #4), so no
   new matching convention was invented. A standalone DebitNote (no `PurchaseBill` referrer, or one
   whose referrer isn't a PurchaseBill) defaults to Others, the same default `ExpenditureClassification`
   itself documents. One defensive fix came out of this: two lines on the same PurchaseBill can
   legitimately share the same `(ProductId, Rate, VatRate)` key with *different* classifications (e.g.
   splitting one shipment's Capital portion from its Others portion) — a raw `ToDictionary` over that
   key would throw on the second line; the handler groups first and takes the first match instead,
   documented inline as a known simplification for that edge case rather than an error path.
2. **Expense activity is bucketed as `ServicePurchaseOthers`.** The brief flagged this as open:
   `ExpenseLine` carries neither a `ProductId` (so no `ProductType` to key Goods-vs-Service off of)
   nor an `ExpenditureClassification` of its own — despite that enum's own doc comment claiming it's
   "shared by `PurchaseBillLine` and `ExpenseLine`", which turned out to be stale/aspirational; only
   `PurchaseBillLine` actually carries the field (confirmed by grep across the whole `src/` tree — see
   this phase's research). Retrofitting `ExpenseLine` with the column was out of scope (this phase is
   permission-seed-migration-only, per the brief). Service was the natural bucket: Expense is
   inherently non-goods in this codebase — no Product, no Quantity, no inventory/stock impact, the
   same "account-based lines" shape that made it structurally distinct from PurchaseBill back in
   Phase 6. Others was the natural default within that: it's `ExpenditureClassification`'s own
   documented default, and there's no signal on an Expense line to justify calling it Capital instead.
3. **Opening/Closing Balance stay an explicitly-documented approximation, not a real ledger balance.**
   The brief flagged this as the real scope risk and pre-resolved the shape: `ContactStatementQuery`/
   `ContactOverviewQuery` (the real running-balance engine, `architecture-spec.md` §4.2) don't exist
   yet — that's a separate, later Ageing/Statement phase. `OpeningBalance` is `Contact.OpeningBalance`
   as-is (a known, already-persisted field, never re-derived). `ClosingBalance` is
   `OpeningBalance + TotalActivity` — the same six buckets the report already computes, summed —
   which works cleanly here specifically because a Contact is exclusively `Customer` or `Supplier`
   in this codebase (every Sales command's `SalesValidation.EnsureCustomerExistsAsync`-equivalent and
   every Purchasing command's `PurchasingValidation.EnsureSupplierExistsAsync` both hard-filter on
   `Contact.Type`, confirmed by grep), so only one "side" of the six buckets is ever nonzero for any
   given Contact — there's no cross-contamination to net against. Payment allocations are explicitly
   excluded, the same "revisit once the Ageing/Statement phase's balance engine exists" framing
   Phase 8a used for `PostedAt`-not-business-`Date`.
4. **`Reports.AnnexThirteen.View` is Admin-only.** The brief asked to weigh this report's actual shape
   explicitly rather than copy either precedent wholesale. Annex 13's output genuinely *is* a
   per-Contact rollup (six summed bucket numbers, not one row per transaction) — the same structural
   shape as Phase 8c's VAT Summary Report, which earned Admin+Member. But VAT Summary's rollup nets
   activity across *every* Contact into three anonymous `VatRate` buckets — no single party is ever
   named, so there's nothing PAN-adjacent or contact-identifying to expose even in aggregate. Annex 13
   is different: every row is pinned to one specific Contact's identity, including their PAN — the
   same PAN-exposure factor that made `TdsReportView` Admin-only (Phase 8d's scope decision #2) — and
   that factor isn't diluted by the rollup shape here, because the rollup still names the party. A
   rollup that identifies who it's about is a materially different exposure than one that doesn't, so
   the PAN factor alone was decisive even though the "flat per-transaction fact table" factor (Phase
   8b/8d's other reason) doesn't apply here. Admin-only, not a coin flip between the two precedents.

## Bugs hit and fixed along the way

One handler-robustness fix, caught during design rather than by a failing test (see scope decision #1
above for the full reasoning): the first draft of the DebitNote-classification lookup used a raw
`ToDictionary` keyed on `(PurchaseBillId, ProductId, Rate, VatRate)` over every line of every
referenced source PurchaseBill. Two lines on the *same* PurchaseBill sharing that exact key with
different `ExpenditureClassification` values — a legitimate scenario, e.g. splitting one shipment's
Capital portion from its Others portion — would throw `ArgumentException: An item with the same key
has already been added` the moment a DebitNote reversed a PurchaseBill shaped that way. Fixed by
grouping first and taking the first match; not exercised by this phase's own test data (which
deliberately uses distinct Rates per line to avoid the collision in the *happy-path* assertions), so
this was caught by re-reading the code against the established `(ProductId, Rate, VatRate)`-matching
precedent's own known edge cases, not by a red test — worth remembering that `ToDictionary` on any
matching key reused from `PurchasingValidation.GetPurchaseBillRemainingByLineAsync` (which itself uses
`GroupBy`, precisely to avoid this) needs the same treatment, not a raw `ToDictionary`, anywhere else
this key gets reused in a future phase.

One pre-existing-workflow gotcha, hit during the Manual E2E pass, not a defect in this phase's own
code: `dotnet ef migrations add` scaffolds a migration file but does **not** apply it to the actual
local dev database — confirmed again exactly as `CLAUDE.md`'s own gotcha list already documents. The
first `curl` call against the new `/reports/annex-thirteen` endpoint as the freshly-created Admin
returned a `403` naming `Reports.AnnexThirteen.View` even though the seed data and permission
constants were all correct, because the `AddPhase8eReportPermissions` migration's `RolePermissions`
`InsertData` rows simply weren't in the database yet. Running a plain `dotnet ef database update` (no
`--connection` override) against the real dev DB immediately fixed it. Worth restating since this is
the second time in this codebase's history a phase's manual E2E pass has been the thing that actually
catches a forgotten `database update` — the automated test suites never touch the local dev database
at all (`Application.UnitTests` uses an InMemory `TestAppDbContext`, `Api.IntegrationTests` spins up
its own disposable Testcontainers instance), so a missed `database update` is invisible to
`dotnet test` and only surfaces the moment a real browser session hits the real local API.

## What's next

**Phase 8+** (see `roadmap.md`): Annex 5 Materialised View Report is the last Nepal-specific statutory
report in the Reports sequence (`erp-module-scan.md` line 264's Tax Report category list). After the
statutory reports, Customer/Supplier Ageing & Statement are next per the roadmap — and will be the
first phase to build the real running-balance engine (`ContactStatementQuery`/`ContactOverviewQuery`,
`architecture-spec.md` §4.2) that this phase's Opening/Closing Balance approximation is explicitly
deferring to, per scope decision #3 above.
