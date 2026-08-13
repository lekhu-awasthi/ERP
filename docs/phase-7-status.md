# Phase 7 status — Inventory & stock ledger

**Status: COMPLETE.** The FIFO stock ledger is real now, replacing Phase 5/6's stubs
(`AlwaysOkStockAvailabilityPolicy`, PurchaseBill's entirely-absent stock increment).
`IStockLedgerService` (`Application.Inventory.Stock`) is the FIFO consumption engine —
`IncrementAsync`/`ConsumeAsync`/`GetAvailableQuantityAsync`/`PreviewConsumptionCostAsync`, a plain
injectable service mirroring how `IGlPostingRule`/`IDocumentNumberGenerator` are structured, not a
MediatR handler. `ApprovePurchaseBillCommandHandler` now creates a real `StockLedgerEntry` layer per
Goods line at `UnitCost=line.Rate`; `ApproveInvoiceCommandHandler` now really consumes those layers
per Goods line, and — a scope decision this phase made explicitly (see below) — also posts a second
Debit COGS/Credit Inventory GL leg using the FIFO-computed cost, alongside the existing Sales/AR/VAT
lines. `FifoStockAvailabilityPolicy` replaces the literal always-Ok stub, branching on
`TenantSettings.NegativeStockBalanceAction` (Reject/Warn/DoNothing); a genuine confirmable-warning
signal (`StockAvailabilityWarningException`, HTTP 422 — deliberately distinct from `ConflictException`'s
409) lets the Angular client resubmit `ApproveInvoiceCommand` with `OverrideWarning=true` without a
second round trip, per architecture-spec.md §3.5's own recommendation.

Two new document types round out the phase: `WarehouseTransfer` (Consume from one warehouse,
Increment into another at the exact carried-over FIFO cost — the one `ApprovableTransaction` in this
codebase with no GL posting at all, a pure location move) and `InventoryAdjustment` (Increase lines
create a new layer at a user-entered cost, Decrease lines consume existing layers at their real FIFO
cost — and unlike WarehouseTransfer, this *does* post GL, against a new tenant-configured
Adjustment/Variance account, since a quantity/value correction is a real asset-value event). Query
side: `ProductStockPositionQuery` (Opening/In/Out/Balance per Product+Warehouse) and
`InventoryLedgerQuery` (the kardex — chronological movements with a running balance), the latter
backed by a small new `StockMovement` audit-trail entity added specifically because
`StockLedgerEntry` alone (a mutable-in-place FIFO layer) can't reconstruct OUT-side history once a
layer has been partially consumed by more than one document — see scope decision #2.

Confirmed by hand end-to-end against the real API/DB (see "Manual E2E" below): a fresh Admin can set
up a Chart of Accounts (Cash, AR, Inventory, VAT Receivable, AP, VAT Payable, TDS Payable, Sales
Revenue, Inventory Adjustment, Purchase Expense, COGS), two Warehouses, a Supplier, a Customer, and a
13%-VAT Goods Product; approve a PurchaseOrder→PurchaseBill for 100 units (creates a FIFO layer,
Stock Position shows Balance=100); approve a WarehouseTransfer moving 40 units to a second warehouse
(no GL, Stock Position splits correctly across both warehouses); approve an Invoice selling 40 units
(GL posts Debit COGS 4000/Credit Inventory 4000 alongside the existing Sales/AR/VAT lines, the COGS
figure exactly matching the FIFO layer's unit cost); approve an InventoryAdjustment with one Increase
line (10 units @ 90) and one Decrease line (5 units, FIFO-costed) in the same document (GL posts both
legs, net effect correct and balanced); and confirm the Inventory Ledger/kardex and Stock Position
views reconcile exactly against every movement. The Warn/Reject policy branches were also confirmed
directly against the API: an over-large Invoice line returns HTTP 422 with a confirmable message on
first approve, HTTP 409 (a hard, non-overridable `ConflictException`) on retry with
`overrideWarning=true` once the shortfall is real (see scope decision #3's tension write-up), and
HTTP 409 immediately (no warning step) when `NegativeStockBalanceAction=Reject`.

## Roadmap Phase 7 exit criteria — final status

- [x] `StockLedgerEntry` (FIFO layer) model, scoped `(ProductId, WarehouseId)` — new `inventory`
      schema, `TransactionDate` (the source document's own business Date, not real-insert-time) is
      the FIFO ordering key, `CreatedAt` is only a deterministic same-date tiebreaker
- [x] FIFO consumption engine (`IStockLedgerService`/`StockLedgerService`) — `IncrementAsync`,
      `ConsumeAsync` (throws `ConflictException`, never goes negative — see scope decision #3),
      `GetAvailableQuantityAsync`, `PreviewConsumptionCostAsync` (read-only, used nowhere yet — see
      scope decision #4)
- [x] Unit tests for FIFO consumption — 15 tests in `StockLedgerServiceTests`: exact-layer-match,
      partial-layer, spanning multiple layers, ordering by `TransactionDate` not insertion order,
      consuming more than available (throws, doesn't go negative), zero-quantity no-op (both
      directions), no-layers-at-all, `GetAvailableQuantityAsync` scoping and empty-result, preview
      non-mutation and capping. Plus 4 tests for `InventoryAdjustmentPostingRule` (increase-only,
      decrease-only, mixed-nets-correctly, zero-produces-no-lines)
- [x] Real `StockAvailabilityPolicy` (`FifoStockAvailabilityPolicy`) replacing
      `AlwaysOkStockAvailabilityPolicy` (deleted) — branches on `TenantSettings.NegativeStockBalanceAction`;
      `OverrideWarning` threaded through `ApproveInvoiceCommand`/the `/invoices/{id}/approve`
      endpoint/`SalesService.approveInvoice`; Angular shows a `window.confirm` dialog on HTTP 422
      and resubmits with the flag set
- [x] Real stock Increment wired into `ApprovePurchaseBillCommandHandler` (Goods lines only,
      `UnitCost=line.Rate`) and real Consume wired into `ApproveInvoiceCommandHandler` (Goods lines
      only), both inside the same `SaveChangesAsync` as the GL posting — no split transaction
- [x] COGS GL leg on Invoice approval (scope decision #1) — `InvoicePostingRule` extended with
      optional `CogsAccountId`/`InventoryAccountId`/`CogsAmount`; `TenantSettings` grew
      `DefaultInventoryAccountId`/`DefaultCogsAccountId` (no per-Product override exists, unlike
      Sales/Purchase accounts — see scope decision #1)
- [x] `WarehouseTransfer` aggregate + CRUD/Approve — Consume from `FromWarehouseId`, Increment into
      `ToWarehouseId` at the exact weighted-average cost `ConsumeAsync` returned, no `IGlPostingRule`
      at all (the one document type in this codebase that doesn't post GL)
- [x] `InventoryAdjustment` aggregate + CRUD/Approve + `InventoryAdjustmentPostingRule` — mixed
      Increase/Decrease lines in one document net correctly on both the Inventory and
      Adjustment/Variance accounts (worked out on paper first, see scope decision #5); new
      `TenantSettings.DefaultInventoryAdjustmentAccountId`
- [x] `ProductStockPositionQuery` + `InventoryLedgerQuery` — architecture-spec.md §4.3's query side;
      the latter required a small new `StockMovement` audit entity (scope decision #2)
- [x] Permission keys: `Inventory.WarehouseTransfer.{View,Create,Edit,Approve}`,
      `Inventory.InventoryAdjustment.{View,Create,Edit,Approve}` (maker-checker, same seed pattern
      as every prior phase), `Inventory.InventoryLedger.View` (single shared key for both read-only
      report screens — Member and Admin both granted, see scope decision #6)
- [x] Angular: `warehouse-transfer-list-page`/`-detail-page`, `inventory-adjustment-list-page`/
      `-detail-page` (cloning the established transactional-document chrome, `[selected]` per-option
      throughout — never `[value]` on the `<select>` itself), `stock-position-page`/
      `inventory-ledger-page` (read-only report screens, not document forms), Invoice's
      Warn-and-confirm-with-override flow, `accounting-defaults-page` extended with the three new
      Inventory-side fields, dashboard nav links
- [x] One EF Core migration (`AddPhase7InventoryStockLedger`) for the `inventory` schema tables +
      the new `TenantSettings` columns + the `RolePermissions` seed rows — purely additive (no
      column drops/renames), so no manual reordering was needed; `DocumentNumberingRule` rows for
      `WarehouseTransfer`/`InventoryAdjustment` need no seeding (confirmed: rows are created lazily
      by `IDocumentNumberGenerator` on first use, same as every other document type since Phase 2)
- [x] `dotnet build`, `dotnet test` (67 Domain + 123 Application [104 pre-existing + 19 new] +
      4 Api.IntegrationTests, all green, Docker Desktop running), `ng build` all pass. `ng test`
      could not be run to a verdict in this environment — Angular's Vitest-based unit-test builder
      failed to start its worker-fork pool (`[vitest-pool-runner]: Timeout waiting for worker to
      respond`) against all 3 pre-existing spec files, unrelated to any Phase 7 code (none of the
      failing specs touch Inventory); `ng build` (which does full template/type compilation of every
      new component) succeeded cleanly, and the manual E2E pass below exercised every new page
      directly in a real browser against the real API
- [x] Manual E2E against real API/DB/browser (see summary above and "Bugs hit and fixed") —
      reproduces the roadmap's own exit criteria: PurchaseBill creates a FIFO layer, Stock Position
      shows the right balance, an Invoice sells some of it (COGS posted correctly), a
      WarehouseTransfer moves stock, an InventoryAdjustment corrects it (mixed Increase/Decrease),
      and the Inventory Ledger/kardex matches by hand

## Scope decisions

1. **Invoice approval now posts a real COGS leg (Debit COGS / Credit Inventory), using the
   FIFO-computed cost — not confirmed against the reference product** (erp-module-scan.md never
   observed this; the brief flagged it as an open question to resolve, not a confirmed-live
   behavior, and asked for it to be called out explicitly here rather than presented as fact).
   Recommended and implemented per the brief's own suggestion: mirror the existing
   `TenantSettings`-level default-GL-account fallback pattern (`DefaultSalesAccountId` etc.) one
   level up, since unlike Sales/Purchase accounts, Product carries no per-Product
   `InventoryAccountId`/`CogsAccountId` at all — `DefaultInventoryAccountId`/`DefaultCogsAccountId`
   are tenant-wide only, no per-Product override exists or is planned. `InvoiceAccountResolver`
   gained a `resolveInventoryAccounts: bool` parameter (true only when the invoice has at least one
   Goods line, so an all-Service invoice or a tenant that's never configured the new Inventory
   defaults never fails a check it doesn't need) that resolves and validates both accounts up front,
   fail-fast, before any stock is touched — same precedent as the existing AR/VAT-account checks.
   The actual `CogsAmount` isn't known until `ApproveInvoiceCommandHandler` calls
   `IStockLedgerService.ConsumeAsync` (the real FIFO cost), so it's threaded back into the
   already-resolved `InvoicePostingInput` via a C# record `with` expression after Consume runs,
   keeping `InvoicePostingRule.BuildLines` itself a pure function of already-resolved data, same
   split every other posting rule in this codebase follows.
2. **`InventoryLedgerQuery`'s kardex view needed a new `StockMovement` entity beyond what the
   roadmap's own item 1 asked for.** `StockLedgerEntry` is a FIFO *layer* — its `QuantityRemaining`
   mutates in place as `ConsumeAsync` walks it, so once a layer has been partially consumed by more
   than one document, the *history* of which document took how much is gone from `StockLedgerEntry`
   alone; only the current remaining balance survives. A literal "chronological movements" kardex
   (roadmap's own phrase, task 8) needs OUT events visible, which the layer model can't produce.
   Added a small, append-only `StockMovement` audit row (`Direction: In|Out`, `Quantity`,
   `UnitCost`, source document, `TransactionDate`) written by `StockLedgerService.IncrementAsync`/
   `ConsumeAsync` alongside their real work — one row per service call (not per FIFO layer touched
   inside a multi-layer Consume), since a kardex reports one line per transaction, not per internal
   layer-walk step, and Consume's own weighted-average `UnitCost` is exactly the right figure to
   show for an Out row anyway. Never read back by the FIFO engine itself — purely a read-model.
3. **Resolved a real tension between two roadmap requirements**: item 2 says `ConsumeAsync` should
   "throw if asked to consume more than is available... never go negative", while item 3's
   Warn-and-allow behavior implies an invoice can be approved despite insufficient stock. These
   can't both mean "the ledger physically goes negative" — a FIFO engine with a hard
   never-negative invariant (matching the explicit unit-test requirement, "consuming more than
   available should throw, not go negative") cannot also silently invent inventory that doesn't
   exist. Resolved by treating `NegativeStockBalanceAction` as governing only whether a *pre-flight*
   warning/hard-block happens before an approval attempt, not what happens if the attempt is made
   anyway: `Reject` hard-blocks before touching stock; `Warn` throws a confirmable
   `StockAvailabilityWarningException` (422) unless `OverrideWarning=true`, in which case the
   handler proceeds to actually call `ConsumeAsync` — which will still throw a `ConflictException`
   (409) if the shortfall is real, just later and with a shortfall-specific message instead of a
   generic warning. `DoNothing` skips the pre-flight check entirely and goes straight to attempting
   Consume. Confirmed by hand: an invoice for 1000 units (only 25 in stock) gets 422 on first
   approve, then 409 ("only 25.0000 remain in stock") on retry with the override flag — the
   override lets the user *attempt* to proceed past a soft warning, it doesn't manufacture stock.
4. **`PreviewConsumptionCostAsync` exists but is called from nowhere this phase.** Built for
   architecture-spec.md §3.4's "live preview before approve" principle (extended to Invoice's COGS
   leg), but wiring it into `PreviewInvoiceGlPostingQuery` would require adding a `WarehouseId` to
   that query (it currently only carries line data, no warehouse context — stock availability is
   inherently warehouse-scoped) and touching the Angular invoice-detail-page's preview call site.
   Deliberately deferred rather than expanding this already-large phase further: the GL-preview-
   before-approve section on Invoice's detail page currently shows only the Sales/AR/VAT lines, not
   an estimated COGS leg — the real COGS leg appears only in the post-approval GL Transactions
   section, sourced from the actual FIFO consumption. `CreditNoteAccountResolver` explicitly passes
   `resolveInventoryAccounts: false` for the same underlying reason stated in decision #5 below.
5. **`InventoryAdjustmentPostingRule` posts each direction's *total* as its own balanced pair, not
   one pair per line** — applying Phase 6 bug #3's lesson (verify the *net* effect on every account
   a paired-effect document touches, don't just trust that the rule's own entry balances) before
   writing the rule, not after finding it broken. A single InventoryAdjustment can carry both
   Increase and Decrease lines; posting `IncreaseAmount` as one Debit-Inventory/Credit-Adjustment
   pair and `DecreaseAmount` as one Debit-Adjustment/Credit-Inventory pair means the net Inventory
   effect (`Debit − Credit = IncreaseAmount − DecreaseAmount`) is exactly the real on-hand value
   change regardless of which direction dominates, and the net Adjustment-account effect mirrors it
   (a net credit reads as other income — found more than expected; a net debit reads as an expense —
   wrote off more than expected). Confirmed both by 4 new unit tests and by hand (10 units increased
   @90, 5 decreased at FIFO cost 100 in one document: GL posted Debit Inventory 900/Credit
   Adjustment 900/Debit Adjustment 500/Credit Inventory 500, net Inventory +400, balanced).
6. **`InventoryLedgerView` is a single shared permission key covering both read-only report
   screens** (Stock Position and Inventory Ledger), not a four-key `{View,Create,Edit,Approve}` set
   like every transactional document type gets. Neither screen is a document with its own
   lifecycle — they're pure views over `StockLedgerEntry`/`StockMovement`, the same reasoning
   Configuration's simple lookups get a `{View,Manage}` pair instead of the document-shaped four
   keys. Granted to both Admin and Member (unlike most Manage-type keys, which are Admin-only) since
   it's read-only visibility into data Member already indirectly sees via the documents that create
   it (PurchaseBill/Invoice/WarehouseTransfer/InventoryAdjustment).
7. **CreditNote/DebitNote do not reverse the FIFO stock ledger this phase** — a Credit Note against
   a Goods-line Invoice does not restock the ledger or reverse the COGS leg; a Debit Note against a
   Goods-line PurchaseBill does not reverse the FIFO layer/consumption. Only the GL reversal (already
   built in Phases 5/6) happens. This was explicitly out of the roadmap's Phase 7 item list (1–11
   don't mention it) and doing it correctly is nontrivial for the same reason Phase 6 bug #3 was
   nontrivial — a stock reversal has to net out against the *specific* FIFO layers/movements the
   original document touched, not just re-run Increment/Consume naively, which risks exactly the
   kind of "each half balances on its own, the combined effect doesn't" bug that bit DebitNote's GL
   in Phase 6. Flagged here explicitly (the user asked about this directly during the build) as a
   known gap for whoever picks up CreditNote/DebitNote stock-reversal next, rather than a silent
   omission.
8. **Landed-cost/import-duty allocation is out of scope for PurchaseBill's stock-increment
   UnitCost** — `ApprovePurchaseBillCommandHandler` uses `UnitCost=line.Rate` (the price actually
   paid per unit, pre-VAT), not a landed cost that would also allocate freight/duty/`IsImport`
   details onto the per-unit stock value. The roadmap's own task 4 flagged this as an open
   precision question ("or Rate-plus-landed-cost if you want to be precise"); `Rate` was chosen as
   the simpler, defensible baseline — landed-cost allocation is a real but separate feature
   (it would need to split a document-level freight/duty charge across lines by some proration
   rule) better scoped on its own rather than folded into this already-large phase.
9. **Money precision**: `decimal(18,4)` for `StockLedgerEntry.QuantityIn/QuantityRemaining/UnitCost`
   and `StockMovement.Quantity/UnitCost`, matching every prior phase's convention.
10. **FK delete behavior**: `Restrict` everywhere a Phase 7 entity references another aggregate
    (`Product`, `Warehouse`), `Cascade` for aggregate-owned children (`WarehouseTransfer`→`Lines`,
    `InventoryAdjustment`→`Lines`) — same split every prior phase established. New schema
    `inventory` for `StockLedgerEntry`/`StockMovement`/`WarehouseTransfer(Line)`/
    `InventoryAdjustment(Line)`.
11. **No new Domain.UnitTests were added** — all new test coverage (19 tests: 15 FIFO engine + 4
    posting rule) lives in `Application.UnitTests`, since `IStockLedgerService` and
    `InventoryAdjustmentPostingRule` are both Application-layer services, and the new Domain
    aggregates (`WarehouseTransfer`/`InventoryAdjustment`/`StockLedgerEntry`) are thin enough
    (Create/AddLine/Approve, same shape as every prior phase's aggregates) that they're only
    exercised indirectly through the Application-layer tests and the manual E2E pass, matching
    Phase 6's own precedent (scope decision #9 there) rather than a deliberate new choice.

## Bugs hit and fixed along the way

1. **`internal` factory methods on the new Domain entities didn't compile when called from
   Application** — `StockLedgerEntry.Consume`/`StockMovement.Create` were first written `internal`
   (matching the pattern every prior phase's child-line factories use, e.g. `CashTransferLine.Create`),
   but those precedents are all called from *within* the Domain assembly (a parent aggregate's own
   method, e.g. `CashTransfer.AddLine`). `IStockLedgerService` — the intended caller of both — lives
   in the *Application* assembly; `internal` doesn't cross that boundary the way it does for
   same-assembly calls. Caught immediately by `dotnet build` (`CS1061`/`CS1501`), not a runtime
   surprise. **Fixed** by making both `public`, with a doc comment explaining why (the only
   intended caller is cross-assembly). **Worth knowing for future phases**: the "child factory is
   `internal`, only the parent aggregate calls it" pattern only holds when the *only* caller really
   is another Domain type — the moment an Application-layer service needs to construct or mutate an
   entity directly (as opposed to going through a parent aggregate's own public method), that
   entity's factory/mutator needs to be `public`, not `internal`.
2. **A live, currently-shipping instance of the documented `[value]`-vs-`@for` select race, on
   pages Phase 7 didn't touch** — found while manually testing Invoice's Warehouse selection (a
   Phase 7-critical field, since it drives which warehouse's FIFO layers get consumed) and
   Accounting Defaults' new Inventory fields. `accounting-defaults-page.html`'s 7 pre-existing
   `<select>` elements (Sales/AR/VAT-Payable/Purchase/AP/VAT-Receivable/TDS-Payable) and
   `invoice-detail-page.html`'s Customer/Warehouse `<select>`s all used `[value]="someSignal()"`
   directly on the `<select>` tag (the pattern CLAUDE.md's gotcha list already warns against,
   present since Phase 5/6 but apparently never retrofitted on these specific selects even after
   the gotcha was documented). This was not just a cosmetic display glitch — it was empirically
   confirmed to represent a **real, silent correctness bug**: an Invoice was created and approved
   with its Warehouse field visually showing "Branch Warehouse" throughout the entire session (in
   the create form, after Save Draft, after Approve, in the GL Transactions view), while a direct
   `sqlcmd` query against `[sales].[Invoices]` showed the row's actual persisted `WarehouseId` was
   "Main Warehouse" the whole time — confirmed independently via the Inventory Ledger, which showed
   the Invoice's stock consumption really did happen against Main Warehouse, not the warehouse the
   UI displayed as selected. A user relying on the visible selection to confirm which warehouse
   they were selling from would have been silently wrong. Root cause: `warehouseId()`/`contactId()`
   (the bound signal) and `warehouses()`/`customers()` (the select's options) each resolve via
   independent async subscribes; when the signal-driven `[value]` write reaches the DOM before the
   matching `<option>` element exists, the browser silently drops the assignment and falls back to
   whichever option happens to be first in list order — and Angular's fine-grained signal reactivity
   never retriggers that `[value]` binding once the options arrive late, so the mismatch never
   self-corrects (exactly phase-6-status.md's bug #1, just newly confirmed to cause *wrong data*,
   not just a wrong-looking form). **Fixed** on both files: removed `[value]` from the `<select>`
   entirely, added `[selected]="option === boundSignal()"` on each `<option>` (including the
   placeholder), matching the safe pattern this codebase has used correctly since
   `purchase-bill-detail-page.html`. Re-verified by hand: created a fresh Invoice against Branch
   Warehouse using a reliable native-`change`-event dispatch (see note below on tooling), reloaded,
   and confirmed both the visual selection and a direct DB query now agree. The other three
   still-broken instances found during the audit (`quotation-detail-page.html`,
   `payment-detail-page.html`, and a per-line select in `journal-voucher-detail-page.html`) were
   deliberately left unfixed — out of Phase 7's scope, since none of them touch stock — and flagged
   as a follow-up task instead of silently left for the next person to rediscover.
3. **Browser-automation tooling note, not an app bug**: this session's `form_input` tool (which
   sets a `<select>`'s DOM value and dispatches an event) and native click/keyboard interaction with
   the OS-level `<select>` dropdown popup both proved unreliable for driving Angular's `(change)`
   handler in this sandboxed browser environment — the visual DOM state would update but the bound
   Angular signal sometimes silently would not, in a way that was easy to mistake for an app bug
   (and did, briefly, cause a stray extra 40-unit consumption against the wrong warehouse in this
   session's own manual-test data before being traced to the tooling rather than the code — see bug
   #2's root-cause trail). The reliable workaround used throughout the rest of the manual E2E pass:
   dispatch a real `change`/`input` `Event` via the native property setter
   (`Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set.call(el, value)` then
   `el.dispatchEvent(new Event('change', { bubbles: true }))`), which Angular's zone.js patching
   picks up correctly every time. Noted here in case it helps whoever runs the next phase's manual
   E2E pass in a similarly sandboxed environment.

## What's next

**Phase 8+** (see `roadmap.md`): Workflow (Tasks, Transaction Approval queue — now meaningful across
7 phases' worth of real Draft-status documents), Reports (Trial Balance/Balance Sheet/Income
Statement first), CRM, the Role Reference full editor. Worth prioritizing early, given this phase's
own findings: (a) CreditNote/DebitNote stock reversal (scope decision #7) — a real gap once anyone
actually returns Goods against an invoiced/billed line; (b) the three remaining `[value]`-vs-`@for`
select instances flagged as a follow-up task (bug #2) — genuine, currently-shipping display/
correctness risk on Quotation/Payment/JournalVoucher, just not stock-critical enough to block this
phase; (c) landed-cost allocation onto PurchaseBill's stock UnitCost (scope decision #8) if precise
import-cost accounting becomes a real requirement; (d) wiring `PreviewConsumptionCostAsync` into
Invoice's GL-preview-before-approve flow (scope decision #4) once a `WarehouseId` is threaded through
that query, for full COGS-leg preview parity with the rest of the posting logic.
