# Phase 50 — Measured indexes, and the guards that cross an assembly boundary

## TL;DR

The phase that was not allowed to add an index until `tools/scale/` said so. It ran the harness, and
the harness argued back three times: the index the roadmap specified **regressed** the paths it was
not added for, the covering index that should have fixed that bought **0.03 %**, and a hand-written
"optimisation" this phase invented for the same path made four other paths up to **39× worse**. What
ships is one index, one paging change, one declaration mechanism, one new test project and three
measured refusals.

| | before | shipped |
|---|---|---|
| Cheque Register, first page (logical reads / CPU) | 2,001 / 168.6 ms | **1,082 / 29.5 ms** |
| …last page, offset 49,950 | 4,422 / 680.0 ms | **1,289 / 40.9 ms** |
| …date range | 2,606 / 49.4 ms | **577 / 13.7 ms** |
| …status tab | 1,731 / 85.6 ms | **1,327 / 15.2 ms** |
| …search term matching nothing | 430,084 / 2,258.4 ms | **3,957 / 864.8 ms** |
| …search term matching something | 263,164 / 1,694.3 ms | *326,015 / 2,331.1 ms — carried, see #1* |
| Cheque paths over their 500 ms p95 budget | 3 of 8 | **2 of 8** (both search) |
| tenant-scoped entities whose business date the convention could silently miss | any | **0 — it fails the model build** |
| cross-assembly invariants with a test | 0 | 1, and a project for the next one |

**The one-sentence result:** `Cheque` now carries `(OrganizationId, ChequeDate DESC)` and
`ListChequesQueryHandler` pages by key, both because a number said so; and the two name lists that
let the gap exist for thirty-three phases can no longer be wrong without failing the build.

**Three findings are worth more than the speed-up.**

1. **The blind spot was never about cheques.** Three separate sweeps derived their set from "a
   document list" — phase 34c's `TenantIndexConvention`, phase 42's `ToKeyPagedResultAsync`
   conversion, phase 47's `Sort by` sweep — and the Cheque Register is not a document list, so it was
   invisible to all three. The same screen, missed the same way, three times. Phase 47 found the
   first instance and called it "a convention matching by name is a list, and a list becomes a wrong
   list"; the sharper statement is that **a sweep whose set is derived from a screen *shape* is blind
   to every screen of a different shape, and stays blind silently.**

2. **Asking the mirror question found two more, and they were fine by luck.** `StockLedgerEntry` and
   `StockMovement` also spell their business date `TransactionDate`, and both fell through the name
   match into the master-data branch exactly as `Cheque` did. Neither was slow — because each carries
   a hand-written `(OrganizationId, ProductId, WarehouseId, TransactionDate)` composite that
   `HasLeadingTenantIndex` waved through. Had that composite led on anything else, nobody would have
   known. The name list was wrong in **three** places and visibly wrong in one.

3. **This phase's own change was the best example of the rule it was obeying.** Reasoning from the
   query plan, it added the tenant predicate to the three joined tables in `ListChequesQueryHandler`
   — correct in this codebase's idiom, and it did improve the search path. It also took the
   register's Received tab from 2,143 logical reads to **83,734**, and its first page from 29.5 ms of
   CPU to **674.9 ms**. It is reverted. It was found only because the roadmap requires re-measuring the
   paths the change was not for, which is the whole reason this phase was gated.

---

## Decision A — the measurement had to be built before it could be taken

`seed-bulk.sql` predates the Cheque Register being anything anyone measured, and the `Cheques` table
held **five rows in the entire database** — across 139 organizations and three 50,001-invoice
tenants. A pass against that is the empty-tenant reading the harness's own README warns about in
bold: 20 ms p95, every status code 200, and a completely unindexed sort declared fast.

So the phase's first work was `tools/scale/seed-cheques.sql`: 50,000 cheques and the 50,000 payments
they hang off, on the existing phase-42 tenant. Two seeding decisions are argued in the script's own
header and are repeated here because they bound what the numbers mean:

* **The payments are Draft.** `seed-bulk.sql`'s governing rule is that a seeded row never owes rows
  no posting rule wrote — it is why its products are Service-typed — and an Approved payment owes a
  GL entry and an allocation. A Draft payment owes neither, carries `Code = 'DRAFT'` because a
  document number is assigned at Approve, and is legal fifty thousand times over because
  `(OrganizationId, Code)` on `Payments` is not unique. It also leaves `GlLines` untouched, which is
  what keeps the three financial statements comparable across this phase's own pair.
* **The cheques carry the full status mix anyway**, though phase 17 pairs a Pending cheque with a
  Draft payment. This is a divergence, stated rather than hidden. It costs nothing under measurement
  because **neither query that reads this table reads `payment.Status`**, and it buys the register's
  two status tabs and its dashboard counts a real distribution instead of one value.

`run-measurement.sh` gained the register's eight paths and — this is the part that matters — a second
data-presence assertion, because 34c's rule is that a harness must assert its target is populated
before timing it and the existing assertion only knew about invoices.

### Decision A.1 — wall time could not answer this question, so a second instrument was built

The 41-endpoint pass is in the repository and is *not* what any claim here rests on. Two passes of
the identical shipped configuration disagree by up to 3× on the report class; between the before pass
and the first after pass, `stmt.trial-balance` "improved" from 5,319 ms to 1,117 ms and
`reg.sales-register` "regressed" from 3,132 ms to 5,190 ms, and this phase changed neither. The
harness README has said so since 34c and phase 42 discarded two passes over it.

`tools/scale/probe-cheque-io.sh` is the answer: for each path it clears the plan cache, issues the
request three times, and reads the per-execution **logical reads and CPU** back out of
`sys.dm_exec_query_stats` for every cached statement that named the `Cheques` table. Those are
properties of the plan, not of what else the machine was doing. Every number in this document that
decides something comes from there; the wall-clock table is reported as corroboration and labelled as
such.

---

## Decision B — what the measurement authorised, and the three things it refused

Taken on the 50,001-cheque tenant, `UPDATE STATISTICS … WITH FULLSCAN` on both sides, plan cache
cleared per path. Logical reads / CPU ms.

| path | before | **shipped** |
|---|---|---|
| `cheques.page1` | 2,001 / 168.6 | **1,082 / 29.5** |
| `cheques.last` | 4,422 / 680.0 | **1,289 / 40.9** |
| `cheques.received` | 2,606 / 106.9 | **2,090 / 33.9** |
| `cheques.status` | 1,731 / 85.6 | **1,327 / 15.2** |
| `cheques.daterange` | 2,606 / 49.4 | **577 / 13.7** |
| `cheques.search.hit` | 263,164 / 1,694.3 | 326,015 / 2,331.1 |
| `cheques.search.miss` | 430,084 / 2,258.4 | **3,957 / 864.8** |
| `cheques.dashboard` | 2,110 / 48.1 | 2,110 / 49.0 |

**Refused #1 — the covering search index.** With `(OrganizationId, ChequeDate)` in place, a term
matching nothing went from 430,084 reads to **590,603**: phase 34c's first finding, reproduced on a
different table three phases later, because the optimizer stopped scanning once and started seeking
the ordering index with a lookup per row. 34c fixed its version of this with a covering search index,
so this phase built the same thing — `(OrganizationId, ChequeNo)` INCLUDE-ing every projected column
— and measured **588,456 reads. A 0.03 % improvement.** It is not shipped. The reason is structural
and is finding #4 below.

**Refused #2 — the tenant predicate on the joined tables.** See Decision C.

**Refused #3 — `(OrganizationId, CreatedAt DESC)`.** The convention gives every document table the
list index beside the range index, because every document list orders `CreatedAt DESC`. This one does
not: it orders by `ChequeDate DESC` and has no `Sort by` menu at all. An index nothing can ask for is
write cost plus one more way for the optimizer to change its mind, so `DeclaredBusinessDates` carries
a `ListOrdersByCreatedAt` flag and `Cheque` sets it false. The scaffolded migration contains exactly
one `CreateIndex`, which is how that was verified rather than assumed.

**Accepted, with its size reported honestly.** The index is declared **descending**, because for this
one entity the range index is also the ordering index. Two back-to-back passes with statistics
refreshed and the cache cleared: ascending 1,450 reads / 29.2 ms, descending **1,286 / 24.4 ms**. An
earlier probe had read that gap as nearly 2× and that probe carried different plan-cache state — the
smaller, repeatable number is the one in the code comment, because the difference between the two is
the difference between a measurement and a number.

---

## Decision C — the change this phase made for one path, and what it did to the others

The search paths are the worst thing on this table by two orders of magnitude, and the reason is
visible in one line of generated SQL:

```sql
WHERE [c].[OrganizationId] = @org
  AND ([c].[ChequeNo] LIKE @term OR [c0].[Name] LIKE @term)
```

`[c0]` is `Contacts`, three joins away. **`ListChequesQuery` is the only list in the application that
matches a column on a joined table** — every other one matches its own `Code` and `Reference`, which
is exactly why `TenantIndexConvention`'s covering search index is expressible as a rule at all. The OR
spans two tables, so the optimizer must form the joined row for all 50,000 cheques before it can
evaluate the term. No index on `Cheques` changes that, which is why refusal #1 measured at 0.03 %.

A hand-written `UNION` of the two halves was measured first (1,547 ms CPU against the OR's 1,625 ms —
not an improvement worth a rewrite). Then the statistics showed `Contacts` being read 153,134 times,
which is the **whole table across all three tenants**, because the tenant predicate sits only on the
`Cheques` side. Stating it on all four tables took the search page query to 1,312 ms and the `Cheques`
reads from 153,392 to 888. That looked like the answer, and it is correct in this codebase's own idiom
— there is no global query filter here, so a join that does not say `OrganizationId` is a join across
every tenant.

**Then the untouched paths were re-measured, and it was a disaster.** Taken twice — once against the
index-only configuration where the idea arose, and again at the end of the phase against what
actually ships, so the refusal is not an artefact of the order things were tried in
(`probe-cheque-p50-D-tenant-predicate.csv`):

| path | shipped | shipped + the tenant predicate |
|---|---|---|
| `cheques.page1` | 1,082 / 29.5 ms | 6,330 / **674.9 ms** |
| `cheques.last` | 1,289 / 40.9 ms | 9,903 / **953.3 ms** |
| `cheques.received` | 2,090 / 33.9 ms | **83,734** / 452.7 ms |
| `cheques.status` | 1,327 / 15.2 ms | **67,774** / 369.8 ms |
| `cheques.daterange` | 577 / 13.7 ms | 32,424 / 289.3 ms |
| `cheques.search.hit` | 326,015 / 2,331.1 ms | 222,030 / 2,478.1 ms |

Four tenant predicates gave the optimizer four new leading-`OrganizationId` indexes to drive from, and
it chose to drive from the wrong one on every path except the one the change was for. **Reverted** —
and the refusal is recorded in the handler itself, above the `where` clause somebody would otherwise
correctly think was missing three conditions.

This is phase 34c's lesson — *an index added for one access path changes the plan for every other path
over the same table* — arriving from the other direction, against a change this phase was pleased with,
and caught only because the roadmap made re-measuring the untouched paths a condition of the phase
rather than a courtesy.

---

## Decision D — the paging change, which was not in scope and turned out to be the important one

`cheques.last` failed its 500 ms budget on both sides of the index change (1,436 ms before, 1,750 ms
after). Phase 42 swept every paginated **document list** onto `ToKeyPagedResultAsync` and got sixteen;
this register is the seventeenth list and is not a document list, so phase 42 never saw it — the same
blind spot, for the third time in one phase. Phase 42's own words describe what was happening
exactly: *a page's rows are fetched before they are eliminated.*

Applying the existing helper — page the keys, then fetch those rows, then project in memory over the
fifty rows that survived — cost about fifteen lines and delivered two things:

* `cheques.last`: 4,230 reads / 697.6 ms → **1,289 / 40.9 ms**, a 17× reduction in CPU.
* `cheques.search.miss`: 590,603 reads / 2,622 ms → **3,957 / 864.8 ms**, a 150× reduction in reads.

The second was not the intention and is the more valuable. It comes from the *other* half of phase
42's helper — **a count of zero is a complete answer, so the page query is never issued** — and it is
what makes the index shippable at all. Without it the phase would have had to choose between a 12×
improvement on the register's default path and a 37 % regression on a search term matching nothing.
With it there is no trade: every path except a search term that *matches* is better than it was.

---

## Decision E — where a cross-assembly invariant lives, decided once for everything after

Phase 40 stated the rule and phase 47 carried it as item #6: `ISortableQuery` says an ordering may be
offered *exactly when an index leads on `(OrganizationId, <that column>)`* and calls the `Sort by`
menu "a reading of Infrastructure's `TenantIndexConvention`"; the convention says which indexes it
builds. Two prose paragraphs facing each other across an assembly boundary, because
`Application.UnitTests` references Application only.

**The decision: a new `tests/Infrastructure.UnitTests` project, and the generalisation that goes with
it — an invariant that can be decided from types and model metadata belongs in a unit-test project
that references both assemblies; only an invariant that needs a live database belongs in
`Api.IntegrationTests`.** The EF model builds without a connection, so `ModelFixture` opens nothing;
the alternative would have put a build-failing structural invariant inside the suite that fails in its
constructors whenever Docker Desktop is off, where a failure is routinely and correctly ignored. That
sentence is the deliverable, not the one test.

The project also became the instrument for Decision F — the mirror question needed something that
could build the real model, and until this project existed nothing could.

### The test, and the regression that proved it was worth writing

The first version asserted that every sortable list's entity has a `(OrganizationId, CreatedAt)` index
and a `(OrganizationId, <business date>)` index. Phase 34a's rule is to prove a guard bites by
injecting the regression it is supposed to catch, so `ListSort.Amount = "amount"` was added — **and
the test passed.** It was asserting that two indexes exist, both of which do; it was not asserting the
*rule*, which is about the orderings actually on offer. Rewritten to iterate
`ListSort.DocumentOrderings` through a map of ordering → column, it fails with the member named and
tells the reader that an ordering nobody can name a column for is an ordering nobody can index. The
file was restored **by hash**, not by `git checkout` (34a's rule) — and that turned up its own trap,
recorded in the gotchas: a restored backup carries an *older* mtime, MSBuild calls the assembly up to
date, and the suite keeps running the injected build while the source on disk is clean.

The business date is resolved **from the model** rather than from the convention's own list, or the
assertion would be circular: it is the single required `DateOnly`, tie-broken to `Date` for the two
aggregates that carry a required `DueDate` as well. That second half is what lets it resolve
`Cheque.ChequeDate` without reading `DeclaredBusinessDates`.

The sweep is pinned at **15** sortable queries over 16 document lists — phase 47's "fifteen queries
over sixteen screens", where `ListPaymentsQuery` backs two. The first attempt asserted 14 and was
wrong; the count is now derived and stated rather than remembered.

---

## Decision F — the name list stays, and can no longer be silently wrong

The roadmap asked whether matching by name is the defect and whether the convention should take the
date column from the entity instead. Neither, exactly. Taking it from the entity is what the name
match already *is*; the defect is that **failing to find one is indistinguishable from master data**,
and that is what was fixed:

* `BusinessDateNames` still derives fourteen of seventeen, and reading it is still how someone learns
  what a document is here.
* `DeclaredBusinessDates` declares the ones it cannot — today, `Cheque` — and carries the
  `ListOrdersByCreatedAt` flag Decision B needed. A declaration naming a property the entity does not
  have throws with both named.
* `DatesThatAreNotBusinessDates` excuses the ones whose required date is not a business date, with the
  reason: `StockLedgerEntry` and `StockMovement` (read by product and warehouse, never by tenant and
  date — phase 26c) and `AlertSendLog` (an occurrence key, phase 20e).
* Anything in neither **fails the model build**, naming the entity and the property.

The predicate is "a required `DateOnly`", and both halves are deliberate: a business date here is
always a `DateOnly` — every audit stamp is a `DateTimeOffset` — and a nullable one is a secondary date
a document carries beside its own (`Invoice.DueDate`, `Quotation.ExpiryDate`, `Cheque.ReceivedDate`).
Neither half is a guess about names, which is the point.

`SortSweepGuardTests.Exempt` was updated rather than left: its entry gave two reasons and this phase
measured one of them away, so the text now says the screen shape is the *only* surviving reason, and
says what follows from that — if the register ever grows list chrome, the index is already there and
the exemption should go. That is the opposite of what the phase-47 text implies, and it is phase 45's
lesson that an allow-list reason can become the argument for the other conclusion.

---

## Decision G — `role="toolbar"`, re-recorded

Phase 40 item #6 and phase 47 item #5: the roving-tabindex implementation still has exactly one
consumer. The roadmap's instruction was to extract it **only if a second toolbar appears in this
phase's work**. This phase shipped one index, one paging change, one declaration mechanism and one
test project, and no UI at all. No second toolbar appeared. It is re-recorded, unchanged, which the
roadmap correctly calls the honest outcome and which takes one line.

---

## The live extras carried in from phase 49 — not taken

All three needed the user at a browser and none of them blocked anything here, so none were taken and
this is recorded plainly rather than quietly:

* **Product Batch / Product Serial No, from an account holding their permission keys.** Not read. The
  2026-09-16 pass got `permission denied` on both and that is still where it stands, so **phase 51
  designs from the two product tabs, or takes this read first.** Its kickoff says so explicitly.
* **Cadehi's Mode of Inventory Tracking → Physical Movement**, to read Delivery Note / GRN. Not
  flipped — it is a config write on the user's own tenant and therefore their call, not a session's.
* **Cadehi's expiry on 2026-09-22.** Six days away at the time of writing, so there was nothing to
  confirm yet. Phase 49 Decision A does not depend on it.

---

## What shipped

* `tools/scale/seed-cheques.sql` — 50,000 cheques and their Draft payments, with the two seeding
  decisions argued in the header.
* `tools/scale/probe-cheque-io.sh` — the logical-reads instrument, and the reason every claim above
  has a number that is reproducible on a busy machine.
* `run-measurement.sh` — the register's eight paths, and a data-presence assertion for the second
  table; `refresh-stats.sql` — `Cheques`.
* `TenantIndexConvention` — `DeclaredBusinessDates`, `DatesThatAreNotBusinessDates`, the
  `ListOrdersByCreatedAt` flag, the descending business-date index where the list orders by it, and a
  model-build failure for any tenant-scoped entity carrying a required date nobody has classified.
* `20260916…_ChequeBusinessDateIndex` — one index, `(OrganizationId, ChequeDate DESC)`.
* `ListChequesQueryHandler` — `ToKeyPagedResultAsync`, projecting after the page is materialised.
* `tests/Infrastructure.UnitTests` — the project, `ModelFixture`, `ListSortIndexCorrespondenceTests`
  (4 tests) and `TenantIndexConventionTests` (4).
* `SortSweepGuardTests.Exempt` — the cheque entry's surviving reason, and what now follows from it.

**No new permission keys, no Domain change, no Angular change**, and no change to what any screen
returns: `ToKeyPagedResultAsync` produces the same `PagedResult<ChequeDto>` the offset path did.

Tests: Domain 674, Application.UnitTests 1188, **Infrastructure.UnitTests 8 (new)**,
Api.IntegrationTests 30 — all green with Docker Desktop running. Angular **561, unchanged, and that is
the honest number**: this phase changed no Angular code, so it added no Angular test, and inventing
one to move a count would be the failure this codebase's own rule about counting tests warns about.
`ng build` clean at **643.39 kB** against the 680 kB budget, no warning.

---

## Carried items

1. **The Cheque Register's search matches a joined column, and that is what it costs.** A term that
   *matches* is 326,015 logical reads and 2.3 s, over its 500 ms budget, because
   `ChequeNo LIKE … OR Contacts.Name LIKE …` spans three joins and the row must be formed before the
   term can be evaluated. Measured and refused this phase: a covering index (0.03 %), a `UNION` of the
   two halves (no better), and tenant predicates on the joins (better here, catastrophic everywhere
   else — Decision C). What is left is a **semantic or structural** change, not an index: drop the
   contact-name half, or denormalise `ContactId`/`ContactName` onto `Cheque` the way `AccountId`
   already is — phase 17 denormalised that one for exactly this reason and stopped one field short.
   The second is a migration and a backfill and belongs to a phase that decides it on purpose.
2. **A term matching nothing is still 864 ms of CPU** for 3,957 reads, which is the *count* query
   doing the same cross-join OR before returning zero. The page query is now never issued; the count
   still is, because a count is what tells it to stop.
3. **`search.common` / `search.code` / `search.miss` remain over budget** (763 / 712 / 993 ms p95),
   unchanged by this phase and untouched by it. Global search is phase 34c's carried residual and the
   roadmap's out-of-sequence full-text item, which is a semantics change rather than a performance fix.
4. **Nobody has heard the app.** Phase 40 #1, 47 #1, 48's and 49's too. An hour with NVDA.
5. **`role="toolbar"`'s roving tabindex still has one consumer.** Decision G.
6. **The three live reads carried from phase 49 were not taken**, and phase 51 inherits the first of
   them as a decision: read the two reports, or design from the product tabs and say so.
