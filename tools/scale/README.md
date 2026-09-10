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

# 3. Before EVERY measurement pass, on both sides of any comparison.
sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/refresh-stats.sql

# 4. The pass itself: 33 endpoints x (2 warm-up + 10 timed) requests.
bash tools/scale/run-measurement.sh <label> 10

# 5. The comparison table, with Decision A's budget applied.
bash tools/scale/summarise.sh before after after2
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
