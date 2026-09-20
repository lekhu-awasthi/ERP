# Phase 53 — Re-planning, and a census instead of a walkthrough

## TL;DR

**The plan ended at 52, so this phase rebuilt it. No code was written.** The method is the finding:
every earlier confirm-live pass read the reference product *screen by screen*, which means each one
was blind to whatever it did not open — phase 30's "a list sampled from a few screens becomes a wrong
list", applied to the product as a whole. This pass instead extracted the vendor's **entire
permission-key catalogue** out of its own JS bundle: 166 keys of the form
`<feature>-<view|add|edit|approve|void|full-access|export>`, which is the vendor's own enumeration of
every gated feature it ships. Where a key exists the feature exists; where none exists, it does not.

| | |
|---|---|
| Live read taken | **yes** — 2026-09-20, both tenants, **read-only, no writes** |
| Vendor permission keys extracted | **166**, from `static/js/main.*.chunk.js` + `13.*.chunk.js` (7.3 MB) |
| Vendor document types | 22 — **we hold 20** |
| Vendor reports | 51 on Cadehi — **we hold a counterpart to every one** |
| Substantial features we lack | **1** (bank reconciliation, + its statement importer) |
| Routes that looked like scope and are not | **1** (Recurring Invoices) |
| Carried items closed by evidence | phase 52 #1 (the four unread line types) |
| New phases planned | **3** (54, 55, 56) |
| Code changed | **none** |

**The headline is a negative result and it is the valuable one.** This codebase is at feature parity
with the reference product except for bank reconciliation. That is why the forward plan is three
phases rather than ten — padding it would be inventing work, and the roadmap's own method says items
a session cannot schedule stay *out* of the sequence rather than get folded into it.

---

## Decision A — census the keys, not the screens

A screen-by-screen pass answers "what does this screen do". It cannot answer "what else is there",
and every scan in `docs/erp-module-scan.md` has that shape. Two phases in a row were surprised by it:
phase 51 found batch and serial tracking that the 2026-09-10 pass had missed entirely, and phase 52
found a `Unit:` selector no scan had recorded. A re-planning phase whose whole job is *what next*
cannot afford that blind spot.

The vendor's React bundle carries its permission-key strings as literals, because the client gates
its own controls on them. Extracting them is a complete enumeration of gated features with no reading
required, and it is checkable in both directions: a feature with no key is not a feature.

**It immediately paid for itself twice** — once on Recurring Invoices (Decision C) and once on
Delivery Note / GRN, where the five-key sets prove the deferral is parked against something real
rather than against a menu entry.

---

## Decision B — the document-type diff, and what it leaves

Vendor types carrying `*-add`/`*-approve`: `quotation`, `sales-order`, **`delivery-note`**, `invoice`,
`credit-note`, `customer-receipt`, `purchase-order`, **`goods-received-note`**, `purchase-bill`,
`debit-note`, `supplier-payment`, `expenses`, `journal-voucher`, `cash-transfer`, `quick-payment`,
`quick-receipt`, `warehouse-transfer`, `inventory-adjustment`, `production-order`,
`production-journal`, `cheque-register`, `opening-balance`.

**20 of 22 are ours.** The two that are not are Delivery Note and Goods Received Note, which sit
behind `TenantSettings.InventoryTrackingMode` and stay deferred — both tenants still read
`inventoryTrackingMode: "Accounting Movement"`, and the re-entry condition is unchanged (the user
flips the setting, the screens are read, then it is a phase of its own).

The reports catalogue is 51 on Cadehi and `web/src/app/features/reports/` holds a counterpart to
every entry — we are in fact a superset (the two migrated registers and the two traceability reports
phase 51 built). The two traceability reports do not appear on Cadehi at all, which is consistent with
them being permission-gated rather than withdrawn, and leaves phase 51's carried item exactly where
it was.

**Bank reconciliation is the remainder**, and it became phases 55 and 56.

---

## Decision C — Recurring Invoices is a route, not a feature

`/sales/recurring-invoices` is a real client route. It renders real Approved/Draft list chrome with a
`+ ADD NEW` button, and a session planning from a route list would have scoped a phase around it.

It calls `recurring-invoices?limit=20`. The server answers **404 Not Found** — on `api-v2.tigg.dev`
*and* `api-v2.tigg.app`, i.e. both builds — and the screen renders `404 Not Found` where the grid
belongs. **No `recurring-invoice-*` key exists among the 166.**

Recorded in the roadmap under "Read, and absent" rather than left out, so that no future session
promotes it from a route list. **The general rule, which is the reusable part: a route is not a
feature, and the vendor's own key catalogue is the check.** This is the same class as phase 47's
Decision G — items dropped *with the reason*, so nobody re-opens them from a list.

---

## Decision D — phase 52's four unread line types now have evidence, and one caveat is load-bearing

Phase 52 deferred Opening Stock, Inventory Adjustment, Production Journal and BOM **unread** rather
than excluded, because the reference tenant held none of those documents. Moonbeam holds four
Production Orders, and that was enough to change the picture.

| Type | Evidence | Unit? |
|---|---|---|
| **Production Order** | live payload of approved `PRO0014` | **Yes** — `measurement_unit_id` per `raw_materials[]` line, **plus `fg_measurement_unit_id` on the header** |
| **Production Journal** | `/inventory/manufacturing/add`: `Product \| Quantity \| Rate \| Amount` | header shows none — see the caveat |
| **Inventory Adjustment** | `/inventory/inventory-adjustment/add`: `Product \| Quantity \| Type \| RATE \| Amount` | header shows none — see the caveat |
| **Opening Stock** | Opening Balances → Product: `NAME \| CATEGORY \| QUANTITY \| RATE \| AMOUNT` | **No** |

**The caveat is phase 52's own finding turned against this read, and it is why this phase did not
declare the question closed.** The vendor renders the unit control *inside the Qty cell*, not as a
column. An **empty** grid therefore proves nothing: the Production Order add form shows a bare
`Product | Quantity` header too, while its approved documents demonstrably carry `measurement_unit_id`.
Only Opening Stock is settled by its grid, and only because it is not a line grid at all — one row per
product, with no cell for a control to hide in. The other three need one row-present read each, which
is phase 54's first step.

**Two things this turned up that the plan did not have:**

1. **`fg_measurement_unit_id` is on the header.** Phase 52's rule was *every line naming a product and
   a quantity*. A finished good named on the **header** is the first thing that rule does not reach,
   and it is phase 54's first decision rather than a detail.
2. **BOM's `Qty/Unit` column could not be found.** The roadmap recorded BOM's Raw Materials table as
   carrying `Qty` **and** `Qty/Unit`, and named the interaction between an entered unit and that ratio
   as a real open question. `/inventory/bom/add` renders neither column, on either tenant, and both
   BOM lists are empty so no saved BOM was readable. Downgraded from *open* to **unconfirmed** — to be
   settled in the same row-present read, and deleted rather than carried if it does not exist.

---

## Decision E — bank reconciliation split at the demonstrable seam

The vendor's bundle names the endpoints (`/bank-statements-matched`, `/bank-reconciliations`,
`/bank-reconciliations/:id`, `/fetch-statements`, `/balance-history/:id`,
`/reconciliation-report?account_id=`, `bank-reconciliation-export`) and the matcher's own client state
names its shape: `selectedTxnBank` against `selectedTxnTigg`, collected as `bs_ids` + `tx_ids` and
POSTed together, each feed filtered on `reconciled: false`. So it is a **two-pane N:M matcher**, and a
reconciliation is its own record joining many statement lines to many transactions — a `reconciled`
marker falls out of it rather than being the model.

**Split into two phases at the point where each half is runnable**, per the roadmap's sizing rule. A
matcher with nothing to match against is not demonstrable, so the statement importer is 55 and the
matcher is 56.

**The importer half carries a genuine design question**, which is why it is not a footnote: phase 38's
rule is that an importer resolves a row into the command it *would* send, so the dry run and the real
run share every line of resolution. **A bank statement line resolves into no command** — it is a raw
row awaiting a match, whose only destination is a table.

**The read this needs has a real start condition, and it is recorded rather than worked around.**
Entered by URL, `/accounting/recon` fetches `cash-and-bank-accounts/undefined` and renders
`404 account not found` — it takes its account from router state pushed by the Bank Accounts page.
Five query-parameter spellings were tried and all 404'd. Moonbeam has **no** cash-and-bank accounts;
Cadehi has exactly one and it is `type: "Cash"`. So the read needs a tenant holding a **Bank**-type
account, reached through the Bank Accounts list. Phase 32's lesson applies: ask before falling back to
the phase-8f derive-instead rule.

---

## Decision F — the deferred and dropped lists, re-confirmed

Phase 47's Decision G dropped eight items *with reasons* so no future session re-opens them from a
list, and a re-planning session is exactly when to check those reasons still hold. They do. Two items
were touched by this read and **both deferrals got stronger, not weaker**:

- **Delivery Note / GRN** — the ten permission keys prove the feature is fully built behind the
  `InventoryTrackingMode` flag. The seam is real; the deferral is parked against something that exists.
- **E-commerce / marketplace** — the Daraz integration is **live**, not aspirational: both tenants
  call `general-settings/daraz/access-token` on every page load, and the product payload still carries
  `marketplace_skus: []` and `sku_id`. That is a named third-party marketplace, i.e. precisely the
  storefront the PRD excludes, so the finding confirms the non-goal rather than reopening it.

Also re-observed unchanged: `service_charge_applicable` and `print_profile_id` on the product payload
(both dropped in phase 47), and `batch_tracking_enabled` / `serial_no_tracking_enabled` (phase 51).

The three standing "outside the sequence" items — the NVDA hour, the two traceability reports' real
columns, and full-text search — keep their start conditions verbatim. Phase 52 had added a fourth;
this read supplied its evidence, so it left the list and became phase 54.

---

## What this phase changed

| File | Change |
|---|---|
| `docs/erp-module-scan.md` | new dated appendix, "Re-planning read (2026-09-20, phase 53)" — appended, never rewritten |
| `docs/roadmap.md` | index rows 43–52 compacted to one line each and put in numeric order; the 48–52 planning sections moved out; the **Forward plan (54–56)** written; the deferred list re-confirmed; phase 53 added to the index |
| `docs/roadmap-history.md` | the 48–52 planning detail, moved verbatim (the fourth such move, after 2026-09-02, 09-14 and 09-15) |
| `docs/phase-53-status.md` | this file |
| `CLAUDE.md` | phase index entry + Current status |

**No source file was touched**, so the test counts are unchanged from phase 52: Domain 703,
Application.UnitTests 1266, Infrastructure.UnitTests 12, Api.IntegrationTests 30, Angular 585.

---

## Known limitations / carried items

1. **Three of the four line types are still not settled** — Inventory Adjustment, Production Journal
   and BOM need one row-present read each, because the vendor hides the control in the Qty cell. This
   phase has the evidence that the question is answerable, not the answer. Phase 54 owns it.
2. **Bank reconciliation's screen was never opened.** Its shape here is inferred from endpoint names
   and client state, which is strictly weaker than a page load. The start condition is recorded; phase
   56 must ask before falling back.
3. **BOM's `Qty/Unit` is unconfirmed, not disproven.** Both BOM lists are empty on both tenants.
4. **The two measurement debts are still unpaid** — phase 51's `StockLedgerEntry` index and phase 52's
   `.Include(SecondaryUnits)`. Assigned to phase 54, which touches both areas, per phase 34c's rule.
5. **The census reads the vendor's *client*.** A server-side feature the client does not gate would be
   invisible to it. It is a strong lower bound on what exists, not a proof of what does not — which is
   why Recurring Invoices was checked against the live API as well as against the key list.
