# Next session kickoff — and the first decision is what the phase *is*

Phase 56 is complete and **uncommitted** (a ready commit message is in the session hand-off). Start
here; do not continue its thread.

**`docs/roadmap.md`'s forward plan is empty, and that is a real state rather than an oversight.**
Phase 53's permission-key census found this codebase at parity with the reference product except for
bank reconciliation; phases 55 and 56 built it. Nothing since has been read that adds a feature to
the sequence. So this session's first job is not to implement — it is to **choose**, from the five
options below, and then to work the way the chosen one needs.

Read `docs/phase-56-status.md`'s TL;DR and "Carried into a later phase" before choosing; that is
where four of the five come from.

---

## The five things that could be next

**A — Quick Approve** *(a feature phase, evidence already in hand)*. On the reference product's
account Overview, each unmatched statement row carries an account picker and a green tick: it turns
the statement line into a **Customer Payment / Supplier Payment / Quick Receipt / Quick Payment**,
routed by the chosen account's type, tying the new document back with a `statement_id`. Read in full
on 2026-09-21 — the payload and the four-way routing table are in `docs/erp-module-scan.md`'s
phase-56 appendix. **All four documents already exist here** (phase 17), so this is wiring plus one
decision: whether the created document is auto-reconciled against the line it came from (it almost
certainly should be, and that is the interesting part). Sized like phase 56's smaller half.

**B — the auto-match suggestion engine** *(a feature phase that needs a decision nobody has made)*.
`/bank-statements-matched`, `account_suggestions`, `is_suggestion_completed`, and
`POST /bank-reconciliations` with `action: "approve"` / `all: true`. The *surface* is read and
recorded; the **matching algorithm is not**, and the vendor's demo tenant produced
`account_suggestions: null` on every row, so there is nothing to copy. This is the only item here
that would require inventing behaviour, which is exactly what the roadmap's method says to be
careful about. If chosen, the scope decision is whether a deliberately dumb rule (exact amount +
date window) is worth shipping, or whether the honest answer is to defer it with a re-entry
condition.

**C — the reconciliation report's `.xlsx` export**. The vendor has `bank-reconciliation-export`.
`BankReconciliationReportDto` already carries every figure and both lists, so this is a
`ReportSpreadsheetExporter` call and a button — an hour, not a phase. **Fold it into whatever else
is chosen** rather than making it the phase.

**D — the balance-history chart**. `GET /balance-history/:id` → `[{day, tigg_balance, bank_balance}]`
for the last 30 days, drawn as two lines on the account Overview. Derivable entirely from what phase
56 already reads. Also too small to be a phase on its own; it pairs naturally with A.

**E — a re-planning phase, the way 53 was one.** Phase 53's method was to stop reading screens and
census the vendor's own enumeration of its features. That census is a year older than the product
now; the vendor shipped batch/serial traceability in August 2026 (found by accident in phase 51) and
its August release notes name e-commerce sales. **Re-running the key extraction over the current
bundle is cheap** — one regex over `main.*.chunk.js` + `13.*.chunk.js` — and it is the only option
here that can discover something none of us has thought of. If the diff against the recorded 166 is
empty, that is a real and useful answer, and the phase becomes "A plus C plus D".

**The recommendation, if one is wanted: E first as a half-day, then A+C+D as the build.** E is cheap
and is the only thing that can change the answer; A is the best-evidenced feature left.

---

## Whatever is chosen, these three are unchanged

From `docs/roadmap.md`, "Outside the sequence" — items a session cannot schedule because their start
condition needs somebody other than a session:

- **An hour with NVDA.** Start condition: a person with headphones. Still the only thing between this
  codebase and a finished WCAG 2.1 AA story. Phase 40's rule: record what is heard *verbatim* before
  changing anything.
- **The two traceability reports' real column sets** (Product Batch, Product Serial No). Start
  condition: an account or tenant holding the two keys the demo Admin lacks. Phase 32's lesson — ask
  whether another tenant exists before invoking the derive-instead precedent.
- **Full-text search.** Start condition: a tenant complaining about search latency, or a product
  decision about what "search" should mean. Phase 42 was explicit that the remedy is a *semantics*
  change, not a performance fix.

---

## Read first, whichever way this goes

- `docs/phase-56-status.md` — the TL;DR, "Decision A" (why a reconciliation joins `GlLine`s and what
  that costs), and "Carried into a later phase".
- `docs/erp-module-scan.md` — the **2026-09-21 phase-56 appendix**, which carries the module's whole
  endpoint map, the Quick Approve routing table and the two vendor defects.
- `docs/phase-lessons.md`: the **53, 54, 55 and 56** paragraphs.
- `docs/known-gotchas.md` — the new heading *"`OrderBy` over a projected record"*, which is the
  fourth instance of a family that has now cost four phases.
- `docs/e2e-recipes.md` before writing any seed script. Phase 56 added four entries, including the
  import template's `**`-suffixed headers and the Journal-Voucher-as-GL-seed recipe.
- `CLAUDE.md`'s Current status.

## The bar, unchanged

- `dotnet build` / `dotnet test` / `ng build` / `ng test` all green (`ng test` from `web/`, Node 24).
  `Api.IntegrationTests` needs Docker **and** a quiet machine — it failed 10/30 once in phase 56
  while builds ran alongside it and passed 30/30 on a clean re-run.
- A hand-driven E2E on a **fresh Organization**, seeded by curl + cookie jar, with at least one
  negative path: a **403 naming the exact key against a nonexistent id**, beside a 200 from the same
  user on the same organization in the same run. `scratchpad/e2e56_403.py` is a working template
  including both controls and the `UPDATE tenancy.OrganizationMemberships` restore.
- If anything is added that a report or a list reads, the claim goes in **SQL**, not in prose.
- `docs/phase-N-status.md` with a TL;DR, plus the refresh of `CLAUDE.md`'s Current status,
  `docs/phase-lessons.md` and `docs/roadmap.md`.
- **Do not commit** — hand over a ready commit message.
