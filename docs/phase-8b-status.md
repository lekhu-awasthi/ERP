# Phase 8b status — Sales & Purchase Master Reports

**Status: COMPLETE.** Two pure-read query handlers (`SalesMasterReportQuery` under
`Application.Sales.Queries.SalesMasterReport`, `PurchaseMasterReportQuery` under
`Application.Purchasing.Queries.PurchaseMasterReport`) produce a denormalized line-item fact
table over Invoice/CreditNote lines and PurchaseBill/DebitNote lines respectively — no new
commands, aggregates, or schema tables beyond a permission-seed-only migration, matching Phase
8a's "pure read" framing and the roadmap's own description of this phase. Both queries filter on
each document's own business `Date` field, not `GlJournalEntry.PostedAt` — a deliberate departure
from Phase 8a's three GL reports (scope decision #1). Two new View-only permission keys
(`Reports.SalesMasterReport.View`/`Reports.PurchaseMasterReport.View`), granted **Admin-only**,
diverging from Phase 8a's Admin+Member precedent (scope decision #2). Angular gets two new
read-only report pages (`sales-master-report-page`/`purchase-master-report-page` under
`features/reports/`) with date-range pickers plus optional Contact/Product/Warehouse filter
dropdowns, an unpaginated flat table, and dashboard nav links.

Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below): a fresh
Admin set up a Chart of Accounts, a Customer and Supplier, a Warehouse, and a VAT-rated Product,
approved a PurchaseBill (10 units in), converted it to an Invoice-side sale via a separate
Quotation→Invoice flow (4 units sold), issued a CreditNote against the Invoice (2 units returned)
and a DebitNote against the PurchaseBill (3 units returned), then pulled both Master Reports —
Sales Master Report showed exactly two rows (the Invoice line and the CreditNote line, correct
Warehouse resolved on both), Purchase Master Report showed exactly two rows (the PurchaseBill line
and the DebitNote line, same Warehouse-resolution behavior), and every Contact/Product/Warehouse
filter combination narrowed the row set correctly. A Member account got a 403 on both report
endpoints, confirming the Admin-only grant.

## Roadmap Phase 8b exit criteria — final status

- [x] `SalesMasterReportQuery(OrganizationId, FromDate, ToDate, ContactId?, ProductId?,
      WarehouseId?)` — one row per Invoice/CreditNote line (`Type` column distinguishes them),
      joined to Contact/ContactGroup/Warehouse/Product, `Approved`-only, filtered on `Invoice.Date`/
      `CreditNote.Date` in `[FromDate, ToDate]` inclusive — not `GlJournalEntry.PostedAt`
- [x] `PurchaseMasterReportQuery(OrganizationId, FromDate, ToDate, ContactId?, ProductId?,
      WarehouseId?)` — mirror shape over PurchaseBill/DebitNote lines
- [x] Only `Approved` documents included (Draft/Void excluded) — confirmed by
      `SalesMasterReportQueryHandlerTests`/`PurchaseMasterReportQueryHandlerTests`' first test,
      which seeds one Draft document alongside two Approved ones and asserts the Draft never
      appears
- [x] Row shape: Contact, Type, ContactGroup, Warehouse, EntryNo (document Code), ReferenceNo,
      EntryDate, ProductCode, Product, Quantity, Rate, Amount, VatType (VatRate), VatAmount,
      TotalAmount — `ItemDiscount`/`TransactionDiscount`/`NetSales` from the reference product's
      confirmed live shape omitted entirely (scope decision #3)
- [x] CreditNote/DebitNote `WarehouseId` resolution via their source Invoice/PurchaseBill when
      `ReferrerType` matches (scope decision #4) — `null` for a standalone reversal, matching the
      `WarehouseId` filter correctly excluding rows whose resolved warehouse doesn't match
- [x] Permission keys `Reports.SalesMasterReport.View`/`Reports.PurchaseMasterReport.View`,
      **Admin-only** (Member explicitly denied) — diverges from Phase 8a's Admin+Member grant,
      see scope decision #2
- [x] Angular: `sales-master-report-page`, `purchase-master-report-page` under
      `organizations/:id/reports/*`, date-range pickers plus Contact/Product/Warehouse filter
      `<select>`s using `[selected]` per-option (never `[value]` on the `<select>` itself, per
      CLAUDE.md's repeated gotcha), unpaginated flat table (scope decision #5), dashboard nav links
- [x] Unit tests: `SalesMasterReportQueryHandlerTests` (3) and `PurchaseMasterReportQueryHandlerTests`
      (3), all against the InMemory `TestAppDbContext`, seeding real Contact/Warehouse/
      ProductCategory/UnitOfMeasurement/Product/Account/TenantSettings rows and real Invoice/
      CreditNote/PurchaseBill/DebitNote documents through their real Create/Approve command
      handlers (same "exercise the real Approve path" pattern Phase 8a's report tests used) —
      covers date-range filtering + Approved-only, ContactId/ProductId filtering, and
      WarehouseId filtering including the CreditNote/DebitNote referrer-resolution path
- [x] `dotnet build`/`dotnet test` (Application.UnitTests all green; Api.IntegrationTests not
      re-run this phase — no Infrastructure/Api-layer behavior beyond two new minimal-API GET
      endpoints and a purely additive permission-seed migration, the same "no new DbContext
      surface" reasoning Phase 8a's status doc used) and `ng build`/`ng test` all pass
- [x] Manual E2E against real API/DB/browser (see summary above)

## Scope decisions

1. **Both reports filter on each document's own business `Date` field (`Invoice.Date`,
   `CreditNote.Date`, `PurchaseBill.Date`, `DebitNote.Date`), not `GlJournalEntry.PostedAt`** —
   the brief's own explicit instruction, and the opposite choice from Phase 8a's three GL reports.
   The reasoning is the inverse of Phase 8a's: Trial Balance/Balance Sheet/Income Statement are
   *GL* reports, answering "what does the ledger say," so they correctly key off the moment
   something actually posted to it. A Master Report is a *document register* — "what did the
   Sales/Purchase team record as having happened on this date" — and every document in this
   codebase already carries its own business `Date` field for exactly that purpose (the field a
   user picks when creating the document, independent of whenever it later gets Approved). Using
   `PostedAt` here would mean a PurchaseBill dated 2026-01-15 but approved 2026-02-01 shows up in
   the wrong month's Purchase Register — the same distortion Phase 8a's scope decision #2 flagged
   as an *accepted approximation* for GL reports, but here there's no reason to accept it at all:
   the business `Date` field already exists on every relevant aggregate, so there's no
   cross-cutting change needed to use it (unlike Phase 8a, which would have needed to thread a new
   field through every `IGlPostingRule<TDocument>` call site to get the same result). This also
   means Phase 8a's `GlDateBoundary` helper isn't reused here — `Invoice.Date`/`PurchaseBill.Date`/
   etc. are already `DateOnly`, so the query's `FromDate`/`ToDate` range check is a direct
   `>= && <=` comparison with no UTC-day-boundary conversion needed.
2. **`Reports.SalesMasterReport.View`/`Reports.PurchaseMasterReport.View` are Admin-only** (Member
   gets an explicit `IsGranted=false` denial row), diverging from every other `Reports.*.View` key
   in this codebase (Phase 8a's three, `InventoryLedgerView`), which are all Admin+Member. Explicit
   judgment call per the brief's own request to decide and document rather than silently follow
   the existing pattern. The distinction: Phase 8a's reports are *rollups* — a Trial Balance shows
   net account balances, a Balance Sheet shows group totals — genuinely aggregated numbers that
   don't expose any single transaction's specific terms beyond what a Member could already piece
   together by opening that transaction's own document (which they already have `.View` on). A
   Master Report is structurally different: it's a **flat, unaggregated fact table** — literally
   every Rate ever charged or paid, per line, across the *entire* tenant history, sliceable by
   Contact/Product/Warehouse in one screen. That's not "the same information, reorganized" the way
   a rollup is; it's "every individual transaction's commercially sensitive terms, laid bare in
   bulk, cross-referenced" — a fundamentally different and larger exposure than opening one
   Invoice at a time (the brief's own framing: "these reports surface Rate/margin-adjacent data
   across all of a tenant's transactions, not just one document at a time"). Nepali SME tenants
   commonly treat exactly this kind of bulk pricing/margin visibility as owner-only information
   (who a business actually charges what, in aggregate, is closer to payroll-level sensitivity
   than to "can view an Invoice"). PRD FR-3.5's eventual per-report granularity (a future Role
   Reference editor letting a tenant grant this to specific non-Admin roles) remains the long-term
   answer if a tenant wants a Member to have it — same deferral Phase 8a's scope decision #4 used
   for its own granularity question.
3. **`ItemDiscount`/`TransactionDiscount`/`NetSales` columns from the reference product's confirmed
   live shape are omitted entirely, not modeled as always-zero placeholders.** Explicit choice
   between the brief's two offered options. `InvoiceLine`/`CreditNoteLine`/`PurchaseBillLine`/
   `DebitNoteLine` carry no discount fields anywhere in this codebase — no `Discount`/
   `DiscountPercent`/`DiscountAmount` property exists on any line entity, `Amount` is always
   `Quantity * Rate` with no adjustment. Shipping three columns that are *always* 0 would silently
   imply "this system tracks discounts, they just happen to be zero on every row so far" — a false
   signal to whoever reads the report, and a maintenance trap the moment discount support is ever
   added for real (every historical zero would need re-auditing to confirm it means "genuinely no
   discount" versus "discount tracking didn't exist yet"). Omitting the columns is honest about the
   current shape and costs nothing to add later — a real discount feature would need new fields on
   every affected line entity regardless, at which point the report can add real columns backed by
   real data. **Flagged prerequisite**: real discount modeling (fields on `InvoiceLine`/
   `PurchaseBillLine`/etc., presumably threaded through `AddLine`'s `Amount` computation) is a
   cross-cutting change of its own, out of scope for both this phase and any phase before it — the
   next report or phase that needs it should add it once, not per-report.
4. **CreditNote/DebitNote's `WarehouseId`/`WarehouseName` columns resolve from the source Invoice/
   PurchaseBill when `ReferrerType` matches** (`DocumentType.Invoice`/`DocumentType.PurchaseBill`)
   **and are `null` for a standalone reversal** — the same lookup
   `ApproveCreditNoteCommandHandler`/`ApproveDebitNoteCommandHandler` already perform for FIFO
   reversal (Phase 7's post-completion fix), reused here for display rather than stock mutation.
   This isn't a new gap this phase introduced — `CreditNote`/`DebitNote` have never carried a
   `WarehouseId` column of their own (confirmed by reading `Domain.Sales.CreditNote`/
   `Domain.Purchasing.DebitNote` directly: no such property exists), the same fact that made the
   Phase 7 FIFO-reversal fix need this same resolution path in the first place. Practical
   consequence for the `WarehouseId` filter: a standalone CreditNote/DebitNote (no `ReferrerId`, or
   one whose referrer isn't the expected source type) can never match a `WarehouseId` filter and is
   always excluded once that filter is set — correct behavior (it has no meaningful warehouse to
   match), but worth knowing if a user expects "show me everything, including reversals with no
   warehouse" under a warehouse filter and doesn't get it. Filtering happens in application code
   after the query executes (not composed into the LINQ query itself), since resolution requires a
   second lookup against the referred document — see the handler's `continue`-based skip for the
   reasoning.
5. **Both report pages ship unpaginated**, per the brief's own explicit call to decide and
   document. No pagination component exists anywhere in this codebase yet (every other list page —
   Contacts, Products, Invoices, JournalVouchers, etc. — also renders its full result set in one
   unpaginated table), so building one specifically for these two reports would be introducing new
   shared UI infrastructure inside a phase explicitly scoped as "pure read, no scope creep." A
   tenant with a genuinely large transaction history hitting a slow/huge unpaginated table is a
   real future concern, but it's a concern for *every* list page in this codebase equally, not
   something specific to Master Reports — the fix, if it's ever needed, is a shared pagination
   component built once and retrofitted everywhere, not a one-off solution built into these two
   pages alone.

## Bugs hit and fixed along the way

One test-authoring bug, caught before the phase was reported done (not a defect in the shipped
query handlers themselves): the first draft of both handler test suites captured a document's
*Create*-time result (`CreateInvoiceResult`/`CreatePurchaseBillResult`, whose `Code` is always the
literal `"DRAFT"` placeholder — see `Invoice.DraftCode`/`PurchaseBill.DraftCode`) and asserted it
against the report row's `EntryNo`, which reflects the real number assigned at *Approve* time
(architecture-spec.md §3.1's "document numbers are assigned at Approve, not at Create," already
called out in CLAUDE.md's known-gotchas list as something every phase's tests need to respect).
Fixed by capturing each `Approve*Result` instead and asserting against *that* result's `Code`. A
second, related bug surfaced once the first was fixed: `FakeDocumentNumberGenerator`'s counter
starts fresh at 1 per instance, and the tests' helper methods were each constructing a **new**
`FakeDocumentNumberGenerator()` per `Approve*CommandHandler` call — meaning every single approved
document in a test independently got assigned `"{DocumentType}-0001"`, so two different Invoices in
the same test collided on the identical code and any assertion distinguishing them by `EntryNo`
silently passed or failed for the wrong reason. Fixed by threading one shared
`FakeDocumentNumberGenerator` instance (stored on the test's `Seed` record) through every
Create/Approve call within a single test, matching how a real `IDocumentNumberGenerator` instance
is shared across an entire request pipeline in production. Neither bug touched
`SalesMasterReportQueryHandler`/`PurchaseMasterReportQueryHandler` themselves — both are pure test
double misuse, caught by the tests' own assertions failing loudly (wrong string compared, or a
`DoesNotContain` unexpectedly matching) rather than a silent false-positive.

## What's next

**Phase 8+** (see `roadmap.md`): the Nepal-specific statutory reports (VAT Summary, TDS Report,
Annex 13/5) are next in the Reports sequence, followed by Customer/Supplier Ageing & Statement —
both explicitly out of this phase's scope per the brief. Workflow (Tasks, Transaction Approval
queue), CRM, and the Role Reference full editor remain further out. Two smaller open items carried
forward: (a) if a tenant's transaction history ever grows large enough for the unpaginated table to
become a real problem, build a shared pagination component and retrofit it across every list page
in this codebase, not just these two reports (scope decision #5); (b) if real discount support is
ever added, `SalesMasterReportQuery`/`PurchaseMasterReportQuery` should add real
`ItemDiscount`/`TransactionDiscount`/`NetSales` columns backed by the new fields at that point, not
before (scope decision #3).
