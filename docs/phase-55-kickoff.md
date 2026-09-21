# Phase 55 kickoff — bank statement import

Phase 54 is complete and **uncommitted** (a ready commit message is in the session hand-off). Start
here; do not continue its thread.

This is an ordinary feature phase, and the first half of the bank-reconciliation pair.

---

## What this phase is

`/config/import-statement` in the reference product: upload a bank statement file, see its lines, see
what was rejected. It renders `POST ENTRIES` over a `Total rows / Valid / Errors` counter — which is
phase 38's dry-run shape, already built here.

It is the feeder for phase 56's matcher and **independently demonstrable**, which is why the pair was
split here: a matcher with nothing to match against is not runnable.

## Read first

- `docs/roadmap.md` — **Forward plan (55–56)**, specifically the `### 55.` entry, and the **Ordering
  rule** above it. (The `### 54.` entry is gone; 54 is done.)
- `docs/phase-38-status.md` — **TL;DR and the `PlanAsync`/`ImportRowPlan` decision**, the row ledger,
  and the `Provisional` plan. This phase lives or dies on that machinery.
- `docs/phase-21a-status.md` — **Decision C and bug 1** (no concurrency token on a job row), and
  `IJobActingUser`.
- `docs/phase-21c-status.md` — `ImportRowReader.GetOptionalDate`'s day-first-before-month-first
  format list.
- `docs/erp-module-scan.md`, the **2026-09-20 appendix** ("Re-planning read") — the Bank
  Reconciliation section, which names the endpoints and says what was *not* read and why.
- `docs/phase-lessons.md`: the **38, 53 and 54** paragraphs.
- `CLAUDE.md`'s Current status and the gotchas under *Imports and exports* and *Background jobs*.
- `docs/e2e-recipes.md` before writing any seed script.

## The read to take first

`/config/import-statement` was opened in phase 53 but only glanced at — the appendix records the
`POST ENTRIES` control and the `Total rows / Valid / Errors` counter, and one vendor defect (a stale
Delivery Note column filter row leaking in from a sibling screen). **It has never been driven with a
file.**

So: take a row-present read of the whole flow — upload something, see the counter, see an error row,
find out what `POST ENTRIES` does and when it is enabled. **No start condition beyond a browser**;
the user logs in, never enter credentials.

**Phase 54's caveat applies here in a new form.** An empty screen could not tell a *disabled* control
from a *display* element; here the equivalent risk is that a counter showing `0 / 0 / 0` cannot tell
you what a rejected row looks like, or whether the vendor commits per row or per file. Get a file in
front of it — including a deliberately broken row.

Two things worth settling in the same pass, because both are cheap once the screen is open:

- **What a statement line actually holds.** Date, description, debit/credit or a signed amount,
  balance, reference — and whether the importer maps columns or fixes them.
- **Whether `fetch-statements` is reachable from this screen.** The roadmap puts the bank *feed*
  explicitly out of scope (no vendor-side actor exists here); confirm it is a separate control rather
  than the same one, so the exclusion is recorded against something observed.

## The scope decisions to make

1. **The design question, and it is the phase's real one: what does phase 38's machinery do when a
   row resolves into no command?** Phase 38's rule is that an importer resolves a row into the
   command it *would* send (`PlanAsync` → `ImportRowPlan`) and only then sends it, so the dry run and
   the real run share every line of resolution. A bank statement line resolves into **no command** —
   it is a raw row awaiting a match, and its only destination is a table. Decide whether that is a
   tenth `ImportTemplateDefinition` with a degenerate plan, or a different mechanism, and record the
   reasoning. Phase 38 already has the precedent that a template generated from the document in front
   of you is *neither* (the landed-cost grid), so "it doesn't fit, therefore it isn't one" is an
   available and legitimate answer — it just has to be argued.
2. **What the imported row is, as an aggregate.** It has a lifecycle of sorts (unmatched → matched)
   but phase 51's lesson says a *Status* filter is often a derived column rather than a modelled
   lifecycle. Look at what the vendor's own feed filters on (`reconciled: false`) before inventing
   states.
3. **Permission keys, derived not defaulted.** The census found bank reconciliation carries **no keys
   of its own** beyond `bank-reconciliation-export` — it rides `bank-view` / `bank-edit` /
   `bank-full-access`. Derive ours the same way rather than minting a new family by reflex, and note
   that phase 32b's per-location scope applies because a bank account is location-scoped.
4. **Deletion.** `bank-statements-delete` exists in the vendor's endpoint list. Any feature that
   writes rows (or a blob) owes its deletion story decided with it — phase 21b Decision E.
5. **Reuse, do not re-derive.** The date-column format list (`ImportRowReader.GetOptionalDate`, phase
   21c — day-first before month-first, never a bare `TryParse`; assert the ambiguous case) and the row
   ledger that makes a crashed import resumable and lets a validate pass claim only the rows it
   rejects.
6. **Where the compiler stops** is now a standing decision, not a flourish. Phases 51, 52 and 54 each
   turned on it, and 54's instance was the *third* Void restocking an entered quantity. If this phase
   adds a value that must not be confused with another (an amount in the statement's currency? a
   signed amount?), say so in the type.

## Exit bar

The roadmap's standard bar, plus this phase's specifics:

- `dotnet build` / `dotnet test` / `ng build` / `ng test` all green (`ng test` from `web/`, Node 24).
- A hand-driven E2E on a **fresh organization**: seed master data by curl + cookie jar
  (`docs/e2e-recipes.md` first — a wrong field name is usually a 400 naming no field), reserve browser
  clicks for this phase's own screens. Phase 54 added four recipe entries, including the twelve
  `accounting-defaults` spellings that actually deserialise.
- **The dry run and the real run must be shown to agree** — that is phase 38's whole claim, and the
  cheapest proof is a file with one bad row: the validate pass names exactly that row, and the apply
  pass skips exactly that row through the same mechanism.
- At least one **negative** path: a 403 naming the exact key against a **nonexistent id** (so
  403-not-404 proves the behaviour fired before the handler), beside a 200 from the same user on the
  same organization in the same run. Prove it **last**, or against a throwaway user — a restricted
  role cannot restore itself through the API (phase 52's carried item 4; phase 54's E2E restores the
  membership by `UPDATE tenancy.OrganizationMemberships` and then re-checks a 200).
- If this phase adds an index or changes a query plan, it owes a **number** — `tools/scale/` is the
  harness, logical reads not the wall clock, and `tools/scale/comparison-phase54.md` is the most
  recent worked example. Note that an ad-hoc `sqlcmd` batch has no `sys.dm_exec_query_stats` row (it
  is cached as a plan stub); use `SET STATISTICS IO` for anything issued outside the app.
- `docs/phase-55-status.md` with a TL;DR block, and the usual refresh of `CLAUDE.md`'s Current status,
  `docs/phase-lessons.md` and `docs/roadmap.md`'s index row.
- **Do not commit** — hand over a ready commit message, and end by generating the phase 56 kickoff.

## Carried into this phase from 54

- **Phase 56's start condition is unchanged and still unmet**: the reconciliation screen takes its
  account from router state, and neither tenant holds a **Bank**-type cash-and-bank account. If this
  phase's live pass can create or find one, say so — it would unblock 56's read.
- Phase 54's four carried items are in `docs/phase-54-status.md`; none blocks this phase. The one
  worth knowing is that **the print pipeline renders a quantity with no unit for all nine
  unit-bearing types** — deliberately left alone rather than fixed for one type.
