# Phase 45 kickoff prompt

*(Generated at the end of phase 44. Paste the block below into a fresh session. Delete this file
once phase 45 has started — it is a handoff, not a document.)*

---

Phase 45 — Multi-UOM × variants, and import ergonomics.
Goal: close the modelling gap phase 24 opened and phases 37 and 38 carried, then the three import
ergonomics items beside it. The roadmap heading "45. Multi-UOM × variants, and import ergonomics" is
the scope.

Before starting: `git log --oneline -3` should show the phase-44 commit on top.

**This phase needs a live read too, and it is a design question rather than a census.** Phase 24
settled that a variant is a Product with a parent pointer; what it did *not* settle is whether a
variant carries its own secondary-unit conversion rates and prices, or inherits its parent's. Read
the live product before designing: open a parent product with Attributes Used on Moonbeam, then one
of its variants, and record which unit/price fields the variant's own form shows, whether they are
pre-filled from the parent, and whether editing one variant's conversion changes its siblings. That
one read decides the whole shape — an inherited conversion is a read-through, an owned one is five
more columns and a sweep.

Read first (docs):
- `docs/roadmap.md`, heading "45. Multi-UOM × variants, and import ergonomics", plus the "Method"
  and "Ordering rule" paragraphs above the 42–47 block.
- `docs/phase-24-status.md` — the variant model in full, and why it is a parent pointer rather than a
  stock-key change.
- `docs/phase-38-status.md`, Grep "Attributes Used", "landed-cost drawer" and "dry run" — three of
  this phase's four items are that phase's carried ones, with its reasoning attached.
- `docs/phase-37-status.md`, Grep "multi-UOM".
- `docs/phase-39-status.md`, carried item #4 (`ListSmsLogsQuery`'s missing search term, the one
  exemption the widened guard found that would earn one).
- `docs/phase-lessons.md`: the 24, 37, 38 and **44** paragraphs. The 44 one is the freshest warning
  about this exact failure mode — a control recorded as unbuilt because two options could not be told
  apart turned out to be a parent and its modifier, and an inherited rule read exactly like an
  observed one.
- `docs/known-gotchas.md` headings: "Reporting Tags: OR within a category, AND across categories
  (phase 44)" (for how a recorded-but-unobserved decision gets overturned), the phase-24 appending-to-
  a-tracked-parent entry, and the import-related entries under "Imports and exports".
- `docs/erp-module-scan.md`: the phase-24 variant sections.

Scope decisions to make (recommended default, precedent):
- **A variant's conversion rates and prices** — decided by the live read above, not in advance. If
  they are inherited, the sweep-guard allow-list reason phase 24 left behind is retired by a
  read-through and nothing is stored. If they are owned, this is a schema change plus phase-35a's
  three-assertion rule (write, read, and every prefill between — 35a's own lesson was that 14 of 15
  detail DTOs dropped a new field silently).
- **Bulk setup of a product's Attributes Used** (38): a small importer or a multi-select on the
  product form. Recommend the multi-select — the gap is ergonomic, and phase 38's own rule is that a
  thing generated from the document in front of you is not an `ImportTemplateDefinition`.
- **The landed-cost drawer merges per product instead of replacing** (38). Recommend replace, to
  match every other child-collection editor in this codebase (phase-4 bug #1's snapshot-and-
  RemoveRange idiom), and say so explicitly rather than leaving the asymmetry.
- **The dry run inside a rolled-back transaction** (38): recommend **no**, and record that Confirm
  Upload's post-hoc errors are the accepted shape — phase 38 already built the plan/apply split that
  makes the dry run honest without a transaction.
- **`ListSmsLogsQuery` gets its search term** (39 #4).
- Permission keys: reuse the existing `Catalog.Product.*` and import keys; say so explicitly if any
  item disagrees.

Exit bar: dotnet build/test, ng build/test clean (`ng test` from `web/`); a hand-driven E2E against
the real API proving a variant round-trips its units and prices whichever way the live read settles
it, plus the Attributes Used path; at least one negative path (a 403 naming the exact key against a
nonexistent id, so 403-not-404 proves the behavior fired, beside a 200 from the same user on the
same organization in the same run); `sqlcmd` confirming any new column, with a predicate true of the
data only if the write was right; `docs/phase-45-status.md` with a TL;DR, the live findings written
up **whichever way they go**, and the scope decisions with reasoning; then update CLAUDE.md's phase
index line and Current status, append the phase-lessons paragraph, add any new gotcha (one line
under 220 characters in CLAUDE.md, narrative in `docs/known-gotchas.md`), and add the roadmap
index-table row. Do not commit; hand over a commit message.

**Two things phase 44 left for you that are not in the roadmap heading.** First, its status doc's
carried item #6: phase 44 added four Angular screens and **no Angular tests** — the count stayed at
447. If phase 45 touches the product form, that is the moment to close the habit rather than widen
it. Second, `docs/kickoff-42.md` is a stale handoff file that was never deleted; delete it when you
delete `docs/phase-45-kickoff.md`.

End the session by generating the Phase 46 kickoff prompt using the handoff instructions in
CLAUDE.md's Context discipline bullet.
