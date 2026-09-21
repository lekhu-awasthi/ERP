# Phase 56 — bank reconciliation

## TL;DR

**The two-pane N:M matcher, and the live read settled six things inference could not.** The
right-hand pane is `/gl-transactions` — **GL rows**, not the document-level `/transactions` phase 55
recorded as the other candidate, which none of the six screens calls. N:M is real: a 1:2 and a 2:2
were both driven live and both came back with **one** `reconciliation_id` on all four rows. The gate
is **sum equality**, and it is enforced by the *server*: 678 against 113 answered
`400 "transactions cannot be reconciled"`. Unreconcile is `DELETE /bank-reconciliations/:id` and
releases both sides. `/bank-statements-matched` is **not** the reconciled list — it stayed empty
while a reconciliation existed — it is the auto-match **suggestion** queue.

**Six decisions.** **A — a reconciliation joins `GlLine`s, not documents**, because a bank statement
line corresponds to one movement of money and only the line is one movement. **B — membership is a
nullable key on each side, not a link table**, because a key cannot express a row in two
reconciliations while a link table can and would need a rule. **C — it posts nothing**, and the E2E
proves the GL is byte-identical before and after. **D — no new permission keys**: reconciling rides
phase 55's `Accounting.BankStatement.Manage`, reading rides `.View`. **E — a reconciled statement
line cannot be deleted** (a 409), which diverges from the reference product deliberately. **F — the
report is one as-of date**, not a range.

**The phase's real bug was found by the E2E and nothing else could have found it.** The reader first
exposed `IQueryable<BookMovement>` and handlers ordered over it. Every handler test passed; SQL
Server returned **500**, because EF cannot translate `OrderBy(x => new BookMovement(…).PostedAt)`.
That is the fourth trip through the door phases 25, 34b and 42 each found. The fix is structural —
the reader hands back answers, never a query — and `BankBookTransactionReaderShapeTests` now pins
that shape in a suite that runs without Docker.

**A guard was taught rather than exempted, and one of its claims was falsified.**
`SearchSweepGuardTests` asserted a coupling with no exit — every date-ranged list is searchable —
which was a fact about the fifteen lists that existed when it was written, stated as a law. It now
honours `Exempt`.

Tests: Domain **738 (+16)**, Application.UnitTests **1343 (+23)**, Infrastructure.UnitTests
**13 (+1)**, Angular **613 (+8)**. `dotnet build` / `ng build` / `ng test` clean; `ng build` does not
warn and the bundle is **644.64 kB** against the 680 kB budget. One migration, purely additive: two
nullable `AddColumn`, one `CreateTable`, one `CreateIndex`, and **no drop of any kind in `Up`**.

---

## Step 1 — the live read

Cadehi (`cadehi.tigg.app`), Cash In Hand. The user logged in; no credentials were entered. **Three
statement lines were deliberately committed and then deleted**, authorised by the user before the
write — the phase-52/54 precedent. The tenant ends the pass with 0 statement lines and both Invoices
unreconciled, i.e. as it was found. The full read is the phase-56 appendix in
`docs/erp-module-scan.md`; what it settled:

| Question the kickoff left open | Answer |
| --- | --- |
| Which feed is the right-hand pane? | **`/gl-transactions`** — a GL row against the account, carrying its source document's `code`, `reference_no` and business `date`. `/transactions` is used by **none** of the six screens. Book Statement calls the same endpoint without the `reconciled` filter. |
| Is N:M real, or does the button enable at 1:1? | **Real.** 1:2 and 2:2 both accepted, one `reconciliation_id` shared by every row on both sides. |
| What does RECONCILE post, and what gates it? | `POST /bank-reconciliations {account_id, bs_ids[], tx_ids[]}`. The gate is **the two selected sums being equal, with at least one row each side** — transcribed from its bundle *and* probed: an unequal pair is a **400**. |
| What is `/bank-statements-matched`? | The **suggestion queue**, not the reconciled list: `total: 0` while a reconciliation existed, and its screen reads "Reconcile Selected / Reconcile All / Go to Manual Reconciliation". |
| `account_suggestions` / `is_suggestion_completed`? | The same engine. **Out of scope, decided rather than inherited** — see below. |
| `/balance-history/:id` and `/reconciliation-report`? | A per-day `{day, tigg_balance, bank_balance}` series, and six scalars rendered as a four-row statement as of one date. |

**Two vendor defects, probed and not reproduced.** A **reconciled statement line can be deleted**
(the cascade is at least correct — both book rows were released), and both that delete and
`DELETE /bank-reconciliations/:id` leave the reconciliation row alive holding two empty lists and
`reconciled_at: "0001-01-01T00:00:00Z"`. So the vendor accumulates empty shells.

**One thing the read found that nobody had asked about.** "Select account" on the account Overview's
unmatched table is not part of matching: it is **Quick Approve**, which turns a statement line into a
Customer Payment / Supplier Payment / Quick Receipt / Quick Payment depending on the chosen account's
type, tying the new document back with a `statement_id`. This codebase has all four of those
documents already (phase 17), so it is a real future phase rather than a gap. Recorded and
explicitly excluded.

---

## Decision A — a reconciliation joins GL lines, and this was the phase's real question

The kickoff said so: *"decide whether a reconciliation joins to **documents** or to **GL rows**, and
say what that costs the report. Getting this wrong is not a bug you find in a test."*

**It joins `GlLine`.** What a bank statement line corresponds to is one movement of money into or out
of one account, and that is exactly what a GL line against that account is. A *document* is too
coarse in both directions — phase 36 established that one document can post more than one entry, and
a document may touch the bank account twice or not at all — and an *entry* is too coarse for the same
reason one level down. The reference product agrees, which is a check rather than the argument.

**What it costs, stated here rather than discovered later.** `GlJournalEntry` stores no copy of its
document's number, reference or business date (phase 26a), so:

1. the code and reference need a join back across the thirteen GL-posting types. That is
   `GlSourceDocumentResolver`, which already exists, and reusing it is why
   `BankBookTransactionReader` exists rather than three screens each writing the join.
2. **the date is the posting date**, not the document's. The reference product shows a business date
   because its GL row denormalises one; ours cannot. This is *not* a new divergence — the Journal
   report, the Detail General Ledger and the GL Master Report all already show and filter on
   `PostedAt` — and phase 26a's rule is that a report must show the same date field it filters on,
   which this does. Every column on every screen here is labelled **Posted**.

The visible consequence, in the browser pass: a receipt raised on 16-09 shows as posted 21-09,
because that is when it was approved. That is the honest answer and the screens say which date it is.

---

## Decision B — membership is a nullable key on each side, not a link table

`BankReconciliation` carries **only who and when**. `BankStatementLine.ReconciliationId` and
`GlLine.ReconciliationId` are the membership — the reference product's own shape, confirmed by both
sides of a 1:2 and a 2:2 match coming back with the same id.

**It is the shape and not a shortcut.** A nullable key cannot express a row belonging to two
reconciliations; a link table can, and would then need a unique index to forbid it. This codebase has
twice preferred the model that cannot hold the illegal state over the model that holds it and is
policed — `ProductBatch` stores no quantity (51), `PrimaryQuantity` is never a column (52).

**And the aggregate has no amount**, which is the kickoff's rule applied to the other side: a stored
"matched amount" could disagree with the sum of what it joins. The amount is computed from the rows
in `GetBankReconciliationQuery` and returned by the create command, never stored.
`A_reconciliation_carries_only_who_and_when` asserts the absence with its premise beside it.

**A mutable column on an append-only fact row, argued rather than assumed.** What makes `GlLine`
append-only is its *money*: `Debit`, `Credit` and `AccountId` stay write-once and a correction is a
new entry (phase 16a). `ReconciliationId` records nothing about the posting — it records that
somebody compared this movement with an external record. **A reversal does not inherit it**, unlike
`GlJournalEntry.LocationId`: a location says where a posting happened and a reversal happens in the
same place, while a reconciliation says this movement was seen on a statement, and a reversing
movement was not.

**What a Void of a reconciled document does: nothing, deliberately.** Void posts a second, reversing
entry rather than mutating the original (phase 16a), so a reconciled line survives intact and no key
dangles. The reversal appears on the matcher as a new unreconciled movement and the report's
Difference moves by its value — which is the signal the user needs. Refusing the Void instead would
be an interaction nobody has posed (phase 45) and would reach thirteen handlers. **Re-entry
condition:** a user reporting that a voided document left a reconciliation they cannot interpret.

---

## Decision C — it posts nothing, and the E2E proves it rather than asserting it

A reconciliation is a statement *about* two records, not a third record. If it posted it would move
the very balance it exists to compare, and the reported difference would become a function of how
much reconciling had been done.

The E2E's central `sqlcmd` block, before and after a 1:2 match:

```
GlJournalEntries,2      GlLines,4      SumDebit,791      SumCredit,791      CashAccountBalance,791
```

and the report either side of the same match:

```
before   bank=1582.0 book=791.0 difference=791.0 | unreconciled bank 1582.0/3 book 791.0/2
after    bank=1582.0 book=791.0 difference=791.0 | unreconciled bank  791.0/2 book    0/0
                                                   the two balances did not move: True
```

That is the claim in one frame: **the unreconciled figures move, the balances do not.**

Because it posts nothing, nothing else applies either: no document number, no Draft/Approve/Void, no
approval queue, and **not lock-date sensitive** — reconciling a period is something you usually do
*after* closing it, and nothing written here changes a figure the lock date protects.

---

## Decision D — no new permission keys

The 2026-09-20 census found the whole reconciliation module carries no keys beyond
`bank-reconciliation-export`, riding `bank-view` / `bank-edit`; phase 55 derived
`Accounting.BankStatement.View`/`.Manage` from that same `bank-edit`. So:

- **reconciling and unreconciling ride `.Manage`**, reading rides `.View`.
- **the report rides `.View`, and its export would too.** The vendor's only reconciliation-specific
  key is on the *export* — but this codebase has **no "report Export" key to follow as a precedent
  anywhere**, which `PermissionKeys` says in as many words where the export-job keys are declared.
  An export is gated by the report it exports.

Minting a third key for a second act on the same data by the same person is the "new family by
reflex" the roadmap warned against, and there is no tenant who would want a bookkeeper able to load a
statement but not to say it agrees with the books. **No new keys means no permission-seed migration**,
which is the whole of the saving.

---

## Decision E — deletion, and the guard phase 55 promised

`DELETE /bank-reconciliations/:id` releases both sides and **removes the row**. The whole
reconciliation is the unit: its one invariant is that its two sides total the same figure, so
removing one row from either side would leave a record breaking the only rule it had.

**A reconciled statement line cannot be deleted — a 409 naming the count.** This is a deliberate
divergence: the reference product deletes one happily, and its own behaviour is the argument against
copying it. The undo here is *undo this import*, one click over a whole file, and letting that
silently dissolve reconciliations made afterwards is exactly the invisible side effect this codebase
rules against. The user unreconciles first, which is one extra step and is the step where they see
what they are undoing. Both directions are in the E2E, including the import undo succeeding once
nothing is reconciled.

---

## Decision F — the report is one as-of date, and the two sides use different date fields

Four figures plus the two lists that explain them, as of a single date — the live filter bar carries
one Date control, not a range, which is right for the subject: a reconciliation report answers "do
the two records agree today", and a balance is cumulative.

**The book side cuts off on `PostedAt` and the bank side on the statement line's own `Date`**, and
that is not an inconsistency to iron out: they are two records kept by two different people, which is
the entire premise of reconciling them. Each row on the screen says which date it is using
(*"Posted up to…"* / *"Bank-dated up to…"*).

**Both sides come from readers the matcher also uses**, so the report's *Unreconciled* figures are
the sums of exactly the rows the matcher offers — by construction, not by coincidence.
`The_report_and_the_matcher_cannot_disagree` drives all four on one dataset and asserts they agree
before *and* after a reconciliation, which is phase 36's rule (two reports agree only through one
shared reader **plus** a test reading both on the same data).

---

## The bug the E2E found, and why nothing else could have

`BankBookTransactionReader` first exposed its joined query already projected, as
`IQueryable<BookMovement>`, and each handler ordered, counted and summed over that. Every handler
test passed. Against SQL Server the list endpoint returned **500**:

```
The LINQ expression '… .OrderByDescending(ti => new BookMovement(…).PostedAt)'
could not be translated.
```

EF cannot order by a member of a record the client would have to construct. InMemory evaluates the
whole expression in C# and never notices.

**This is the fourth trip through one door**: phase 25 (a captured `Func` in a validator), phase 34b
(a static call in a predicate), phase 42 (a `Sum` over an already-projected record), and now an
`OrderBy` over one. Each time the symptom was the same — green unit tests, a 500 only an E2E sees.

**The fix is structural rather than three careful rewrites.** The reader now hands back *answers* —
`CountAsync`, `SumAsync`, `PageAsync`, `ForReconciliationAsync`, `DescribeAsync` — orders and pages on
the entities' own columns, and projects only after `ToListAsync`. There is no shape a caller can
compose that reintroduces the fault. The joined pair is a small class with two entity-typed
properties rather than a positional record, and **both halves of that are load-bearing**: EF sees
through `x.Entry.PostedAt` as a column, while a record's constructor in an intermediate operator is
precisely what it could not translate.

`BankBookTransactionReaderShapeTests` asserts no public member returns an `IQueryable`, with the
premise half beside it. That pins the *structure*, not the symptom (phase 50's rule), and it runs in
a suite that does not need Docker.

---

## The guard that was taught, and the claim that was falsified

`ListBookTransactionsQuery` reached both sweep guards, and neither outcome was an exemption of
convenience.

**Sort:** it is exempt, and the reason is a fact about the data rather than about the harness. The
rule is that an ordering may be offered exactly where an index leads on
`(OrganizationId, <column>)`, which for a document means two: `CreatedAt` and its business date. **A
GL posting has one date** — phase 26a — and no `CreatedAt` beside it, so a *Sort by* menu here would
be a control with nothing to choose between, which is the outcome the rule exists to produce. The
reference product's control on these panes is a *direction* toggle, a different control this codebase
has nowhere. The premise is asserted independently in
`ListSortIndexCorrespondenceTests.A_gl_posting_has_exactly_one_date`, so the reason cannot quietly
stop being true — phase 54's rule that a refusal asserted in the negative needs its premise asserted
too.

**Search:** exempt for the rule's own reason (a panel already scoped to one parent row) plus a cost —
the one field worth searching is the document number, which `GlJournalEntry` deliberately does not
store, so matching it means a thirteen-way join before the page is formed. That is phase 50's
`ListChequesQuery` finding with twelve more tables. **Re-entry condition:** a phase that
denormalises the code onto the entry.

**And one guard's claim turned out to be false.**
`Every_date_range_query_is_also_searchable_and_carries_a_business_date` asserted a coupling with *no
exemption path*: every date-ranged list must be searchable. That was a fact about the fifteen lists
alive when it was written, stated as a law. It now honours `Exempt` and is renamed
`…_or_states_why_not`, which keeps all of its force — a date-ranged list must still either take a
term or give a reason — while removing a claim this phase falsified. Phase 55's own lesson is to
teach the guard rather than exempt the screen; the corollary is that a guard shown to be wrong should
learn, not acquire a special case with no reason attached.

---

## What was not added, and why

**No index.** Neither `GlLine.ReconciliationId` nor `BankStatementLine.ReconciliationId` is indexed,
because nothing has measured them. Every read of either is *already* seeking on the account — the
module never asks "what is reconciled?" across the chart of accounts, only "what is still unmatched
on this bank account" — so the reconciliation state is a residual predicate over a set the account
has narrowed. `GlLines` is large on any real tenant and is read by eleven reports, and phase 34c's
rule is that an index added for one path changes the plan for every other path on the table; phase 50
restated it after a reasoned-about index took a sibling tab from 2,143 logical reads to 83,308.
**Re-entry condition**, written into `GlLineConfiguration`: the matcher's right-hand pane measured on
`tools/scale`'s 50k-invoice dataset, on logical reads and never the wall clock.

**So this phase owes no performance number**, and the reason is the same one phase 55 gave: its only
new index is the convention-shaped `(OrganizationId, BankAccountId)` on a table it creates.

**No foreign-key constraints on either membership column.** The delete path releases both sides in
one `SaveChanges`, so a constraint would buy nothing a handler is not already doing — while a cascade
would mean deleting a reconciliation deletes GL lines, which is the one thing that must never happen
to an append-only fact row.

**The suggestion engine is out of scope**, decided rather than inherited: it is its own screen with
its own verbs on the same endpoint (`action: "approve"`, `all: true`), and it needs a matching
*algorithm* this phase has no evidence for. **Quick Approve is out of scope** for the same reason and
is the more interesting of the two, because every document it creates already exists here.

**No `.xlsx` export of the report.** The vendor has one (`bank-reconciliation-export`). It is the one
thing in the module this phase did not build, and it is listed as carried rather than quietly
dropped — the report's DTO already carries every figure and both lists, so it is a
`ReportSpreadsheetExporter` call and nothing else.

---

## Manual E2E

Against real SQL Server on a **fresh Organization**, seeded by direct API calls, every status code
printed. `scratchpad/e2e56.py` and `e2e56_403.py`.

Seeded to mirror the shape driven live: one cash account, two approved Journal Vouchers of 678 and
113 debiting it, and a statement carrying 678, 113 and 791 imported through phase 55's own importer
(so the two phases are exercised together).

```
-- NEGATIVE: an unbalanced selection is a 400 naming both totals --
  [400] reconcile 678 against 113
        {"StatementLineIds": ["… must total the same amount. The bank side totals 678.00
                               and the book side totals 113.00."]}

-- 1:2 --
  [200] reconcile 791 against 678+113     reconciliation … amount=791.0 (1 bank, 2 book)

  [409] reconcile an already-reconciled line   "2 of the selected transactions are already reconciled."
  [409] delete a reconciled line               "1 … have been reconciled and cannot be deleted."
  [409] undo the import                        (the same guard, over a whole file)
  [404] reconcile across accounts              "One or more bank statement lines were not found…"
```

**The claim that matters, in SQL:**

```
Reconciliations,1   StatementLinesReconciled,1   StatementLinesPending,2   GlLinesReconciled,2

GlJournalEntries,2  GlLines,4  SumDebit,791  SumCredit,791  CashAccountBalance,791   <-- unchanged

bank:PROBE C,791    book:JournalVoucher,678    book:JournalVoucher,113   <-- exactly what was ticked
```

Then unreconcile: `released 1 statement line(s), 2 book row(s)`, and afterwards
`Reconciliations,0  StatementLinesReconciled,0  GlLinesReconciled,0  StatementLinesStillThere,3` —
**unreconciling is not deleting**. Followed by a 2:2 match, a second unreconcile, and the import undo
now succeeding.

**The negative path**, three legs plus two controls, one user, one organization, one run:

```
[404] DELETE a nonexistent reconciliation (Admin)       "Reconciliation not found."
[403] DELETE the SAME nonexistent id (restricted)       "…(Accounting.BankStatement.Manage)."
[403] RECONCILE as the restricted user                  the same key
[200] GET the statement / the book feed / the report    same user, same run
[404] DELETE the SAME nonexistent id (Admin again)      back to 404, not 403
```

403-not-404 against a nonexistent id proves `AuthorizationBehavior` fired *before* the handler looked
for the row, and the three 200s prove the restricted role is a real role rather than a broken one.
The membership is restored by `UPDATE tenancy.OrganizationMemberships` (phase 52: the API cannot undo
it), because phase 51's lesson is that a run ending with its own user restricted leaves the browser
pass looking at a broken app.

**Browser pass**, on all four screens. The Bank Accounts card now offers Statement / Reconcile / Book.
The matcher rendering `678.00` against `113.00` with **"Out by 565.00 — the two sides must total the
same"** and the button dead; then `791.00` against `791.00`, *1 selected* against *2 selected*, and
the button live; then **"Reconciled 1 statement line against 2 transactions."** with the matched rows
gone from both panes and the books pane reading "Nothing left to reconcile". The statement list's
Status column with one Reconciled badge and three Pending, the Reconciled filter narrowing to one
row, and the badge linking through to a detail page reading *"791.00 matched · Reconciled by Phase16c
Tester"* with both sides and an Unreconcile that returns to a matcher with everything back. The
report as its four rows with Difference highlighted and the expandable section listing the three
unreconciled statement lines. **No console errors.**

---

## Bugs found during the phase

1. **`OrderBy` over a projected record 500s on SQL Server** while InMemory evaluates it in C#. Found
   by the E2E, fixed structurally, pinned by `BankBookTransactionReaderShapeTests`. Its own section
   above.
2. **`SearchSweepGuardTests` asserted a coupling with no exemption path**, which this phase's query
   falsified. The guard now honours `Exempt`.
3. **`listBankStatementLines` typed its options as `ListQueryOptions`**, so the new `reconciled`
   filter would not compile through it — caught by `ng build`, noted because the fix had to widen the
   signature *and* omit the parameter entirely for "All": a `reconciled=` with no value binds as
   `false` server-side and would have silently hidden every matched line.
4. **The import template's headers carry a trailing `**`** to mark a required column (`Date**`), so a
   seed script matching header text exactly finds nothing. One for `docs/e2e-recipes.md`.

---

## Carried into a later phase

1. **The reconciliation report's `.xlsx` export** — the vendor's `bank-reconciliation-export`. The
   DTO already carries every figure and both lists; it is a `ReportSpreadsheetExporter` call.
2. **Quick Approve** — "Select account" plus a tick on the account Overview's unmatched table, which
   creates a Customer Payment / Supplier Payment / Quick Receipt / Quick Payment from a statement
   line and ties it back with a `statement_id`. All four documents exist here already (phase 17), so
   this is a real phase rather than a gap. The full read is in the scan appendix.
3. **The auto-match suggestion engine** — `/bank-statements-matched`, `account_suggestions`,
   `is_suggestion_completed`, and `POST /bank-reconciliations` with `action: "approve"` / `all: true`.
   Read and recorded; it needs a matching *algorithm* nothing here has evidence for.
4. **The balance-history chart** — `GET /balance-history/:id` returns `[{day, tigg_balance,
   bank_balance}]` for the last 30 days and the vendor draws the two lines on the account Overview.
   Derivable from what this phase already reads.
5. **`is_bank_user`** on the vendor's user payload swaps its entire shell. Noticed in phase 55, still
   not investigated.
