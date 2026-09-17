# Phase 52 kickoff — A unit on the document line

Phase 51 is complete and committed (`0654a71b`, with its browser-pass docs following in
`e7c45d9`). Start here; do not continue its thread.

---

Phase 52 — **A unit on the document line**, `docs/roadmap.md` heading "### 52.". The phase that
makes phase 45 mean something: a secondary unit is priced catalog metadata today and *nothing
consumes it*. No document line stores a unit, so a conversion rate converts nothing (45 #2).

It follows 51 deliberately, and 51 has now paid part of its cost: the four line types that move
stock already carry an allocation, and `LineStockAllocator` is the **one place** a line's worth of
stock is moved. Phase 51's own experience says the second dimension really is cheaper on a line that
already carries one — but read its warning about *which* half is cheaper (the trap section below).

## Read first

- `docs/roadmap.md` heading "### 52.", plus the **Ordering rule** paragraph above it.
- **`docs/phase-45-status.md`** (TL;DR, then Grep "conversion rate", "matrix", "Action") — a variant
  owns its unit matrix, a parent is refused one, and phase 45 added Update/Delete so a mistyped
  conversion rate is no longer permanent. That last fact is this phase's whole problem: **a rate can
  change after a document was approved under it.**
- **`docs/phase-51-status.md`** (TL;DR + Decisions D, E and the "bug only the E2E could find"
  section) — the line-allocation shape you are about to extend, why the control sits where it does,
  and the sweep lesson that will bite this phase in exactly the same place. Its *browser pass*
  section is short and worth the read: both corrections it produced were the author's rather than
  the code's, and one of them is the E2E-script trap named in the exit bar below.
- `docs/phase-37-status.md` (TL;DR, then Grep "catch-up") — the **stored-versus-live** choice, made
  once already. A shortfall is issued at an assumed cost and corrected at the real one, and the
  correction has to reach three views. A conversion factor is the same shape of question with a
  different answer available.
- `docs/phase-7-status.md` (TL;DR) — quantities reach the stock ledger in the **primary** unit, and
  `StockLedgerEntry` has no unit column. Deciding it stays that way is a decision, not a default.
- `docs/phase-25-status.md` (TL;DR) — the conservation law, which is this phase's acceptance test
  too: `quantity in the entered unit × factor = quantity in the primary unit`, and every report
  showing a quantity has to say which unit it means.
- `docs/phase-24-status.md` (TL;DR) — a variant *is* a Product, so the unit matrix already hangs off
  the row the line names. The parent/variant rule is settled and this phase consumes it.
- `docs/phase-lessons.md`: the 7, 24, 25, 37, 45, 51 paragraphs.
- `docs/known-gotchas.md`: "A sweep driven by the compiler stops exactly where the compiler stops",
  "A dimension with its own quantity is a second truth", "Guard the add and the edit, never the
  delete", "A child appended to an already-tracked parent's encapsulated collection", and phase 51's
  shortfall-key entry.
- `docs/e2e-recipes.md` before any seed script — **including phase 51's three new entries**, one of
  which (`defaultAccountsReceivableId` / `defaultAccountsPayableId`, and no cash account on that
  payload) cost phase 51 two E2E runs.

## The first decision, before any line changes

**What an approved document, a return and a conversion do when the product's conversion rate is
later edited.** A stored factor versus a live lookup — the same choice phase 37's cost catch-up
made, and the roadmap names it as this phase's first question.

State it concretely before choosing:

- A bill is approved for `10 CTN` of a product whose Carton→Piece rate is `12`. The ledger receives
  `120 pieces`.
- Someone then edits the rate to `10` (phase 45 made that possible, and made it possible on purpose
  — a mistyped rate used to be permanent).
- What does the **approved bill** now say it received? What does a **Debit Note** converted from it
  return? What does the **stock report** show?

A **stored factor** (the line records `12` at approve time) makes the document immutable and correct
about its own history, at the cost of a column that can silently disagree with the catalogue. A
**live lookup** keeps one source of truth and rewrites history. Phase 37 chose the equivalent of
stored-plus-correction — an assumed value, then a catch-up posted when the real one arrives — and
its lesson is that the correction must reach **every** view or two of them drift. Decide, and record
the reasoning before the first migration.

Two more that fall out of it and should be settled in the same sitting:

- **Does `StockLedgerEntry` gain a unit?** Phase 51 says no by analogy — a dimension that can be
  derived should not be stored — but the analogy is not free here, because the *entered* unit is
  genuinely not derivable from the primary quantity alone. If the line stores the unit and the
  factor, the ledger needs nothing; say so explicitly rather than by omission.
- **What does a report showing a quantity mean by it?** Phase 51 left `unitName` on both new report
  rows reading the product's primary unit. Every quantity-bearing report has to answer the same
  question, and "the primary unit, always" is a legitimate answer that has to be *said* — phase 51's
  Decision A discipline applied to a smaller thing.

## A trap phase 51 laid, specifically for this phase

**Optional parameters sweep nothing.** Phase 51 threaded two arguments through `IStockLedgerService`
and got exactly half a sweep for free: `ConsumeAsync`'s **return type** changed, so the compiler
enumerated all five call sites and that half was correct from the first green build.
`IncrementAsync` only gained **optional parameters**, so nothing failed to compile — and
`ApprovePurchaseBillCommandHandler` shipped un-swept past a clean build, 1,900 green tests and a
correct detail DTO. Every receipt of a batch-tracked product created an un-batched layer, and the
first symptom was three steps later and in another subsystem.

A unit on the line is *exactly* the same shape of change. So decide up front how the call sites will
be enumerated: change a signature in a way that breaks them, make the parameter required where it
must be supplied, or write the list down by hand and check it off. Do **not** rely on having
remembered.

While there: `LineStockAllocator` and `DocumentLineAllocationWriter` are phase 51's two shared
seams, and CLAUDE.md's rule is to retire copies 1..N before writing copy N+1. If a unit needs a
third seam, ask first whether it belongs in one of those two.

## Scope decisions to make

- **Which line types carry a unit.** Phase 51's rule was *the control is where the read put it, and
  everywhere else derives or refuses*, and it is worth asking whether the same rule applies — the
  scan records `100BTL` on a Moonbeam invoice line as the display shape to match, which is one
  screen and therefore one sample. Find the rule rather than sampling a list (phase 30).
- **Conversions and returns.** A Credit Note converted from an Invoice, a Debit Note from a Purchase
  Bill: phase 51 prefills the allocation from the source line, and the unit has the same precedent
  (phase 19's `ExpenditureClassification`). Phase 35a's rule is the one to fear: adding a field to
  many aggregates owes **write, read, and every prefill between** — it bit phase 51 four times, and
  the compiler caught all four only because the editable line type is exhaustive.
- **Permission keys**, derived per feature rather than defaulted. A unit on a line is very likely
  *no new key at all* (it rides the document's own), which is what phase 24 concluded for variants
  and phase 51 for batches — but say so rather than assume it.
- **Whether the importers carry it.** Phase 38's `PlanAsync`/`ImportRowPlan` and the pre-commit dry
  run are the mechanism if yes; phase 51 declined an importer with a reason (an entity whose
  quantity is derived has nothing to import its source document does not already carry) and this
  phase should give its own reason either way.

## Exit bar

- `dotnet build` / `dotnet test` clean. `Api.IntegrationTests` needs Docker Desktop; without it 10
  of 30 fail in their constructors with `DockerEndpointAuthConfig`, which is not a regression, and
  it also fails nondeterministically under load — re-run before believing it.
- `ng build` clean with no budget warning (phase 51 left the initial bundle at **643.68 kB** against
  680 kB).
- `ng test` from `web/` on Node 24 (`nvm use 24.11.0`) with a count above **576**.
- Manual E2E on a **fresh Organization** seeded by curl, proving the conservation law for units end
  to end: a receipt in a secondary unit, an issue in another, and the primary-unit quantity in the
  three views agreeing. Then **edit the conversion rate** and re-read all three — that is the test
  the first decision exists for, and it is the one no handler test can express. Plus one 403 naming
  the exact key beside a 200 from the same user in the same run. Verify via `sqlcmd`. **Restore the
  user's role before any browser pass** — phase 51's script ended on a two-key custom role, and the
  app then read as broken in ways that had nothing to do with the phase.
- Any performance claim backed by a number from `tools/scale/` with the statistics state stated.
  Note that phase 51 added an index to `StockLedgerEntry` and took **no** measurement — if this
  phase goes near the FIFO walk's performance, it inherits the re-measurement (phase 34c's rule).
- `docs/phase-52-status.md` with a TL;DR; update CLAUDE.md's phase index line and Current status,
  append the `docs/phase-lessons.md` paragraph, add any new gotcha (one line under 220 chars in
  CLAUDE.md, narrative in `docs/known-gotchas.md`, endpoint shapes in `docs/e2e-recipes.md`), and
  add the roadmap index-table row.
- **Do not commit** — hand over a ready commit message.
- End by generating the next kickoff prompt. After 52 the planned sequence is empty, so that prompt
  is a **re-planning** one: the three items outside the sequence each have a start condition (an
  hour with NVDA, full-text search, and phase 51's unread report columns), and a fresh roadmap pass
  should read the reference product again — the 2026-09-16 read found genuinely new scope, so the
  assumption that the catalogue is static has already been falsified once.
