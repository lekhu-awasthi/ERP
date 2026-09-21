# `tools/scale` — the phase 34c measurement harness

Everything needed to reproduce the numbers in `docs/phase-34c-status.md`. The dataset and what is
measured were fixed in advance by `docs/phase-34a-status.md`'s Decision C; these scripts execute it.

## The dataset

One organization holding **50,001 invoices, 50,002 contacts, 20,001 products, 20,001 purchase
bills, 70,002 GL journal entries and 210,006 GL lines**, spread across the organization's accounting
start date to today. That is the top of NFR-5.1's stated range, so a pass is a pass for every tenant
the PRD anticipates.

Two seeding decisions shape what the numbers mean, and both are argued in the status doc: products
are **Service**-typed (so no seeded document owes stock-ledger rows the posting rules never wrote,
which keeps every seeded row's GL shape *exactly* the reference shape — at the price of leaving the
inventory reports outside the measurement), and `PostedAt` is derived from each document's business
date rather than from "now" (so a date-ranged GL report has a range to filter).

## Running it

```bash
# 1. Master data and the reference rows, through the real API. Creates a fresh organization every
#    time and writes its ids to .seed-ids.env (which is git-ignored).
bash tools/scale/seed-master.sh

# 2. The 190,000 bulk rows, by direct INSERT. ~50-65 s. Takes the org id from step 1's output.
sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-bulk.sql \
  -v OrgId="<guid>" NumInvoices=50000 NumContacts=50000 NumProducts=20000 NumBills=20000

# 2b. The Cheque Register's 50,000 cheques and the Draft payments they hang off (phase 50).
sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-cheques.sql \
  -v OrgId="<guid>" NumCheques=50000

# 3. Before EVERY measurement pass, on both sides of any comparison.
sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/refresh-stats.sql

# 4. The pass itself: 41 endpoints x (2 warm-up + 10 timed) requests.
bash tools/scale/run-measurement.sh <label> 10

# 5. The comparison table, with Decision A's budget applied.
bash tools/scale/summarise.sh before after after2

# 6. When the subject is ONE screen, this is the number to trust (phase 50).
bash tools/scale/probe-cheque-io.sh <label>

# 7. Phase 54's two debts: stock layers and product secondary units, which the 34c seed has none of
#    (its products are Service-typed on purpose). Writes no documents, no GL, no movements.
sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-phase54.sql \
  -v OrgId="<guid>" NumLayers=200000 NumLayerProducts=2000 NumSecondaryUnits=20000
bash tools/scale/probe-phase54-io.sh <label>
```

## Three things that will bite

- **`seed-master.sh` rewrites `.seed-ids.env` every time it runs.** Seeding a second organization
  repoints the harness at it, and a pass against an empty tenant looks like a spectacularly fast one
  — 20 ms p95, every status code 200. `run-measurement.sh` refuses to start unless a 50-row invoice
  page comes back over 1,000 bytes, which is the only signal that distinguishes them.
- **Refresh statistics on both sides of any comparison.** On a bulk-`INSERT`-seeded database,
  statistics quality moves a report that joins to a line table by 2× — more than most changes under
  test.
- **The report class is not reliably measurable on a working machine.** `results-after.csv` and
  `results-after2.csv` are two passes of the *identical* configuration and they disagree by up to 4×
  on the financial statements, while agreeing within 30 % on every list page. `summarise.sh`
  therefore judges each row over both after-passes and prints `MIXED` where they straddle the budget.
  Claims about reports in the status doc come from within-pass period-sensitivity probes instead.

## The committed results

### Phase 50 (2026-09-16) — the current pair, and a second instrument

Taken on the **same** tenant as phase 42, with `seed-cheques.sql` run against it — so `Payments` and
`Cheques` each hold 50,000 more rows than they did in phase 42, and the phase-42 files are therefore
not comparable with these either. Use each pair within itself.

| file | what it is |
|---|---|
| `results-p50-before.csv` | The pre-phase schema, after the cheques were seeded. |
| `results-p50-after.csv` / `results-p50-after2.csv` | Two passes of the index-only configuration. |
| `results-p50-after3.csv` | The **shipped** configuration: the index plus key-paging. |
| `probe-cheque-p50-before.csv` | No index. The baseline every claim is against. |
| `probe-cheque-p50-A-index-asc.csv` | `(OrganizationId, ChequeDate)` ascending, as first scaffolded. |
| `probe-cheque-p50-B-index-desc.csv` | The same index descending — the direction that shipped, and the pair that justifies it (1,450/29.2 ms against 1,286/24.4 ms on the first page). |
| `probe-cheque-p50-C-covering-search.csv` | **Refused.** The covering `(OrganizationId, ChequeNo)` search index: 591,494 logical reads to 588,456 on a non-matching term. 0.03%. |
| `probe-cheque-p50-D-tenant-predicate.csv` | **Refused.** The tenant predicate on the three joined tables: better for search, and the Received tab from 2,090 logical reads to 83,734. |
| `probe-cheque-p50-shipped.csv` | What ships: the descending index plus `ToKeyPagedResultAsync`. |
| `probe-cheque-p50-early-desc-probe.csv` | An exploratory pass kept only because the status doc corrects a number from it — it read the ascending/descending gap as nearly 2× on a different plan-cache state, and the controlled pair above says 1.13×. |
| `comparison-phase50.md` | `summarise.sh p50-before p50-after3`. |

**The fifth thing that will bite, and phase 50 built a tool for it:** on a working machine the wall
clock cannot answer a question this small. Between this phase's before and after passes
`stmt.trial-balance` "improved" from 5,319 ms to 1,117 ms and `reg.sales-register` "regressed" from
3,132 ms to 5,190 ms, and the phase changed neither. **`probe-cheque-io.sh` is what the phase's
claims actually rest on**: per path it clears the plan cache, issues three requests, and reads the
per-execution logical reads and CPU out of `sys.dm_exec_query_stats`. Those are properties of the
plan rather than of the machine, they reproduce, and any future phase measuring one screen should
copy that script rather than the 41-endpoint pass.

Note also that `run-measurement.sh` now asserts **two** tables are populated, not one. The `Cheques`
table held five rows in the whole database before this phase, which would have read as a
spectacularly fast register.

### Phase 42 (2026-09-14) — the current pair

Taken on a **third** freshly seeded 50,001-invoice tenant, so `GlLines` holds about 630,000 rows
rather than 34c's 420,000. The 34c files below are therefore **not** directly comparable with these
— use them within their own pair, not across.

| file | what it is |
|---|---|
| `results-p42-before.csv` | The pre-phase code, on that tenant. |
| `results-p42-after3.csv` / `results-p42-after4.csv` | Two passes of the final configuration. |
| `probe-p42-before.csv` / `probe-p42-after.csv` | The period-sensitivity probe (1 month / 1 year / 3 years per report, plus the subscription quota count), consecutive inside one pass so the machine is controlled for. |
| `comparison-phase42.md` | `summarise.sh p42-before p42-after3 p42-after4`. |

Two intermediate passes were taken and are deliberately **not** committed: the ageing allocation
fix landed between them, so they measure a configuration that no longer exists. Phase 34c made that
mistake once and its README says why; phase 42 discarded rather than averaged, for the same reason.

**A fourth thing that will bite, added by phase 42:** `dotnet build` cannot copy over a running
API (`MSB3027 … locked by: ErpApp.Api`), and every pass needs a rebuild between it and the last.
Stop the API, build, start it, refresh statistics, then measure — in that order, every time.

### Phase 34c (2026-09-10) — the original pair

| file | what it is |
|---|---|
| `results-before.csv` | The pre-phase schema: no index leading on `OrganizationId` on any document table. |
| `results-after.csv` | All 50 indexes, same database, same statistics treatment. |
| `results-after2.csv` | An independent repeat of `after`, unchanged configuration. Its disagreement with `after` is a result in its own right. |
| `comparison.md` | `summarise.sh before after after2`, which is the table in the status doc. |

Four earlier passes were taken before the before/after pair was properly controlled (the database
had grown a second tenant between them, and one had a narrower statistics refresh). They are
deliberately **not** committed — the status doc explains why they were discarded rather than
averaged in.
