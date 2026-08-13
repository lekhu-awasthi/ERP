# Phase 8a status — Core Financial Reports

**Status: COMPLETE.** Three pure-read query handlers (`TrialBalanceQuery`, `BalanceSheetQuery`,
`IncomeStatementQuery`, all under `Application.Accounting.Queries`) join `GlLine`/`GlJournalEntry`
against `Account`/`AccountGroup` — no new commands, aggregates, or schema tables, matching the
roadmap's own framing of this phase as "pure GL queries, no new writes needed". The only new
infrastructure is `ITreeQuery<AccountGroup>` (`Application.Common.Trees`/`Application.Accounting`),
the architecture-spec.md §5-flagged "get full subtree" helper Balance Sheet's group rollups need —
see scope decision #1 for why it's an in-memory BFS rather than the spec's originally-recommended
raw SQL Server recursive CTE. Three new View-only permission keys
(`Reports.TrialBalance.View`/`Reports.BalanceSheet.View`/`Reports.IncomeStatement.View`), granted
to both Admin and Member, follow `Inventory.InventoryLedgerView`'s precedent exactly. Angular gets
three new read-only report pages (`trial-balance-page`/`balance-sheet-page`/`income-statement-page`
under `features/reports/`) with date/date-range pickers, mirroring `stock-position-page`'s chrome,
plus dashboard nav links.

Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below): a fresh
Admin set up a Chart of Accounts (Current Assets → nested Cash & Bank → Cash in Hand; Current
Liabilities → Accounts Payable; Owner's Equity → Owner Capital; Sales Income → Sales Revenue;
Operating Expenses → Rent Expense), approved three JournalVouchers (Debit Cash/Credit Owner Capital
10,000; Debit Cash/Credit Sales Revenue 3,000; Debit Rent Expense/Credit Accounts Payable 2,000),
and pulled all three reports: Trial Balance showed the exact natural-side balances and balanced at
15,000/15,000; Balance Sheet correctly rolled the doubly-nested Cash account up into its
grandparent "Current Assets" group (13,000), showed Total Liabilities 2,000, Total Equity 11,000
(10,000 Owner's Equity + a 1,000 Net Income plug line), and asserted `Assets == Liabilities +
Equity` (13,000 = 13,000); Income Statement showed Sales Revenue 3,000 / Rent Expense 2,000 / Net
Income 1,000 for the selected date range. Changing the Trial Balance's As Of Date to a date before
any of the postings correctly zeroed every row out, confirming the PostedAt cutoff (scope decision
#2) works against real data, not just unit-test doubles.

## Roadmap Phase 8a exit criteria — final status

- [x] `TrialBalanceQuery(OrganizationId, AsOfDate)` — every active Account, `netDebit =
      sum(Debit) - sum(Credit)` from `GlLine` joined to `GlJournalEntry` where
      `PostedAt <= AsOfDate` (end of day UTC), presented on its natural side (`Debit` column if
      positive, `Credit` column with the sign flipped if negative); `TotalDebit`/`TotalCredit`/
      `IsBalanced` surfaced on the response, same spirit as JournalVoucher's live "Difference: Rs.
      0" check
- [x] `BalanceSheetQuery(OrganizationId, AsOfDate)` — Asset/Liability/Equity accounts, each
      section grouped by top-level `AccountGroup` with a full-subtree rollup via
      `ITreeQuery<AccountGroup>`; a synthetic "Net Income (Current Period)" plug row under Equity
      (`GroupId = Guid.Empty`) since there's no period-close/retained-earnings posting anywhere in
      this codebase; `TotalAssets`/`TotalLiabilities`/`TotalEquity`/`IsBalanced`
      (`Assets == Liabilities + Equity`) computed independently of the group-rollup breakdown, so
      the invariant holds even if a group's own tree tagging is inconsistent with its Accounts' —
      see the handler's `AccountProjection`-based totals and scope decision #3
- [x] `IncomeStatementQuery(OrganizationId, FromDate, ToDate)` — Income minus Expense accounts
      with real GL activity in `[FromDate, ToDate]` (start/end of day UTC); `TotalIncome`/
      `TotalExpense`/`NetIncome` plus a per-account row breakdown
- [x] `ITreeQuery<AccountGroup>` (`AccountGroupTreeQuery`) — `GetSubtreeIdsAsync(organizationId,
      rootId)` returns the root's id plus every descendant's id via an in-memory BFS over the
      tenant's full `AccountGroup` set (scope decision #1)
- [x] Permission keys `Reports.TrialBalance.View`/`Reports.BalanceSheet.View`/
      `Reports.IncomeStatement.View`, granted to both Admin and Member (scope decision #4)
- [x] Angular: `trial-balance-page`, `balance-sheet-page`, `income-statement-page` under
      `organizations/:id/reports/*`, date/date-range pickers (no `<select>` on any of the three
      pages, so the repeated `[value]`-vs-`@for` gotcha doesn't apply here), dashboard nav links
- [x] Unit tests: `TrialBalanceQueryHandlerTests` (3), `BalanceSheetQueryHandlerTests` (2, including
      the nested-subgroup rollup case), `IncomeStatementQueryHandlerTests` (2) — all against the
      InMemory `TestAppDbContext`, seeding real Accounts/AccountGroups and posting real GL entries
      via `CreateJournalVoucherCommandHandler`/`ApproveJournalVoucherCommandHandler` rather than
      hand-constructing `GlJournalEntry` rows directly, so the tests exercise the same balanced-GL
      invariant the real Approve path does
- [x] One EF Core migration (`AddPhase8aReportPermissions`) — purely additive `RolePermissions`
      seed rows (`InsertData`/`DeleteData` only, no schema change at all, confirming the roadmap's
      "no new migrations needed" framing meant no new tables, not literally zero migrations: the
      seed-data mechanism for permission grants is itself migration-backed in this codebase,
      inherited from every prior phase's `RolePermissionConfiguration.HasData` pattern)
- [x] `dotnet build`, `dotnet test` (114 Application.UnitTests, 7 new + 107 pre-existing, all
      green; 4 Api.IntegrationTests green against real SQL Server via Testcontainers, Docker
      Desktop running), `ng build`, `ng test` (7 pre-existing specs green) all pass
- [x] Manual E2E against real API/DB/browser (see summary above) — reproduces the roadmap's own
      bar: a real Chart of Accounts, real approved JournalVouchers, all three reports pulled
      through the real UI and hand-verified against paper arithmetic, including the nested-group
      rollup and the AsOfDate cutoff

## Scope decisions

1. **`ITreeQuery<AccountGroup>`'s "get full subtree" read is an in-memory BFS over
   `IAppDbContext`, not architecture-spec.md §5's originally-recommended raw SQL Server recursive
   CTE.** The brief asked to "build that now" per the spec's own suggestion, but that same spec
   section's reasoning for why `AccountGroup` uses an adjacency list (`ParentGroupId` self-FK) over
   `HIERARCHYID` in the first place — "the observed depth (a few levels) doesn't need
   [HIERARCHYID's] query-performance advantages at this scale" — applies just as directly to
   *querying* that adjacency list as it does to storing it. A portable LINQ query against
   `IAppDbContext` (identical code path against the InMemory provider in unit tests and real SQL
   Server in production) avoids yet another instance of the `Database.SqlQuery<T>` composability
   gotchas this codebase has hit repeatedly (see CLAUDE.md's "Known gotchas" — the
   `DocumentNumberGenerator`'s `UPDATE...OUTPUT` non-composability bug and the interface-vs-concrete
   `PropertyInfo` translation bug are both instances of the same underlying class of problem: LINQ
   translation against non-trivial SQL is a real, recurring cost in this codebase, not a one-off).
   `ITreeQuery<T>`'s interface is intentionally provider-agnostic (`Task<IReadOnlyList<Guid>>
   GetSubtreeIdsAsync(...)`) so a real CTE-backed implementation could replace `AccountGroupTreeQuery`
   later behind the same seam, without touching `BalanceSheetQueryHandler`, if a tenant's Chart of
   Accounts ever grows large enough (many hundreds of groups) for the current per-top-level-group
   query-and-BFS approach to matter performance-wise. It does not today: a typical tenant's Chart of
   Accounts has a handful of top-level groups times a few descendants each, and `BalanceSheetQuery`
   only calls `GetSubtreeIdsAsync` once per top-level group within each of the three relevant root
   types (at most a handful of calls, each a single indexed `AccountGroups` query), not once per
   Account.
2. **These three reports filter on `GlJournalEntry.PostedAt` (the Approve-time posting timestamp),
   not any originating document's own business `Date` field** (`Invoice.Date`, `JournalVoucher.Date`,
   `PurchaseBill.Date`, etc.) — the brief's own explicit scope call. `GlJournalEntry` doesn't carry
   the source document's business date today; threading one through would mean touching every
   existing `IGlPostingRule<TDocument>`/`GlJournalEntry.Post()` call site across
   Sales/Purchasing/Accounting/Inventory (six document types' worth of `ApproveXCommandHandler`s),
   a real cross-cutting change with its own migration, not something that belongs inside a
   deliberately small "pure GL queries" phase. **Practical consequence, stated plainly**: a
   JournalVoucher (or any document) with a business `Date` of, say, 2026-01-15 but approved on
   2026-02-01 lands in whichever reporting period covers 2026-02-01, not 2026-01-15 — a back-dated
   approval reports in the wrong period under this scheme. Accepted as an approximation for this
   phase (documented here and in `GlDateBoundary`'s doc comment, not silently baked in per the
   brief's instruction); flagged as a fast-follow if real usage shows tenants routinely
   back-date-approve documents into a closed period. `GlDateBoundary`'s UTC-boundary choice (not the
   browser's local timezone) is the same "everything in this codebase is UTC, there's no per-tenant
   timezone concept yet" reasoning already implicit in `GlJournalEntry.PostedAt`'s own
   `DateTimeOffset.UtcNow` stamp.
3. **Balance Sheet's `TotalAssets`/`TotalLiabilities`/`TotalEquity` (and the `IsBalanced` invariant)
   are computed straight from `Account.RootType` across every active Account of that type, entirely
   independently of the group-rollup breakdown shown alongside them.** This was a deliberate
   decoupling, not an oversight: the fundamental accounting identity `Assets = Liabilities + Equity
   + (Income − Expense)` holds by construction from the raw ledger (`sum(Debit) == sum(Credit)`
   across every posted entry, split by which side of the equation each `AccountRootType` normally
   sits on) *regardless* of how — or how correctly — the Chart of Accounts happens to be organized
   into groups. Computing the totals from the group-rollup sums instead would make `IsBalanced`
   silently depend on every Account's `GroupId` chain actually reaching a top-level group whose
   `RootType` matches the Account's own `RootType` — a real but separate data-integrity concern
   (nothing in `CreateAccountGroupCommand`/`CreateAccountCommand` stops an Admin from nesting a
   Liability group under an Asset parent) that would otherwise silently corrupt the balance
   invariant this report exists to prove. The group-rollup breakdown is purely a display
   convenience; the totals are the source of truth. Verified directly by the nested-subgroup test
   (`BalanceSheetQueryHandlerTests`): Cash sits two levels down
   (`Current Assets → Cash & Bank → Cash in Hand`) and still rolls up correctly into `Current
   Assets`'s displayed balance, while the `IsBalanced` assertion is proven independently of that
   rollup logic.
4. **All three report permission keys are granted to both Admin and Member**, following
   `Inventory.InventoryLedgerView`'s precedent (a single View-only key, not the four-key
   `{View,Create,Edit,Approve}` document shape every `ApprovableTransaction` gets) rather than
   Admin-only. Judgment call, explicitly flagged per the brief's own request to decide and document
   rather than silently default: these are read-only rollups of data a Member already has `.View`
   access to piecemeal via JournalVoucher/Invoice/PurchaseBill/CashTransfer/etc. — a report that
   just aggregates already-visible numbers doesn't obviously need a stricter gate than the
   underlying documents themselves. PRD FR-3.5's eventual per-report granularity (e.g. a Member who
   can see Trial Balance but not Balance Sheet) is left for the future Role Reference editor
   (`roadmap.md`'s Phase 8+ list) rather than modeled with finer-grained keys now, matching how
   every other simple-View-key precedent in this codebase (Configuration lookups, Inventory Ledger)
   has deferred that same granularity question.
5. **No new Domain.UnitTests or Domain-layer code at all this phase** — `GlDateBoundary` and
   `AccountGroupTreeQuery` both live in `Application`, and the three query handlers are ordinary
   `IRequestHandler<TQuery,TResponse>` implementations with no new Domain aggregate or entity
   behavior to unit-test in isolation (unlike, say, Phase 7's `StockLedgerEntry.Consume`). All new
   test coverage lives in `Application.UnitTests/Accounting`, seeding real Accounts/AccountGroups
   and posting real balanced GL entries through the existing JournalVoucher Create/Approve handlers
   rather than hand-constructing `GlJournalEntry`/`GlLine` rows directly — this means the tests
   incidentally also prove the reports read correctly from data shaped exactly the way the real
   Approve path produces it, not a synthetic shortcut.

## Bugs hit and fixed along the way

None. Unlike every prior phase's status doc, there's no gotcha or defect to report here — the
three query handlers compiled and passed their unit tests on the first attempt once the
`AccountProjection`-vs-anonymous-type-as-`dynamic` refactor in `BalanceSheetQueryHandler` was
cleaned up during authoring (caught by normal code review before ever running, not a runtime
surprise), the generated migration was purely additive `InsertData`/`DeleteData` with no scaffolding
gotcha to reorder, and the manual E2E pass matched hand-computed arithmetic exactly on the first
try. Worth noting for whoever reads this doc looking for the next codebase-wide gotcha: this phase's
low defect rate is likely because it added no new mutable state, no new `<select>` elements (so the
repeated `[value]`-vs-`@for` race simply doesn't apply to any of the three new pages), and no new
EF Core entity mappings to get subtly wrong — it's a genuinely "pure read" phase in the way the
roadmap predicted.

## What's next

**Phase 8+** (see `roadmap.md`): Sales/Purchase Master Reports next, then the Nepal-specific
statutory reports (VAT Summary, TDS Report, Annex 13/5) — all deliberately out of this phase's
scope per the brief. Workflow (Tasks, Transaction Approval queue), CRM, and the Role Reference full
editor remain further out. Two smaller open items carried forward from this phase's own scope
decisions: (a) if back-dated approvals into closed reporting periods turn out to matter in
practice, threading a business-date field through `GlJournalEntry`/every `IGlPostingRule<TDocument>`
call site is the real fix (scope decision #2); (b) if a tenant's Chart of Accounts ever grows large
enough for `AccountGroupTreeQuery`'s per-top-level-group query-and-BFS approach to show up in
profiling, swap it for a real SQL Server recursive CTE behind the same `ITreeQuery<AccountGroup>`
interface (scope decision #1) — no call site changes needed.
