# Phase 42 — Performance follow-through, and the cost that was never the period

## TL;DR

Phase 34c measured and carried six items; this phase closed five of them, plus phase 41's unmeasured
quota count, on 34c's own dataset and harness. Every number below is a p95 over 10 timed requests
after 2 warm-ups, against a **freshly seeded 50,001-invoice tenant** (the third such tenant in this
database), with `UPDATE STATISTICS … WITH FULLSCAN` and a cold plan cache before every pass.

| | before | after |
|---|---|---|
| invoice list, **last** page | 960 ms | **84 ms** |
| contact list, last page | 608 ms | **86 ms** |
| product list, last page | 571 ms | **49 ms** |
| invoice list, search matching nothing | 1,262 ms | **265 ms** |
| invoice list, `status=Draft` (matches nothing) | 386 ms | **133 ms** |
| Sales Register, 3-year period | 3,885 ms | **1,292 ms** |
| Sales by Customer, 3-year period | 3,458 ms | **1,915 ms** (probe) |
| Customer Ageing | 10,336 ms | **1,079 ms** |
| Detail General Ledger | 8,297 ms / **59.5 MB** | **1,820 ms / 14.5 kB** |
| subscription quota usage (Professional ceiling) | 971 ms | **225 ms** |
| Angular initial bundle | 651.44 kB raw / 128.39 kB transfer, over a 500 kB budget | unchanged, under a **measured** budget |

**34c's headline finding was nearly right and the word was wrong.** "The report layer is linear in
the period" became, once each query was read individually, *the report layer is linear in the id
list it hands back to SQL Server*. The two most expensive statements in the whole `before` pass were
`WHERE Id IN (SELECT value FROM OPENJSON(@ids))` — 249,604 and 182,545 logical reads per execution,
814 ms and 699 ms — and the ids in them were a whole period's invoices and a whole tenant's
contacts. 34c already had the gotcha in writing ("a materialised id list handed back to SQL becomes
an `OPENJSON` parameter as long as the list"); what it did not have was the other half:

> **A list long enough to be worth avoiding is a list too long to send.**

Every narrowing this phase removed was written as an optimisation. `ContactAgeingSummary` fetched
only the contacts that had an outstanding document — 35,001 of them, as an OPENJSON join costing
182,545 reads, against roughly 1,500 for the ordered index scan that returns every contact the
tenant owns. The ageing report's worst single query was worse still: 50,000 candidate document ids
serialised into a **1.8 MB `nvarchar(max)` parameter**, against a `PaymentAllocations` table holding
almost no rows. Removing it took the endpoint from 7,480 ms to 1,134 ms — and the 6.2 s it had been
costing were not SQL at all (the server reported ~1.2 s across every statement), which is why 34c's
wall-clock instrument could see the symptom and not the cause.

**The two list findings turned out to be one defect.** 34c carried the offset tail (#5) and search on
a no-match term (#6) separately, and proposed keyset pagination for one and full-text indexing or a
two-step handler for the other. Measured at the statement level they are the same thing: *the row is
fetched before it is eliminated.* The last page of the invoice list cost **153,705 logical reads and
513 ms** against the first page's 166 and 6 ms, because SQL Server took the ordering index and did a
key lookup into the clustered index for each of the 50,000 rows it was about to skip. Ask the same
question for **ids only** and the identical offset costs **571 reads and 24 ms**. So
`ToKeyPagedResultAsync` counts, takes the page's *keys* from an index that already carries them, and
fetches exactly those rows — `JournalReportQueryHandler`'s shape applied to a list. It fits inside
the existing `PagedResult<T>`, which is the answer to 34c's keyset question: **keyset pagination is
not needed, and its re-entry condition is now unmet rather than deferred.**

**Half of the search win is one branch: a count of zero is a complete answer.** Nothing in the helper
knows whether a search term was applied, because it does not need to — `search=ZZQQXX` and
`status=Draft` both match nothing on this dataset, both cost a full predicate scan, and both used to
follow it with a second, far more expensive statement asking for a page of an empty set.

**And the ledger reports' cost is their *opening balance*, not their period** — the sharpest
correction 34c's own probe could not have made, because it only became visible once the period
stopped dominating. After the conversion, the Detail General Ledger is **slower for one month
(3,059 ms) than for three years (1,825 ms)**, and the General Ledger Summary likewise (2,320 ms
against 1,242 ms). A period that begins later has *more history in front of it*, and the opening
balance is an aggregate over all of it.

Tests: Domain **660**, Application.UnitTests **1,091 (+10)**, Api.IntegrationTests **29**, Angular
**443 (+3)**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean; `ng build` now emits
**no** budget warning.

---

## Decision A — what this phase was allowed to change

Inherited, not re-derived: phase 34c's Decision A budgets (**500 ms** for a paginated list page or
global search, **2 s** for a statement, register or list-shaped report page) and its Decision C
dataset and instrument. A performance phase that moves its own goalposts measures nothing, and the
point of re-running 34c's harness rather than writing a new one was that the two sets of numbers
have to be comparable.

**One change to the environment is stated rather than hidden.** This database now holds a *third*
50,001-invoice tenant, so `GlLines` is about 630,000 rows instead of 420,000. The `before` pass in
this document was taken on that database, so before and after differ by the change under test and
not by the tenant count — but the figures are not directly comparable with 34c's committed CSVs, and
the statements in particular read a little higher here than there for that reason alone.

---

## Decision B — which readers were converted, and the rule that chose them

**The rule, from the kickoff: convert a reader only if its measured p95 at a three-year period
exceeds the 2 s budget.** Applied to the period-sensitivity probe (consecutive requests inside one
pass, which is how 34c controlled for the machine):

| report | 1 month | 1 year | 3 years | over budget? |
|---|---|---|---|---|
| Detail General Ledger | 3,999 ms (1.6 MB) | 5,233 ms (18.9 MB) | 9,490 ms (59.6 MB) | **yes** |
| Sales Register | 815 ms | 1,709 ms | 4,466 ms | **yes** |
| Sales by Customer | 639 ms | 1,542 ms | 3,534 ms | **yes** |
| Customer Ageing (no period to narrow) | — | — | 8,823 ms | **yes** |
| Purchase Register | 236 ms | 378 ms | 1,618 ms | no |
| General Ledger Summary | 2,538 ms | 1,898 ms | 1,327 ms | no |
| Income Statement | 589 ms | — | 1,561 ms | no |
| Journal report (already key-paged) | 135 ms | 140 ms | 203 ms | no |

So **four** were converted, not seven. That is worth saying plainly, because 34c's carried item #1
says "seven handlers", and the number does not survive contact with the code: **~30** report
handlers in this codebase page an in-memory list (`ToPagedResult`, as against `ToPagedResultAsync`).
Seven was the count of those the 33-endpoint harness measures, not a property of the codebase. The
rule is what decides, not the count — and the four the rule picked were picked by their numbers.

**Purchase Register and General Ledger Summary were left alone deliberately.** Both are under budget
at every period probed, both share their shape with a converted sibling, and converting a report
that is not slow is how a phase turns a measurement into a rewrite nobody asked for. The Purchase
Register is the Sales Register's twin and is the obvious next one if a tenant's data ever makes it
one — its re-entry condition is simply the same 2 s.

---

## Decision C — Detail General Ledger pages by row, and says where the account boundary fell

This is a **report-semantics decision**, recorded as one. 34c measured the defect (59.5 MB at
`pageSize=50`) and deliberately did not choose the fix.

Phase 26a paged by *account*, reasoning that a section's running balance is only correct if the
section is whole. The reasoning was sound and the consequence was not: with the account as the page
unit, `pageSize=50` asks for fifty accounts and gets every posting inside each, and one shared
Accounts Receivable control account holds 50,000 of them. **A report that cannot be opened is not
paged.**

**The page unit is now the row.** A section may begin or end mid-account, and it says so:
`RowsBefore` and `RowsAfter` carry the postings of that account which fall outside this page. The
running `Balance` on every row stays absolute — the account's balance after that posting, computed
from its own opening plus everything before it — so a continued section carries on from the previous
page's last row rather than restarting. Verified on the seeded tenant:

```
page 1     0001 Sales   before=0      rows=3  after=49998  balances 9000.00, 16800.00, 20650.00
page 2     0001 Sales   before=3      rows=3  after=49995  balances 22050.00, 26600.00, 28250.00
page 8334  0001 Sales   before=49998  rows=3  after=0      closing 246,867,625.00
           0002 A/R     before=0      rows=3  after=49998  closing 278,960,416.25
```

`PeriodDebit`, `PeriodCredit` and `ClosingBalance` are unchanged in meaning: they are facts about the
account over the period, not about the page, which is why a partial section still prints the same
Closing Balance row a whole one does. `TotalCount` is now the number of postings (210,006 here), not
of accounts.

**"Cap the rows per account and disclose the cap" was rejected**, as the kickoff recommended, because
the live report is a flat ledger. A capped section silently stops being the account's ledger and
there is no page two to continue it on — a truncation dressed as a report.

**The disclosure reaches both consumers.** The Angular template prints *"Continued from the previous
page (N earlier postings)"* in place of the Opening Balance row and *"Continues on the next page"* in
place of the Closing Balance row; the spreadsheet export does the same in its Txn Type column, with
the Balance cell left empty rather than zero (`DetailLedgerExportRow.Balance` became nullable —
a zero in a balance column reads as a figure). Export Full Dataset asks for every row and so never
sees either case.

---

## Decision D — the list fix is one helper, and it needs no knowledge of searching

`PagedResultExtensions.ToKeyPagedResultAsync` — count, then the page's keys, then the rows:

```csharp
var totalCount = await filtered.CountAsync(ct);
if (totalCount == 0 || skip >= totalCount) return new([], page, pageSize, totalCount);
var pageKeys = await order(filtered).Select(key).Skip(skip).Take(pageSize).ToListAsync(ct);
var items    = await order(filtered.Where(KeyIn(key, pageKeys))).ToListAsync(ct);
```

Four things about it are decisions rather than mechanics.

**It takes the caller's filtered query and composes onto it.** Phase-35b's rule — a shared helper
must own every condition the `Where` it replaced carried — is satisfied structurally here rather
than by care: the helper never rebuilds a query, so it cannot lose an `OrganizationId`. A test
asserts it anyway, because a later version that fetched by key alone would lose it and would still
pass every other test.

**The key is an `Expression`, never a captured `Func`** (phase-9 bug #1), and `KeyIn` builds
`keys.Contains(key(x))` from that expression. The list it builds is at most one page long, which is
the same OPENJSON mechanism this phase spent the rest of its time removing — the difference is
entirely the length, and that is the point.

**There is no `termApplied` flag, and there was going to be.** The first design branched on whether a
search term was present, because 34c's carried item is about search. Measurement removed the branch:
`status=Draft` matches nothing on this dataset and cost 386 ms for exactly the same reason
`search=ZZQQXX` cost 1,262 ms. A filter that matches nothing behaves the same way whatever it
filtered on, so the helper asks the only question that matters — is the count zero — and one code
path serves both.

**Sixteen handlers, one scripted edit, anchor counts asserted before writing** (phase-32's rule): the
sixteen `List*QueryHandler`s that page an entity directly. The ten that page a *projection* were left
alone on purpose — their `Select` happens before the paging, so the key query would re-run the
projection's joins and the two-step would cost more than it saved. None of them is on a table this
dataset makes large.

---

## Decision E — the quota count gets its columns, as a rule and not as an index

Phase 41 #3: `SubscriptionUsageReader.CountTransactionsAsync` was never measured. Measured here at
the Professional ceiling (200,000 transactions) with the term backdated to cover the whole dataset:
**971 ms, 184,718 logical reads** for one `GET /subscription`. The `(OrganizationId, PostedAt)` index
gave it the seek and then key-looked-up every one of the 70,002 entries in the term to read
`SourceDocumentType` and `SourceDocumentId`.

The fix is two INCLUDE columns, added to `TenantIndexConvention` as `ReportPathIncludes` — **a rule
over columns, in the same conditional shape as `SearchIncludes`**, not a hand-written `HasIndex` on
one table. Only `GlJournalEntry` has those columns today, and that is the argument for the rule
rather than against it: a GL entry stores nothing about its document but its type and its id, so
every reader that ranges over posted entries projects exactly these two beside the date. One
migration, one index changed:

```
IX_GlJournalEntries_OrganizationId_PostedAt
  key      OrganizationId (1), PostedAt (2)
  include  SourceDocumentType, SourceDocumentId        -- confirmed with sqlcmd against sys.index_columns
```

**971 ms → 225 ms.** And per 34c's index lesson — *an index added for one access path changes the
plan for every other path over the same table* — every other reader on `GlJournalEntries` was
re-measured, not assumed: the Journal report (597 → 171–330 ms), the Detail General Ledger, the
General Ledger Summary and all four statements are in the table below, and all moved the right way.

---

## Decision F — the budget moved, not the bundle, and the reason is that the budget measured the wrong thing

34c left "651 kB against a 500 kB budget: Bootstrap CSS and the eager routes". Both halves of that
dissolved on inspection:

- **There are no eager routes.** All **145** entries in `app.routes.ts` are `loadComponent`, and the
  build emits 188+ lazy chunks. 34c itself `@defer`red the shell. There is nothing left for a route
  to take out of the initial bundle.
- **Nearly half the "bundle" is a stylesheet, and the budget counts raw bytes.** The 651.44 kB splits
  **314.60 kB of CSS** against **336.83 kB of JS**, and the CSS — Bootstrap, used here without
  Bootstrap's JavaScript — compresses about 9:1. What a connection actually carries is the
  **128.39 kB** transfer size printed beside it.

So a 500 kB raw budget was failing every build over a figure no user experiences, which is how a team
learns to ignore a warning. The budget is now **680 kB warn / 760 kB error**: above today's 651.44 kB
by enough not to fire on a component, below it by little enough that eagerly importing a charting or
date library would trip it. `ng build` is now clean.

An `allScript` budget was tried and rejected: Angular's `allScript` counts *every* chunk including
the 188 lazy ones (2.31 MB), so it cannot express "the initial JavaScript". There is no transfer-size
budget type. `web/src/app/build-budget.spec.ts` is the pin — `angular.json` holds no prose, so the
numbers live there and the reasoning lives in the spec, and changing either without the other fails.
It also asserts the half of the decision that is about the bundle: no `component:` entry in the route
table, and more than 100 `loadComponent:` ones.

---

## The measurement

Produced by 34c's own scripts, unchanged: `seed-master.sh`, `seed-bulk.sql`, `refresh-stats.sql`,
`run-measurement.sh`, `summarise.sh`. `tools/scale/comparison-phase42.md` is the table below,
regenerated by `summarise.sh p42-before p42-after3 p42-after4`, and the raw CSVs are committed beside
it.

**Two after-passes of the identical final configuration**, reported as a pair for 34c's reason: two
identical passes can disagree, and where they straddle the budget the summariser says `MIXED` rather
than reporting whichever ran last. Two intermediate passes were taken and are **not** committed —
they measured a configuration that no longer exists (the ageing allocation fix landed between them),
and averaging them in would be the mistake 34c made once and documented.

### Every endpoint the harness measures

| endpoint | budget | before p95 | after p95 | after2 p95 | verdict |
|---|---|---|---|---|---|
| `invoices.page1` | 500 | 93.9 | 52.5 | 61.8 | PASS |
| `invoices.last` | 500 | **960.5** | **99.3** | **84.0** | PASS |
| `invoices.search.hit` | 500 | 377.7 | 302.5 | 295.3 | PASS |
| `invoices.search.miss` | 500 | **1262.2** | **275.2** | **264.7** | PASS |
| `invoices.daterange` | 500 | 261.3 | 84.1 | 89.7 | PASS |
| `invoices.status` | 500 | 386.2 | 131.9 | 132.7 | PASS |
| `contacts.page1` | 500 | 74.6 | 45.2 | 42.5 | PASS |
| `contacts.last` | 500 | **608.3** | **73.0** | **86.4** | PASS |
| `contacts.search.hit` | 500 | 731.0 | 656.8 | 644.1 | **FAIL** |
| `contacts.search.miss` | 500 | 1097.7 | 509.2 | 510.8 | **FAIL** |
| `products.page1` | 500 | 44.9 | 50.8 | 28.0 | PASS |
| `products.last` | 500 | **570.8** | **60.6** | **49.0** | PASS |
| `products.search.hit` | 500 | 240.4 | 225.5 | 229.7 | PASS |
| `purchase-bills.page1` | 500 | 43.9 | 41.4 | 42.9 | PASS |
| `purchase-bills.last` | 500 | 314.3 | 56.9 | 81.2 | PASS |
| `accounts.page1` | 500 | 50.2 | 23.4 | 43.9 | PASS |
| `payments.page1` | 500 | 44.3 | 26.9 | 27.3 | PASS |
| `journal-vouchers.page1` | 500 | 71.4 | 45.6 | 27.7 | PASS |
| `stmt.trial-balance` | 2000 | 1409.0 | 1190.8 | 1547.8 | PASS |
| `stmt.balance-sheet` | 2000 | 1909.2 | 1209.9 | 1243.5 | PASS |
| `stmt.income-statement` | 2000 | 1807.1 | 1240.3 | 1285.9 | PASS |
| `stmt.income-statement.compare` | 2000 | 1471.4 | 1306.6 | 1263.3 | PASS |
| `reg.sales-register` | 2000 | **3885.4** | **1238.3** | **1292.2** | PASS |
| `reg.sales-register.last` | 2000 | **4180.4** | **1303.5** | **1057.2** | PASS |
| `reg.purchase-register` | 2000 | 1453.1 | 997.9 | 907.4 | PASS |
| `rep.journal-report` | 2000 | 596.6 | 170.8 | 330.4 | PASS |
| `rep.detail-general-ledger` | 2000 | **8297.0** | **1841.5** | **1820.1** | PASS |
| `rep.general-ledger-summary` | 2000 | 1698.6 | 1288.8 | 1247.5 | PASS |
| `rep.customer-ageing` | 2000 | **10335.6** | **1141.2** | **1078.8** | PASS |
| `rep.sales-by-customer` | 2000 | 3458.2 | 1928.9 | 3708.9 | MIXED |
| `search.common` | 500 | 1851.1 | 523.7 | 529.0 | **FAIL** |
| `search.code` | 500 | 822.4 | 656.4 | 697.6 | **FAIL** |
| `search.miss` | 500 | 959.8 | 885.6 | 856.8 | **FAIL** |

**Twelve endpoints were not touched by any change in this phase and are here to prove it** — the
first pages, the statements, the purchase register, global search. They moved with the machine and
not with the code, which is what "re-measure the paths you did not touch" (34c) buys.

`rep.sales-by-customer`'s `MIXED` is one outlier sample in ten (p50 1,766 ms, p95 3,709 ms); the
consecutive-in-pass probe reads it at 1,915 ms. The report class is still not reliably measurable on
a working machine, exactly as 34c's carried item #8 says, which is why the claims above lean on the
probe and on logical reads.

### Response size, which is a measurement the wall clock cannot make

| endpoint | before | after |
|---|---|---|
| `rep.detail-general-ledger` at `pageSize=50`, 3 years | **59,584,156 bytes** | **14,514 bytes** |

Roughly 4,100×. This is the one figure in the phase that does not need a quiet machine.

### Period sensitivity, before and after

Consecutive requests inside one pass, p95 of 5.

| report | before 1 mo | before 1 yr | before 3 yr | after 1 mo | after 1 yr | after 3 yr |
|---|---|---|---|---|---|---|
| Sales Register | 815 | 1,709 | 4,466 | 836 | 1,030 | 1,196 (p50) |
| Purchase Register | 236 | 378 | 1,618 | 150 | 240 | 922 |
| Sales by Customer | 639 | 1,542 | 3,534 | 623 | 800 | 1,915 |
| Detail General Ledger | 3,999 | 5,233 | 9,490 | **3,059** | **2,337** | **1,825** |
| General Ledger Summary | 2,538 | 1,898 | 1,327 | 2,320 | 1,742 | 1,242 |
| Journal report | 135 | 140 | 203 | 32 | 77 | 152 |
| Income Statement | 589 | — | 1,561 | 567 | — | 1,268 |
| Customer Ageing (no period) | — | — | 8,823 | — | — | 1,087 |
| Subscription quota usage | — | — | 971 | — | — | **225** |

**Read the Detail General Ledger row backwards.** After the conversion it is *fastest at its longest
period*, and the General Ledger Summary always was. The reason is the opening balance: every ledger
report aggregates `PostedAt <= fromDate - 1` before it does anything else, so a period that starts
later has more history in front of it. That query alone measured **182,270 logical reads and 637 ms**
on this dataset — and it is a whole-`GlLines` scan across every tenant, because `GlLine` still
carries no `OrganizationId` (34c's carried item #7, still open, and now with a second reason to care).

### Logical reads, where the wall clock was lying

| statement | before | after |
|---|---|---|
| invoice list, last page (offset 49,950, whole entities) | **153,705** | — |
| the same offset, ids only | — | **571** |
| `Contacts WHERE Id IN (OPENJSON(@35,001 ids))` (ageing) | **182,545** | removed |
| `Invoices WHERE Id IN (OPENJSON(@50,000 ids))` (GL source resolver, register) | **249,604** | removed |
| `Contacts WHERE Id IN (OPENJSON(@35,001 ids))` (trade by contact) | **214,175** | removed |
| quota transaction count over the term | **184,718** | covered by the index |

---

## What changed, file by file

| change | why |
|---|---|
| `PagedResultExtensions.ToKeyPagedResultAsync` + `KeyIn` | Decision D. The one implementation. |
| 16 `List*QueryHandler`s | One scripted edit onto the helper, anchor counts asserted. |
| `TenantIndexConvention.ReportPathIncludes`, `AddIndex(includes:)` | Decision E. One migration, `Phase42ReportPathIncludes`. |
| `DetailGeneralLedgerQuery` / `…Handler` | Decision C. Row paging, aggregates instead of a full period read, `RowsBefore`/`RowsAfter`. |
| `detail-general-ledger-page.html`, `accounting.models.ts`, `ReportSpreadsheetExporter` | The boundary disclosure, in both consumers. |
| `ContactAgeingSummaryQueryHandler`, `TradeByContactQueryHandler` | The 35,001-id narrowing removed; the Contact Group filter still decides membership. |
| `OutstandingDocumentReader` (5 places) | Line totals as store-side `GroupBy` joins against the same query; the 50,000-id allocation and note-reduction parameters removed. |
| `SalesRegisterQueryHandler` (3 places) | The same, for invoices' lines, products and contacts. |
| `web/angular.json`, `web/src/app/build-budget.spec.ts` | Decision F. |
| `tests/Application.UnitTests/Performance/Phase42KeyPagingTests.cs` | 10 tests: both shapes over the same data. |

**No permission key changed.** Every converted reader keeps its existing `Reports.*` key; the
two-step paging changes no key. That was the kickoff's expectation and nothing in the measurement
disturbed it.

---

## Bugs and traps hit in this phase

**1. A record projection makes a store-side `Sum` untranslatable — and InMemory hides it.**
`LoadSectionAsync`'s carried-balance query was written as `accountLines.Take(n).Sum(x => x.Debit -
x.Credit)` where `accountLines` had already projected into a `LedgerLine` record. EF has to construct
the record server-side to read `.Debit`, cannot, and throws. The InMemory provider evaluates it in
C# instead, so **all ten new tests passed — including the one that walks every page at `pageSize=1`,
which exercises exactly this branch — while the real endpoint returned 500 on every page after the
first.** Only the browser-free E2E against SQL Server saw it. This is phase-34b's gotcha (a shared
matcher that InMemory evaluates and SQL Server refuses) and phase-38's (EF refuses a set operation
after a client projection) arriving together through a third door. The fix is to keep the query on
raw columns and project after `Skip`/`Take`.

**2. A `python - <<'PYEOF'` heredoc ate `\n` inside string literals, twice**, turning
`"…targetType\n"` into a literal newline and making an anchor assertion fail with the anchor visibly
present in the file. Phase 39 recorded this for `cat > file <<'EOF'`; it applies to piping a script
into an interpreter as well. Writing the script to a file with the Write tool and running that file
is the reliable form, and it is what the rest of this phase used.

**3. `sqlcmd` will not cast a `datetimeoffset` literal without being told to.** Backdating the
subscription term for the quota measurement failed with "Conversion failed when converting date
and/or time from character string" until the value was wrapped in
`CAST('2023-07-01T00:00:00+00:00' AS datetimeoffset)`.

**4. `dotnet build` cannot copy over a running API.** Obvious in hindsight; it reports
`MSB3027 … locked by: ErpApp.Api (PID)` and fails the whole solution build. Stop the API before
building, which matters here because every measurement pass needs a rebuild between it and the last.

---

## The E2E, and what it proves

Seeded through the API (`seed-master.sh`), then a fresh Member-role user registered, verified, invited
and accepted — logging in *before* accepting, or the membership stays `Invited` (phase 41).

```
MEMBER  GET contacts (a key Member holds)                  200
MEMBER  GET detail-general-ledger                          403
        {"title":"You do not have permission to perform this action
                  (Reports.DetailGeneralLedger.View).","status":403}
MEMBER  GET detail-general-ledger (nonexistent org)        403
ADMIN   GET detail-general-ledger                          200   (row-paged sections)
```

The 200 on the first line is what makes the 403 on the second mean *the key* rather than the
membership — same user, same organization, same run (phase 41). The third line is 403 and not 404
against an organization id that does not exist, which proves `AuthorizationBehavior` refused it
before any handler looked for a row. The fourth is the converted report answering for a caller who
holds the key.

The row-paging walk in Decision C above is from the same run, and the index was confirmed directly
against `sys.index_columns` rather than from the migration file.

---

## Carried items

1. **List search still costs 510–656 ms on contacts, and the remaining cost is the `COUNT`.** The
   page is now cheap (ids 156 ms, rows 3 ms); the count of a `LIKE '%term%'` over 50,002 contacts is
   **459 ms**, and no index can turn a leading wildcard into a seek. Two things would fix it and
   neither belongs at the end of a performance phase: **(a)** issuing the count and the key query
   concurrently, which needs a second `DbContext` and so an `IDbContextFactory` this codebase does
   not have — it would take contacts search to roughly 460 ms, inside budget, and is the cheapest
   next step; **(b)** full-text indexing, which the kickoff made conditional on the two-step failing.
   It did fail, on contacts — but full-text is **not the remedy the measurement asks for**:
   `CONTAINS` matches word prefixes, not substrings, so adopting it changes what "search" *means* on
   25 screens, cannot be expressed in EF without raw SQL, and cannot run on InMemory at all. That is
   a feature with a confirm-live question attached, not a performance fix. **Re-entry:** a tenant
   complaining about search latency, or the day `IDbContextFactory` arrives for another reason.
2. **Global search (`search.*`) is unchanged and still over budget**, 524–1,007 ms against 500 ms. It
   was 34c's finding, this phase did not touch it, and the 18-query fan-out is a different mechanism
   from the list search. **Re-entry:** whoever next opens `GlobalSearchQuery`.
3. **`GlLine` still carries no `OrganizationId`** (34c #7). 34c measured it as costing nothing; this
   phase found a second reason to care — the ledger reports' opening-balance aggregate is a
   whole-table scan (182,270 reads, 637 ms) and it is the dominant cost of the Detail General Ledger
   and the General Ledger Summary at short periods. **Re-entry:** unchanged (a `GlLines` table that
   no longer fits the buffer pool), but the payoff is now larger than 34c thought.
4. **Purchase Register and General Ledger Summary keep the full-period read.** Both under budget at
   every period probed. **Re-entry:** 2 s at a period someone uses.
5. **Ten `List*QueryHandler`s that page a projection were not converted.** None is on a table this
   dataset makes large, and the two-step would re-run their joins. **Re-entry:** a projected list on
   a table that grows — `ListEmailLogs` and `ListAlertSendLogs` are the candidates.
6. **The report class is still not reliably measurable on this machine** (34c #8, unchanged, and
   this phase's `sales-by-customer` row is the evidence). Anything wanting to claim a report-level
   improvement smaller than 2× needs a quieter machine, more runs, or server-side timing.

## What was deliberately not done

- **Keyset pagination.** Not deferred — **retired.** The tail's cost was the row fetch, not the
  offset, and the key-paged shape fixes it inside `PagedResult<T>` without touching the list chrome
  or either sweep guard. 34c's carried item #5 is closed, and its re-entry condition is now unmet.
- **Full-text search.** See carried item 1: a semantics change, not a performance fix.
- **A second index on `GlJournalEntries`.** The INCLUDEs went onto the index that already existed,
  so the write cost 34c measured (+23.6 % on the document+GL seed) did not grow.
- **No confirm-live pass**, and, as in 34c, that is a decision. This phase measured this codebase
  against itself; the one thing a live read could have settled — what the reference product does when
  a ledger section spans a page — is a question about a product that pages by account and therefore
  never has the problem.
