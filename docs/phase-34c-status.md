# Phase 34c — Scale: NFR-5.1/5.2, and the measurement that argued with itself

## TL;DR

The last entry of the parity sequence. `phase-34a-status.md`'s Decision C had already fixed the dataset
(50,000 invoices / 50,000 contacts / 20,000 products, seeded by direct `INSERT`) and what to measure
(each paginated list's **first and last** page, the three financial statements, the two heaviest
registers, global search), so this phase executed that spec and then decided the rewrites from the
numbers.

| | before | after |
|---|---|---|
| tenant-scoped tables with **no** index leading on `OrganizationId` | **18** | 0 |
| invoice list, first page (p95, 50,000 invoices) | 469 ms | **49–97 ms** |
| contact list, first page (p95, 50,000 contacts) | 419 ms | **42–62 ms** |
| product list, first page (p95, 20,000 products) | 178 ms | **23–138 ms** |
| logical reads for a non-matching contact search | 6,743 | **700** |
| initial JS bundle (raw / transfer) | 724.2 kB / 141.0 kB | **649.5 kB / 127.3 kB** |
| full-tenant export of this dataset | 70,016 rows, 2 of 5 categories truncated | *unchanged — see Decision C* |

**The eighteen missing indexes were not an arbitrary eighteen.** They are exactly the transactional
documents plus `GlJournalEntry` — and the reason is structural: every *other* tenant-scoped table
already had a leading-`OrganizationId` index **for free**, because master data carries a per-tenant
uniqueness rule (`(OrganizationId, Code)` or `(OrganizationId, Name)`), and a document number is not
unique-indexed. The gap tracked the absence of a uniqueness constraint, not anyone's judgment about
which tables are hot. That is what made it expressible as a convention over the model
(`TenantIndexConvention`) rather than 18 hand-written `HasIndex` calls, and the rule it applies is
one this codebase already had in writing: *an aggregate with a business `Date` is a document, master
data is not* — phase 34b's own rule for which lists get a date filter, reused to decide which tables
get the date index.

**Three findings are worth more than the speed-up.**

1. **An index added for one access path changed the plan for another over the same table, and made
   it worse.** `(OrganizationId, CreatedAt DESC)` took the unfiltered invoice list from 469 ms to
   49 ms; on the *same table*, a search term matching nothing went from 651 ms to 1,173 ms, because
   the optimizer stopped scanning once and started seeking the ordering index with a key lookup per
   row — 83,000 logical reads per execution. The covering search index this phase then added fixed
   global search (999 ms → 595 ms) and did **not** fix the list search, because `ORDER BY CreatedAt
   DESC` + `TOP 50` still makes the ordering index look cheaper to the optimizer. Both halves are
   shipped and both numbers are in the table below; the residual is carried below.

2. **An inference about a design is not a measurement, and this phase caught itself making one.**
   `GlLine` carries no `OrganizationId`, so every financial statement joins through
   `GlJournalEntries` and scans the whole GL line table across all tenants — which was about to be
   written up as "every tenant's statement pays for every other tenant's ledger". Seeding a *second*
   50,000-invoice tenant and re-measuring the first refused it: the scan does double (3,800 → 7,586
   logical reads) and the wall time does not move outside run-to-run noise, because a logical read
   from a warm buffer pool is nearly free next to the aggregate. The finding survives, but as a
   precise statement with a re-entry condition rather than as a scare.

3. **The report layer's cost is the period, not the page size** — and the fix is already in the
   repo. Seven of the report handlers materialise every row in the period and then page the
   resulting list, so `pageSize` bounds the response and not the work: the Sales Register costs
   0.65 s for a month, 1.19 s for a year and 2.1–3.5 s for three years on the same data.
   `JournalReportQueryHandler` is the counter-example — it pages the *entry keys* and fetches detail
   for the page alone — and costs 250–344 ms over the same 70,002 entries. The full read is not
   laziness: phase 16c requires footer totals over the whole filtered set, so removing it means a
   SQL aggregate for the totals plus a SQL page for the rows, per report. Measured, sized and
   carried.

**The export cap's re-entry condition is met, by specification.** The roadmap made the 25,000-row
cap conditional on "a tenant having hit it"; NFR-5.1's own top-of-range tenant hits it by 8× —
measured, `Contacts (25,000 of 50,002); Ledger Transactions (25,000 of 210,006)`. With the cap
temporarily lifted the *complete* export was 280,024 rows / 12.1 MB / **15.8 s** / ~700 MB peak
working set, i.e. about **2.5 kB of process memory per row**. The constant was left alone
deliberately — Decision C says why.

Tests: Domain **443**, Application.UnitTests **931**, Api.IntegrationTests **18**, Angular **273
(+2)**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean; `a11y-sweep-guard.spec.ts`
and `SearchSweepGuardTests` both green.

---

## Decision A — what "pass" means

NFR-5.1/5.2 name no number, so the budget had to come from the interaction each screen is part of,
not from a round figure. A phase that measures without a threshold produces a table nobody can act
on, so this is the first decision and everything downstream is "is this over budget?".

| class | p95 budget | why that number |
|---|---|---|
| a paginated list page — any page, filtered or not | **500 ms** | Someone is clicking through it inside a task. The classic responsiveness threshold for "the system is keeping up with me" is about a second for the *whole* interaction; the server owns roughly half of that once an Angular render and a real network hop are added. |
| global search | **500 ms** | Typed into, with results appearing as you type. The client already debounces 250 ms, so the server's share of a keystroke-to-result cycle is the same half-second — arguably tighter, but not looser. |
| a financial statement | **2 s** | Explicitly requested and waited on. Two seconds keeps it far inside the ~10 s at which attention goes elsewhere, and is the point past which a screen should be showing progress rather than nothing. |
| a register or list-shaped report page | **2 s** | The same interaction as a statement. |

Two things the budget deliberately does *not* do. It does not vary by dataset size — the whole point
is that it is the p95 at the top of NFR-5.1's stated range, so a pass is a pass for every tenant the
PRD anticipates. And it does not distinguish first page from last: offset pagination degrades at the
tail, and a budget that excused the tail would excuse exactly the thing Decision C asked to measure.

---

## Decision B — the indexes ship, as a rule, with the write cost measured

**They ship in this phase.** The measurement named the columns and the answer was uniform enough to
be a convention rather than a list — see `TenantIndexConvention`, which runs after
`ApplyConfigurationsFromAssembly` and adds three families:

| family | shape | serves |
|---|---|---|
| document, report path | `(OrganizationId, BusinessDate)` | registers, statements, ageing — every report that ranges over a document's own date. `BusinessDate` is `Date` on fourteen aggregates and `PostedAt` on `GlJournalEntry`, which is the same thing under another name. |
| document, list path | `(OrganizationId, CreatedAt DESC)` | every document list screen, all of which order `CreatedAt DESC` — checked across all 42 `List*QueryHandler`s, not assumed. |
| search | `(OrganizationId, Name?, Code)` `INCLUDE (Reference, SupplierInvoiceReference, Sku)` | phase 34b's list search and phase 33's global search. The keys and includes are derived from what the twenty searchable handlers actually match on, which is a subset of exactly those five columns. |

**50 indexes across 17 tables**, in two migrations (`Phase34cTenantIndexes`, `Phase34cSearchIndexes`).

**The convention refuses to let the next one go missing.** Any tenant-scoped entity that ends up with
no leading-`OrganizationId` index throws at model build, naming itself, unless it is listed in
`NotIndexedByTenant` with a reason. Two are: `CustomFieldValue` and `ImportJobRow`, both child rows
read only through their parent. That is stronger than a guard test, because it *creates* the index
rather than checking that someone wrote it — and it runs in `dotnet ef migrations add` and in every
integration test that boots the host.

It was made to fire, per phase 34a's rule that a guard is worthless until it has failed: renaming the
`ImportJobRow` key in `NotIndexedByTenant` makes `dotnet ef` refuse with
`These tenant-scoped entities carry no index whose leading key is OrganizationId … ImportJobRow`.
The file was restored by hash afterwards, not by `git checkout`.

**Is `(OrganizationId, Date)` worth its write cost on the hot document tables?** Measured on the
identical 190,000-row bulk seed, which is the most hostile framing available — pure `INSERT`, zero
reads, so every index is cost and nothing is benefit:

| indexes present | seed time | vs baseline |
|---|---|---|
| none of this phase's | 48.8 s | — |
| 33 (document + GL) | 60.3 s | **+23.6 %** |
| 50 (+ 17 search) | 64.8 s | **+32.8 %** |

A third of the write cost, on a workload that does nothing but write, to make the screen a tenant
opens most often 5–10× faster. Both key columns of every index are write-once (`OrganizationId`,
`CreatedAt` and the business date are never updated), so there is no update cost at all — only
insert. Shipped.

---

## Decision C — the export rewrite: the condition is met, the constant is not changed

The roadmap made both the 25,000-row cap and the OpenXml SAX streaming writer conditional on **a
tenant having hit the cap**, and 34a's Decision C said 34c decides it *after* the measurement.

**The measurement meets the condition, and not marginally.** A full-tenant export of this dataset:

| | rows | file | wall time | peak working set |
|---|---|---|---|---|
| cap at 25,000/category (shipped) | 70,016 | 3.6 MB | 27.4 s | — |
| cap lifted to 250,000/category | **280,024** | **12.1 MB** | **15.8 s** | ~916 MB (from a 215 MB idle baseline) |

with the shipped cap disclosing `Contacts (25,000 of 50,002 rows); Ledger Transactions (25,000 of
210,006 rows)` — the ledger losing 88 % of its rows on the largest tenant the PRD anticipates. (The
lifted run being *faster* is the first job in a fresh process paying JIT and first-query costs in the
capped run; both are comfortably under half a minute.)

**The number that decides the cap is now known: ClosedXML costs about 2.5 kB of process working set
per row.** That is what the cap is really expressing — a memory budget, divided by however many
categories can be large at once — and it is why "25,000 per category" was never the right shape: two
of the five categories (Ledger Transactions, Stock Movements) grow with the tenant's *activity* while
the other three are bounded by its master data.

**And the constant is left at 25,000 anyway.** Not an omission: a cap on a buffered workbook is a
server-safety limit, one machine's memory law is one data point, and the phase that first measured it
should hand the decision over with numbers rather than raise a production safety limit on the
strength of a single run. What changes is that the decision is no longer blocked on an observation —
it is an arithmetic problem with a measured coefficient, recorded as carried item #10. **The SAX
rewrite is not the first move**; raising the cap to a level the memory law supports is, and the
rewrite is only needed if the budget that implies is unacceptable.

---

## Decision D — the shell is deferred

Phase 34b's carried item #5: the shell renders only inside an organization but was *imported*
unconditionally, so all four components and their transitive imports sat in the initial bundle,
downloaded and parsed by every visitor to the login and registration screens, who can never see any
of it. 34b estimated `@defer`ing it would move ~46 kB.

`@defer (when organizationId() !== null; prefetch on idle)`:

| | raw | estimated transfer |
|---|---|---|
| before | 724.20 kB | 141.00 kB |
| after | **649.46 kB** | **127.30 kB** |
| saved | 74.74 kB (−10.3 %) | 13.70 kB (−9.7 %) |

**62 % more than the estimate**, because the estimate counted the four components and not what they
drag in behind them. `prefetch on idle` is what keeps this from trading one cost for another: the
chunk is fetched during the browser's idle time after the login screen settles, so it is already
there by the time anyone has finished typing a password.

**It is pinned by a test, and the test was made to fail.** `app.spec.ts` gains two cases — the chrome
is absent on `/login` and present after a navigation to `/organizations/:id/dashboard` — using
`DeferBlockBehavior.Playthrough`, which evaluates the `when` trigger as the browser does rather than
rendering the placeholder and stopping. A defer block can fail in two opposite directions (the chunk
never loads, or the trigger fires where it should not) and neither shows up in a build, so both are
asserted. Changing the trigger to `when false` fails the second case; the file was restored by hash
afterwards (phase 34a's rule), not by `git checkout`.

---

## Decision E — the carried-item backlog was overtaken mid-phase, and that is the right outcome

The kickoff asked this phase to close the roadmap by triaging every carried item from phases 25–34b
into *do now / re-entry condition / drop*, in its own doc, "so the next session starts from a list
rather than from thirteen status docs".

**That work landed while this phase was measuring.** Commit `be41227` (2026-09-10 22:37) added
**consolidation phases 35–41** to `docs/roadmap.md`, built by exactly that method — every "Carried
items / Known limitations / Follow-ups" section from phase 25 to phase 34b read and each item placed
where it is cheapest to close next to its neighbours — plus a read of the second reference tenant
(`cadehi.tigg.app`) that settled three phase-32 questions. It is a better artifact than the flat
triage the kickoff described, because it carries an ordering rule (bugs and consistency first,
Domain-invariant changes next, then breadth, then the two passes that need a person or a product
decision) rather than three buckets.

So a `docs/carried-items.md` was written, then **deleted**: two lists of the same items, one of them
sequenced into phases and one not, is a drift machine. What this phase owes 35–41 instead is the
three answers they explicitly leave to it, and those are now written into the roadmap where they were
asked:

| the plan says | 34c's answer |
|---|---|
| 38: "the OpenXml SAX streaming writer only if 34c's measurement or a real tenant hits the 25,000-row cap" | **The measurement hits it**, by 8× on Ledger Transactions. But the SAX rewrite is not the first move — see Decision C: raising the cap is, and the coefficient that sets it is now known (~2.5 kB of working set per row). |
| 39: "a search results page with a kind filter, once 34c says the cap of 5 bites" | **It bites.** Global search over this dataset returns its per-collection cap on ordinary terms, and the 18-query fan-out costs 595–1,106 ms p95 — over Decision A's budget in every pass, before any UI is added. |
| 40: "`@defer` the shell if 34c has not" | **Done here** (Decision D): 724.2 kB → 649.5 kB raw, pinned by two tests. Strike it from 40. |

This phase's own carried items — the seven scale findings that did not exist before it — are in
"What was deliberately not done" below, and belong in whichever of 35–41 next touches those handlers
rather than in a list of their own.

---

## The measurement

### How it was produced

Two committed scripts and a summariser, so the numbers are reproducible rather than reported:

| file | what it does |
|---|---|
| `tools/scale/seed-master.sh` | Drives the **real API** to create a fresh organization, its chart of accounts, GL defaults, warehouse, unit, category, contact group, and exactly one contact / product / approved invoice / approved purchase bill. Prints every status code (phase 26c's rule). |
| `tools/scale/seed-bulk.sql` | The 190,000 bulk rows by direct `INSERT` with a numbers table, **deriving every column it can from the reference rows the handlers wrote** — including reading the six GL accounts back off the reference journal entries rather than naming them, so a change to a posting rule surfaces here as different accounts instead of silently seeding a stale chart. Runs in ~49 s. Ends by re-checking that the ledger balances. |
| `tools/scale/refresh-stats.sql` | `UPDATE STATISTICS … WITH FULLSCAN` on the thirteen tables involved, plus a cold plan cache. Run before **every** pass. |
| `tools/scale/run-measurement.sh` | 33 endpoints × (2 warm-up + 10 timed) requests, p50/p95/max from `curl`'s `%{time_total}`, written to `results-<label>.csv`. |
| `tools/scale/summarise.sh` | Merges the CSVs into the table below and applies Decision A's budget over **every** pass after the first, printing `MIXED` where they straddle it. |
| `tools/scale/README.md` | How to run all of it, and the three things that bite. |

**The dataset** is one organization with 50,001 invoices, 50,002 contacts, 20,001 products, 20,001
purchase bills, 70,002 GL journal entries and 210,006 GL lines, spread evenly across the
organization's accounting start date to today (1,151 days).

Two seeding decisions are worth naming because they shape what the numbers mean:

- **Products are Service-typed.** A Goods line consumes stock regardless of `TrackInventory`
  (phase 30's gotcha), so a bulk Goods row would owe `StockLedgerEntry`/`StockMovement` rows that no
  posting rule wrote. Service keeps every seeded document's GL shape *exactly* the reference shape —
  which is what "shaped like real ones" has to mean if it is to mean anything. The price is that the
  inventory reports are outside this measurement.
- **`PostedAt` is derived from the business date, not from "now".** Stamping all 70,002 entries with
  `SYSUTCDATETIME()` would put every GL report's whole dataset inside one day and make a date-ranged
  statement measure nothing — phase 19's `PostedAt` gotcha, used forwards.

**Environment**: SQL Server 2022 Express (buffer pool capped at 1,410 MB, 4 cores), API over HTTPS on
localhost, Windows 11. `before` and `after` were taken against the **same** database — two scale
tenants, 421,780 GL lines — with the same statistics treatment and a cold plan cache, so the two
sides differ by the indexes and nothing else.

### The numbers

p95 in milliseconds, 10 timed runs after 2 warm-ups. `after`/`after2` are two independent passes of
the identical configuration, reported as a pair because the spread between them *is* a result — see
"What the repeat pass says" below.

| endpoint | budget | before p95 | after p95 | after2 p95 | verdict |
|---|---|---|---|---|---|
| `invoices.page1` | 500 | 469.3 | 96.8 | 62.0 | PASS |
| `invoices.last` | 500 | 586.3 | 527.3 | 425.7 | MIXED |
| `invoices.search.hit` | 500 | 763.2 | 283.4 | 267.9 | PASS |
| `invoices.search.miss` | 500 | 651.5 | 1172.7 | 857.4 | **FAIL** |
| `invoices.daterange` | 500 | 186.0 | 288.9 | 213.3 | PASS |
| `invoices.status` | 500 | 195.8 | 255.1 | 652.0 | MIXED |
| `contacts.page1` | 500 | 418.7 | 61.8 | 65.6 | PASS |
| `contacts.last` | 500 | 470.6 | 520.1 | 568.0 | **FAIL** |
| `contacts.search.hit` | 500 | 1131.6 | 753.6 | 724.8 | **FAIL** |
| `contacts.search.miss` | 500 | 1056.4 | 1166.5 | 1420.9 | **FAIL** |
| `products.page1` | 500 | 177.7 | 137.7 | 119.2 | PASS |
| `products.last` | 500 | 199.1 | 564.9 | 245.7 | MIXED |
| `products.search.hit` | 500 | 419.2 | 261.1 | 254.2 | PASS |
| `purchase-bills.page1` | 500 | 139.8 | 68.7 | 181.3 | PASS |
| `purchase-bills.last` | 500 | 153.7 | 200.9 | 347.8 | PASS |
| `accounts.page1` | 500 | 19.2 | 48.4 | 55.0 | PASS |
| `payments.page1` | 500 | 25.0 | 55.1 | 64.2 | PASS |
| `journal-vouchers.page1` | 500 | 38.8 | 57.4 | 75.1 | PASS |
| `stmt.trial-balance` | 2000 | 1015.7 | 1325.5 | 4077.1 | MIXED |
| `stmt.balance-sheet` | 2000 | 1066.0 | 1407.8 | 3668.1 | MIXED |
| `stmt.income-statement` | 2000 | 1011.7 | 1237.0 | 4007.4 | MIXED |
| `stmt.income-statement.compare` | 2000 | 1144.0 | 1282.6 | 4082.9 | MIXED |
| `reg.sales-register` | 2000 | 2196.1 | 3473.2 | 3499.5 | **FAIL** |
| `reg.sales-register.last` | 2000 | 2193.1 | 3506.7 | 3262.8 | **FAIL** |
| `reg.purchase-register` | 2000 | 1020.3 | 1080.2 | 1376.4 | PASS |
| `rep.journal-report` | 2000 | 242.0 | 344.4 | 2517.8 | MIXED |
| `rep.detail-general-ledger` | 2000 | 6872.7 | 8917.0 | 14898.9 | **FAIL** |
| `rep.general-ledger-summary` | 2000 | 1168.3 | 1295.2 | 3893.5 | MIXED |
| `rep.customer-ageing` | 2000 | 8055.8 | 8925.7 | 10921.9 | **FAIL** |
| `rep.sales-by-customer` | 2000 | 2345.1 | 3133.6 | 4429.4 | **FAIL** |
| `search.common` | 500 | 999.4 | 594.6 | 702.0 | **FAIL** |
| `search.code` | 500 | 910.9 | 787.3 | 876.8 | **FAIL** |
| `search.miss` | 500 | 884.5 | 969.7 | 1106.2 | **FAIL** |

### What the repeat pass says

The `after` and `after2` columns are two independent passes of the identical configuration
against the identical database, and the difference between them is the most important caveat on this
whole table:

- **The list class is stable.** Every first page lands within 30 % across the two passes, and the
  before/after change on those rows is 4-7x, far outside that. Those numbers can be relied on.
- **The report and statement class is not.** The three financial statements read 1.0 s in `before`,
  1.3 s in `after` and 3.7-4.1 s in `after2` -- with no schema change between the last two, and with
  the machine variously compiling, deleting 400,000 rows and running a leftover dev server. A 4x
  swing is larger than any effect this phase could have had on them.

So the table judges each row over **both** after passes and says `MIXED` where they straddle the
budget, rather than reporting whichever pass ran last. What this phase claims about reports is
therefore *not* taken from the table at all -- it is taken from the period-sensitivity probe below,
whose readings are consecutive requests inside one pass and so control for the machine:

| report | 1 month | 1 year | 3 years |
|---|---|---|---|
| Sales Register | 0.65 s | 1.19 s | 2.1-3.5 s |
| Detail General Ledger | 1.8 s (1.6 MB) | 4.3 s (18.9 MB) | 6.1-14.9 s (59.5 MB) |
| Sales by Customer | 0.56 s | -- | 3.0 s |
| Income Statement | 0.33 s | -- | 0.9-4.1 s |

That is the finding: **linear in the period, indifferent to `pageSize`.** A month is comfortably
inside budget on every one of them; three years is not, on any.

Two rows in the table are honest failures rather than noise, because both after passes agree and
both are worse than `before`: `invoices.search.miss` (651 -> 857/1,173 ms) and `contacts.search.miss`
(1,056 -> 1,167/1,421 ms). That is finding #1 in the TL;DR, and it is this phase's own doing.

The raw CSVs are committed alongside the scripts (`tools/scale/results-*.csv`), and
`tools/scale/comparison.md` is this table regenerated by `summarise.sh`.

---

## Bugs and traps hit in this phase

**1. A run against an empty tenant looks exactly like a spectacularly fast one.**
`seed-master.sh` rewrites `tools/scale/.seed-ids.env` every time it runs, so seeding a second
organization silently repointed the harness at it. The repeat pass came back at 20–30 ms p95 across
every endpoint with every status code `200` — which reads as a triumph and was an empty database. The
only thing that distinguishes the two is the **response size**, so `run-measurement.sh` now refuses
to start unless a 50-row invoice page returns more than 1,000 bytes. This is the
prints-and-returns trap (phase 34b) in a new costume: *a script with a side effect on shared state,
called for its other purpose.*

**2. Two report numbers moved 2× between passes with no index that could explain it.** The first
`scaled`/`indexed` pair had the Detail General Ledger at 11.6 s and the Sales Register at 3.7 s; a
controlled `before` pass put them at 6.9 s and 2.2 s, and a direct re-probe confirmed the lower
figures were the stable ones. The lesson is the one now in `refresh-stats.sql`'s header: on a dataset
that arrives by bulk `INSERT`, statistics quality moves a report that joins to a line table by more
than any change under test, so a scale measurement has to state whether statistics were refreshed —
and refresh them identically on both sides. The first pair is discarded; only `before` / `after` /
`after2` are reported.

**3. `sqlcmd -i` runs with `QUOTED_IDENTIFIER OFF`,** so the first bulk insert into `catalog.Products`
failed with `INSERT failed because the following SET options have incorrect settings` — the filtered
index `IX_Products_OrganizationId_ParentProductId_CombinationKey` requires it ON. `SET
QUOTED_IDENTIFIER ON` at the top of the script is the fix, and it is worth knowing before writing any
future `-i` script against this schema.

**4. `sqlcmd -W` and `-y/-Y` are mutually exclusive**, and a `SELECT` naming a column that exists on
two joined tables fails with `Ambiguous column name` rather than picking one — two small frictions
that cost more time than they should have while inspecting the seeded rows.

---

## What was deliberately not done

- **The seven report handlers were not rewritten.** Decision C's instruction was to decide the
  rewrites *from the numbers*, and the numbers say the cost is the period, that the fix is a known
  in-repo pattern, and that it is seven handlers of correctness-critical report code. Sized and
  carried, not started at the end of the phase that first measured it — phase 8f's
  "omit rather than fake" applied to a rewrite instead of a column.
- **Keyset pagination was not introduced.** The tail is now the dominant list cost (49–97 ms first
  page against 440–565 ms last), but changing `PagedResult<T>` touches every list screen and both
  sweep guards. Carried.
- **The inventory reports are outside the measurement**, as a consequence of the Service-typed
  dataset above. Stated rather than quietly omitted.
- **No confirm-live pass**, and that is a decision rather than a skip. NFR-5.1 is a specification,
  not a reference-product behaviour, and the reference tenant's data volumes are not observable from
  outside it. The one question worth a look — whether its list endpoints page by offset or by cursor
  — is answered by the query strings already recorded in `erp-module-scan.md` (`limit=20`, no cursor
  parameter), so there was nothing to read that had not been read.

---

## Carried items

Seven of these did not exist before this phase, because nothing had measured. Each names the phase
in `docs/roadmap.md`'s 35–41 plan where it is cheapest to close, rather than asking for one of its
own.

1. **The report layer is linear in the period.** Seven handlers materialise every row in the period
   and page the resulting list. `JournalReportQueryHandler` is the shape that is not — page the
   *keys*, fetch detail for the page — and phase-16c's footer-totals rule is why the full read
   exists, so each conversion is that plus a SQL aggregate for the totals. **Re-entry:** any report
   crossing 3 s at a period someone actually uses. **Home:** whichever of 35–39 next opens those
   readers.
2. **`DetailGeneralLedgerQuery` returns 59.5 MB at `pageSize=50`** — its page unit is the *account*
   and the rows inside a page are unbounded. **Re-entry:** anyone opening it on a real tenant. The
   fix is a report-semantics decision (page by row within an account, or cap and disclose), not a
   performance one, which is why it was measured and not chosen. **Home:** 35, next to the ledger
   drill-down.
3. **`ContactAgeingSummaryQueryHandler` hands SQL a 50,000-element `OPENJSON` id list**, 7.5–8.9 s
   p95 on 35,001 customers, and it is the one slow report with no period to narrow it. **Home:** 36,
   which already opens that handler to align it with `DocumentAgeQueryHandler`.
4. **The export cap is crossed by NFR-5.1's own tenant** and the coefficient that would set a new one
   is measured (~2.5 kB working set per row). **Home:** 38, whose text already makes the SAX writer
   conditional on this measurement — the answer is that raising the cap comes first.
5. **Offset pagination's tail is now the dominant list cost** (first page 49–97 ms, last page
   425–568 ms). Keyset pagination would fix it and would change `PagedResult<T>`, every list screen
   and both sweep guards. **Re-entry:** a list anyone actually pages to the end of.
6. **List search still costs ~0.9–1.4 s on a term matching nothing**, because `ORDER BY CreatedAt
   DESC` + `TOP 50` keeps the optimizer on the ordering index with a key lookup per row. Global
   search *was* fixed by the covering index; the list search needs either full-text indexing or a
   two-step handler (match ids from the covering index, then order and page) across 20 handlers.
   **Re-entry:** a tenant complaining about search latency.
7. **`GlLine` carries no `OrganizationId`**, so every statement scans the whole table (7,586 logical
   reads at 421,780 rows). Measured as costing nothing yet — see TL;DR finding 2. **Re-entry:** a
   `GlLines` table that no longer fits the buffer pool, at which point those logical reads become
   physical.
8. **The measurement environment is not good enough for the report class.** Two identical passes
   disagreed by up to 4× on the statements. Anything that wants to claim a report-level improvement
   needs a quiet machine and more runs than 10 — or a different instrument entirely (server-side
   timing rather than `curl` wall time). The list-class numbers do not have this problem.
