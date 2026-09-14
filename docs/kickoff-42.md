# Phase 42 kickoff

**Phase and goal. Phase 42 — and the first thing to know is that `docs/roadmap.md` has no numbered
entry past 41.** The forward plan is exhausted. So this phase opens with a **triage of what is
carried**, not with execution of a written scope, and the triage is itself a deliverable: the roadmap
needs a "### 42" block written before it is closed.

The recommendation below is a reading of the carried list, not an instruction. Read the list first and
disagree if the evidence says so — phase 41 is the standing proof that a carefully-reasoned earlier
decision can rest on a false premise.

---

## Read first

- **`docs/roadmap.md`** — specifically that there is nothing after the "### 41 — done" block except
  the drop list and the *Deferred beyond this roadmap* section. Read that deferred section carefully:
  one of its entries (**Delivery Note / GRN**) now has a *reachable* re-entry condition, and that
  changes whether it is still deferred.
- **`docs/phase-41-status.md` TL;DR and Decision G** — for the shape of the biggest single carried
  item (the missing vendor actor), and for the method: the decision was settled by reading what the
  *seller publishes*, not by reasoning harder.
- **`CLAUDE.md`'s Current status block** — the consolidated carried list from phases 36–41, which is
  the actual input to this phase's triage.
- **`docs/phase-37-status.md`** and **`docs/phase-36-status.md`** — if you take the recommendation
  below, these are the two that matter. 37 for how a stock *value* correction has to reach three
  views, 36 for `OutstandingDocumentReader` and the one-reader-two-presentations rule.
- **`docs/known-gotchas.md`** headings: *"A stock value correction must reach three views"*,
  *"A return relieves at the cost the layers give up"*, *"A dated stock report must derive from
  StockMovement"*, and 41's three new ones.

---

## The recommendation: "inventory truth" — the leftovers that are wrong, not merely missing

Most of the carried list is *absent features*. Four items are **divergences** — places where two views
of the same fact disagree, or where the ledger and the GL drift. Those outrank everything else,
because an absent feature is visible and a divergence is not.

1. **A standalone Debit Note's Goods line credits Inventory without touching the stock ledger**
   (carried from 37). This is the sharpest one: the GL says inventory fell, the FIFO layers say it did
   not. Phase 29 fixed the *bill-linked* path (the capitalised-cost release leg); the standalone path
   was left. Phase 37's rule applies directly — a stock value correction must reach **three** views
   (the layers, the Inventory account, and the append-only `StockMovement` history), and any two can
   be patched into agreement.
2. **Inventory Master lacks WarehouseTransfer and OpeningStock rows** (37). A movement report missing
   two movement types is a report that silently under-reports, which is phase 26c's
   derive-from-`StockMovement` lesson with two sources dropped.
3. **Product-to-location is enforced on the picker but not at save** (36). A client-side-only rule is
   not a rule; the API accepts what the UI forbids.
4. **The Sales Register's money columns are still transaction-currency** (36), so a multi-currency
   tenant's register does not foot to its own GL.

Those four are one coherent phase with a single question behind them — *does every view of a stock or
money fact agree?* — and a natural exit bar: a test or an E2E that reads two views on the same data
and asserts they match, which is phase 36's `OutstandingDocumentReader` pattern applied to stock.

**If you take it, the confirm-live question is narrow:** does the reference product's Inventory Master
include transfers and opening stock, and what does a standalone Debit Note on a Goods line do to its
stock? Both are observable read-only on Cadehi.

---

## The three alternatives, with why each is *not* the recommendation

- **The vendor side** (41 Decision G). The honest root fix: an actor outside `OrganizationId`, so a
  plan, a quota and an expiry are set by the party selling them. Everything in 41 keeps working
  unchanged the day it exists. **Not recommended now** because it is a new bounded context, a new
  identity and a permission model that is not tenant-scoped — larger than 41 was — and because the
  PRD scopes this product to the tenant-facing ERP. Worth putting to the user as an explicit
  yes/no before assuming either way.
- **Delivery Note / GRN** (deferred, but *reachable*). The roadmap's own re-entry condition is met:
  Cadehi's General page offers **Physical Movement**, and switching the mode is a config write on a
  fresh trial tenant — **the user's call, and worth asking**. If they say yes, this becomes a phase of
  its own (FIFO consumption moves from Invoice/Bill Approve to DO/GRN Approve under a handler-level
  gate, plus a goods-received-not-billed default account) and it outranks the recommendation above.
  **Ask before planning around it.**
- **Deal/Task detail pages + `UpdateOrganizationCommand`** (39). Coherent and bounded — three route
  families, `Deal`/`WorkTask` into the three polymorphic parent enums (**by name, never by ordinal**),
  and the nine read-only Organization fields that have no write command at all. Good work, but all
  *absent*, not *wrong*.

---

## The item no session can close

**Nobody has heard the app.** Phase 40's live regions, its roving toolbar and phase 41's deliberately
role-less subscription banner are right *by specification and by DOM evidence*, not by ear — no screen
reader was available in that environment. This is an hour with NVDA on Windows and a person's ears.
**Name it to the user as something to schedule, not something to assign to a session.** Phase 41 added
one more surface to that list, so the backlog of unheard work is growing, not shrinking.

---

## Exit bar

`dotnet build` / `dotnet test` / `ng build` / `ng test` clean (`ng test` **from `web/`**;
Api.IntegrationTests needs **Docker Desktop actually running**, and stop any `ErpApp.Api` dev server
first or the build fails on a locked DLL copy rather than on anything real).

All guards green: `a11y-sweep-guard`, `sweep-guard` (23's date-input guard), `SearchSweepGuardTests`,
32b's location sweep guard, 35a's `LocationReadPathSweepGuardTests`, 35b's two, 36's
`product-picker-location-sweep-guard.spec.ts`, 34b's nav guard, 39's
`RichTextWritePathSweepGuardTests` / `RichTextSharedCasesTests`, and **41's
`MeteredTransactionSweepGuardTests`** — which fails the build if a new GL-posting document type is
added without deciding whether it is metered. If phase 42 touches posting at all, that guard will
speak, and it is telling the truth.

---

## Traps, freshly earned in 41

- **A negative permission proof needs its positive half in the same run.** `AuthorizationBehavior`
  returns the *identical* message for "not a member" and "lacks the key", so a 403 only proves the key
  if the same user gets a 200 on a read of the same organization. `accept-invitation` is
  authenticated — **log in before accepting**, or the membership stays `Invited` and every later 403
  is vacuous while looking perfect.
- **`sqlcmd -S localhost` against a named instance returns nothing and reports nothing.** It is
  `DESKTOP-H0R00ME\SQLEXPRESS` — read it from the connection string. Only printing every status code
  made the resulting 400 visible three steps later.
- **State two surfaces show, one of which changes, needs a shared store.** Found only by driving the
  app; both components were individually correct and every unit test passed.
- **A `document.querySelector('button.btn-primary')` in a browser pass hits the date-range picker's
  Apply button**, which precedes every page form in the DOM. Nothing fails — the save just never
  happens. Use the accessibility-tree `ref`.
- **Before recording a field as dead, read what the vendor publishes.** Two tenants of the same kind
  are one sample. tiggapp.com/pricing settled a two-phase-old question in one page load and carried
  contractual definitions no screen shows.
- Plus everything in CLAUDE.md, which every session loads anyway.

---

## Then

`docs/phase-42-status.md` with a TL;DR, plus the standing doc sweep: CLAUDE.md phase-index row +
Current status rewrite, a `docs/phase-lessons.md` paragraph, any new one-line gotcha with its
narrative in `docs/known-gotchas.md`, and a **"### 42" block written into `docs/roadmap.md`** — which
this phase has to author rather than replace, since none exists. End by generating phase 43's kickoff.
