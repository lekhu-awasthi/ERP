# Phase 46 kickoff prompt

*(Generated at the end of phase 45. Paste the block below into a fresh session. Delete this file
once phase 46 has started — it is a handoff, not a document.)*

---

Phase 46 — Metered add-on axes and the subscription edges.
Goal: turn the two-axis quota model phase 41 built into the full metered one the plan rows already
imply, and settle the four subscription edges phase 41 recorded rather than resolved. The roadmap
heading "46. Metered add-on axes and the subscription edges" is the scope.

Before starting: `git log --oneline -3` should show the phase-45 commit on top. `Api.IntegrationTests`
needs Docker Desktop running — without it nine Testcontainers-backed tests fail in their constructors
before any assertion, which reads like nine regressions and is not. Phase 45 ended with 29/29 green.

**Phase 45 changed the words on two of this phase's own surfaces, and you should read them before
touching either.** A user asked why the banner says "contact your Tigg representative to choose a
plan" while *Subscription & Features* lets them choose one themselves. It was a real contradiction:
the copy was inherited from the reference product, where a vendor rep does the selling, and this
codebase models exactly one actor. The banner, the Usage panel and the Record panel now name the
screen instead of a person, and the Record panel leads with "This records an agreement; it does not
take payment." `subscription-notice.spec.ts` asserts in both directions that no banner names an
external actor. **If phase 46 ever introduces a vendor-side actor, that copy is the first thing that
becomes wrong again** — and if it does not, the copy is now the honest description of the model.

**This phase's live read is a question about what a tenant's own Admin may do, and phase 41 already
warns you the answer may not be observable.** Phase 41 found that `Tenancy.Subscription.Manage` sits
on the tenant's own Admin, so a quota is "a record and a guard, not a control" — the vendor-side
actor this codebase does not model. Before designing a metered axis, read the live Tigg
Subscriptions screen on Moonbeam **and** on the second tenant, and record: whether SMS credits, AI
scans or billing locations appear there as purchasable quantities; whether any of them is editable by
the logged-in Admin; and what the screen shows when a quantity is exhausted. If none of that is
visible, phase 41's rule applies — **read what the vendor publishes (its price list sells the "dead"
fields) before concluding a field is dead**, and say plainly in the status doc that the enforcement
is a guard over a record nobody in this model can sell.

Read first (docs):
- `docs/roadmap.md`, heading "46. Metered add-on axes and the subscription edges", plus the "Method"
  and "Ordering rule" paragraphs above the 42–47 block.
- `docs/phase-41-status.md` in full — all five carried items are this phase's scope, and its
  reasoning (especially the vendor-side-actor re-entry condition) is the phase's premise.
- `docs/phase-18-status.md`, Grep "SMS credit" — the credit ledger that already exists, and why it is
  a ledger rather than a counter.
- `docs/phase-22-status.md`, Grep "scan" — the AI extraction log that would back the second axis.
- `docs/phase-32-status.md`, Grep "MultipleLocations" — billing locations as an entitlement capped at
  one, which is the third axis in a different shape.
- `docs/phase-31-status.md`, Grep "expiry" — subscription expiry, and carried item #6 (the gate does
  not cover configuration writes).
- `docs/phase-lessons.md`: the 41, 31, 20e and **45** paragraphs. The 45 one is the freshest warning
  about this failure mode — a question framed as a fork had already been answered by a decision in
  another phase, and an allow-list's stated reason had become the argument against itself.
- `docs/known-gotchas.md` headings: "Before calling a tenant-level limit enforcement, ask who can
  write it (phase 41)", "A field dead on two free-trial tenants is one sample (phase 41)", and the
  phase-20e claim-then-act entry (for the quota race).

Scope decisions to make (recommended default, precedent):
- **The three metered axes** (41 #4). `SubscriptionQuotaBehavior` already meters two; each new axis
  is a reader plus a ceiling on the plan row. SMS credits have a ledger (phase 18) and AI scans have
  a log (phase 22), so both readers are a `SUM`/`COUNT` over rows that already exist; billing
  locations are a `COUNT` that phase 32 already enforces as a cap of one. **Recommend adding all
  three as readers and ceilings and saying explicitly which of them any actor can actually raise** —
  a ceiling nobody can sell is still worth having as a guard, but it must be labelled as one.
- **Plan change vs entitlement flags** (41 #5): keep the flags un-reconciled by design (phase 22's
  Decision C made them immutable at creation) and **surface the mismatch** on the subscription
  screen. Recommend surfacing rather than reconciling, and name what a user is meant to do about it.
- **The quota race** (41 #6): recommend **leaving the overshoot documented** unless a hard limit is
  actually wanted, because the claim-row-under-a-unique-index idiom (phase-20e Decision C) is real
  work and an overshoot of one on a soft quota is not a correctness failure. If you do build it, the
  unique index on (organization, term, ordinal) is the mechanism and InMemory does not enforce it —
  verify the race against SQL Server.
- **Expired-tenant behaviour** (41 #7, 31 #5/#6): still derived, never observed. Recommend extending
  the existing gate over **configuration writes** at the same time, since that is 31's carried item
  and the same code path.
- Permission keys: reuse `Tenancy.Subscription.*`; say so explicitly if any item disagrees.

Exit bar: dotnet build/test (Docker up for `Api.IntegrationTests`), ng build/test clean (`ng test`
from `web/`); a hand-driven E2E against the real API proving each new axis both under and over its
ceiling; at least one negative path (a 403 naming the exact key against a nonexistent id, so
403-not-404 proves the behavior fired, beside a 200 from the same user on the same organization in
the same run); `sqlcmd` confirming any new column, with a predicate true of the data only if the
write was right; `docs/phase-46-status.md` with a TL;DR, the live findings written up **whichever way
they go**, and the scope decisions with reasoning; then update CLAUDE.md's phase index line and
Current status, append the phase-lessons paragraph, add any new gotcha (one line under 220 characters
in CLAUDE.md, narrative in `docs/known-gotchas.md`), and add the roadmap index-table row. Do not
commit; hand over a commit message.

**What phase 45 left for you that is not in the roadmap heading.**

1. **The multi-select on the Attributes Used editor was deliberately not built** (status doc,
   limitation #1): it still lists every option of every tenant attribute as an individual checkbox,
   with no per-attribute *Select all* and no filter. Re-entry: a tenant whose attribute catalogue
   outgrows one screen.
2. **A secondary unit is still priced metadata nothing consumes.** No document line stores a unit, so
   multi-UOM is not *done* in the larger sense, and the phase that changes that touches every
   line-bearing aggregate.
3. **An interactive control nested inside a row anchor is still invalid HTML.** Phase 45 fixed the
   *symptom* — the Custom Status picker now swallows the click that was navigating — but the four
   list grids still render a `<select>` inside `<a routerLink>`, which no `preventDefault` makes
   valid. The structural fix is to take the picker out of the anchor (Bootstrap's `stretched-link`
   over the row's left half) on four templates. A sweep script for the shape is in the phase-45
   session notes; it finds exactly those four. Worth doing when one of those grids is next opened.
4. **Two screens now carry an "explain the empty picker" pattern** — Custom Statuses' per-section
   empty text and the Reporting Tags editor's setup link. If phase 46 adds any control whose options
   a tenant must define first, that is the shape to copy: say what the thing is, and link to where it
   is defined. A blank control reads as a broken one.
5. `docs/phase-46-kickoff.md` is this file; delete it when phase 46 starts.

End the session by generating the Phase 47 kickoff prompt using the handoff instructions in
CLAUDE.md's Context discipline bullet.
