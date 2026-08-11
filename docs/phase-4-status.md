# Phase 4 status — Accounting core

**Status: COMPLETE.** Chart of Accounts (`AccountGroup` tree + `Account` leaf) exists under a new
`accounting` schema, backed by real EF Core/SQL Server persistence. `JournalVoucher` is the first
real `ApprovableTransaction` in this codebase — Draft → Approve, with `IDocumentNumberGenerator`
called from `Approve()` (not `Create()`) for the first time. The shared GL posting engine
(`IGlPostingRule<TDocument>` + `GlJournalEntry.Post()`) is built and proven against both
`JournalVoucher` and `CashTransfer` (a simplified fan-out UI over the same posting path). Angular
gets its first "transactional document" chrome — a multi-line editable Debit/Credit table with a
live client-side Total/Difference indicator and an explicit two-step Draft-save vs Approve action
— which Sales/Purchase (Phase 5+) will clone. Confirmed by hand end-to-end: a fresh Admin can
create an `AccountGroup` for each of the 5 root types, an `Account` under one, a `JournalVoucher`
with 2 balanced lines (Draft → Approve, real number assigned, GL Transactions section populated),
and a `CashTransfer` with a 2-destination fan-out (Draft → Approve, GL posts as one balanced
Credit-the-source / Debit-each-destination entry) — all through the real UI against the real
API/DB.

## Roadmap Phase 4 exit criteria — final status

- [x] `AccountGroup` (tree, 5 root types) + `Account` (leaf), standard CRUD,
      `IRequirePermission`/`IOrganizationScoped` from the start
- [x] `JournalVoucher` aggregate — Draft/Approve lifecycle, `AddLine`, `Approve()` (assigns the
      real number via `IDocumentNumberGenerator`, posts GL, `sum(Debit)==sum(Credit)` enforced)
- [x] `IGlPostingRule<TDocument>` abstraction + shared `GlJournalEntry.Post()` factory (throws if
      unbalanced), built and tested against `JournalVoucher`; `PreviewGlPostingQuery` and
      `ApproveJournalVoucherCommandHandler` call the exact same rule instance type — no
      duplicated debit/credit math (proven by `PreviewGlPostingQueryHandlerTests`)
- [x] `CashTransfer` — simplified UI over JournalVoucher (`FromAccountId` + N `(ToAccountId,
      Amount)` fan-out lines), posts as one balanced multi-line GL entry through the same
      `IGlPostingRule`/`GlJournalEntry.Post()` path, not a parallel posting path
- [x] Angular: Journal Voucher create/list/detail — multi-line editable table, live Total/
      Difference, two-step Draft-save vs Approve, read-only GL Transactions section once Approved
- [x] `dotnet build`, `dotnet test` (67 Domain + 87 Application + 4 Api.IntegrationTests, all
      green), `ng build`, `ng test --watch=false` (7 tests, all green) all pass
- [x] Manual E2E against real API/DB: registered a fresh user, verified email, created an
      Organization, created an `AccountGroup` for each of Asset/Liability/Equity/Income/Expense,
      created two `Account`s (auto-numbered `0001`/`0002`), created a `JournalVoucher` with 2
      balanced lines (Cash in Hand Dr 1000 / Sales Revenue Cr 1000), saved Draft, Approved
      (assigned real number `0001`, GL Transactions section shows the same 2 balanced lines),
      created two more `Account`s, created a `CashTransfer` (Cash in Hand → Bank Account 300 +
      Petty Cash 200), saved Draft, Approved (assigned `0001`, GL Transactions shows Cash in Hand
      credited 500 / Bank Account and Petty Cash each debited their share) — every round-trip hit
      the real API/DB, confirmed via direct SQL query against the dev database

## Scope decisions

1. **Permission granularity split for the first time.** `AccountGroup`/`Account` stay on the
   existing `.View`/`.Manage` pair (simple master data, same treatment as Phase 2/3's taxonomy
   lookups). `JournalVoucher`/`CashTransfer` get the finer
   `Accounting.{DocType}.{View,Create,Edit,Approve}` split (architecture-spec.md §3.7) — the first
   use of the fuller matrix, introduced now rather than retrofitted once Sales/Purchase (Phase 5+)
   also need Approve as a distinct permission (retrofitting later would mean an additive migration
   touching every existing transactional permission key).
2. **Maker-checker seed data.** For `JournalVoucher`/`CashTransfer`, Admin is granted all four
   actions; Member is granted View+Create+Edit but **Approve is explicitly denied**
   (`IsGranted=false`) — the concrete cut architecture-spec.md §3.2's "Approve is a distinct
   permission" note calls for, now that a real Draft→Approve document type exists.
3. **`Status` enum carries a `Void` placeholder member, no `VoidCommand` built.** Matches the
   roadmap's own exit criteria (Draft→Approve→GL-post→view only) and the "build the seam, not the
   feature" precedent already used for `DocumentNumberingRule`.
4. **Multi-currency skipped entirely.** No `Currency`/`ExchangeRate` field on `JournalVoucher`/
   `CashTransfer` — `TenantSubscription.MultiCurrencyEnabled` stays unread until a real `Currency`
   aggregate exists in a later phase, per the roadmap brief's own sequencing recommendation.
5. **`RowVersion` added to `JournalVoucher`/`CashTransfer`** (`.IsRowVersion()`, matching
   `Organization`/`DocumentNumberingRule`'s precedent) since a Draft can plausibly sit open for a
   while before Approve — but, matching the fact that **no existing command in this codebase
   threads a client-supplied RowVersion through its DTO either**, Update/Approve commands don't
   accept one: optimistic concurrency protection is automatic from load-then-save within a single
   request. Added one `DbUpdateConcurrencyException → 409` mapping to `ExceptionHandling.cs` (a
   small, safe, additive change) so a genuine conflict surfaces as a clean error instead of a 500.
6. **Product's deferred Account FK columns backfilled, but not wired to any command yet.**
   `Product.SalesAccountId`/`SalesReturnAccountId`/`PurchaseAccountId`/`PurchaseReturnAccountId`
   (nullable FK to `Account`, `Restrict` delete) are added via `Product.SetAccounts(...)` — a real
   method, mapped in EF, migrated — but no `UpdateProductCommand` change or Angular UI exposes it
   this phase. Nothing reads these columns yet either; they're a clean seam for Phase 5+'s
   Sales/Purchase posting rules, added now specifically because `Account` didn't exist when Phase
   3 first deferred them.
7. **No shared `ApprovableTransaction` base class.** Matches this codebase's existing "no Entity/
   AggregateRoot base, plain sealed classes" convention (see `ITenantLookupEntity`'s doc comment)
   — `JournalVoucher`/`CashTransfer` each declare their own `Status`/`ApprovedByUserId`/
   `ApprovedAt` properties directly rather than inheriting a shared shape.
8. **`Update` commands replace the entire line set** (`ClearLines()` + re-`AddLine()` per
   submitted row) rather than diffing individual line changes — the simplest correct approach for
   a client-driven multi-line editable table that always resubmits its whole current state. See
   the bugs section below for the EF Core-specific gotcha this triggered.
9. **Money precision**: `decimal(18,4)` for `Debit`/`Credit`/`Amount`, matching Phase 3's
   established convention (no `ExchangeRate` field exists this phase, so the `decimal(18,6)` rule
   for it doesn't apply yet).
10. **Uniqueness**: `(OrganizationId, Name)` unique on `AccountGroup` and `Account` alike (Account
    additionally unique on `(OrganizationId, Code)`) — no separate uniqueness constraint on
    `JournalVoucher.Code`/`CashTransfer.Code` beyond what `IDocumentNumberGenerator` itself
    guarantees, matching Contact/Product's precedent.
11. **FK delete behavior: `Restrict` everywhere** a document references an `Account`
    (`JournalVoucherLine.AccountId`, `GlLine.AccountId`, `CashTransfer.FromAccountId`/
    `CashTransferLine.ToAccountId`, `Product`'s four new FK columns), **`Cascade`** for aggregate-
    owned children (`JournalVoucher`→`Lines`, `CashTransfer`→`Lines`, `GlJournalEntry`→`Lines`) —
    same split Phase 3 established.
12. **Angular's line-table state is a plain `signal<EditableLine[]>` array, not a `FormArray`.**
    This codebase has no `FormArray` precedent yet, and a signal array (`update()`-based immutable
    edits, matching the existing `items.set(...)` idiom used everywhere else) is simpler for a
    "client always resubmits the whole table" edit than wiring up dynamic reactive-forms controls.
13. **Debit/Credit are enforced mutually exclusive per line client-side** (typing into one clears
    the other) in addition to the server-side FluentValidation `(Debit > 0) ^ (Credit > 0)` rule
    and the Domain-level `JournalVoucher.AddLine` guard — three layers, but each catches a
    different class of bad input (UX nicety, malformed request shape, and defense-in-depth against
    anything that bypasses the first two).

## Bugs hit and fixed along the way

1. **EF Core InMemory provider mis-tracked a same-count Clear()+re-Add() on an encapsulated
   (private-backing-field) child collection, marking the wrong entities Modified/Deleted instead
   of Added/Deleted.** `UpdateJournalVoucherCommandHandler`'s "replace the whole line set" flow
   (`journalVoucher.ClearLines()` then `AddLine()` per submitted row) threw
   `DbUpdateConcurrencyException: Attempted to update or delete an entity that does not exist in
   the store.` A diagnostic test dumping `ChangeTracker.Entries()` before `SaveChangesAsync`
   showed 2 lines marked `Modified` and 2 marked `Deleted` — not the expected 2 `Added` + 2
   `Deleted` — meaning the change tracker's collection-diffing paired the brand-new line objects
   (fresh GUIDs, never persisted) with unrelated already-tracked entries instead of recognizing
   them as new. Reproduced even with a fresh `TestAppDbContext` instance per handler call (ruling
   out DbContext-instance reuse as the cause) and even with `.IsRequired().OnDelete(Cascade)`
   configured explicitly. **Fixed** by not relying on collection-navigation-triggered fixup at
   all: the handler now explicitly snapshots the old lines before mutating
   (`var oldLines = journalVoucher.Lines.ToList()`), then explicitly calls
   `db.JournalVoucherLines.RemoveRange(oldLines)` / `.AddRange(journalVoucher.Lines)` after
   `ClearLines()`+`AddLine()` — same defensive "add the new child to its own DbSet explicitly"
   precedent `AddSecondaryUnitCommandHandler` (Phase 3) already established for a *different*
   reason (there, because the parent wasn't `Include`d at all). Applied identically to
   `UpdateCashTransferCommandHandler`. Worth remembering for any future "client resubmits an
   entire encapsulated child collection" update handler.
2. **`TestAppDbContext.Create()` only ever opened a fresh randomly-named InMemory database, so a
   test driving two sequential handler calls (e.g. Create then Update) against "the same db"
   variable was actually reusing one DbContext *instance*, not simulating the real Api's
   one-DbContext-per-HTTP-request pattern.** Not itself the root cause of bug #1 above (the
   explicit RemoveRange/AddRange fix was still required even with fresh instances), but a gap
   worth closing regardless: added `TestAppDbContext.Create(string databaseName)` so a test can
   open multiple fresh `DbContext` instances against the same underlying InMemory database,
   matching production's actual DbContext lifetime much more closely. Used by
   `UpdateJournalVoucherCommandHandlerTests`/`UpdateCashTransferCommandHandlerTests`.
3. **TypeScript's overload resolution failed on `Observable<CreateXResult> | Observable<UpdateXResult>` when the two result shapes differ**, the same class of quirk CLAUDE.md's
   `HttpClient` `params` gotcha describes but here hitting `.subscribe()` on a ternary-assigned
   `request$: Observable<A> | Observable<B>` (`account-list-page.ts`, `account-group-list-page.ts`
   — `CreateAccountResult`/`UpdateAccountResult` and `CreateAccountGroupResult`/
   `UpdateAccountGroupResult` each differ by one field). `ng build` failed with `TS2349: This
   expression is not callable` pointing at `.subscribe()`, not at the real cause. **Fixed** by
   switching those two `save()` methods to an explicit `if (editingId) {...} else {...}` branch
   (the same shape `contact-detail-page.ts`'s `save()` already uses) instead of a shared
   `request$` variable — `journal-voucher`/`cash-transfer` detail pages didn't need this fix since
   their Create/Update result shapes are identical.
4. **`Api.IntegrationTests`' new `AccountingFlowTests` initially seeded an `OrganizationMembership`
   for a random `Guid` `_userId` with no backing `identity.Users` row**, which works fine against
   the InMemory-provider-backed `Application.UnitTests` (no FK enforcement) but failed against the
   real Testcontainers SQL Server with `FK_OrganizationMemberships_Users_UserId` violated. Fixed by
   seeding a real `User.Register(...)` row first and using its generated `Id` as `_userId` — a
   reminder that `Api.IntegrationTests`' real-SQL-Server backing enforces FKs Application-layer
   InMemory tests silently skip.

## What's next

**Phase 5 — Sales chain** (see `roadmap.md`): `Quotation` → `Invoice` (first real use of
`IGlPostingRule` for a non-`JournalVoucher` type, first `WarehouseId` requirement, stubbed
`StockAvailabilityPolicy` per the roadmap's own sequencing note) → `Payment` (Direction=Received),
reusing this phase's `IGlPostingRule<TDocument>`/`GlJournalEntry.Post()` engine and the Journal
Voucher Angular screens as the template for the new transactional-document chrome. Also the
natural point to circle back and actually wire `Product.SalesAccountId`/etc. into a real command
once Invoice's posting rule needs to read a Product's default GL accounts.
