# Phase 53 kickoff — re-planning, because the sequence is empty

Phase 52 is complete and **uncommitted** (a ready commit message is in the session hand-off). Start
here; do not continue its thread.

---

**There is no phase 53 in `docs/roadmap.md`.** The forward plan ended at 52, so this session's job is
to build the next one — not to implement a feature somebody already scoped. That is a different kind
of session and it should not be rushed into code.

## Why a fresh read comes first

The assumption that the reference product's catalogue is static **has already been falsified once**:
the 2026-09-16 pass found batch and serial tracking, two new reports and a new product tab that no
earlier scan had seen, and that became phase 51. The 2026-09-17 pass (phase 52) then found a `Unit:`
selector on the product Overview that no scan had recorded either.

So: read the live product again before deciding anything. `docs/erp-module-scan.md`'s dated
appendices are the format — append, never rewrite. The user logs in; never enter credentials.

**Worth reading specifically**, because phase 52 saw them in passing and did not chase them:

- The **product Overview's `Unit:` selector**, which re-expresses on-hand stock into any of the
  product's units by dividing live. This codebase deliberately does not have it (phase 52,
  Decision G / carried item 2). Is it only on that panel, or is it on the stock reports too?
- The **Secondary Unit tab's `Action` column is delete-only** — there is no edit. Phase 45 read that
  column as edit-and-delete and built both; our `UpdateSecondaryUnitCommand` is therefore a
  deliberate improvement, not a match, and that is now recorded. Check whether anything else phase 45
  inferred from that tab needs the same correction.
- **Deleting a secondary-unit row that an approved document references is unguarded** in the
  reference product. Ours is safe by construction (the line names the unit lookup, not the row), but
  it is worth knowing whether the vendor's own documents survive it as cleanly.

## Read first

- `docs/roadmap.md` — the index table (now 0–52), the **Ordering rule**, and the three standing
  "outside the sequence" items with their start conditions.
- `docs/phase-52-status.md` (TL;DR + Decisions E and F) — what a unit does and does not reach, and
  the four line types deferred **unread**.
- `docs/erp-module-scan.md`, the **2026-09-17 appendix** — the live read phase 52 took.
- `docs/phase-lessons.md`: the 51 and 52 paragraphs.
- `CLAUDE.md`'s Current status, which names the two unmeasured plan changes.

## The four candidates already on the table

Not a plan — a starting set. A re-planning session should weigh these against whatever the fresh read
turns up, and is free to rank them differently or drop them.

1. **An hour with NVDA.** Still the only thing between this codebase and a finished WCAG 2.1 AA
   story. Phases 40 and 47 built everything derivable and both recorded plainly that no screen reader
   was run. **Start condition:** a person with headphones. Record what is heard verbatim before
   changing anything.
2. **The two traceability reports' real column sets** (carried from 51). One page load each.
   **Start condition:** an account holding the vendor's equivalent of `Reports.ProductBatch.View`.
   Ask before falling back again — phase 32's lesson is that a second tenant existed and four written
   scope decisions were wrong.
3. **Full-text search** (carried from 42). A *semantics* change, not a performance fix.
   **Start condition:** a tenant complaining about search latency, or a product decision about what
   "search" should mean.
4. **The four line types with no unit** (new, from 52): Opening Stock, Inventory Adjustment,
   Production Journal, Bill of Materials. These were **unread**, not excluded — the reference tenant
   had none of those documents, so the list endpoint 404'd on its first row. BOM carries a real open
   question of its own: its Raw Materials table has `Qty` **and** `Qty/Unit`, where `Qty/Unit` is a
   per-output-unit ratio, and how an entered unit interacts with that ratio is an interaction nobody
   has posed (phase 45's rule for not deciding it). **Start condition:** a tenant with any of those
   four document types to read.

## Two measurement debts, named so they are not inherited silently

Neither is urgent and neither is a claim — both are plan changes made without a number, which
phase 34c's rule says must be re-measured by whoever next touches that area:

- **Phase 51's index on `StockLedgerEntry`.** An index added for one path changes the plan for every
  other path on the table, and the FIFO walk is the other path.
- **Phase 52's `.Include(x => x.SecondaryUnits)` on `ListProductsQueryHandler`.** It is a join over
  the page rather than the table, but `listAllProducts` asks for a very large page, so a tenant with
  thousands of products pays for it on every document form.

`tools/scale/` is the harness; the number to trust is logical reads and CPU from
`sys.dm_exec_query_stats`, never the wall clock (phase 50).

## What this session owes

- A **re-read** of the reference product, appended to `docs/erp-module-scan.md` with its date, in the
  established format.
- A **rebuilt forward plan** in `docs/roadmap.md`: phases with numbers, an ordering rule that says
  *why* that order, and a start condition for anything that cannot simply be scheduled.
- Explicit **re-confirmation or retirement** of the deferred and dropped lists. Phase 47's Decision G
  dropped eight items with reasons so no future session has to re-open them from a list; a
  re-planning session is exactly when to check those reasons still hold.
- No code, unless the read turns up something small and obviously broken — and if it does, say so
  rather than folding it into a plan.
- **Do not commit** — hand over a ready commit message.
- End by generating the next kickoff prompt, which after this one is an ordinary phase prompt again.
