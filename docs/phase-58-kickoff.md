# Phase 58 kickoff — there is no phase 58 yet, and that is the finding

Phase 57 is complete. Start here; do not continue its thread.

**This is a re-planning prompt, in the shape of `phase-53-kickoff.md`, and it is deliberately not a
feature phase.** Phase 53 rebuilt the plan by censusing the vendor's own permission-key catalogue;
phases 54, 55, 56 and 57 were what that census produced, and all four are done. Phase 57 re-ran the
census on 2026-09-21 against a **byte-identical bundle** and reproduced it exactly — 166 keys, the
same 22 document types, the same 51 reports. So the sequence is empty for a reason that has been
checked twice, and inventing a phase 58 to fill it would be the thing phase 53 explicitly refused.

Read `docs/roadmap.md`'s **"Forward plan — empty, and that is the honest statement"** before
anything else. It is short, and it is the whole situation.

---

## What is actually available, and what each one needs first

### 1. The auto-match suggestion engine — a phase, but a **product decision** first

The only bank-module feature not built, and the only item here that is a phase on its own terms.

The vendor's surface is `/bank-statements-matched` — a suggestion queue reading
`account_suggestions` / `is_suggestion_completed` per row, with *Reconcile Selected*, *Reconcile All*
and *Go to Manual Reconciliation* — plus the multi-verb
`POST /bank-reconciliations {action: "approve" | "un-match"}`. All of it is recorded in
`docs/erp-module-scan.md`'s phase-56 appendix.

**Why it has been excluded twice, by phases 56 and 57, for the same reason:** its demo tenant returns
`account_suggestions: null` on **every row**, so the one thing that matters — what makes two rows a
suggested match — cannot be read, only invented. That is a product decision, not a parity gap, and it
is the same shape as full-text search below.

**Start condition:** a decision about what "suggest a match" should mean here. Amount and date within
a window? The bank's narration against a contact's name? A mapping learned from what this tenant has
already approved? **Ask the user; do not infer it from an empty field.** Once it is decided, the
phase is ordinary — the matcher, the writer (`BankReconciliationWriter`) and the sum rule all exist,
and the engine only has to *propose* pairs that go through them.

### 2. A fresh re-planning read — cheap, and the honest default if nothing else is wanted

Phase 53's method, re-run: one regex over `static/js/main.*.chunk.js` + `13.*.chunk.js` for
`"<feature>-<view|add|edit|approve|void|full-access|export>"`, plus the bundle's own
`{id, url, name}` report array. Phase 57 did exactly this and it took under an hour.

**Two things to carry into it, both learned in 57:**

- **Reproduce to the row or you have measured nothing.** 57 got 162 against 53's 166, and the gap was
  four `-alter` keys its stricter pattern did not list — on an identical bundle. A re-run that lands
  "about the same" is a number, not evidence.
- **The vendor keeps two vocabularies for one thing.** Its role-editor tree says
  `gl-materialised-view` where the router says `report-gl-materialised-view`, so comparing the tree
  against our screens manufactures five or six phantom gaps. Compare where both sides are typed
  (phase 46's rule).

**Cadehi's trial expired 22-09-2026.** Check which tenants are still reachable before planning any
live read, and **ask rather than deriving** (phase 32: a second tenant existed and four written scope
decisions were wrong).

### 3. Deferred, with real start conditions — not schedulable by a session

- **Delivery Note / Goods Received Note**, behind `TenantSettings.InventoryTrackingMode = Physical
  Movement`. **Phase 57 added a third item to this deferral:** the **Inventory Variance Report**
  (`/reports/new/inventory-variance`), which opens live as *"Inventory Tracking and Physical
  Inventory Tracking Not Enabled"* — the same flag. Scoping this phase from the two document types
  alone would ship it without the report. **Start condition:** the user flips that setting on a
  tenant and the screens are read.
- **An hour with NVDA.** Start condition: a person with headphones. Still the only thing between this
  codebase and a finished WCAG 2.1 AA story. Phase 40's rule: record what is heard *verbatim* before
  changing anything.
- **The two traceability reports' real column sets** (Product Batch, Product Serial No). Start
  condition: an account or tenant holding the two keys the demo Admin lacks. Ask before invoking the
  derive-instead precedent.
- **Full-text search.** Start condition: a tenant complaining about search latency, or a product
  decision about what "search" should mean. Phase 42 was explicit that the remedy is a *semantics*
  change, not a performance fix.
- **A vendor-side actor** (41 Decision G): every subscription ceiling is self-liftable until an actor
  outside `OrganizationId` exists. A console, a support role, or an API key — a phase of its own.

---

## If a phase is chosen, the standing bar still applies

- `dotnet build` / `dotnet test` / `ng build` / `ng test` all green (`ng test` from `web/`, Node 24).
  `Api.IntegrationTests` needs Docker **and** a quiet machine.
- A hand-driven E2E on a **fresh Organization**, seeded by curl + cookie jar, with every status code
  printed.
- At least one negative path: a **403 naming the exact key against a nonexistent id**, beside a 200
  from the same user on the same organization in the same run — and proved **last**, because a
  restricted role cannot restore itself through the API.
- Any claim a report or a list makes goes in **SQL**, not in prose.
- Any index decision carries a number, or an explicit "measured and refused" — and **phase 57's
  lesson is that the column a debt names is a hypothesis**: it measured `ReconciliationId`, found it
  worth one logical read, and found the real 277× somewhere else on the same table.
- `docs/phase-N-status.md` with a TL;DR, plus the refresh of `CLAUDE.md`'s Current status,
  `docs/phase-lessons.md` and `docs/roadmap.md`'s index row.
- **Do not commit** — hand over a ready commit message.

## Read first

- `docs/roadmap.md` — the empty forward plan, *Outside the sequence*, and *Deferred beyond this
  roadmap*.
- `docs/phase-57-status.md` — the TL;DR, **Step 1** (the census re-run and what it found), and
  **Carried into a later phase**.
- `docs/erp-module-scan.md` — the **2026-09-21 phase-57 appendix** (the catalogue re-run,
  `is_bank_user`, the Quick Approve picker) and the **phase-56 appendix** above it (the module's
  whole endpoint map).
- `docs/phase-lessons.md`: the **53, 56 and 57** paragraphs.
- `CLAUDE.md`'s Current status.

**Say plainly which of the options above is being taken, and why — including "none of them, here is
what the user asked for instead". What this document must not produce is a phase invented to keep the
sequence non-empty.**
