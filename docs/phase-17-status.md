# Phase 17 — Accounting breadth

## TL;DR

Five surfaces round out Accounting to reference parity: Quick Payment/Receipt, Bank Accounts,
Cheque Register, Allocate Customer/Supplier Payment, Opening Balances. The blocking prerequisite —
`Payment.Approve()`'s allocation invariant — was relaxed from "exactly Amount" to "at most Amount"
so a zero/partially-allocated Payment can be Approved. Live confirmation against the Tigg UAT
tenant (`moonbeamtradingandsuppliers.tigguat.com`) turned up one real surprise: Tigg's own "Quick
Receipt" is not a thin variant of Customer Payment at all — it's a generic multi-line
Accounts-table document (like a mini Journal Voucher) that happens to let you pick a
Customer/Supplier ledger sub-account as one of its lines, which is how "Received From" shows up in
its list view. That doesn't fit this codebase's Payment aggregate (single Contact, single Account,
no per-Contact ledger sub-accounts), so Quick Payment/Receipt here stays a thin variant of the
existing `Payment` aggregate — same functional outcome (no mandatory allocation), different UI
shape from Tigg's. See decision #7. Bank/Cash confirmed as a real `AccountKind` on `Account` (Bank
vs Cash only — no separate "Wallet" kind; e-wallets like E-sewa/Khalti are just Bank-kind accounts
pointing at a wallet provider in the Bank lookup). Cheque status is a flat 5-state field (Pending,
Deposited, Cleared, Bounced, Cancelled), not the roadmap's guessed linear pipeline. Opening
Balances has no Location field in this tenant (Location entitlement not on) — matches this
codebase's own no-Location scope, so `OpeningBalanceLine`/`OpeningStockLine` don't carry one.

## Decisions

### Decision #1 — Payment.Approve() invariant relaxation

Changed from `Allocations.Sum(Amount) == Amount` (required, non-zero) to `Allocations.Sum(Amount)
<= Amount` (zero, partial, or full all valid; over-allocation still rejected). This is the
prerequisite both Quick Payment/Receipt (no mandatory allocation) and the Allocate screens (list
Approved Payments with room left to allocate) depend on.

**Why:** FR-7.4 explicitly describes Quick Payment/Receipt as needing no invoice allocation; FR-5.12
needs Approved-but-under-allocated Payments to exist as a data shape, which the old invariant made
impossible (an Approved Payment could never have a remainder).

**How to apply:** `Payment.cs` — see the domain XML doc. `ApprovePaymentCommandHandler`'s
pre-check mirrors the same relaxed rule (kept in sync so a would-be `InvalidOperationException`
maps to a 409 `ConflictException` instead of an unhandled 500, same pattern the handler already
used for the old invariant). `GetDefaultPaymentAllocationsQueryHandler` needed no change — Quick
Payment/Receipt's Angular form simply never calls it (there's no specific Invoice/PurchaseBill to
suggest against when Quick Payment/Receipt doesn't force one).

### Decision #2 — JournalVoucher as an allocation source: out of scope

`PaymentAllocation` stays hard-FK'd to `PaymentId` only. A JournalVoucher line never becomes an
allocatable credit this phase.

**Why:** `JournalVoucherLine` carries no `ContactId` today, and generalizing `PaymentAllocation` to
a polymorphic `(SourceType, SourceId)` pair plus adding `ContactId` to `JournalVoucherLine` is a
real schema-and-posting-rule refactor — bigger than this phase's other four surfaces combined, and
orthogonal to unblocking Quick Payment/Receipt or the Allocate screens (which only need
Payment-sourced credits: Customer/Supplier/Quick Payment/Quick Receipt, all the same `Payment`
aggregate already). Flagged via `spawn_task` per the Phase 16d discipline for stating scope cuts
explicitly rather than leaving them silently absent.

**How to apply:** The Allocate Customer/Supplier Payment screens' backend query (decision #8) only
ever lists `Payment` rows. If a later phase generalizes this, it's additive to that query, not a
rewrite.

**Addendum (same-day follow-up session) — implemented.** The scope cut above held for the rest of
Phase 17's build, but was picked up immediately afterward in the same branch, once the Allocate
screens existed to plug into. `PaymentAllocation.PaymentId` became `SourceType`/`SourceId`
(`DocumentType`-discriminated, mirroring `TargetDocumentType`/`TargetDocumentId` exactly) --
`SourceId` is the `Payment`'s own Id for `SourceType=Payment`, or the contributing
`JournalVoucherLine`'s own Id for `SourceType=JournalVoucher` (a JV can have more than one
Contact-tagged line). `JournalVoucherLine` gained a nullable `ContactId`; `JournalVoucher.AddLine`
takes an optional `contactId` param.

Real engineering cost, as predicted: `PaymentAllocation.SourceId` can no longer carry a DB-level FK
constraint (it names rows in two different tables depending on `SourceType`), so `Payment.Allocations`
stopped being an EF-navigable `Include()`-able child collection -- `PaymentConfiguration` now
`Ignore()`s it, and every handler that used to `.Include(x => x.Allocations)` (ApprovePayment,
ApplyPaymentAllocation, GetPayment, UpdatePayment) now queries `PaymentAllocations` directly by
`(SourceType=Payment, SourceId=payment.Id)` and calls a new `Payment.AttachAllocations(...)`
hydration method (explicitly documented as DB-load plumbing, not a domain action) before any
invariant-checking call. `CreatePaymentCommandHandler` needed an explicit
`db.PaymentAllocations.AddRange(payment.Allocations)` it didn't need before (no more
graph-cascade-add via the removed navigation). The scaffolded migration correctly emitted **no** FK
constraint for the renamed `SourceId` column (EF only generates one when a `HasOne/WithMany`
relationship is configured, and none is anymore) -- confirmed by reading the generated migration
before applying it, this codebase's own standing discipline. The one hand-fix needed: the
scaffolded `SourceType` column's backfill `defaultValue: ""` (same class of bug as this phase's own
`Kind` column fix) -- corrected to `"Payment"`, since every pre-existing row necessarily was
Payment-sourced, and verified via `sqlcmd` post-apply (all 7 existing rows backfilled correctly).

`ListAllocatablePaymentsQuery` now unions two credit sources (two separate DB round-trips merged in
memory, then paginated via `ToPagedResult` -- the same in-memory-list-pagination precedent Phase
16c's report queries established, not a single correlated-subquery SQL `UNION`, to avoid the EF
LINQ-translation risk that pattern carries in this codebase): Approved Payments (unchanged), and
Approved JournalVouchers' own Contact-tagged lines with a nonzero amount on the side that reduces
that Contact's control account -- Customer/AR on Credit, Supplier/AP on Debit (**not live-confirmed
against Tigg**, same "safe default" caveat as decision #4; flagged for future verification).
`ApplyPaymentAllocationCommand` gained `SourceType`/`SourceId`/`ParentDocumentId` (the last only
meaningful for `SourceType=JournalVoucher` -- `LockDateBehavior` resolves a JournalVoucher's Date
from the *voucher's* own Id, not a line's, so the command carries the parent explicitly rather than
making the pipeline behavior infer it). The endpoint moved from `POST /payments/{id}/allocate` to
`POST /payment-allocations/apply` (no longer nested under a Payment id, since the source can now be
either type).

**Known limitation (addendum, not fixed).** Four pre-existing "how much of this target document has
already been allocated" consumers -- `ContactAgeingSummaryQueryHandler`, `VoidInvoiceCommandHandler`'s
and `VoidPurchaseBillCommandHandler`'s void-guards, and `GetDefaultPaymentAllocationsQueryHandler` --
were updated only to keep compiling against the renamed `SourceId` column, explicitly re-scoped to
`SourceType == Payment` to preserve their exact pre-addendum behavior. None of them fold in
JournalVoucher-sourced allocations yet: a JV-sourced credit applied against an Invoice won't show up
in Ageing, won't block a premature Void of that Invoice, and won't factor into the FIFO-suggestion
query's "already allocated" figure. Flagged via `spawn_task` rather than left silently absent, same
discipline as the original decision #2 cut -- each of the four is a mechanical extension of the same
"union Payment + JournalVoucher, filtered by Approved status" pattern `ListAllocatablePaymentsQuery`
now uses, not a redesign.

Manual E2E (live browser, Phase17 Test Co org): created a JournalVoucher (Cash Dr 150 / Accounts
Receivable Cr 150, AR line tagged Contact "Acme Traders"), Approved it, confirmed it appeared on the
Allocate Customer Payment screen's Unallocated tab as a `Journal Voucher`-typed row (alongside the
pre-existing `Payment`-typed rows) with the correct Amount/Balance, applied 150 of it against a real
Approved Invoice, watched it move to the Allocated tab with Balance 0.00, and cross-checked via
`sqlcmd` that the resulting `PaymentAllocations` row has `SourceType='JournalVoucher'` and
`SourceId` equal to the contributing line's own Id (not the parent voucher's). Also re-verified the
pre-existing Payment-sourced apply path still works unchanged (`Payment` row, `SourceType='Payment'`).
Backend: Domain.UnitTests 112 (+3: `AddLine_carries_an_optional_contact_id`,
`AddAllocation_creates_a_payment_sourced_allocation`,
`AttachAllocations_replaces_the_in_memory_collection`), Application.UnitTests 231 (+6: two new
`ApplyPaymentAllocationCommandHandler` JournalVoucher-source tests, four new
`ListAllocatablePaymentsQueryHandlerTests`). `ng build`/`tsc --noEmit`/`ng test` all clean (7 specs
unchanged). Fixed one pre-existing `[value]`-on-a-signal-fed-`<select>` bug noticed in
`allocate-payment-page.html`'s target-document picker while touching that file (the exact gotcha
CLAUDE.md flags) -- switched to the `[selected]`-per-`<option>` pattern.

### Decision #3 — Bank/Cash marker on Account: `AccountKind` enum, Bank vs Cash only (no Wallet)

Live-confirmed via Tigg's "New Bank Account" dialog (Accounting → Bank Accounts → + Add New): the
"Type of Account" control is a strict two-way toggle, **Bank** or **Cash** — there is no third
"Wallet" option anywhere in account creation. E-wallets (E-sewa, Khalti) are Bank-kind accounts
whose "Select Bank" institution happens to be a wallet provider, not a distinct account kind. Also
confirmed: Bank-kind accounts get a required "Select Bank" (institution) picker, an optional
Account Number, and a numbering-pool code (`BA0055` was the next code offered live); Cash-kind
accounts skip Select Bank/Account Number entirely and get a `BC00xx` code. The Opening Balance
screen's own "Account Type" column independently corroborated the same three-way split this
codebase cares about: `Normal` (existing accounts, unaffected), `Bank`, `Cash` (Tigg's own extra
`Customer`/`Supplier` account-type rows are that product's per-contact sub-ledger design, which
this codebase deliberately doesn't replicate — Contacts are a separate entity here, not Account
rows).

**Decision:** `Account` gets `AccountKind` (`Other = 0` default, `Bank`, `Cash`) — `Other` is the
CLR default so no `.ValueGeneratedNever()` gotcha (see CLAUDE.md's enum-default gotcha) — plus a
nullable `BankId` (FK to the new `Bank` lookup, populated when `Kind == Bank`) and a nullable
`AccountNumber` string (confirmed live: shown under some but not all bank cards on the Home
dashboard, e.g. "NMB Bank Ltd. / x14"). Skipped: Tigg's "Bank Info" sub-form's separate
Savings/Current "Account Type" field and its redundant secondary "Account Name" field — outside
this phase's exit criteria (card grid + live balance), and adding it later is additive, not a
migration risk.

### Decision #4 — Cheque bounce → GL impact: safe default, not live-confirmed

Live-confirmed the real status set (Cheque → Edit dialog's Status dropdown): **Pending, Deposited,
Cleared, Bounced, Cancelled** — a flat field editable directly, not the roadmap's guessed linear
`Issued/Received → Presented → Cleared/Bounced` pipeline. What the dropdown does **not** reveal is
whether flipping to Bounced auto-reverses GL, and actually setting a real cheque in the shared UAT
tenant to Bounced to observe the side effect would be a live data mutation on the user's reference
tenant with no clean rollback — not done.

**Decision (the brief's own documented fallback):** Bounced marks the Cheque (and, via
`LinkedPaymentId`, flags the Payment) for follow-up only. No automatic GL reversal on Bounced. An
actual GL reversal happens only through the Payment's own existing Void action (`GlJournalEntry.
PostReversalOf`, the Phase 16a mechanism) — the user explicitly voids the Payment once a bounce is
confirmed, rather than the system inferring a reversal from a status flip. Recorded as an assumption
that should be live-verified in a future phase if it turns out wrong.

**How to apply:** `TransitionChequeStatus` command changes `Cheque.Status` only — no GL code path
attached to any transition, including Bounced. `Payment.Void` stays the only GL-reversing action a
Cheque-linked Payment can trigger, unchanged from Phase 16a.

### Decision #5 — Cheque numbering: user-entered, not system-generated

Live-confirmed: the "Edit Received Cheque" panel has a plain required "Cheque Number" text field,
same value as the physical cheque leaf (e.g. `46657575`). No system-generated code, no
`IDocumentNumberGenerator(DocumentType.Cheque)` entry needed. `DocumentType` enum is left
unchanged (no new `Cheque` member) — a Cheque isn't itself an `ApprovableTransaction` numbering-pool
document; it's a child record of `Payment` the same way `PaymentAllocation` is, keyed by its own
`Id`, with `ChequeNo` as a plain user-entered string.

### Decision #6 — PaymentMode ↔ Cheque linkage: `RequiresChequeDetails` flag

Confirmed structurally (not a UI toggle visible on `PaymentMode` itself, but the mechanism is the
cleanest fit for how Cheque creation actually attaches to a Payment): add `bool
RequiresChequeDetails` to `PaymentMode`, defaulting `false` for every existing mode and set `true`
only for a mode literally named "Cheque" (seeded/renamed by the tenant admin, same as any other
`PaymentMode` row — no hardcoded name matching in application code). `CreatePaymentCommand`
gains an optional nested Cheque details payload (ChequeNo, BankAccountId, ChequeDate) that the
handler uses to create a linked `Cheque` row when `PaymentModeId` resolves to a mode with
`RequiresChequeDetails == true`. This avoids the fragile name-matching the roadmap flagged as a
risk, at the cost of one new boolean column.

### Decision #7 — Quick Payment/Quick Receipt: thin variant of the existing Payment aggregate, own Angular routes

Live-confirmed a real mismatch with the roadmap's assumption. Tigg's actual "New Quick Receipt"
form (`#/accounting/payments-received/add`) is **not** a lighter Customer-Payment form — it's a
generic document with a single "Deposited To" account and a multi-line "Accounts" table (Select
Account + Amount per line), where "Select Account" is a full Chart-of-Accounts picker (any account,
including per-Contact Customer/Supplier ledger accounts — confirmed by typing a customer's name
into that picker and getting it back as a selectable account row). Tigg's Quick Receipt **list**
view's "Received From" column is derived post-hoc from whichever line happened to reference a
Customer-type account, not a first-class field on the document.

This codebase has no per-Contact ledger sub-accounts (Contacts and Accounts are separate entities
by design — see architecture-spec.md), so porting Tigg's actual shape would require the same kind
of `PaymentAllocation`-polymorphism-scale refactor ruled out of scope in decision #2. Instead:
Quick Payment/Quick Receipt here is `CreatePaymentCommand`/`ApprovePaymentCommand` with
`Allocations = []` — same `Payment` aggregate, same permission keys
(`PaymentCreate`/`PaymentEdit`/`PaymentApprove`/`PaymentVoid`), no new `DocumentType`. `ContactId`
stays non-nullable — live-confirmed Tigg's own Quick Receipt list still names a real contact
("Received From"), so a real Contact is expected UX even for a "quick" entry, matching this
codebase's existing `Payment.ContactId` requirement exactly (no domain change needed).

**Angular shape:** own routes/components (`quick-payment-page`, `quick-receipt-page`), not a mode
flag bolted onto the existing `payment-detail-page`. Reason: the existing form's `canApprove()`
hard-requires `remaining() === 0` (client-side gate, `payment-detail-page.ts:89-90`) and always
fetches `GetDefaultPaymentAllocationsQuery` FIFO suggestions on Contact selection — both wrong for
Quick Payment/Receipt. A new lightweight component (Contact, Date, PaymentMode incl. Cheque
details per decision #6, Account, Amount, Reference, Approve with zero allocations) is cleaner than
threading a "skip allocation" branch through the existing component's suggestion-fetch and
approve-gate logic.

### Decision #8 — Allocate screen backend: new query under `Application.Payments`

`ListAllocatablePaymentsQuery` (or similar), alongside the existing
`GetDefaultPaymentAllocationsQuery` folder — same bounded context, same `Payment`/`PaymentAllocation`
tables. Lists Approved Payments where `Allocations.Sum(Amount) < Amount`, scoped by
`OrganizationId` + `Direction` (Customer screen = Received, Supplier screen = Paid) + optional
`ContactId` filter, with an `Allocated`/`Unallocated` tab implemented as a status filter
(`Allocations.Sum(Amount) == 0` → Unallocated tab; `> 0 && < Amount` → still shows in Unallocated
per the confirmed live column shape which tracks *remaining* balance, not a binary flag) rather
than two separate queries.

## What shipped

**Backend.** `Payment.Approve()` relaxed to `Allocations.Sum(Amount) <= Amount` (decision #1), plus a
new `Payment.AllocateFurther()` for applying more of an already-Approved Payment's remaining
balance (the Allocate screens' own write path — a natural consequence of decision #1 not
anticipated until implementation). `AccountKind` (Other/Bank/Cash) + nullable `BankId`/
`AccountNumber` on `Account`; new `Bank` lookup entity; `ListBankAccountsQuery` computing live
balances the same way `TrialBalanceQueryHandler` does (GL sum, no cutoff). New `Cheque` aggregate
(`Domain.Payments`) with a 5-state status and an explicit allowed-transition table (Pending →
Deposited → Cleared/Bounced/Cancelled); `PaymentMode.RequiresChequeDetails` flag drives
`CreatePaymentCommand`/`UpdatePaymentCommand` to create/update a linked Cheque. New
`OpeningBalanceLine` (Accounting) and `OpeningStockLine` (Inventory) entities — no lifecycle, a
single save posts a real GL entry (auto-provisioned "Opening Balance Equity" contra account) or a
real FIFO layer (`IStockLedgerService.IncrementAsync`) immediately; correcting an existing line
reverses the prior posting first (`GlJournalEntry.PostReversalOf` / `IStockLedgerService.
ReverseIncrementAsync`) rather than netting by hand. New `ListAllocatablePaymentsQuery`/
`ApplyPaymentAllocationCommand` for the Allocate screens, scoped to Payment-sourced credits only
(decision #2). Two new `DocumentType` members (`OpeningBalance`, `OpeningStock`), 8 new permission
keys (`Configuration.Bank.*`, `Accounting.BankAccount.View`, `Accounting.Cheque.*`, `Accounting.
OpeningBalance.*`) with seed rows continuing the GUID tail past `...ff` into `...0100`-`...010c`.
One migration (`Phase17AccountingBreadth`) — hand-corrected before applying: the scaffolded `Kind`
column's `defaultValue: ""` didn't match the enum's `Other` string representation (would have
produced unparseable data for the 112 pre-existing Accounts); fixed to `"Other"` and verified via
sqlcmd that every existing row backfilled correctly.

**Frontend.** Five screens: `bank-account-list-page` (card grid, live balances, All/Inactive tabs,
inline Add New with a Bank/Cash toggle), `cheque-register-page` (Dashboard/Received/Issued tabs,
period+contact filters, inline status-transition dropdown), `quick-payment-page` (one component
parameterized by route `data.direction`, serves both Quick Payment and Quick Receipt — see decision
#7), `allocate-payment-page` (one component parameterized the same way, serves both Allocate
Customer/Supplier Payment — Unallocated/Allocated tabs, inline per-row Apply form), and
`opening-balances-page` (Account/Product tabs under Configurations). New `bank-list-page`
(Configuration lookup CRUD, same shape as Payment Modes) since Bank-kind Accounts need a
populated "Select Bank" picker. `payment-mode-list-page` extended with the `requiresChequeDetails`
checkbox. All models/services added to the existing `core/accounting`, `core/configuration`,
`core/payments`, `core/inventory` modules rather than new ones, matching the established
per-bounded-context service pattern.

**Manual E2E** (fresh Organization, curl + cookie jar, then live in the browser): Quick Receipt
created and approved with zero allocations; Bank Account balance verified against an independent
`sqlcmd` GL sum; Opening Balance set then corrected (60000 → confirmed net, not summed) with the
Bank Account/Trial Balance/`sqlcmd` all agreeing; Opening Stock set and confirmed in Stock Position
with zero query changes to that report; a Cheque created via a Cheque-mode Payment, transitioned
Pending → Deposited → Bounced with the Bank Account balance unchanged throughout (decision #4) and
the Bounced→Deposited terminal-state guard rejected with a 409; a real allocation applied via the
Allocate screen moved a Payment from Unallocated to Allocated and the target Invoice's balance
dropped by exactly that amount (cross-checked via Customer Statement); every new endpoint's 403
independently confirmed to name its own exact permission key; the "already consumed by a later
document" guard on Opening Stock correction fired live and correctly (attempted to shrink stock
already partly consumed by an Invoice created for allocation testing) with no partial-write damage
(`sqlcmd`-verified the failed transaction left zero trace). All backend suites green: Domain.
UnitTests 109 (was 76: +7 Payment, +3 AccountTests, +5 ChequeTests, +4 OpeningBalanceLine/
OpeningStockLine), Application.UnitTests 225 (was 216: +9 across
CreateOrUpdateOpeningBalanceLineCommandHandlerTests, ApplyPaymentAllocationCommandHandlerTests,
CreateOrUpdateOpeningStockLineCommandHandlerTests). Angular: 7 specs unchanged, `ng build`/`tsc
--noEmit` clean. Api.IntegrationTests (Testcontainers) not re-run this session — Docker Desktop
wasn't running locally, same gap phase-16d recorded.

## Bugs hit and fixed

1. **`ApplyPaymentAllocationCommandHandler` let a domain `InvalidOperationException` reach the
   client as a raw 500 instead of a 409.** The handler called `Payment.AllocateFurther` without the
   same handler-level pre-check every other command in this codebase uses (see the `Payment.
   Approve`/`UpdateTaskStatusCommandHandler` precedent) — over-allocating threw
   `InvalidOperationException` straight out of the domain call. Caught by the very first
   `ApplyPaymentAllocationCommandHandlerTests` run (`Handle_throws_when_allocation_would_exceed_
   the_payments_amount` failed with the wrong exception type). Fixed by adding the same
   sum-plus-new-amount pre-check other Approve/status-transition handlers use, before calling the
   domain method.

2. **`computed()` reading a `FormControl.value` never re-evaluates in this app — it's zoneless
   (Angular 21 default, confirmed via no `zone.js` in `package.json`).** `quick-payment-page`
   wrapped `selectedPaymentMode` in a `computed(() => this.paymentModes().find(m => m.id ===
   this.form.controls.paymentModeId.value))`. `computed()` only re-runs when a tracked *signal* it
   read last time changes; `FormControl.value` is a plain property, not a signal, so the computed
   silently cached its first (empty) result forever — selecting "Cheque" never revealed the Cheque
   Details fields. Direct template reads of `form.controls.x.value` (not wrapped in `computed()`,
   e.g. the Bank Accounts Add-New form's Bank/Cash toggle) are unaffected — zoneless CD still
   reruns the template function itself after an Angular-bound `(change)` event, so a plain
   (uncached) read comes back fresh. Only caught by live browser testing — `tsc`/`ng build`
   cannot see this class of bug. Fixed by tracking the selected id in its own plain `signal()`,
   written directly by the `<select>`'s `(change)` handler, instead of deriving it from the
   FormGroup. See the new memory note on this — likely to recur in any future phase that pairs
   Reactive Forms with a `computed()`.

3. **Quick Payment/Receipt's success message showed the Draft-time code (`"DRAFT"`) instead of the
   real Approved code.** `create(...).subscribe({ next: (created) => { approve(...).subscribe({
   next: () => successMessage.set(...created.code...) }) } })` closed over `created` (the Create
   response, whose `code` is always the literal `"DRAFT"` placeholder — document numbers are
   assigned at Approve, not Create, this codebase-wide) instead of the Approve response's own
   `code`. Only visible live in the browser ("Quick Receipt DRAFT approved." instead of "Quick
   Receipt 0004 approved."). Fixed by naming the Approve callback's parameter and reading `.code`
   from it instead.

## Known limitation (not fixed this phase, not a regression)

The Allocate screen's inline Apply form pre-fills Amount with the *source* Payment's own remaining
Balance, but neither the client nor `ApplyPaymentAllocationCommand`/`Payment.AllocateFurther` caps
the entered amount against the *target* document's own remaining outstanding balance — only against
the payment's own total. This gap already existed in the original `CreatePaymentCommand`/
`UpdatePaymentCommand` allocation path before this phase (a hand-typed allocation there isn't
capped against target outstanding either; only the FIFO-suggestion query computes a sane default).
Not fixed here since it predates Phase 17 and wasn't part of its scope — flagged here rather than
left silently unstated.

---

## Addendum (Phase 22) — Quick Payment/Receipt is now Draft-then-Approve

Decision #7 shipped this screen as **one action**: `createPayment` then, in its own success callback,
`approvePayment`, then a form reset. That was a reasonable read of "Quick" for a screen a person types
by hand from a receipt in front of them.

**Phase 22 changed it**, because the Document inbox can now pre-fill this exact form from a scanned
bill whose values a model suggested. One click posting straight to the General Ledger, from suggested
values, with no review step, is the one place in the product where "check it before you save" had no
second chance. It now saves a **Draft** and offers **Approve** as a separate action — matching every
other document type, and putting the Draft in the Transaction Approval queue where a second person can
approve it.

Two details worth keeping, both of which are really decision #7's own reasoning coming back around:

- **Both steps stay on `quick-payment-page`.** Navigating the Draft to `payment-detail-page` was the
  obvious move and is wrong: that page's `canApprove()` requires
  `allocations.length > 0 && remaining === 0`, so a zero-allocation Quick Payment would arrive with a
  permanently disabled Approve button. That gate is exactly why decision #7 gave this screen its own
  component in the first place.
- **The approved code is read off the `approve` response**, never the `create` one. That is this
  phase's own bug #3 restated — numbering happens at Approve, so `created.code` is still `"DRAFT"` —
  and the two-step split makes it easier to get wrong, not harder, because the two responses are now
  handled in different methods.

See `docs/phase-22-status.md`, Decision B.

