# Phase 56 kickoff — bank reconciliation

Phase 55 is complete and **uncommitted** (a ready commit message is in the session hand-off). Start
here; do not continue its thread.

This is the second half of the bank-reconciliation pair, and the last entry in the forward plan.

---

## What this phase is

`/accounting/bank-accounts/:id/manual-reconcile` in the reference product: a **two-pane N:M
matcher**. Imported bank statement lines on the left, the app's own cash/bank transactions on the
right, tick some of each, press RECONCILE, and a reconciliation record joins them. Plus the matched
list, the reconciliation report and its export.

Phase 55 built the left-hand pane's data and left the seam marked in four places.

## Read first

- `docs/phase-55-status.md` — **the whole thing**, but especially "Carried into phase 56" and
  Decision C (why `ReconciliationId` is not on the aggregate yet and what adding it costs).
- `docs/roadmap.md` — **Forward plan (56)**, and note that two bullets in it were rewritten by
  phase 55 because they were wrong.
- `docs/erp-module-scan.md` — the **2026-09-21 appendix** ("Bank statement import, read in full"),
  which carries the module's full route list, the endpoint map and the statement row's shape.
- `docs/phase-lessons.md`: the **53, 54 and 55** paragraphs.
- `docs/phase-36-status.md` — `OutstandingDocumentReader` and the rule that two reports agree only
  through one shared reader plus a test reading both on the same data.
- `docs/phase-26a-status.md` — a report joining `GlLine` back to its document, and the
  same-date-field rule.
- `CLAUDE.md`'s Current status and the gotchas under *Report filters and live-read semantics*.
- `docs/e2e-recipes.md` before writing any seed script. Phase 55 added five entries.

## The read to take first — **and its start condition is met**

The roadmap recorded "the read needs a tenant holding a Bank-type account". **That was wrong and
phase 55 removed it.** `/accounting/recon` is one entry point that takes its account from router
state; the six real screens take the account from the **URL**:

```
/accounting/bank-accounts/:id/import            /accounting/bank-accounts/:id/bank-statement
/accounting/bank-accounts/:id/book-statement    /accounting/bank-accounts/:id/manual-reconcile
/accounting/bank-accounts/:id/matched           /accounting/bank-accounts/:id
```

The matcher renders fully on Cadehi's existing **Cash** account
(`759b81d2-3c8a-4299-8506-6aaf0513af5c`), right-hand pane already populated from the tenant's own
transactions, header reading `No txn selected | RECONCILE | No txn selected`, with Amount / Type /
Date / Sort by controls on each pane. **No start condition beyond a browser**; the user logs in,
never enter credentials.

**Phase 55's caveat applies here in a new form.** Its left-hand pane will be *empty* on both
tenants, because neither has imported a statement — and phase 54's lesson is that an empty pane
cannot tell you what a populated one does. The reference product's validate endpoint writes nothing,
so getting rows onto that pane needs a **real commit** to the reference tenant
(`POST /bank-statements`). That is the phase-52/54 deliberate-and-reverted precedent, and
`/bank-statements-delete` with `{account_id, action: "delete", statements: [ids]}` is the revert —
**ask the user before the write**, and do it on Cadehi's Cash account with obviously-marked rows.

Worth settling in the same pass, because all are cheap once the screen is open and none can be
inferred:

- **What RECONCILE actually posts**, and what comes back. The client collects `bs_ids` + `tx_ids`;
  read the request and the response.
- **Whether N:M is real or aspirational** — can you tick two on each side, or does the button
  enable only at 1:1? The state names plural, which is not the same as the server accepting plural.
- **What the right-hand pane is.** `/transactions` returns *documents*
  (`{id, date, source, code, note, amount, npr_amount, currency_code, conversion_rate, status,
  account}` — a Sales Order came back on Cadehi), while `/gl-transactions` is the other feed. Which
  one the matcher uses, and whether the other is the Book Statement screen, decides what a
  reconciliation actually points at.
- **`account_suggestions` / `is_suggestion_completed`** on the statement row, with
  `show_suggestions=true` on its list query, are an **auto-match suggestion engine**. Phase 55 read
  them and did not scope them. Decide explicitly — including "out of scope, recorded" — rather than
  inheriting the field.
- **`/balance-history/:id`** and `/reconciliation-report?account_id=` — the report half.

## The scope decisions to make

1. **What a reconciliation is, as an aggregate.** The roadmap's reading is that it is its own record
   joining many statement lines to many transactions, and that `reconciled` falls out of it rather
   than being the model — which phase 55 confirmed from the vendor's list (a two-option filter over
   `reconciliation_id`, with `reconciliation_data` carrying the matched documents). Confirm it at
   the endpoint, then decide the join table's shape. **Phase 51's rule applies to the other side
   too**: if a "matched amount" column can disagree with the sum of what it joins, it should not
   exist.
2. **What the right-hand pane points at, and this is the phase's real question.** A statement line
   is an external fact. A *transaction* is this tenant's own document — and phase 36's lesson is
   that one document can have more than one GL entry, while phase 26a's is that `GlJournalEntry`
   stores no copy of its document's number or business date. So decide whether a reconciliation
   joins to **documents** or to **GL rows**, and say what that costs the report. Getting this wrong
   is not a bug you find in a test.
3. **Does it post anything?** Phase 55's aggregate posts nothing, deliberately and loudly. A
   reconciliation probably should not either — it is a statement *about* two records, not a third
   record — but that is a decision to argue, not assume, and the answer governs whether lock dates,
   approval and Void apply at all.
4. **Permission keys.** The census found no keys of its own beyond `bank-reconciliation-export`.
   Phase 55 shipped `Accounting.BankStatement.View`/`.Manage`, both Admin+Member, with the
   derivation on the constants. Decide whether reconciling rides `.Manage` or earns a third key, and
   record the reasoning. **Do not re-add a location scope**: `Account` has no `LocationId`, so phase
   32b has nothing to bind to (the phase-55 kickoff asserted otherwise and was wrong).
5. **Unreconciling.** Phase 21b's rule in a new place: whatever this writes, decide how it comes
   back off. The vendor's statement delete works by row id and says nothing about a reconciled row —
   phase 55 left that guard unwritten *because the table did not exist*, and this phase owes it.
6. **What phase 55 left marked for you**: `BankStatementLine.ReconciliationId` (nullable), the
   Status column and its Reconciled/Pending filter on `ListBankStatementLinesQuery` and the Angular
   page, and the index that filter needs. Three tests assert the absence in both directions and
   will have to be edited rather than deleted.
7. **Where the compiler stops** is a standing decision. Phase 55's `StatementAmount` is signed from
   the *account's* point of view; a document's amount is not. This phase puts them side by side,
   which is exactly the moment the type earns its keep — do not unwrap it to compare.

## Exit bar

The roadmap's standard bar, plus:

- `dotnet build` / `dotnet test` / `ng build` / `ng test` all green (`ng test` from `web/`, Node 24).
- A hand-driven E2E on a **fresh organization**: seed by curl + cookie jar (`docs/e2e-recipes.md`
  first). Phase 55 added five recipe entries, including the `?bankAccountId=` query parameter on
  the multipart import POST and the both-keys-present shape of the statement delete.
- **The matcher's own claim proven in SQL**: after reconciling, the statement lines and the
  transactions are both marked, the reconciliation joins exactly what was ticked, and — if decision
  3 says so — the GL is untouched. Phase 55's `sqlcmd` block is the template.
- At least one **negative** path: a 403 naming the exact key against a **nonexistent id**, beside a
  200 from the same user on the same organization in the same run. Prove it last and restore the
  membership by `UPDATE tenancy.OrganizationMemberships` (phase 52), or the browser pass looks at a
  broken app (phase 51). Phase 55's `e2e55_403.py` is a working template including the 404 control.
- If this phase adds an index or changes a query plan, it owes a **number** — `tools/scale/`, logical
  reads, never the wall clock. Note that phase 55 deliberately left
  `(OrganizationId, BankAccountId, Date)` unbuilt with its re-entry condition written into
  `BankStatementLineConfiguration`; if this phase's Status filter changes that calculus, that is the
  moment to measure it rather than assume.
- `docs/phase-56-status.md` with a TL;DR block, and the usual refresh of `CLAUDE.md`'s Current
  status, `docs/phase-lessons.md` and `docs/roadmap.md`'s index row. **56 is the last entry in the
  forward plan**, so the roadmap also needs a new one — or an honest statement that the three
  "outside the sequence" items are what remains.
- **Do not commit** — hand over a ready commit message.

## Carried into this phase from 55

- **The generic CSV import pipeline is read and out of scope**: upload → column mapping → an
  editable staging grid → POST ENTRIES, for Delivery Note / GRN / Inventory Adjustment. Two of the
  three are the `InventoryTrackingMode` deferral. Recorded in the scan appendix so nobody re-opens
  it; do not scope it into 56.
- **The bank feed is out of scope, against evidence**: `validate-account` / `validate-otp` /
  `disconnect-bank` plus `fetch-statements` make it a per-account bank connection with an OTP
  handshake, and the account payload carries `bank_connected` / `rapid_connected` to match.
- **`is_bank_user`** on the vendor's user payload swaps the entire shell for a different one.
  Noticed, never investigated. It may be nothing; it may be the reason the reconciliation module
  looks unlike the rest of the product.
- Phase 55's four other carried items are in `docs/phase-55-status.md`; none blocks this phase.
