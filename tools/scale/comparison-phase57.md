# Phase 57 — the index debt phase 56 wrote down, measured

Phase 56 shipped bank reconciliation with **no** index on `GlLine.ReconciliationId` or
`BankStatementLine.ReconciliationId`, and wrote the re-entry condition into `GlLineConfiguration`:

> RE-ENTRY CONDITION: the matcher's right-hand pane measured on tools/scale's 50k-invoice dataset,
> on logical reads and never the wall clock (phase 50).

Phase 57 reads that pane (Quick Approve creates the document that appears in it), so by phase 34c's
rule — an unmeasured plan change is re-measured by whoever next touches the area — this is the phase
that owed the number.

**The number says the debt was about the wrong column.**

## Method

`probe-phase57-io.sql`, run with `sqlcmd`. Every statement in it is the SQL **EF actually issues**,
lifted from the API's own log while driving
`GET /bank-accounts/{id}/book-transactions` against the 50k dataset — not a hand-written
approximation of the shape.

- Tenant `A792C471-…`, the `tools/scale` 50k-invoice dataset: **210,006 `GlLines`** over 70,002
  entries.
- Account `37C85E54-…` (*Accounts Receivable*, **50,001 lines** — the busiest in the tenant). It is
  not a Bank account, which the predicate cannot tell apart, and it is strictly worse than any bank
  account a real tenant would have.
- `UPDATE STATISTICS accounting.GlLines WITH FULLSCAN` before every pass (phase 34c: statistics
  quality on a bulk-`INSERT`-seeded database moves a join by 2×, more than most changes under test).
- **Logical reads**, from `SET STATISTICS IO`, never the wall clock. Wall time on this machine moves
  3–4× between two passes of an identical configuration (phase 50), and an ad-hoc `sqlcmd` batch is
  cached as a plan *stub* with no `sys.dm_exec_query_stats` row, so `SET STATISTICS IO` is the
  instrument here rather than the DMV (phase 54).

## Result — `GlLines` logical reads

| Path | Before | `(AccountId, ReconciliationId)` INCLUDE | `(AccountId)` INCLUDE **(shipped)** |
|---|---:|---:|---:|
| **A** — the matcher's right-hand pane, page 1 (`reconciled=false`) | **153,470** | 554 | **553** |
| **A2** — the same pane's count | 153,470 | 554 | 553 |
| **B** — Book Statement, the same screen with no reconciled filter *(not targeted)* | 153,470 | 554 | 553 |
| **C** — the report's own balance, a store-side sum over the account's history *(not targeted)* | 153,470 | 554 | 553 |
| **D** — Trial Balance, every account in the tenant *(not targeted)* | 10,758 | 6,923 | **6,906** |

`GlJournalEntries` is 747 reads on every path in every configuration — unchanged, and not the
subject.

## What it means

**`ReconciliationId` in the key is worth one logical read.** 554 against 553. The entire 277× is the
index being **covering on `AccountId`**: EF's automatic foreign-key index is one narrow column, and
with a quarter of the table matching a single account the optimizer preferred a full scan of
`GlLines` to fifty thousand key lookups.

**The pane was never slow for the reason phase 56 guessed.** The same 153,470 reads were being paid
by the Book Statement and by the report's own balance figure — both of which predate the
reconciliation module — so this was a pre-existing cost of reading one account's postings, not
something phase 56 introduced. That is the part worth carrying forward: *the column a phase writes
down as the suspect is a hypothesis, and the measurement is entitled to answer a different
question.*

**So the index ships keyed on `AccountId` and replaces the automatic FK index** rather than joining
it: same leading key, same index count, nothing extra to maintain on an append-only table that every
document approval writes to. `ReconciliationId` rides in the `INCLUDE` list because the pane selects
it.

**Every path measured improves and none regresses**, including the two this phase did not target —
which is phase 34c's rule (an index added for one access path changes the plan for every other path
on the same table) asked and answered rather than assumed. Phase 50's refusal came from exactly that
check, when a reasoned-about index took a sibling tab from 2,143 reads to 83,308; this one does the
opposite, and the difference is that it is a *superset* of the index it replaces.

## Not measured, and why

`BankStatementLine.ReconciliationId` still carries no index. The statement table is one tenant's
imported bank lines for one account — three orders of magnitude smaller than `GlLines` on the same
tenant — and the scale dataset seeds none at all, so there is nothing here to measure against.
**RE-ENTRY CONDITION:** a tenant with a statement history large enough for the Pending filter to be
slow, measured the same way.

## Reproducing

```bash
sqlcmd -S 'DESKTOP-H0R00ME\SQLEXPRESS' -d ErpApp -E \
  -Q "SET QUOTED_IDENTIFIER ON; UPDATE STATISTICS accounting.GlLines WITH FULLSCAN;"

sqlcmd -S 'DESKTOP-H0R00ME\SQLEXPRESS' -d ErpApp -E -i tools/scale/probe-phase57-io.sql \
  -v ORG="A792C471-03D7-489D-B890-EC5137105BB3" ACCT="37C85E54-B43D-4F75-B821-B6D60F1CAC88"
```

Run it from `tools/scale/` — `sqlcmd -i` chokes on a forward-slash absolute path (phase 36).
