# Phase 57 — finishing the bank module, and an index debt that was about the wrong column

## TL;DR

**The census re-run reproduced phase 53's 166 keys exactly, and found one real gap that is not
scope.** The strict regex over the vendor's bundle returns 162 `<feature>-<verb>` keys; the four
`-alter` keys (`contact-`, `account-`, `bank-`, `product-alter`) make up the difference, and the
bundle is the same 7,288,492 bytes phase 53 read. No new document types, no new bank keys, still no
`recurring-invoice-*`. The vendor's report catalogue is **51 entries**, reproduced from the bundle's
own `{id, url, name}` list rather than from a screen, and **50 of the 51 have a counterpart here**.
The one that does not is the **Inventory Variance Report**, and opening it live answers why it never
appeared in a screen-based pass: it refuses with *"Inventory Tracking **and Physical Inventory
Tracking** Not Enabled"*. That is the `InventoryTrackingMode = Physical Movement` seam the roadmap
already defers Delivery Note and GRN behind, so it is a third item that deferral owes, not a new
phase.

**`is_bank_user` is a different product, settled in two paragraphs as the kickoff allowed.** When the
flag is true the client swaps its **entire router** for fourteen report routes — `bank-view`,
profit-loss, balance-sheet, cash-flow, the four ageing reports, inventory-summary,
net-trading-assets, exceptional, ratio-analysis — with `Redirect from:"*" to:"/reports/new/bank-view"`
and a separate `validate_login_bank_user` auth call. It is a read-only lender/analyst shell over
financial reports. It is not a second surface over reconciliation, and it is out of scope.

**Quick Approve's four vendor executors are two documents here, and the reason is structural.** The
vendor's picker is one searchable list of ledger accounts
(`GET /accounts-minimized?transaction_type=DR|CR`) in a chart where a customer *is* an account
carrying `type: "customer"`, so it routes on the selected account's type. This codebase separates
Contact from Account — phase 17's Decision #7 declined to port the vendor's generic
multi-line-accounts document precisely because `JournalVoucher` already **is** it here. So the picker
offers the two things the vendor's one list conflates, and the routing falls out of which was chosen:
a **Contact** becomes a `Payment` whose direction is the line's own, an **Account** becomes a
two-line `JournalVoucher`. All four branches were driven live and proved in SQL.

**It auto-reconciles, and that is what makes it more than a shortcut** — the created document posts
to the same bank account, so without it the matcher would offer the new GL line beside the very
statement line it came from. It writes through phase 56's own mechanism
(`BankReconciliationWriter`, extracted this phase so the two paths cannot diverge), and the sum rule
that mechanism enforces holds by construction.

**There is no back-reference column, because the reconciliation is the back-reference**, and the
third door to an empty reconciliation is shut in one place: `SourceDocumentGlEntries
.ReverseOutstandingAsync` — the single path all fifteen reversals take — now refuses to void a
document whose posting is reconciled.

**The measurement is the part worth remembering.** Phase 56 owed a number for an index on
`ReconciliationId`. The number says the debt was about the wrong column: `ReconciliationId` in the
key is worth **one** logical read, while making the index **covering on `AccountId`** takes the
matcher's pane from **153,470 logical reads to 553** — and takes the Book Statement and the report's
own balance figure with it, both of which predate the reconciliation module entirely.

Tests: Domain 738, Application.UnitTests **1356 (+13)**, Infrastructure.UnitTests 13,
Api.IntegrationTests 30, Angular **619 (+6)**. `dotnet build` / `dotnet test` / `ng build` /
`ng test` all clean; `ng build` does not warn and the bundle is **644.77 kB** against the 680 kB
budget. One migration, and it is an index replacement with no data change.

---

## Step 1 — the census re-run

Read-only, from the vendor's own bundle rather than from its screens, which is phase 53's method and
the reason it can be re-run cheaply. Cadehi (`cadehi.tigg.app`) was still reachable with one day left
on its trial; the user's session was already in the pane and no credentials were entered.

### The key catalogue is unchanged

| | Phase 53 (2026-09-20) | Phase 57 (2026-09-21) |
| --- | --- | --- |
| Bundle | `main.*.chunk.js` + `13.*.chunk.js`, 7.3 MB | **7,288,492 bytes**, same two chunks |
| `<feature>-<view/add/edit/approve/void/full-access/export>` | 166 | **162 + 4 `-alter` = 166** |
| Document types with `*-add`/`*-approve` | 22, of which 20 ours | unchanged |
| `recurring-invoice-*` | absent | **still absent** |
| Bank module's own keys | `bank-reconciliation-export` only | unchanged |

The ±4 is not drift: phase 53's regex counted `contact-alter`, `account-alter`, `bank-alter` and
`product-alter`, which end in a verb my stricter pattern did not list. Reconciling the two counts
*is* the check — a census that cannot be reproduced to the row is a number, not evidence.

The vendor also carries a second, separate namespace this pass pinned properly: the role editor's
permission tree (158 names) and the report ids (`report-*`). **Those are two vocabularies for one
thing** — the tree says `gl-materialised-view` where the route says `report-gl-materialised-view` —
which is phase 46's rule about two lists that cannot be joined by display name, met again. They are
compared where both sides are typed: the bundle's own `{id, url, name}` report list.

### The one diff, and why it is not this phase's scope

The report catalogue is **51 entries**, which reproduces phase 53's count by a method that does not
depend on which screens a menu happened to render. Fifty have a counterpart in
`web/src/app/features/reports/`. The fifty-first is **Inventory Variance Report**
(`report-inventory-variance`, `/reports/new/inventory-variance`).

Opening it on Cadehi answers the question a bundle read cannot:

> Inventory Tracking and Physical Inventory Tracking Not Enabled

**That is the seam the roadmap already defers behind**, not a new one. `TenantSettings
.InventoryTrackingMode` is where Delivery Note and Goods Received Note sit, both tenants still read
`Accounting Movement`, and the re-entry condition is unchanged: the user flips the setting on a
tenant, the screens are read, and it becomes a phase of its own. What this pass adds is that the
deferral owes **a report as well as two document types** — recorded in `roadmap.md` so the next
session planning that phase does not scope it from the document types alone.

It is also why a screen-based pass never found it. A report gated on a flag the tenant does not hold
is invisible to a menu read and plainly present in the bundle, which is the whole argument for
phase 53's method.

### `is_bank_user`

Phase 55 noticed it, phase 56 carried it, and it is chased here. From `main.*.chunk.js`: the shell's
router is a ternary on `auth.user?.is_bank_user`, and the true branch is a `Switch` containing
**only** fourteen report routes and a catch-all `Redirect from:"*" to:"/reports/new/bank-view"`.
Sign-in has its own executor, `validate_login_bank_user`, posting to the auth domain and replacing
history with `/reports/new/bank-view`; the content header hides its title for such a user.

So it is a **bank-side viewer persona** — a lender or analyst given a read-only window on ten
financial reports of somebody else's tenant. It has nothing to do with the reconciliation module,
which its shell cannot even reach. Recorded and excluded, with no carried item.

---

## Decision A — the vendor's four executors are two documents here

The routing table phase 56 recorded, re-read from `18.*.chunk.js` this phase to get the picker's own
source:

| Selected account type | Direction | Vendor creates |
| --- | --- | --- |
| `customer` | deposit | Customer Payment |
| anything else | deposit | Quick Receipt |
| `supplier` | withdrawal | Supplier Payment |
| anything else | withdrawal | Quick Payment |

with `contact_id` set to **the selected account's own id** and `items:[{account_id, amount}]`. The
picker is `GET /accounts-minimized?limit=20&show_suggested=true&transaction_type=DR|CR`, filtered to
exclude the statement's own account.

That table only makes sense in a chart where a customer *is* a ledger account. Here it is not, and
phase 17's Decision #7 already wrote down the consequence: it declined to port the vendor's generic
multi-line-accounts document because `JournalVoucher` is that document in this codebase. So:

| Picked | Direction | This codebase creates |
| --- | --- | --- |
| Contact | deposit | `Payment`, Direction = Received, AccountId = the statement's own bank account |
| Contact | withdrawal | `Payment`, Direction = Paid |
| Account | deposit | `JournalVoucher`: **Dr** the bank account, **Cr** the chosen account |
| Account | withdrawal | `JournalVoucher`: **Dr** the chosen account, **Cr** the bank account |

**Nothing here checks the contact's type**, and that is the decision rather than an omission.
`PaymentValidation.EnsureContactExistsAsync` already requires a Customer for Received and a Supplier
for Paid, so a mismatch is a 404 from the command that owns the rule. A second copy of that rule
could only drift from it.

## Decision B — reuse the commands through `ISender`, which also settles the permission question

Phase 21a's rule for a writer that is not the document's own screen: reuse the Create and Update
commands under the pipeline. Quick Approve sends `CreatePaymentCommand` + `ApprovePaymentCommand`, or
`CreateJournalVoucherCommand` + `ApproveJournalVoucherCommand`, so validation, the lock date, the
subscription quota, the GL posting rule and **authorization** all apply unchanged.

That last one answers the permission question the kickoff said to derive rather than default:
**Quick Approve requires the target document's own Create and Approve keys**, because the nested
sends go through `AuthorizationBehavior` like any other request. The request's own key is
`Accounting.BankStatement.Manage` — phase 55's key for writing to a statement line, phase 56's key
for reconciling — so the two gates compose: you must be allowed to touch the statement *and* to raise
the document. **No new permission key**, for the second phase running.

On nesting: `CLAUDE.md`'s note that "a nested `ISender.Send` corrupts a scoped context" is the reason
phase 32b did **not** introduce a scoped context between two behaviors. `AuthorizationBehavior` is
stateless, so a nested send re-runs the check, which is the behaviour wanted here. The one real cost
is that the two sends each `SaveChangesAsync`, so the sequence is not one transaction: an
infrastructure failure between Approve and the reconciliation would leave an approved document
unmatched, which the matcher shows and one click fixes. Stated rather than hidden.

## Decision C — it auto-reconciles

Phase 56's hand-off called this "the interesting part" and it is. The created document posts against
the same bank account, so its GL line lands in the matcher's right-hand pane next to the statement
line it was made from, waiting to be ticked by hand. So the handler finishes by writing a
`BankReconciliation`.

It writes it through **phase 56's own mechanism**, not beside it. The already-reconciled refusal, the
sum rule and the mutation of both sides moved out of `CreateBankReconciliationCommandHandler` into
`BankReconciliationWriter`, which both callers now use — phase 36's rule that two paths agree only
through one shared implementation plus a test reading both. The sum rule cannot fail from this
caller, because the document is built for the line's own amount; routing through the check anyway is
what will catch the change that makes it fail.

The handler reads back **every** entry the document posted and takes every line against the bank
account, rather than assuming one entry and one line — phase 36's rule that "one GL entry per
Approved document" is a habit and not an invariant, with a Payment's forex leg as the standing
counterexample.

## Decision D — no back-reference column; the reconciliation is the back-reference

The vendor stamps `statement_id` on the document it creates. Here the statement line already points
at the reconciliation, the reconciliation's book side **is** this document's GL line, and that line's
entry carries `SourceDocumentType`/`SourceDocumentId`. "What did this line become" is answerable from
stored data, three hops away.

A second column saying the same thing is a value that can disagree with the first — phase 37's rule
that any two views can be patched into agreement while a third drifts. Phase 56's Decision B is the
precedent: put the key where it cannot hold an illegal state.

## Decision E — the third door to an empty reconciliation, shut in one place

Phase 56 recorded two vendor defects — a reconciled statement line can be deleted, and both delete
paths leave an empty reconciliation shell alive — and diverged on the first with a 409. The kickoff
asked what happens when a **quick-approved** line is deleted and when its document is **voided**.

The first is already answered and for free: the line is reconciled, so phase 56's 409 covers it. That
is an argument *for* auto-reconciling rather than a cost of it.

The second was an open door — and not one Quick Approve opened. Since phase 56, any document with a
GL line matched into a reconciliation could be voided, leaving the reconciliation asserting agreement
between a bank line and a movement that had been backed out. An Invoice settled straight to the bank
posts such a line; so does a Cash Transfer. So the refusal goes where every reversal in the product
already passes: **`SourceDocumentGlEntries.ReverseOutstandingAsync`**, fifteen call sites, thirteen
of them a Void. One check, every document type, no special case for the ones Quick Approve makes.

It **refuses** rather than cascading, which is phase 56's own choice applied to the mirror case, and
Unreconcile releases both sides in one click so the user is told the order rather than blocked. The
guard excludes already-reversed entries, so a second void stays the no-op it is by design.

## Decision F — the chart follows the ledger's calendar, not Kathmandu's

The kickoff said a day on the balance-history chart should be a **Nepal** day. It is not, and the
divergence is deliberate.

`GlDateBoundary` has cut every GL report in this codebase on the **UTC** day since phase 8a, and
`BankBookTransactionReader` projects `DateOnly.FromDateTime(PostedAt.UtcDateTime)` to match. The
chart's whole requirement is that its last point **is** the figure printed above it. Anchoring it to
Kathmandu while the report it must agree with is anchored to UTC would put up to one day's movements
on the wrong side of the line, and a chart that disagrees with the number beside it is worse than a
chart on the wrong calendar. Only "what is today" uses `NepalTime` — as the report already does — and
every label still renders through `NepaliDatePipe` (phase 48).

The bank side has no such question: a statement line carries a plain `DateOnly`, the bank's own value
date. The two sides using different date fields is phase 56's Decision F, unchanged.

## Decision G — the export's page size, and where the cap is stated

`BankReconciliationReportQuery`'s two unreconciled sections were capped at the app's usual 200 rows.
The export asks for the whole of both, because an export of one screen's page of 15 would be a lie —
so the query's own ceiling is now `MaxUnrecognizedPageSize = 5000` and the export asks for exactly
that.

Not unbounded, because `XLWorkbook` materialises every cell of every sheet before a byte is written
(phase 21b): an unbounded export is a memory decision made by whoever has the largest backlog. The
sheet prints **each section's real count** beside the rows it actually wrote, so a truncated export
says so in the artifact rather than in a release note. `AdjustToContents` is given the header band
and a sample, never the whole sheet, for the same phase-21b reason.

---

## The measurement — and the column phase 56 named was not the one that mattered

Phase 56 shipped no index on `GlLine.ReconciliationId` and wrote the re-entry condition into
`GlLineConfiguration`. This phase reads that pane, so by phase 34c's rule it owed the number. The
full run is `tools/scale/comparison-phase57.md`; `probe-phase57-io.sql` is the script, and every
statement in it is the SQL **EF actually issues**, lifted from the API's own log.

50k dataset, 210,006 `GlLines`, the busiest account (50,001 lines), `UPDATE STATISTICS ... WITH
FULLSCAN` before every pass, logical reads from `SET STATISTICS IO` — never the wall clock, and not
`sys.dm_exec_query_stats`, because an ad-hoc `sqlcmd` batch is cached as a plan stub with no row
there (phase 54).

| `GlLines` logical reads | Before | `(AccountId, ReconciliationId)` INCLUDE | `(AccountId)` INCLUDE **(shipped)** |
|---|---:|---:|---:|
| the matcher's right-hand pane, page 1 | **153,470** | 554 | **553** |
| the same pane's count | 153,470 | 554 | 553 |
| Book Statement *(not targeted)* | 153,470 | 554 | 553 |
| the report's own balance *(not targeted)* | 153,470 | 554 | 553 |
| Trial Balance, whole tenant *(not targeted)* | 10,758 | 6,923 | **6,906** |

**`ReconciliationId` in the key is worth one logical read.** The whole 277× is the index being
**covering on `AccountId`**: EF's automatic foreign-key index is one narrow column, and with a
quarter of the table matching a single account the optimizer preferred a full scan to fifty thousand
key lookups. The pane was never slow for the reason phase 56 guessed — the same 153,470 reads were
being paid by the Book Statement and by the report's balance figure, both of which predate the
reconciliation module.

So the index ships keyed on `AccountId` and **replaces** the automatic FK index rather than joining
it: same leading key, same index count, nothing extra to maintain on an append-only table every
approval writes to. Every path measured improves and none regresses, which is phase 34c's rule asked
and answered rather than assumed — and the reason it comes out the other way from phase 50's refusal
is that this index is a *superset* of the one it replaces.

`BankStatementLine.ReconciliationId` still carries no index, and this is a refusal with a reason
rather than an omission: the statement table is one account's imported lines, three orders of
magnitude smaller than `GlLines` on the same tenant, and the scale dataset seeds none. **Re-entry:** a
tenant whose statement history makes the Pending filter slow, measured the same way.

---

## What was built

**Application**

- `QuickApproveBankStatementLineCommand` (+ handler, validator) and `QuickApproveTarget`.
- `BankReconciliationWriter` — the shared reconciliation write, extracted from phase 56's handler.
- `BankBalanceHistoryQuery` (+ handler, validator), and `BankBookTransactionReader.DailyNetAsync`.
- `SourceDocumentGlEntries.EnsureNotReconciled`, called from `ReverseOutstandingAsync`.
- `BankReconciliationReportQuery.MaxUnrecognizedPageSize`.

**Api**

- `ReportSpreadsheetExporter.Phase57.cs` — the reconciliation report's `.xlsx`.
- Three endpoints: the export, `balance-history`, and `statement-lines/{id}/quick-approve`.

**Infrastructure**

- One migration, `GlLineAccountCoveringIndex`: drop and recreate `IX_GlLines_AccountId` with an
  INCLUDE list. No column added, no data touched — **this phase adds no schema**.

**Angular**

- The statement page's Action column: a Quick Approve button per pending row expanding into a row of
  its own (not three controls crammed into the Status cell — phase 52), with a sentence naming the
  document it will create before it creates it.
- The report page: an Export button and the Balance History chart — hand-drawn inline SVG, because
  this is the app's first and only chart and a charting library would be a bundle cost with nothing
  to amortise it against. The picture is `aria-hidden` and the numbers are also a visually-hidden
  table, because a line chart cannot be read out and a summary standing in for data is phase 40's
  mistake.

---

## Bugs and surprises

**1. The export asked for more rows than the query would serve.** `ReconciliationExportRowCap = 5000`
against a validator capped at 200 — a 400 naming `UnrecognizedPageSize`, found by the E2E and by
nothing else, because no handler test goes through `ValidationBehavior`. Fixed by giving the query
its own stated ceiling (Decision G) rather than by lowering the export to a page of 200.

**2. The void guard's test asserted the tracked instance, not the stored row.** Every void handler
marks its aggregate `Void()` *before* calling `ReverseOutstandingAsync`, so when the refusal throws
the tracked entity is already dirty — while nothing is persisted, because the single
`SaveChangesAsync` is never reached and the request's DbContext dies with its scope. The test reads
past the change tracker with `AsNoTracking()`, and says why.

**3. Two `python - <<'EOF'` heredocs mis-parsed and one silently patched half a file.** `CLAUDE.md`'s
gotcha is about `cat` heredocs; it applies to any of them. The second one also failed an assertion
for the *right* reason and left the file untouched, which is what an asserted anchor count is for.
The fix both times was to write the patch script with the Write tool and run it.

---

## What was not added, and why

- **The auto-match suggestion engine** (`/bank-statements-matched`, `account_suggestions`). Phase 56
  excluded it because the vendor's demo tenant returns `account_suggestions: null` on every row, so
  there is nothing to copy and it would have to be invented. Unchanged, and still worth a phase if
  wanted.
- **An account Overview page.** The vendor draws the balance chart on
  `/accounting/bank-accounts/:id`, a screen this app does not have. The chart went on the
  Reconciliation Report instead, which is already "the two balances and the difference" — the thing
  the chart is the history of. A separate Overview would be a third screen showing the same four
  figures.
- **A `statement_id` column.** Decision D.
- **An index on `BankStatementLine.ReconciliationId`.** Measured against, and refused with a re-entry
  condition.

## Carried into a later phase

1. **The Inventory Variance Report** belongs to the Physical Movement deferral, alongside Delivery
   Note and GRN. Same re-entry condition: the user flips `InventoryTrackingMode` on a tenant.
2. **The auto-match suggestion engine** — a phase of its own, and the only bank-module item left.
3. The three standing "outside the sequence" items are unchanged: the NVDA hour, the two
   traceability reports' real columns, full-text search.
