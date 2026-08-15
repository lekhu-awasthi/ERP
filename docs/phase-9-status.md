# Phase 9 status — Customer & Supplier Ageing + Statement Reports

**Status: COMPLETE.** Two shared query handlers (`ContactAgeingSummaryQuery`/`ContactStatementQuery`,
both under `Application.Contacts.Queries`) answer all four report screens — Customer Ageing Summary,
Supplier Ageing Summary, Customer Statement, Supplier Statement — discriminated by a `ContactType`
field the same way `Payment`'s own `Direction` already discriminates Received-vs-Paid in one
aggregate (phase-6-status.md's "near-zero-new-code" precedent). No new commands, aggregates, or
schema tables beyond a permission-seed-only migration (`AddPhase9ReportPermissions`), matching every
prior Phase 8 report's "pure read" framing — this is the real running-balance engine
(`ContactStatementQuery`, architecture-spec.md §4.2) that Phase 8e's Annex 13 Opening/Closing Balance
approximation explicitly deferred to.

Unlike every prior Phase 8+ report, this phase's shape landscape was **mixed**: Customer Ageing
Summary and Customer Statement were already confirmed live (architecture-spec.md lines 276–277 /
erp-module-scan.md), but Supplier Ageing Summary / Supplier Statement were never opened in the
hands-on scan pass — only named in the Payable category card list. Per the brief, the user was asked
whether to mirror Customer's confirmed shape onto Supplier or ship Customer-only this phase; the user
instead pointed at the live reference product's own Supplier Ageing Summary / Supplier Statement
screens, which were read directly through the Browser tool (already-authenticated session from a
prior phase). Both screens turned out to be **structurally identical** to Customer's confirmed shape
— same columns, same Opening/Closing Balance row convention, same DR/CR suffix — confirming the
mirror bet was the right call and letting this phase ship all four reports rather than deferring the
Supplier side.

Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below): a fresh Admin
set up a Chart of Accounts (AR/VAT Payable/Sales Revenue, VAT Receivable/AP/Purchase Expense/TDS
Payable, Cash, Inventory/COGS), a Warehouse, a Service Product, a TDS Type (10%), a Contact Group
("Key Accounts"), a Customer (PAN, Opening Balance 1,000) and two Suppliers (one in the group with a
PAN, one deliberately outside it). They approved an Invoice (1,000, 2026-07-20) with a linked
CreditNote reversing 300 and a Payment Received of 200 against it; a PurchaseBill (gross 2,000, TDS
10% → net 1,800, 2026-06-15) with a linked DebitNote reversing gross 400/TDS 40 (net 360) and a
Payment Paid of 500 against it, plus an Expense (gross 500, TDS 50 → net 450, 2026-05-01, never
reduced — Expense can never be a Payment-allocation or DebitNote target in this codebase's data
model), and a small unrelated PurchaseBill on the out-of-group Supplier. Every number returned by all
four report endpoints — and rendered identically through the real Angular pages, including the live
Contact Group filter narrowing Supplier Ageing from two rows to one — matched hand arithmetic exactly
(e.g. Global Supplies' Ageing Total 1,390.00 and Supplier Statement Closing Balance 1,390.00 CR are
independently computed by two different code paths and agree exactly, since this scenario had no
standalone/unlinked reversal to create the documented Ageing-vs-Statement divergence). A second user
invited as Member hit all four reports and got the real API's `403` naming the exact permission key
each time (`Reports.CustomerAgeingSummary.View`, `Reports.SupplierAgeingSummary.View`,
`Reports.CustomerStatement.View`, `Reports.SupplierStatement.View`), rendered cleanly in each page's
own error banner, confirming the Admin-only grant on all four.

## How the Supplier shape was obtained

The brief required an explicit user decision before building the Supplier side (no confirmed shape in
this repo). Offered "mirror Customer" vs "Customer-only this phase", the user instead supplied a live
URL (`moonbeamtradingandsuppliers.tigguat.com/#/reports/new`) and this agent navigated the
already-authenticated Browser-pane session (carried over from Phase 8f's login) directly to
`#/reports/new/supplier-ageing` and `#/reports/new/supplier-statement`. Supplier Ageing Summary
confirmed live: Account Name / Contact Group / 1-30 / 31-60 / 61-90 / 91+ Days / Total — identical to
Customer's confirmed columns, with an empty "Credit Term" column on every visible row (consistent
with this codebase's own finding that no `CreditTermId` field exists anywhere to source it from). A
"Calculation Method" filter (`Actual Payment` vs `FIFO Basis`, default `FIFO Basis`) was also observed
— see scope decision #7 for why this wasn't replicated. Supplier Statement confirmed live: Txn
Date/Txn Type/Txn No/Reference No/Debit/Credit/Balance columns, an Opening Balance row and a Closing
Balance row grouped under an "Account" header, balances suffixed "CR"/"DR" — and critically, the
Opening Balance row's value sat in the **Credit** column, confirming AP's credit-normal polarity
directly against real product behavior rather than assumed from double-entry theory alone (see scope
decision #10).

## Roadmap Phase 9 exit criteria — final status

- [x] `ContactAgeingSummaryQuery(OrganizationId, ContactType, AsOfDate, ContactGroupId?)` and
      `ContactStatementQuery(OrganizationId, ContactType, ContactId, FromDate, ToDate)` — two
      Application-layer query handlers under `Application.Contacts.Queries`, no new
      commands/aggregates/migrations beyond a permission-seed one
- [x] Ageing Summary: per-bill outstanding (`NetAmount - Approved Payment allocations - Approved
      linked CreditNote/DebitNote reversals`) bucketed by age from each bill's own Date, no CreditTerm
      due-date offset (no `CreditTermId` field exists anywhere in this codebase to source one from)
- [x] Statement: flat chronological ledger per Contact with Opening Balance (`Contact.OpeningBalance`
      plus all pre-period Approved activity), a running balance per row, and a Closing Balance —
      Debit/Credit columns and the DR/CR balance suffix follow real double-entry polarity (AR
      debit-normal for Customer, AP credit-normal for Supplier), computed server-side
- [x] Supplier-side NetAmount is GrandTotal minus TdsAmount (not GrandTotal) — TDS is withheld from
      what's actually payable to the supplier, the same accounting fact `PurchaseBillPostingRule`
      already encodes at GL-posting time (scope decision #6)
- [x] Four permission keys (`Reports.{Customer,Supplier}{AgeingSummary,Statement}.View`), all
      **Admin-only** (Member gets an explicit `IsGranted=false` denial row on each) — explicit
      judgment call made and documented (scope decision #3)
- [x] Angular: four report pages (`customer-ageing-summary-page`/`supplier-ageing-summary-page`/
      `customer-statement-page`/`supplier-statement-page`) under `organizations/:id/reports/*`,
      dashboard nav links next to the existing Reports section
- [x] Unit tests: `ContactAgeingSummaryQueryHandlerTests` (2) and `ContactStatementQueryHandlerTests`
      (3), against the InMemory `TestAppDbContext`, seeding real Contact/ContactGroup/Warehouse/
      ProductCategory/UnitOfMeasurement/Product/Account/TdsType/TenantSettings rows and real
      Invoice/CreditNote/PurchaseBill/DebitNote/Expense/Payment documents through their real
      Create/Approve command handlers (same pattern as every prior Phase 8 report) — covers bucket
      boundary off-by-ones (ages exactly 30/31/60/61/90/91 days), TDS-net-payable arithmetic, a linked
      reversal reducing its specific bill's bucket, a standalone reversal excluded from Ageing but
      present in Statement, a fully-settled bill excluded entirely from Ageing, Approved-only and
      date-range filtering, the ContactGroupId filter, Opening Balance carry-in from before FromDate,
      Debit/Credit polarity and the DR/CR sign flip on an overpayment, and the permission-key-per-
      ContactType computed property
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged — no Domain changes this phase;
      Application.UnitTests 134 — 5 new + 129 pre-existing, all green; `Api.IntegrationTests` 4, run
      with Docker Desktop running this session — all green) and `ng build`/`ng test` (7 pre-existing
      specs green, no new Angular specs — matching every prior Phase 8 report page) all pass
- [x] Manual E2E against real API/DB/browser (see summary above), including all four Admin-only
      permission gates confirmed via a real `403` for an invited Member, both via direct API call and
      through each page's own error banner

## Scope decisions

1. **One shared handler per report type, ContactType-discriminated — not four separate handlers, and
   not one handler spanning both report types.** The brief asked to pick a structure and justify it
   rather than default to a precedent. `Contact.Type` already hard-partitions Customer-vs-Supplier
   activity across this codebase's own validation helpers (`SalesValidation`'s customer checks,
   `PurchasingValidation`'s supplier checks), the same structural fact `Payment.Direction` already
   exploited in Phase 6 to avoid a second command/handler pair for Supplier Payment. Ageing and
   Statement, by contrast, are genuinely different *computations* (per-bill netting bucketed by age
   vs. a flat chronological running balance) even for the same ContactType, so folding them into one
   handler would have forced an artificial shared shape neither report actually needs.
2. **Lives under `Application.Contacts`, not split across `Application.Sales`/`Application.Purchasing`
   the way every other Phase 8 report was.** This is the one case in the whole Phase 8+ sequence where
   `architecture-spec.md` itself dictates placement: §4.2 names `ContactStatementQuery`/
   `ContactOverviewQuery` explicitly under Contacts (CRM), not Sales or Purchasing. `ContactAgeingSummaryQuery`
   follows the same placement for consistency — it's structurally the same kind of Contact-scoped
   balance query, just bucketed instead of chronological.
3. **All four permission keys are Admin-only — the strongest PAN/identity-exposure case yet, stronger
   than every prior Admin-only report.** A Statement is a full per-transaction running-balance ledger
   for one named Contact (every Rate/amount they were ever billed or paid, not a rollup or even a flat
   per-line fact table like the Master Reports — this is literally every dollar that ever moved with
   one party, more granular than Annex 5/13/TDS Report). An Ageing Summary lists every Contact's
   PAN-adjacent identity next to their outstanding balance, the same factor that made TDS
   Report/Annex 13/Annex 5 Admin-only. Both factors that independently justified Admin-only elsewhere
   point the same direction here, with no VAT-Summary-style rollup shape to argue otherwise. Customer
   and Supplier each keep their own key (mirroring `SalesMasterReportView`/`PurchaseMasterReportView`'s
   precedent) even though one handler answers both, so an Admin can grant Sales-side visibility
   independently of Purchase-side.
4. **The Supplier shape mirrors Customer's confirmed shape, now itself confirmed live rather than just
   assumed.** See "How the Supplier shape was obtained" above — the user's own real-product read
   replaced what would otherwise have been an unconfirmed bet.
5. **The live screen's "Credit Term" column is omitted entirely, not zero-filled.** `CreditTerm`'s own
   doc comment already says "nothing consumes this today" — no `Contact`, `Invoice`, or `PurchaseBill`
   anywhere in this codebase carries a `CreditTermId`, confirmed by grep. This is the same "omit a
   column needing a capability this codebase doesn't have" precedent Phase 8b's discount columns and
   Phase 8f's IRD-sync columns already set.
6. **Ageing buckets come from each bill's own `Date`, never a Date+CreditTerm.DueDays due date — even
   for Expense, which uniquely among this codebase's documents carries its own `DueDate` field.**
   Retrofitting due-date-based ageing for every other document type (Invoice/PurchaseBill carry
   neither a CreditTermId nor a DueDate) was out of scope for a pure-read report phase; giving Expense
   alone due-date-based ageing while every other document type uses issue-date-based ageing would have
   been an inconsistent special case within one report, so `Expense.DueDate` is deliberately left
   unused here.
7. **TDS reduces the Supplier-side NetAmount used for both Ageing and Statement (`GrandTotal -
   TdsAmount`), not the gross bill total.** This mirrors the real accounting fact
   `PurchaseBillPostingRule` already encodes (TDS reduces the Accounts Payable credit, phase-6-status.md).
   **A pre-existing latent gap was found and flagged, not fixed:** `GetDefaultPaymentAllocationsQueryHandler`
   (Phase 5/6, the Payment-recording screen's own FIFO-suggestion query) uses `PurchaseBill.GrandTotal`
   directly when suggesting how much of a Payment to allocate against a bill, not
   `GrandTotal - TdsAmount` — meaning that screen can suggest allocating more than a TDS-bearing bill's
   *actual* net payable. This phase's own "outstanding" figure uses the technically-correct net figure
   throughout; fixing the pre-existing Phase 6 handler was judged out of scope for a pure-read report
   phase (it would touch already-shipped, independently-tested Payment code), but is worth flagging for
   whoever next touches Payment allocation suggestions.
8. **The live "Calculation Method" filter (`Actual Payment` vs `FIFO Basis`) was not replicated — one
   fixed method is used, equivalent to `Actual Payment`.** This codebase's `Payment` model already
   carries real, explicit per-document allocations (`PaymentAllocation.TargetDocumentId`), unlike a
   simpler payment model that might need a computed FIFO-netting fallback. Building a second,
   alternate netting algorithm with no second real use case to justify it would have been scope
   creep beyond what's needed to prove the running-balance engine correct — the confirmed-live
   default (`FIFO Basis`) doesn't match what this codebase's own data can directly answer, and
   `Actual Payment` is the one genuinely backed by real recorded allocations.
9. **A standalone (unlinked) CreditNote/DebitNote is excluded from Ageing's bucket totals but included
   in Statement's flat ledger — a deliberate, documented divergence between the two reports.** Ageing
   answers "how old is what's still owed on our bills," which requires attributing every reduction to
   a specific bill; Statement answers "what's the running balance of all transactions," which needs no
   such attribution. Real AR/AP ageing reports have exactly this tension (an unapplied/on-account
   credit sits outside any specific bill's ageing bucket until it's applied) — this isn't a shortcut,
   it's the same structural question every ageing report has to answer, resolved narrowly rather than
   with an invented per-bucket FIFO-netting scheme for unlinked reversals (a materially more complex
   feature with no confirmed-live behavior to build against).
10. **Debit/Credit and the DR/CR balance suffix follow real double-entry polarity, computed
    server-side — not a uniform "adds to the balance → Debit" rule.** For a Customer (AR,
    debit-normal), a bill is a Debit and a reduction is a Credit; for a Supplier (AP, credit-normal)
    it's the exact opposite — confirmed directly against the live Supplier Statement screen, whose
    Opening Balance row carried its value in the **Credit** column (see "How the Supplier shape was
    obtained"). Doing this server-side, not in Angular, keeps the frontend a pure formatter of
    already-authoritative numbers, consistent with every other report page in this codebase.
11. **Statement's "Description" column is omitted.** No document type this report reads from carries
    genuine freetext narrative data — only `Expense` has a `Notes` field, and populating Description
    for one of six document types while leaving it blank for the other five would be worse than
    omitting the column outright, the same reasoning Annex 5 applied to its own omitted columns.
12. **`ContactStatementQuery` takes a single `ContactId`, not the live screen's multi-Contact
    multi-select.** Every other single-party report/query in this codebase (`GetContactQuery`, etc.)
    operates on one id at a time; the live UI's multi-select is a bulk-print convenience layered over
    the same one-Account-at-a-time computation, not evidence of a different underlying shape.
13. **`ContactOverviewQuery` (the Contact detail page's Overview tab — Opening/DR/CR/Closing Balance,
    Recent Transactions, "View Full Statement") was not built this phase**, even though
    architecture-spec.md §4.2 names it alongside `ContactStatementQuery`. The roadmap's own Phase 9
    task title is "Customer & Supplier Ageing + Statement Reports" — four specific report screens, all
    four confirmed live and all four shipped. A Contact-detail-page retrofit is a separate, larger UI
    feature (that page's Overview tab currently shows none of Opening Balance/DR/CR/Closing
    Balance/Recent Transactions at all) that can trivially reuse this phase's now-proven running-balance
    engine in a future phase, rather than scope-creeping this one beyond its named deliverables.

## Bugs hit and fixed along the way

One EF Core LINQ-translation bug, caught during design rather than by a failing test: an early draft
of `ContactStatementQueryHandler` used a generic `SumLinesAsync<TLine>(IQueryable<TLine> lines,
Func<TLine, Guid> parentIdSelector, ...)` helper to avoid repeating five near-identical
line-summing blocks. EF Core's LINQ provider can't translate `parentIdSelector.Invoke(x)` inside a
`.Where()` call — a captured `Func` delegate isn't an expression tree the provider can decompose into
SQL, the same class of gotcha CLAUDE.md's known-gotchas list already documents for a different generic
scenario (the "MediatR handler generic over a type parameter constrained by an interface" entry). Not
caught by `dotnet build` (it type-checks fine) and would only have surfaced at runtime against a real
provider — this codebase's InMemory test provider is actually more forgiving here than real SQL
Server would be, so this specific instance was caught by re-reading the code against the known
pattern, not by a red test. Fixed by writing five separate, concrete `.Where(x => ids.Contains(x.FooId))`
blocks instead (matching `AnnexThirteenReportQueryHandler`'s own established style) rather than
fighting the translation boundary.

One test-authoring gotcha (not a product bug): the first draft of both test files tried to express a
partial CreditNote/DebitNote reversal using a *different* Rate than the source line (e.g. reversing
"400 of a 1000-Rate Invoice line" as a CreditNote line at Rate=400). Phase 6's conversion-cap
enforcement (`SalesValidation.EnsureCreditNoteLinesWithinInvoiceRemainingAsync`/
`PurchasingValidation.EnsureDebitNoteLinesWithinPurchaseBillRemainingAsync`) requires an *exact*
`(ProductId, Rate, VatRate)` match against a source line, immediately rejecting both tests with a
`ConflictException`. Fixed by expressing every partial reversal as a fractional `Quantity` at the
source line's *own* Rate instead (e.g. `Quantity=0.4` at `Rate=1000` for a 400 reversal) — worth
remembering for the next test suite that seeds a linked Credit/Debit Note reversal.

One test-arithmetic mistake (mine, not the handler's): the first run of
`ContactAgeingSummaryQueryHandlerTests`' Customer test asserted a Payment-reduced Invoice's remaining
balance in the wrong age bucket (91+ instead of 31-60, and miscomputed the remaining amount as the
allocation amount itself rather than `GrandTotal - allocated`). The handler's actual output (650/1000
across the 31-60 and 91+ buckets) was correct on the first real run; the test's *expected* values were
wrong. Fixed by recomputing the expected bucket assignment by hand against each seeded document's own
age and outstanding amount.

One pre-existing-workflow gotcha, hit while seeding the permission migration: `dotnet ef migrations
add` scaffolded a completely **empty** migration the first time, because only `PermissionKeys.cs` (the
string-constant catalog) had been updated — the actual seed source of truth EF diffs against is
`RolePermissionConfiguration.Configure`'s `HasData(...)` call. Adding the eight new
`RolePermission.Create(...)` rows there first, then re-scaffolding, produced the correct
auto-generated `InsertData`/`DeleteData` migration (and kept `AppDbContextModelSnapshot.cs` in sync,
which a hand-written migration would have silently skipped). This is a different failure mode than the
already-documented "`dotnet ef migrations add` doesn't apply to the dev database" gotcha (phase-8e-status.md)
— this one produces a migration that's *silently wrong/empty* rather than merely un-applied, worth
knowing before hand-writing any future permission-seed migration instead of updating the
`HasData` seed first.

One E2E-script-only gotcha (not a product bug): `POST /api/auth/register` does not create a
`VerificationCode` row on its own — an explicit `POST /api/auth/request-verification-code` call is
needed first before a code exists to look up via `sqlcmd` and pass to `/api/auth/verify-email`. Worth
remembering for the next phase's manual E2E script.

## What's next

**Phase 9+** (see `roadmap.md`): with Ageing/Statement's running-balance engine now proven, a natural
follow-up (not required by this phase's brief, and deliberately deferred per scope decision #13) is
`ContactOverviewQuery` for the Contact detail page's Overview tab (Opening/DR/CR/Closing Balance,
Recent Transactions, "View Full Statement" link into the Statement page this phase already built).
Beyond that, `roadmap.md`'s Phase 8+ section should be consulted for what's next in the broader
Reports/statutory sequence.
