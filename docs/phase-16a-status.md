# Phase 16a status — Void lifecycle + lock-date enforcement

**Status: COMPLETE.** The two integrity guarantees product-requirements.md promises that existed
nowhere in this codebase before this phase: no command could ever produce a `Void` status (flagged
in `phase-8f-status.md`'s Annex 5 write-up — `IsActive` was always `true`), and
`Organization.LockDate` (schema'd since Phase 1b) was enforced by nothing at all (FR-2.2/NFR-3.4).
Both close this phase, across all 13 `ApprovableTransaction` document types.

## Roadmap/brief exit criteria — final status

- [x] All 13 `ApprovableTransaction` types get a real `VoidXCommand`/`VoidXCommandHandler` pair —
      not a subset (scope decision #1)
- [x] GL reversed via a mirror-image `GlJournalEntry` (swap every Debit/Credit line), never
      mutating the original — `GlJournalEntry.PostReversalOf` (scope decision #2)
- [x] Stock reversed correctly per type: Invoice/CreditNote/DebitNote/InventoryAdjustment-Decrease
      restock at the exact original recorded cost; PurchaseBill/CreditNote's own created
      layers/WarehouseTransfer's destination layer/InventoryAdjustment-Increase's layer are
      rejected (409) if partly consumed, never partially unwound (scope decision #3)
- [x] Dependent-document guards: Invoice blocked while a non-Void CreditNote or an Approved
      Payment allocation still references it; PurchaseBill blocked while a non-Void DebitNote or
      an Approved Payment allocation still references it; Quotation/PurchaseOrder blocked from
      Converted by their own `EnsureApproved` guard (no separate lookup needed)
- [x] Void is terminal — no edit/re-approve/un-void; document number never recycled
- [x] 13 new `{Module}.{Type}.Void` permission keys, Admin-granted/Member-denied, seeded via
      `RolePermissionConfiguration.HasData` before scaffolding the migration
- [x] Every report/query filtering `Status == Approved` already excludes Void rows with **no code
      change needed** — confirmed by grep, not assumed (see "Ripple effects" below); the one
      genuine gap found and fixed is Annex Five's `IsActive` (bug #2)
- [x] Lock date enforced in one shared place (`LockDateBehavior`, a MediatR pipeline behavior)
      covering create/edit/approve/void of every lock-date-sensitive command across all 13 types
- [x] Organization settings UI (Admin-only) to view/set/clear the lock date
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged, Application.UnitTests 194 — 13
      new + 181 pre-existing, Api.IntegrationTests 4, all green) and `ng build`/`ng test` (7
      pre-existing specs green, no new Angular specs) all pass
- [x] Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below)

## Scope decisions

1. **All 13 types are voidable this phase — not a subset.** The brief asked me to decide per type
   and record it, expecting some to be deferred. Before deciding anything, I read every one of the
   13 `XStatus` enums and found `Void` **already declared as a placeholder member on all 13** —
   this codebase's own established "build the seam, not the feature" precedent
   (`JournalVoucherStatus`'s own doc comment says exactly this), and `SalesValidation`/
   `PurchasingValidation`'s conversion-cap code already filters `Status != CreditNoteStatus.Void`/
   `!= DebitNoteStatus.Void` — forward-looking code written against a status that didn't exist yet.
   That's conclusive: every type was always meant to get Void in this phase, so "decide per type"
   resolved to "yes for all 13," not a judgment call with room to defer any of them.
   - `Quotation`/`PurchaseOrder`: voidable only from `Approved`, never from `Converted` — no
     separate dependent-lookup needed, since `Void()`'s own `EnsureApproved` guard already rejects
     a Converted document (a different, distinct status) with a 409.
   - `SalesOrder`: voidable via the API, same as its Create/Update/Approve — no Angular UI this
     phase, continuing Phase 5's own "backend-only" scope for this type (its Approval-queue rows
     have had no "Open" link since Phase 12, unchanged here).
   - `WarehouseTransfer`: stock-only reversal, no GL (it never posts one).
   - `InventoryAdjustment`: both GL and mixed Increase/Decrease stock reversal.

2. **GL reversal is a mirror-image entry, not a hand-written reverse posting rule.**
   `GlJournalEntry.PostReversalOf(original)` swaps every line's Debit/Credit and posts a *second*
   entry against the same `SourceDocumentType`/`SourceDocumentId` — `GlJournalEntry` stays
   append-only (no UPDATE/DELETE path exists anywhere in this codebase for a posted entry).
   Weighed directly against the alternative (recompute a reverse via each type's own
   `IGlPostingRule<T>`, the way `DebitNotePostingRule` reverses `PurchaseBillPostingRule`): a
   swap-every-line mirror is foolproof against the exact class of bug `phase-6-status.md`'s bug #3
   documents (a hand-written reversal that balances its own entry while leaving a *paired* account
   like TDS Payable permanently unbalanced) — there's no way for a line-for-line mirror to leave
   any individual account it touched unbalanced, since every line it touched gets its own exact
   opposite. Also means every existing GL-reading report (Trial Balance/Balance Sheet/Income
   Statement) needed **zero code changes** — they sum `GlLine`s by account with no per-document
   uniqueness assumption, so a second entry for the same source document nets to zero by
   construction. `GlJournalEntryConfiguration`'s own index on `(SourceDocumentType,
   SourceDocumentId)` is non-unique, so a second entry for the same document id is a schema no-op.

3. **Stock reversal splits into two mechanically different cases, both built once and reused.**
   - *A line that created a layer* (PurchaseBill's own lines, CreditNote's Invoice-restock,
     WarehouseTransfer's destination side, InventoryAdjustment's Increase lines): a new
     `IStockLedgerService.ReverseIncrementAsync(sourceDocumentType, sourceDocumentId, ...)` finds
     every layer that document created, throws `ConflictException` (409) if **any** of them has
     `QuantityRemaining != QuantityIn` (partly or fully consumed by a later document) *before*
     mutating any of them, and zeroes them out (via the existing `StockLedgerEntry.Consume`) only
     if none are touched — the roadmap's explicit "reject, don't partially unwind" requirement,
     built as one shared method rather than re-implemented per Void handler.
   - *A line that consumed stock* (Invoice's Goods lines, DebitNote reversing a PurchaseBill,
     InventoryAdjustment's Decrease lines): restocked via the existing `IncrementAsync` at the
     *exact* originally-recorded cost — the same "put it back at what it left at" precedent
     `ApproveCreditNoteCommandHandler`'s Invoice-reversal already established in Phase 7's
     follow-up, using `InvoiceLine.CogsUnitCost`. `DebitNoteLine`/`InventoryAdjustmentLine` had no
     equivalent persisted field before this phase (only `InvoiceLine` did) — added
     `ConsumedUnitCost` (nullable `decimal`, populated once from `IStockLedgerService.
     ConsumeAsync`'s own return value at Approve time, mirroring `RecordCogsUnitCost`'s exact
     shape and its `public`-not-`internal` cross-assembly reasoning) to both, a genuine new schema
     column, not a reuse of an existing one.
   - `WarehouseTransfer`'s own case needed a third small wrinkle: since it never posts GL, its
     Void handler captures the destination-side layers' `(ProductId, QuantityIn, UnitCost)`
     *before* calling `ReverseIncrementAsync` (which then validates-and-zeroes them), then
     restocks the *source* warehouse at those exact captured values — undoing the location move
     symmetrically without inventing a fourth stock-reversal shape.

4. **Dependent-document guards are per-type concrete checks, not a generic "find anything
   referencing me" scan.** Only Invoice and PurchaseBill needed both a reversal-document guard
   (non-Void CreditNote/DebitNote) and a Payment-allocation guard — confirmed by reading
   `PaymentValidation.EnsureAllocationTargetsExistAsync`, which only ever validates
   `TargetDocumentType.Invoice`/`.PurchaseBill`; no other of the 13 types is ever a Payment
   allocation target in this codebase, so no other Void handler needed that check. CreditNote/
   DebitNote/JournalVoucher/CashTransfer/Expense/Payment/WarehouseTransfer/InventoryAdjustment
   have no dependent-document guard at all — confirmed by grep, nothing in this codebase ever sets
   `ReferrerId` pointing at any of them.

5. **Lock-date enforcement is one pipeline behavior, not per-handler copy-paste — but it needed
   two marker interfaces, not one, because Create/Update and Approve/Void commands carry
   genuinely different information.** A Create/Update command already has the business `Date` on
   its own payload (`ILockDateSensitive { DateOnly Date }` — since every one of the 26 Create/
   Update commands across the 13 types already has a positional `DateOnly Date` property, adding
   the interface name to the inheritance list was the *entire* change needed, zero new code per
   file). An Approve/Void command only ever carries `(OrganizationId, Id)` — the document's own
   Date has to be looked up from whichever table that id lives in, which the command itself can't
   express generically (`ILockDateSensitiveDocument { DocumentType LockDateDocumentType; Guid
   LockDateDocumentId }`, resolved by `LockDateBehavior`'s own 13-branch switch — the same "13
   concrete blocks, not one generic helper" precedent `TransactionApprovalQueryHandler` (Phase 12)
   established for cross-document-type dispatch in this codebase, chosen again here rather than a
   reflection-based or `IQueryable<T>`-parameterized helper for the same EF Core LINQ-translation-
   safety reasoning). `LockDateBehavior` is registered *after* `AuthorizationBehavior` in the
   pipeline (`DependencyInjection.AddApplication`) so a caller without permission gets a 403
   before this behavior ever queries `Organization.LockDate`.

6. **Setting a lock date does not validate against existing Draft documents dated inside the
   locked window.** The brief asked this to be decided and recorded explicitly. Resolved: no
   validation at set-time — a Draft dated before the new lock date simply fails the next time
   someone tries to edit/approve it, the same "fail at the point of the actual write" pattern
   every other validation in this codebase follows (there's no precedent anywhere here for
   scanning-and-blocking a settings change because some unrelated Draft record might later
   violate it, and doing so would require an unbounded cross-document-type query on every
   `SetOrganizationLockDateCommand` call for a benefit — flagging Drafts a user may not even care
   about yet — that doesn't outweigh the cost).

## Ripple effects — what actually needed changing vs. what didn't

Grepping every report/query in this codebase for its `Status ==`/`Status !=` filters confirmed
almost all of them already write `x.Status == XStatus.Approved` (VAT Summary, Master Reports, TDS
Report, Ageing/Statement's `ContactLedgerReader`, `GetDefaultPaymentAllocationsQuery`, the
conversion-cap `!= Void` checks in `SalesValidation`/`PurchasingValidation`) — none of these needed
a single line changed, since `Approved` was already the *only* status those queries ever matched;
a Void row was already excluded by definition, "going live" for free the instant `Void()` became
reachable. Two real gaps were found and fixed:

- **`ApprovePaymentCommandHandler` never re-validated its allocation targets were still Approved at
  Approve time** — only `CreatePaymentCommandHandler`/`UpdatePaymentCommandHandler` did, via
  `PaymentValidation.EnsureAllocationTargetsExistAsync`. Before this phase that gap was latent
  (nothing could make an Approved Invoice/PurchaseBill stop being Approved); Void makes it real —
  a Draft Payment allocated against an Invoice that gets voided *after* the Payment was drafted
  could previously still be approved, silently posting against a voided document. Fixed by adding
  the same re-check directly in `ApprovePaymentCommandHandler`, right before posting.
- **Annex Five's `IsActive` column could never actually be `false`.** `AnnexFiveReportQueryHandler`
  filtered its base query to `Status == Approved` *and separately* computed `IsActive = Status !=
  Void` — since the base filter already excluded every non-Approved row, a Void Invoice/CreditNote
  never even reached the `IsActive` computation, so the column was always `true`. Fixed by
  widening the base filter to `Status == Approved || Status == Void`, so a voided document still
  appears on the register (matching this report's own "flat bill audit log" shape,
  `phase-8f-status.md`) with `IsActive` genuinely `false`.

## Command/query surface

13 new `Void{Quotation,SalesOrder,Invoice,CreditNote,PurchaseOrder,PurchaseBill,Expense,DebitNote,
JournalVoucher,CashTransfer,WarehouseTransfer,InventoryAdjustment,Payment}Command`/Handler pairs,
one per type, each `IRequirePermission` (a new `*.Void` key) + `IOrganizationScoped` +
`ILockDateSensitiveDocument`. `SetOrganizationLockDateCommand`/`GetOrganizationLockDateQuery`
(`Application.Tenancy`), both gated on the single new `Tenancy.Organization.LockDateManage` key
(Admin-only, covering both read and write — the same single-key shape
`Configuration.AccountingDefaults.Manage` already uses for its own get+update pair).

New Domain: `Void(Guid voidedByUserId)` + `VoidedByUserId`/`VoidedAt` on all 13 aggregates;
`GlJournalEntry.PostReversalOf`; `IStockLedgerService.ReverseIncrementAsync`;
`DebitNoteLine.ConsumedUnitCost`/`RecordConsumedUnitCost` and
`InventoryAdjustmentLine.ConsumedUnitCost`/`RecordConsumedUnitCost`. One migration
(`AddPhase16aVoidAndLockDate`) bundling all of it — 28 new nullable columns plus the 26-row
permission seed, since `dotnet ef migrations add` bundles the entire pending model diff into one
migration regardless of how many logical changes it represents (`CLAUDE.md`'s own known gotcha).

## Angular

Every document type with an existing detail page (12 of 13 — all but `SalesOrder`) gets a Void
button next to its status badge, shown only when `status === 'Approved'`, gated behind a
`window.confirm` (no custom dialog component exists in this codebase to reuse), calling the new
`void*` service method and reloading on success. Every service (`sales`, `purchasing`,
`accounting`, `inventory`, `payments`) gained one `void*` method per type it owns, following the
exact `approve*` method shape each already has. A new `LockDatePage`
(`features/organizations/lock-date-page`, Admin-only route `organizations/:id/lock-date`, linked
from the dashboard next to Role Reference) shows the current lock date, lets it be set or cleared.

## Bugs hit and fixed

Both were latent gaps this phase's own investigation surfaced while adding Void, not new defects
introduced by it — see "Ripple effects" above for the full write-up:
`ApprovePaymentCommandHandler`'s missing allocation-target re-validation, and `AnnexFiveReportQueryHandler`'s
`IsActive` column that could never actually be `false`.

One environment/tooling snag during manual E2E, not a codebase defect: after scaffolding the
migration and applying it once successfully, a later `dotnet ef database update` attempt failed
with `PendingModelChangesWarning` even though nothing in the model had changed since. Re-scaffolding
a probe migration reproduced the exact same diff a second time, which shouldn't happen if the
snapshot were actually in sync — tracked down to having rebuilt only the `Infrastructure` project
standalone in between (`dotnet build src/Infrastructure/...`) rather than the full solution, so the
`Api` project's own output directory (what `dotnet ef ... --startup-project src/Api` actually
reflects over) still held a stale copy. Resolved by removing the accidental probe migration and
re-running `dotnet ef database update` without `--no-build`, letting it rebuild `Api` itself first.
Worth knowing before the next `dotnet ef` call that follows a partial, single-project rebuild.

## Manual E2E

Confirmed by hand end-to-end against the real API/DB/browser, seeded via curl + a cookie jar per
this session's own memory note (browser clicks reserved for the new Void button and Lock Date
settings page): a fresh Admin registered/verified/logged in, created an Organization, a Warehouse,
a full Chart of Accounts (Cash/AR/Inventory/VAT Receivable/AP/VAT Payable/TDS Payable/Sales
Revenue/Purchase Expense/COGS/Inventory Adjustment), Accounting Defaults, a Customer, a Supplier,
and two Goods Products.

- Approved a PurchaseBill (10 units), approved an Invoice selling 4 of them, approved a Payment
  fully allocated against that Invoice. Attempting to void the Invoice first returned a real `409`
  naming the payment guard; voiding the Payment then the Invoice both succeeded. Direct `sqlcmd`
  queries confirmed **every account touched by either document's original posting netted to
  exactly `0.0000`** across its original + reversal `GlJournalEntry` rows, and the product's total
  available quantity across all warehouses was back to exactly `10.0000` — including a restocked
  layer at `UnitCost=50.0000`, the exact original PurchaseBill rate, not a re-derived estimate.
- A second Product's PurchaseBill (5 units) had 2 sold via a separate Invoice; voiding that
  PurchaseBill returned a real `409` naming the partly-consumed-stock rejection — proving the
  roadmap's explicit "reject, don't partially unwind" requirement against the real database, not
  just the unit test.
- Invited and accepted a second user as a Member (system role, `*.Void` denied by default); that
  user's attempt to void a **nonexistent** Invoice id returned a real `403` naming
  `Sales.Invoice.Void` — not a `404` — confirming `AuthorizationBehavior` fires before the handler
  even runs, the same proof pattern Phase 14 established.
- Set the Organization's lock date to `2026-01-31`: creating a JournalVoucher dated on the lock
  date returned a real `409` with the exact lock date and attempted date named in the message;
  creating one dated after it succeeded and was later approved. Editing that approved-later
  voucher backdated to before the lock date returned the same `409`. Moving the lock date forward
  past the voucher's own date and attempting to void it also returned a `409` — resolved via
  `ILockDateSensitiveDocument`'s document-lookup path, not the request payload (the Void command
  carries no `Date` field at all) — proving that resolution path actually fires, not just the
  direct-payload one. Clearing the lock date allowed the same void to succeed immediately after.
  Then, through the real Angular UI in the Browser tool: the new Lock Date settings page correctly
  showed "Currently: not set", setting `2026-03-31` and clicking Save updated the page to
  "Currently: 2026-03-31" with a success banner, and a real Approved PurchaseBill's detail page
  correctly rendered its new Void button next to Convert to Debit Note, with no console errors.
