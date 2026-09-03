# Phase 26a status — Report catalog completion, Accounting group

**TL;DR.** The five missing Accounting reports now exist — **Transaction list**, **Journal report**,
**General Ledger Summary**, **Detail General Ledger**, **GL Master Report** — and FR-9.1's
**Compare** (period-over-period) column now exists on **Trial Balance**, **Balance Sheet** and
**Income Statement**, which Phase 8a never built. Everything reads `GlJournalEntry`/`GlLine` or the
existing document tables; **nothing new is stored** and the only migration is ten permission-seed
rows. All four GL reports were **generated live** on the Moonbeam UAT tenant on 2026-09-02 before
any DTO was designed (findings below); both predictions in the roadmap held — GL Master is the
denormalised fact table, Detail GL is the per-account running-balance ledger.

Five new permission keys, split per the standing rule rather than defaulted: Transaction list /
Journal report / Detail GL / GL Master are **Admin-only** (flat per-transaction registers across
every document type, carrying who created and approved each one); General Ledger Summary is
**Admin+Member** (a bounded per-account rollup, the Trial Balance shape). The Compare columns need
no key of their own — they are a second window over data the existing three keys already disclose.

Every new report gets `.xlsx` export through `ReportSpreadsheetExporter`, and the three financial
statements gained the export they never had. **Dates in exports stay AD this phase**, inheriting
phase-23 Decision A's carried limitation (scheduled for 27b).

Verified end to end against the real API and database: a fresh Organization seeded by curl, one
approved document of **every** type that posts GL (ten of them, plus Opening Balance), all five
reports pulled, all five negative paths returning **403 naming their exact key**, and `sqlcmd`
re-deriving the Detail GL running balance from the raw `GlLine` rows independently of the report.

---

## Confirm-live pass (Moonbeam UAT tenant, 2026-09-02, read-only)

The four reports were listed in `erp-module-scan.md` but never generated. Each was opened and
GENERATE clicked before any DTO was designed, per the phase-8f rule. URL slugs confirmed:
`journal-report`, `general-ledger`, `general-ledger-detail`, `general-ledger-materialized`.

### Journal report — `#/reports/new/journal-report`
- **Filters:** Period, Txn Type. The Show Filters drawer adds Reporting Tags (no Show Columns).
- **Not a flat table.** One block per posted document: a header line carrying Txn Type, Txn No and
  Date; then the document's own GL lines under **Accounts / Description / Debit / Credit**, each
  account rendered `Name (Code)`; then a per-document **Total** row. Zero renders as `-`.
- The **Description** column carries the voucher narration ("being cash deposited on 2083.05.01",
  "cash paid for salary") and, on an Invoice, even its terms-and-conditions text.
- **Paged at document granularity** — its footer read `1 - 100 / 205` while the same period's GL
  Master Report, one row per line, read `1 - 100 / 547`. Newest first. No grand total.

### General Ledger Summary — `#/reports/new/general-ledger`
- **Filters:** Period, Group, Account.
- **Columns:** Code/accounts, Parent, Group Type, Account Class, Opening Balance, Transaction Debit,
  Transaction Credit, Closing Balance.
- **Parent** is the account's own immediate group; **Group Type** is the *top-level* group that
  group descends from — proved by rows where the two differ (Parent `Cash and Bank Balance` under
  Group Type `Current Assets`); **Account Class** is the root type.
- Balances print as a magnitude plus **DR/CR**, and the marker follows the **raw net**, not the
  account's natural side: `Sales Service (DI0002) … 0 DR | 3000 | 0 | 3000 DR` is an Income account
  reported DR because it had been debited on balance.
- Lists accounts with 0/0 movement. Paged (`1 - 100 / 137`). No footer total.

### Detail General Ledger — `#/reports/new/general-ledger-detail`
- **Filters:** Period, Account, and a "Group by" multi-select whose only two options are
  **Account** (ticked by default) and **Sub Account**.
- **Columns:** Txn Date, Txn Type, Txn No., Reference No, Description, Debit, Credit, Balance.
- **One section per account.** Header `Account <CODE> <Name>`, then an **Opening Balance** row, then
  one row per posting in date order with a running Balance suffixed DR/CR, then a **Closing Balance**
  row whose Debit and Credit cells hold the *period totals* (e.g. `265,200 | 566`) and whose Balance
  holds the closing figure.
- **Description = the contra account plus the narration** ("Cash" + "Being the salary paid").

### GL Master Report — `#/reports/new/general-ledger-materialized`
- **Filters:** Period, Txn Type.
- **Columns:** Date, Txn Type, Txn No, Reference No, Account, SubAccount, Parent, Group Type,
  Account Class, Debit, Credit. One row per GL line. Paged (`1 - 100 / 547`). No footer total.
- SubAccount was **empty on every row** of this tenant.

### One cross-cutting observation
All four render a Payment as two different Txn Types — **Customer Payment** and **Supplier
Payment** — for the one underlying aggregate. In a flat ledger a reader has no other way to tell a
receipt from a payment, so both the screens and the `.xlsx` exporter apply that same label rule
(`txnTypeLabel` on the web side, `TxnTypeLabel` in `ReportSpreadsheetExporter`).

---

## Scope decisions

### Decision A — what "Compare" compares against, and why it is not one rule

`ComparePeriod` (`Application/Accounting/Reports/`) is the single place that decides the comparison
window, and it deliberately has **two** shapes because this codebase's financial statements have
two shapes:

- **Range reports** (Income Statement) compare against the **same-length period immediately
  preceding** — the literal reading of the roadmap's own wording, and unambiguous because the
  request itself supplies the length.
- **As-of reports** (Trial Balance, Balance Sheet) take a single date and so have **no length to
  reuse**; "same-length prior period" is undefined for them. They compare against the **same
  calendar date one year earlier**, which is what a comparative balance sheet means everywhere in
  accounting practice.

This is a choice, not a fallback. The alternative considered and rejected was an explicit
user-picked compare date on those two screens: it is strictly more flexible, but it makes the three
screens inconsistent with each other (a toggle on one, a date picker on two) for a case no one asked
for. It remains the obvious future extension — the server contract already carries the compared
window on the response, so adding it later changes no DTO shape.

Three rules follow from this and are worth stating because they are what make the feature honest:

1. **It is one request, not two.** The comparison is a second window inside the same handler, merged
   into the same response. Lining two independent responses up in the browser would mean
   re-deriving the account list, the ordering and the group rollups client-side — the same
   full-set-versus-current-page mistake phase-16c found in four report pages.
2. **The window is echoed back** (`CompareAsOfDate`, or `CompareFromDate`/`CompareToDate`) so the
   screen and the spreadsheet can label the extra columns with real dates. A comparison whose period
   the reader has to guess is worse than no comparison, and a `.xlsx` outlives the screen it came
   from.
3. **Off means null, not zero.** Every `Compare*` field is `null` when Compare is off, so a template
   can tell "not compared" from "compared, and the figure was nil".

One real consequence, recorded because it changes what the report shows: the Income Statement lists
only accounts **with movement**, so with Compare on the row set becomes the **union of both
windows**. An account that traded last period and not this one has to appear, or the comparison
silently hides exactly the change the reader opened the report for.

### Decision B — the date a GL report filters and shows is the *posting* date

The live reports key off each document's own business date. This codebase cannot: `GlJournalEntry`
stores `SourceDocumentType`, `SourceDocumentId` and `PostedAt`, and no copy of the document's date.
Phase 8a recorded filtering on `PostedAt` as an accepted approximation for Trial Balance / Balance
Sheet / Income Statement, and all three still do it.

The three new line-level reports **show the same field they filter on**, so a row can never appear
outside the range printed above it. The rejected alternative was to resolve each document's own
`Date` for display while still filtering on `PostedAt` — which would produce a report whose rows
visibly contradict its own header. Moving the *whole* GL report family onto document dates is a
coherent future change; doing it for one report would only make the family disagree with itself.

### Decision C — three columns the live reports have and these do not

Each is omitted rather than rendered permanently blank, following the Annex 5 and Contact Statement
precedent ("do not carry a column whose source does not exist"):

- **SubAccount** (GL Master). In the reference product a Contact *is* an account beneath a control
  account. Here AR/AP are single shared control accounts resolved from `TenantSettings` — a fact
  `ContactStatementQuery` already records — so there is no subledger to name. It was empty on every
  row of the live report anyway.
- **Group by → Sub Account** (Detail GL). Same reason, and the per-contact ledger a user would want
  instead already exists as the Customer/Supplier Statement.
- **The narration half of Description** (Journal report). `GlLine` stores no narration, and of the
  eleven document types that post GL only Expense and ProductionJournal carry a `Notes` field.
  Filling the column for two and blanking it for nine is worse than not having it. The Detail
  General Ledger keeps the column and fills it with the **contra account names**, which *is*
  derivable and is the substantive half.

### Decision D — the Transaction list is a third union, not a reused projection

The kickoff suggested reusing the Transaction Approval queue's projection. The **idiom** is reused —
one concrete `db.Xs.Where(...)` block per document type rather than a generic `Func`-parameterised
helper (phase-9 bug #1), plus Phase 23's two-pass "page first, resolve second" shape — but the query
itself is new, because the queue's is wrong for this report in three ways: it returns Drafts only,
it gates per document type against thirteen `*.Approve` grants, and it carries neither an amount nor
the created/approved attribution.

Two findings came out of building it:

- **Created By is derived from the audit trail.** No transactional aggregate in this codebase stores
  a creator — grep-confirmed: only `Deal`, `WorkTask`, `AlertDefinition`, `Organization` and
  `SmsCreditLedgerEntry` do. `AuditBehavior` writes a `Create` row per document, so the earliest such
  row is the creator. A document created before Phase 16d introduced that behavior reports `null`,
  which is the honest answer; deriving it from `ApprovedByUserId` would not be.
- **There is deliberately no footer total.** phase-16c's rule is that a footer total covers the whole
  filtered set — but the honest answer here is that there is nothing to total: an Invoice's gross, a
  Journal Voucher's debit side and an Inventory Adjustment's value are not the same unit of account,
  and adding them yields a number with no meaning that a reader would nonetheless believe.
  `RecentTransactionsQuery` made the same call for the same reason.

Every status is included, Draft as well as Void and Converted: the live report's Status filter
offers Draft and Approved as the two values a user picks between, which only makes sense if
unfiltered means both, and a document silently missing from the flat "everything that exists"
register would be indistinguishable from one that was never created.

### Decision E — status mapping is by name, and a test says so

Each of the thirteen document types has its own status enum, and they are not identical — only
Quotation and PurchaseOrder have `Converted`. The handler maps onto the shared
`TransactionListStatus` **by name** (`Enum.Parse`/`Enum.TryParse`), never by ordinal, and
`TransactionListQueryHandlerTests` asserts that every member of all thirteen enums has a counterpart
in the shared one. Ordinal casting would compile, work today, and silently report the wrong state
the first time anyone inserts a member into one of those enums.

### Decision F — paging granularity follows the report's own unit

- Journal report pages **documents**, matching the live footer counts (205 documents vs 547 lines
  for the same period) — and it is the only paging that keeps a block's own Total row correct.
- Detail GL pages **accounts**, because a running balance is only correct if its section is whole; a
  split section would print a Closing Balance that does not match the rows above it.
- GL Summary pages accounts and GL Master pages lines, which are their natural units.

### Decision G — the three financial statements gained an export

Phase 8a shipped Trial Balance, Balance Sheet and Income Statement with no `.xlsx` at all. Adding
Compare columns to a screen whose figures cannot leave it would have shipped half a feature, so the
export was added alongside — a small deliberate widening of this phase's scope, recorded here rather
than left to be noticed later. Compare columns carry the compared date in their own header.

---

## Manual E2E — the exit criterion, against the real API and database

A fresh Organization, master data and one approved document of **every** type that posts GL, all
seeded through the real API by curl + cookie jar. Ten source document types produced GL, plus two
Opening Balance postings:

```
document types that posted GL: CashTransfer, CreditNote, DebitNote, Expense, InventoryAdjustment,
                               Invoice, JournalVoucher, OpeningBalance, Payment, PurchaseBill
```

**Transaction list** — 10 rows, one Draft among nine Approved, each with its own amount in its own
terms, Created By resolved from the audit trail, and the Expense showing contact + notes:

```
Type                  No        Status        Amount  CreatedBy         ApprovedBy        Description
JournalVoucher        DRAFT     Draft         123.00  Phase16c Tester   -                 -
Payment               0001      Approved      500.00  Phase16c Tester   Phase16c Tester   Sunrise Traders
InventoryAdjustment   0001      Approved      300.00  Phase16c Tester   Phase16c Tester   -
Expense               0001      Approved    2,000.00  Phase16c Tester   Phase16c Tester   Everest Supply — Office rent for the month
DebitNote             0001      Approved      135.60  Phase16c Tester   Phase16c Tester   Everest Supply
CreditNote            0001      Approved      113.00  Phase16c Tester   Phase16c Tester   Sunrise Traders
Invoice               0001      Approved    1,130.00  Phase16c Tester   Phase16c Tester   Sunrise Traders
PurchaseBill          0001      Approved    6,780.00  Phase16c Tester   Phase16c Tester   Everest Supply
CashTransfer          0001      Approved   10,000.00  Phase16c Tester   Phase16c Tester   -
JournalVoucher        0001      Approved    5,000.00  Phase16c Tester   Phase16c Tester   -
```

**Journal report** — 11 blocks (9 approved documents + 2 Opening Balance postings; the Draft is
absent, because only an Approve posts GL), every one balanced:

```
  DebitNote 0001  ref=DN-REF-1
      Accounts Payable (0005): Dr 135.60 Cr 0.00
      Inventory (0004): Dr 0.00 Cr 120.00
      VAT Receivable (0007): Dr 0.00 Cr 15.60
      TOTAL Dr 135.60 = Cr 135.60  balanced=True
unbalanced blocks: []
```

**General Ledger Summary** — 15 accounts, `Parent` and `Group Type` genuinely different where the
chart is nested (Cash in Hand sits in `Cash and Bank Balance` under `Current Assets`), and closing
equal to opening + Dr − Cr on every row:

```
Account                 Parent                  GroupType          Class        Open        Dr        Cr       Close
Cash in Hand            Cash and Bank Balance   Current Assets     Asset     0.00 DR  50,000.00 15,000.00  35,000.00 DR
Accounts Payable        Current Liability       Current Liability  Liability 0.00 DR     135.60  8,780.00   8,644.40 CR
Inventory Adjustment    Indirect Expenses       Indirect Expenses  Expense   0.00 DR       0.00    300.00     300.00 CR
rows where closing != opening + Dr - Cr: []
total Dr 126,558.60 == total Cr 126,558.60
```

That `Inventory Adjustment` row is the raw-net DR/CR marker doing its job: an Expense account
carrying a credit balance is exactly the anomaly the column exists to surface, and normalising it to
the account's expected side would have hidden it.

**Detail General Ledger** (Cash in Hand) — opening row, running balance, contra account in
Description, and a Closing row holding the period totals:

```
Account Cash in Hand (0001)
  Opening Balance 0.00 DR
  2026-09-02  OpeningBalance   Opening Balance  -          Opening Balance Equity   Dr 50,000.00 Cr      0.00 => 50,000.00 DR
  2026-09-02  JournalVoucher   0001             JV-REF-1   Rent Expense             Dr      0.00 Cr  5,000.00 => 45,000.00 DR
  2026-09-02  CashTransfer     0001             CT-REF-1   Nabil Bank               Dr      0.00 Cr 10,000.00 => 35,000.00 DR
  Closing Balance  Dr 50,000.00 Cr 15,000.00 => 35,000.00 DR
```

**`sqlcmd` — the same figures re-derived from the raw `GlLine` rows, independently of the report:**

```
--- raw GlLine rows for Cash in Hand, in posting order, with a running balance ---
PostedOn  |DocType        |Dr       |Cr       |RunningBalance
2026-09-02|OpeningBalance |50000.00 |.00      |50000.00
2026-09-02|JournalVoucher |.00      |5000.00  |45000.00
2026-09-02|CashTransfer   |.00      |10000.00 |35000.00

--- period totals and closing balance, computed independently of the report ---
PeriodDebit|PeriodCredit|ClosingNetDebit
50000.00   |15000.00    |35000.00

--- whole-organization GL ---
TotalDebit|TotalCredit
126558.60 |126558.60

--- GL entry count by source document type (matches the Journal report's 11 blocks) ---
CashTransfer 1 | CreditNote 1 | DebitNote 1 | Expense 1 | InventoryAdjustment 1
Invoice 1 | JournalVoucher 1 | OpeningBalance 2 | Payment 1 | PurchaseBill 1
```

Every figure matches the screen: the running balance, the period totals, the closing figure and its
DR marker, and the block count.

**Compare columns**, with the main window placed in the future so the compare window is the one
holding the data — which is what proves the comparison really is a *second, earlier* window rather
than the same figures echoed into extra columns:

```
-- Trial Balance, compare=true --
asOf=2026-09-04  compareAsOf=2025-09-04
totals now Dr 59,961.40 / Cr 59,961.40   balanced=True
totals compare Dr 0.00 / Cr 0.00

-- Income Statement, compare=true --
main 2026-09-04..2026-09-06  compare 2026-09-01..2026-09-03
main income 0.00 expense 0.00 net 0.00
compare income 900.00 expense 7,300.00 net -6,400.00

-- Balance Sheet, compare=true --
asOf=2026-09-04  compareAsOf=2025-09-04  balanced=True
assets 52,361.40 (compare 0.00) | liab 8,761.40 (compare 0.00) | equity 43,600.00 (compare 0.00)
```

**The negative path — all five keys, 403 not 404**, against a nonexistent organization id, so the
403 proves `AuthorizationBehavior` fired *before* the handler could have found nothing:

```
transaction-list       -> 403  "You do not have permission to perform this action (Reports.TransactionList.View)."
journal-report         -> 403  "... (Reports.JournalReport.View)."
general-ledger-summary -> 403  "... (Reports.GeneralLedgerSummary.View)."
detail-general-ledger  -> 403  "... (Reports.DetailGeneralLedger.View)."
general-ledger-master  -> 403  "... (Reports.GeneralLedgerMaster.View)."
```

**Exports** — all eight routes returned HTTP 200 with a real `PK`-magic workbook:

```
transaction-list 7316B | journal-report 8004B | general-ledger-summary 7666B
detail-general-ledger 9013B | general-ledger-master 8241B
trial-balance 7214B | balance-sheet 6770B | income-statement 6784B   (last three: compare=true)
```

### Browser pass

All six screens were opened in the automation pane against the seeded organization, using phase-25's
recipe (ASP.NET dev cert + `erp-web-ssl` + transplanting curl's `erp_auth` cookie — see bug #3 below
for the one thing that had to be fixed to make it work again):

- **Transaction list** — 10 rows, the multi-select Txn Type and Status filter blocks, "Nothing ticked
  means every type", and each row's `View` link into its own document.
- **Detail General Ledger** — per-account sections with Opening Balance, running balance, contra
  accounts in Description (`VAT Payable, Inventory, Sales Revenue, Cost of Goods Sold` on the
  Invoice's AR line), and a Closing Balance row carrying the period totals.
- **Journal report** — one block per document with its own balanced Total row; a Payment renders as
  **Customer Payment**, in the filter list as well as the rows.
- **General Ledger Summary** — Parent and Group Type genuinely different for the nested Cash accounts.
- **GL Master Report** — one row per line with the full classification.
- **Compare** on all three statements. Ticking it re-runs the report and the extra columns are
  headed with the compared dates, not the word "prior": Trial Balance showed
  `Debit (03-09-2025) | Credit (03-09-2025)`, Balance Sheet `03-09-2026 | 03-09-2025` per section,
  Income Statement `31-08-2026 – 03-09-2026 | 27-08-2026 – 30-08-2026`.

---

## Bugs and traps hit

1. **A group filter that matched on group *name*.** The first cut of
   `GeneralLedgerSummaryQueryHandler` resolved the filtered group's subtree to a set of *names* and
   compared each account's `ParentGroupName` against it. Group names are not unique across a chart of
   accounts, so two unrelated "Other" groups would have merged. Fixed by carrying `GroupId` on
   `GlAccountClassification.AccountFacts` and matching on the id.
2. **`ng test` failing on a pre-existing test.** `bs-date.spec.ts`'s exhaustive round-trip
   (33,969 days, ~136,000 assertions) was already close enough to Vitest's 5s default that it began
   timing out on a loaded machine. Sampling it down would defeat its stated purpose, so it got an
   explicit 30s timeout instead. Not caused by this phase, but this phase is where it surfaced.
3. **`.claude/launch.json`'s `erp-web-ssl` pointed at a cert path that cannot resolve.** The entry
   runs `npm --prefix web`, so its working directory is `web/`, and `.certs/dev.pem` resolved to
   `web/.certs/dev.pem` while phase-25's documented export command writes to the repo root. Changed
   to `../.certs/dev.pem`, so the documented recipe now actually works.
4. **A `cat > file <<'EOF'` heredoc silently truncating**, twice, on files well under the ~8 KB
   figure `CLAUDE.md` already records. Switched to writing the file with the editor tool, or to a
   small Python patch script run from the scratchpad.

---

## Shape as built

`Application/Accounting/Reports`: `ComparePeriod`, `GlBalanceMarker`, `GlAccountClassification`
(the Account/Parent/Group Type/Account Class quartet, with an ancestor walk that is the mirror of
Phase 8a's `ITreeQuery` descendant walk), `GlSourceDocumentResolver` (one batched round trip per
document type, resolving a posted entry back to its Txn No and Reference No).

`Application/Accounting/Queries`: `JournalReport`, `GeneralLedgerSummary`, `DetailGeneralLedger`,
`GeneralLedgerMaster` — query, handler and validator each; plus `Compare` added to `TrialBalance`,
`BalanceSheet` and `IncomeStatement`.

`Application/Workflow/Queries/TransactionList`: query, handler, validator.

`Api`: 8 new report routes plus 8 matching `/export` routes across `AccountingEndpoints` and
`WorkflowEndpoints`; 8 new methods on `ReportSpreadsheetExporter` (including `AsOfFileName` for
single-date reports and the shared `TxnTypeLabel`).

`Infrastructure`: 10 seed rows in `RolePermissionConfiguration` and the
`Phase26aReportPermissions` migration — a seed-only migration, no schema change.

`web`: five new report pages, `gl-report-shared.ts` (the eleven GL-posting document types, the
Payment-splits-into-two label rule, and the detail-route switch), Compare + export on the three
statement pages, five routes, five dashboard links.

**Tests:** Domain 249 (unchanged), Application.UnitTests **598** (+27), Angular **128**.
`dotnet build`, `dotnet test`, `ng build`, `ng test` and `tsc --noEmit` all clean.

---

## Carried into later phases

- **AD dates in every `.xlsx` and server-rendered PDF** — phase-23 Decision A's limitation, now
  inherited by eight more export routes. Scheduled for 27b, which builds the Domain `BsDate`
  converter.
- **An explicit compare-date picker** on Trial Balance and Balance Sheet (Decision A) — the response
  contract already carries the window, so this is a UI-only change whenever it is wanted.
- **Reporting Tags filtering** on the Journal report, which the live drawer offers and this one does
  not; it belongs with 27a's Reporting Tags rollout rather than here.
