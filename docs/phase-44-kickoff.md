# Phase 44 kickoff prompt

*(Generated at the end of phase 43. Paste the block below into a fresh session. Delete this file
once phase 44 has started — it is a handoff, not a document.)*

---

Phase 44 — Report semantics that need a live re-read first.
Goal: build only what a live read shows, and record every absent control as absent. The roadmap
heading "44. Report semantics that need a live re-read first" is the scope, **plus** phase 43's
Decision D, which handed this phase the four statutory reports still denominated in transaction
currency after the Sales Register was folded to base.

Before starting: `git log --oneline -3` should show the phase-43 commit on top.

**Read the live tenants FIRST, before any code.** This phase's whole premise is that its controls
have never been operated, and phase 8f's Annex 5 lesson is that a speculative design and the real
screen can have nothing in common. The user logs in themselves — never enter credentials, never
commit them. Cadehi (`cadehi.tigg.app`) is location-enabled and was logged into during phase 43;
Moonbeam is the multi-warehouse, populated one.

Read on **Moonbeam** (multi-warehouse, populated):
- *Display Warehouse in Column* vs *Group by Warehouse* on Inventory Position (36 #2) — phase 36
  deliberately did not build the first because a single-warehouse tenant cannot tell them apart.
- Reporting Tags on the Journal report; Reporting Tags **plus** group-by-warehouse on Inventory
  Position (does selecting both compose, or does one win?).
- *Group By Bill* / *Include Credit Note* on the Sales Register (35b #3) — this codebase has both;
  confirm the live semantics match before trusting them.
- Inventory Master's **Txn Type** filter: does it offer WarehouseTransfer and OpeningStock rows
  (37 #3, 26c Decision D)?

Read on **Cadehi** (location-enabled):
- `sales-summary`'s **Group Wise** location grouping (36 #4) — a group-*by* no other report has.
  35b gave that report the location filter, not the grouping.

Read first (docs):
- `docs/roadmap.md`, heading "44. Report semantics that need a live re-read first", plus the
  "Method" and "Ordering rule" paragraphs above the 42–47 block.
- `docs/phase-43-status.md` — **Decision D in full** (it names what this phase inherits), plus the
  TL;DR and the confirm-live section (product-to-location is settled; do not re-open it).
- `docs/phase-36-status.md`, Grep "Decision K" and carried items #2, #3 and #4.
- `docs/phase-35b-status.md`, Grep "Group By Bill" and carried items #2 and #6.
- `docs/phase-26c-status.md`, Grep "Decision D" and "Inventory Master".
- `docs/phase-37-status.md`, Grep "Inventory Master" and "Txn Type".
- `docs/phase-39-status.md`, Grep "printed header" (carried item #3).
- `docs/phase-lessons.md`: the 26a, 26b, 26c, 28, 35b, 36 and 43 paragraphs.
- `docs/known-gotchas.md` headings: "Folding a report to base currency (phase 43)", "A dated stock
  report derives from StockMovement (phase 26c)", "A report that must agree with a register",
  "An append-only fact row carries only its source ids (phase 35b)", "A confirm-live pass can
  falsify an earlier confirm-live pass (phase 32b)", plus the GL-report date-bracketing entry.
- `docs/erp-module-scan.md`: the 2026-09-02 confirm-live appendix and the 2026-09-11 Moonbeam pass.

Scope decisions to make (recommended default, precedent):
- **The four statutory reports still in transaction currency** (phase 43 Decision D): the Purchase
  Register, the Purchase Return Register (via `PurchaseReturnReader`), the VAT Summary (4 bucketing
  sites) and **Annex 13** (5, several resolving `ExpenditureClassification` back through the source
  bill). The rule is already decided and written down — *a statutory register filed with the IRD is
  denominated in NPR* — so this is application, not a new decision. Follow phase 43's three
  mechanics exactly: fold the **lines** before the bucketing (not the finished buckets, or Total
  stops equalling TaxExempt+Taxable+VAT), fold inside the **shared reader** so two reports cannot
  disagree about one document, and fold **in memory after `ToListAsync`** because
  `ExchangeRates.ToBase` is a static call InMemory evaluates in C# and SQL Server cannot translate.
  A test per report asserting the partition holds, as phase 43 did for the sales side.
- **Build only what the reads show.** Every control the live pass does *not* find is recorded as
  absent with the tenant and date, not deferred vaguely. Phase 36 Decision K is the precedent: a
  column nobody has seen is phase 8f's Annex 5 waiting to repeat.
- **System Audit's location** (35b #2): stamp at audit-write time by loading the row's location in
  `AuditBehavior` for location-bearing types, or exempt it with the reason stated. Recommend the
  stamp — 32b's row scoping otherwise has a hole in the one report that names every action. Note
  that phase 43 added `DocumentType.Deal` and `DocumentType.WorkTask` and made four CRM/workflow
  commands auditable, so `AuditBehavior` now writes rows for two types that carry **no** location;
  whatever is built must handle that rather than assume every audited type is location-bearing.
- **Documents written while their type was out of scope carry no location** (35a #5, 35b #6): a
  one-off backfill command to HeadOffice, run by an Admin when `LocationScopeMode` widens, with the
  count reported — not a silent migration.
- **The printed header's centred arrangement** (39 #3): one change to the shared layout, verified on
  all fifteen types with the existing PDF tests.
- Permission keys: decide per item. The backfill command is a tenant-wide write over documents and
  should sit at the Admin-only bar (`Tenancy.*`-shaped reasoning); everything else should reuse the
  report keys that already exist — say so explicitly if any item disagrees.

Exit bar: dotnet build/test, ng build/test clean (`ng test` from `web/`); a hand-driven E2E against
the real API proving each new or changed report path, including a foreign-currency document proving
each newly folded report reports base currency and that its magnitudes still partition; at least one
negative path per new command (a 403 naming the exact key against a nonexistent id, so 403-not-404
proves the behavior fired, beside a 200 from the same user on the same organization in the same run
— a custom role with a single grant is the shape phase 43 used); `sqlcmd` confirming any new column
and its backfill, with a predicate that is true of the data only if the write was right (phase 43's
migration lesson); `docs/phase-44-status.md` with a TL;DR, the live findings written up **whichever
way they go**, and the scope decisions with reasoning; then update CLAUDE.md's phase index line and
Current status, append the phase-lessons paragraph, add any new gotcha (one line under 220
characters in CLAUDE.md, narrative in `docs/known-gotchas.md`), and add the roadmap index-table row.
Do not commit; hand over a commit message.

End the session by generating the Phase 45 kickoff prompt using the handoff instructions in
CLAUDE.md's Context discipline bullet.
