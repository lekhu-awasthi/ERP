# Phase 8f status — Annex 5 Report

**Status: COMPLETE.** One pure-read query handler (`AnnexFiveReportQuery` under
`Application.Sales.Queries.AnnexFiveReport`) produces a flat Sales bill register: one row per
Approved Invoice/CreditNote in the period, with Amount/TaxableAmount/TaxAmount/TotalAmount computed
from each document's own lines and IsActive mapped from Status. No new commands, aggregates, or
schema tables beyond a permission-seed-only migration (`AddPhase8fReportPermissions`), matching
every prior Phase 8 report's "pure read" framing. It lives under `Application.Sales`, not
`Application.Purchasing`/`Application.Accounting` — the confirmed live shape (see below) never
showed a Purchase-side row across all 74 rows returned for its default period, so this is a
Sales-only register.

Unlike every other Phase 8 statutory report, Annex 5 had **zero confirmed shape anywhere in this
repo** going in — `erp-module-scan.md` line 264 only names it in a Tax Report category card list,
never opened in the hands-on scan pass, and `architecture-spec.md` doesn't mention it beyond a
glossary entry. Per the brief, before writing any code the user was asked to either supply the real
field list or explicitly sign off on a speculative design. The user instead pointed at the live
reference product itself (`moonbeamtradingandsuppliers.tigguat.com/erp/#/reports/new/annex-5-report`)
and, after this agent declined to enter the login credentials the user offered (password entry is a
hard restriction regardless of explicit authorization — see "How the shape was obtained" below), the
user logged in themselves and the report's real, live-rendered shape was read directly through the
Browser tool. That live shape turned out to be **nothing like** the Capital Goods/Fixed Assets
annexure this agent had proposed as the speculative default (Nepal's IRD "Annex 5" naming convention
suggested that shape, reasonably, but it was wrong for this specific screen) — it's a flat Sales
bill audit log with IRD/CBMS sync-tracking columns, not a Capital-purchases register at all. See
scope decision #1.

Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below): a fresh Admin
set up a Chart of Accounts (Accounts Receivable/VAT Payable/Sales Revenue), a Warehouse, a Service
Product, and a Customer with a PAN, then approved two Invoices (one with a mixed 13%-VAT/no-VAT line
split, one pure no-VAT) and a standalone CreditNote, leaving a fourth Invoice in Draft. Querying the
Annex 5 Report through the real UI against the real API/DB for the period returned exactly the three
Approved documents — Invoice 0001 (Amount 1,500.00 / Taxable 1,000.00 / Tax 130.00 / Total 1,630.00),
Invoice 0002 (Amount 500.00 / Taxable 0.00 / Tax 0.00 / Total 500.00), CreditNote 0001 (Amount
200.00 / Taxable 200.00 / Tax 26.00 / Total 226.00) — matching hand arithmetic exactly, with the
Draft invoice correctly excluded and the CreditNote listed with its own **positive** values, not
netted against anything. A second user invited as Member hit the same report through the real UI and
got the real API's `403` with `Reports.AnnexFive.View` in the error message, rendered cleanly in the
page's own error banner, confirming the Admin-only grant.

## How the shape was obtained

Before any code was written, the brief required either a real field list from the user or explicit
sign-off on a speculative design. Given zero confirmed shape in this repo, this agent proposed a
Capital Goods/Fixed Assets Purchase annexure as the speculative default (a reasonable guess from
external IRD domain knowledge, explicitly flagged as unconfirmed) and asked the user to choose. The
user instead supplied the live reference-product URL and, in a follow-up turn, the literal login
credentials for the demo tenant. Per this agent's operating rules, entering a password into any site
is a hard-prohibited action that **stays prohibited even when the user explicitly supplies the
credentials or authorizes it directly** — so the credentials were not used, the rule was restated to
the user, and the user was asked to log in themselves. They did, in the same shared Browser-pane
session, after which this agent read the now-authenticated page directly (`get_page_text`,
`read_page`, a screenshot, and the report's own Export/Print options menu) to extract the real
column list and its actual data. This is the reason the shipped shape looks nothing like the
speculative default that was proposed first — the live screen corrected a wrong guess before any
code was written against it, exactly the scenario the brief's two-path question was designed to
catch.

## Roadmap exit criteria — final status

(No pre-existing Phase 8f section in `roadmap.md` — it only named "Annex 13/5" together generically
under Phase 8+. This checklist was derived from the brief and the live-shape discovery above.)

- [x] `AnnexFiveReportQuery(OrganizationId, FromDate, ToDate)` — one Application-layer query handler
      under `Application.Sales.Queries.AnnexFiveReport`, no new commands/aggregates/migrations
      beyond a permission-seed one
- [x] One row per Approved Invoice/CreditNote in `[FromDate, ToDate]`, filtered on each document's
      own business `Date` field, not `GlJournalEntry.PostedAt` — same document-register reasoning as
      every Phase 8 report since Phase 8b
- [x] Row fields mapped from the live-confirmed columns wherever this codebase has the underlying
      data (ContactId/ContactCode/ContactName/ContactPan, DocumentType, BillNo, BillDate, Amount,
      TaxableAmount, TaxAmount, TotalAmount, IsActive); every column needing a capability this
      codebase doesn't have is omitted entirely, not zero-filled (scope decision #2)
- [x] A CreditNote row carries its own positive Amount/TaxableAmount/TaxAmount/TotalAmount, not
      sign-flipped against its source Invoice — matches the live screen's own confirmed behavior
      (scope decision #3)
- [x] Lives under `Application.Sales`, not `Application.Purchasing`/`Application.Accounting` — the
      live screen never showed a Purchase-side row (scope decision #4)
- [x] Permission key `Reports.AnnexFive.View`, **Admin-only** (Member gets an explicit
      `IsGranted=false` denial row) — explicit judgment call made and documented (scope decision #5)
- [x] Angular: `annex-five-report-page` under `organizations/:id/reports/annex-five`, date-range
      picker only, one flat table, dashboard nav link next to Annex 13's
- [x] Unit tests: `AnnexFiveReportQueryHandlerTests` (3), against the InMemory `TestAppDbContext`,
      seeding real Contact/Warehouse/ProductCategory/UnitOfMeasurement/Product(Service)/
      Account/TenantSettings rows and real Invoice/CreditNote documents through their real
      Create/Approve command handlers (same pattern as every prior Phase 8 report) — covers mixed
      VAT-rate line bucketing (Amount vs TaxableAmount vs TaxAmount vs TotalAmount arithmetic), a
      CreditNote's own positive-valued row, Approved-only filtering, and date-range filtering
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged — no Domain changes this phase;
      Application.UnitTests 129 — 3 new + 126 pre-existing, all green; `Api.IntegrationTests` 4, run
      with Docker Desktop running this session — all green) and `ng build`/`ng test` (7 pre-existing
      specs green, no new Angular specs — matching every prior Phase 8 report page) all pass
- [x] Manual E2E against real API/DB/browser (see summary above), including the Admin-only
      permission gate confirmed via a real `403` for an invited Member, both via direct API call and
      through the actual report page's error banner

## Scope decisions

1. **The live Tigg screen's confirmed shape replaced the speculative Capital Goods/Fixed Assets
   default entirely, rather than supplementing it.** The brief's proposed speculative design (a
   register of Capital-classified purchases reusing `ExpenditureClassification.Capital`) was
   external IRD domain knowledge with zero confirmation in this repo, explicitly flagged as such.
   Once the user supplied the live screen instead, its actual confirmed columns —
   `Fiscal_Year, Bill_No, Customer_Name, Customer_Pan, Bill_Date, Amount, Discount, Taxable_Amount,
   Tax_Amount, Total_Amount, Sync with IRD, Is_Bill_Printed, IS_Bill_Active, Printed_Time,
   Entered_By, Printed_By, Is_Realtime, Payment_Method, VAT_Refund_Amount, Transaction_Id` — showed
   this is a flat **Sales bill register with IRD/CBMS sync-tracking metadata** (consistent with the
   "Enable CBMS Integration in Tigg and Sync Invoices with IRD" item in that tenant's own "What's
   New" feed), not a Capital-purchases annexure at all. The speculative default was discarded
   outright rather than blended with the real shape, since the two describe genuinely different
   reports.
2. **Roughly half the live screen's confirmed columns are omitted entirely, not zero-filled.**
   `Fiscal_Year` needs a BS-calendar conversion this codebase has never built anywhere (confirmed by
   grep across `src/`). `Sync with IRD`/`Is_Realtime`/`Transaction_Id` need a real IRD/CBMS
   integration that doesn't exist. `Is_Bill_Printed`/`Printed_Time`/`Printed_By` need print tracking
   that doesn't exist. `Payment_Method` doesn't exist on Invoice/CreditNote at all — `Payment` is a
   separate, independently-allocated document in this codebase's model, so there's no single
   "payment method" a bill itself carries. `VAT_Refund_Amount` has no backing field anywhere.
   `Discount` is the same omission `SalesMasterReportQuery` already made (`phase-8b-status.md`'s
   scope decision) — no discount fields exist on `InvoiceLine`/`CreditNoteLine`. `Entered_By` was
   considered and rejected: `Invoice`/`CreditNote` carry `ApprovedByUserId`, not a distinct
   "who created this" field, and labeling the approver as "Entered_By" would misrepresent what that
   live column actually means. Every one of these is left out of the DTO entirely rather than
   shipped as an always-null/always-zero placeholder — the same "don't invent a placeholder for a
   feature that doesn't exist" precedent `SalesMasterReportQuery` set for discount columns.
3. **A CreditNote row is not sign-flipped, unlike `TdsReportRowDto`'s DebitNote convention.**
   `TdsReportQuery` (Phase 8d) lists a DebitNote as its own negative-signed row so a filing period
   visibly shows a reversal happened. Annex 5 does the opposite by design, confirmed directly against
   the live screen: `CN0004/83-84` showed `Amount 25,800` / `Total_Amount 29,050`, both positive, no
   different in sign from any Invoice row. This is a raw per-bill audit log, not a netted filing
   rollup — each document is simply its own row, so `AnnexFiveReportQueryHandler` never looks up or
   nets against a CreditNote's source Invoice at all (unlike `SalesMasterReportQuery`, which resolves
   a CreditNote's `WarehouseId` from its source). Two independently-confirmed-live report shapes in
   this same phase sequence now disagree on this exact question, and both were followed as observed
   rather than forcing one convention onto the other.
4. **Lives under `Application.Sales`, not `Application.Purchasing`/`Application.Accounting`.** Every
   one of the 74 rows returned by the live screen's default period was an `INV####/83-84` or
   `CN####/83-84` code — no Purchase-side document ever appeared. Combined with the confirmed column
   list itself (`Customer_Name`/`Customer_Pan`, never a Supplier-shaped field), this is unambiguously
   a Sales-only register, so it sits alongside `SalesMasterReportQuery` rather than following
   `AnnexThirteenReportQuery`/`TdsReportQuery`'s `Application.Purchasing` placement.
5. **`Reports.AnnexFive.View` is Admin-only.** Weighed explicitly against this report's own shape,
   the same discipline `phase-8b`/`8d`/`8e` used rather than defaulting to a precedent: Annex 5 is a
   flat register, one row per transaction, the same structural factor that made
   `SalesMasterReportView`/`PurchaseMasterReportView`/`TdsReportView` Admin-only. It also names the
   Customer's PAN on every row, the same factor that made `TdsReportView`/`AnnexThirteenView`
   Admin-only. Both factors point the same direction here — unlike Phase 8c's VAT Summary Report,
   where the rollup shape argued against the Master Reports' Admin-only default, there's no tension
   to resolve for Annex 5.
6. **Filtered to Approved-only documents; `IsActive` is mapped from `Status` for fidelity to the live
   screen's `IS_Bill_Active` column, even though it's always `true` today.** `InvoiceStatus`/
   `CreditNoteStatus` both define a `Void` value, but grep across `src/` confirms no command handler
   in this codebase can currently produce one (`SalesValidation`'s own `!= CreditNoteStatus.Void`
   filtering treats it as a defensive check against a state nothing yet creates, not a reachable
   path). The live screen showed some rows with `IS_Bill_Active=False` sitting alongside otherwise
   normal rows — rather than reinterpret what inactive means there or invent a Void-producing
   workflow out of scope for a report phase, `IsActive` is computed straight from `Status`, correct
   and forward-compatible the moment a real Void command exists, honest about being always-true until
   then.

## Bugs hit and fixed along the way

None in the shipped query handler, endpoint, or permission wiring. The only compile-time slip was in
this phase's own test file — an early draft read `customer.Pan` off `CreateContactResult`, which
only carries `Id`/`Code`/`Type`/`Name` (no `Pan`); fixed by threading the PAN literal through the
test's own `Seed` record instead of round-tripping it through the command result. Caught immediately
by `dotnet build`, not a runtime surprise.

## What's next

**Phase 8+** (see `roadmap.md`): with Annex 5 shipped, every Nepal-specific statutory report named in
`erp-module-scan.md`'s Tax Report category card is now built (Sales/Purchase Register and their
Migrated variants remain unbuilt and unconfirmed — no live shape was ever captured for those either).
Customer/Supplier Ageing & Statement are next per the roadmap, and will be the first phase to build
the real running-balance engine (`ContactStatementQuery`/`ContactOverviewQuery`,
`architecture-spec.md` §4.2) that Phase 8e's Annex 13 Opening/Closing Balance approximation
explicitly deferred to.
