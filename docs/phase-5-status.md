# Phase 5 status — Sales chain

**Status: COMPLETE.** `Quotation` → `Invoice` → Customer `Payment` is live end-to-end, matching
the hands-on pass documented in `erp-module-scan.md`. `Invoice` is the first real use of
`IGlPostingRule<TDocument>` for a non-`JournalVoucher`/`CashTransfer` type, and the first
aggregate in this codebase with a required `WarehouseId`. `GetInvoiceConversionTemplateQuery`
implements architecture-spec.md §3.3's document-conversion pattern for the first time
(Quotation→Invoice), reused a second time for Invoice→CreditNote. `Warehouse` (a minimal
single-column lookup) and `TenantSettings`' three Phase-5 default-GL-account fields close the gaps
Invoice's GL posting needed. As a stretch goal beyond the roadmap's core 1–4, `SalesOrder` and
`CreditNote` were also built full-stack on the backend; `CreditNote`'s Angular UI shipped too
(as the Invoice-conversion target), `SalesOrder`'s Angular UI was deliberately deferred (see scope
decisions).

Confirmed by hand end-to-end against the real API/DB: a fresh Admin can set up a Chart of Accounts
(Cash in Hand, Accounts Receivable, VAT Payable, Sales Revenue), a Warehouse, Accounting Defaults
(Default Sales/AR/VAT accounts), a Customer, and a 13%-VAT Product; create a `Quotation`, Approve
it (real number assigned, no GL/stock side effect); click "Convert to Invoice" (server pre-fills
Customer/Lines/Reference from the Approved Quotation), pick a Warehouse, Approve the `Invoice`
(real number assigned, GL posts Debit AR / Credit Sales Revenue / Credit VAT Payable, balanced);
create a `Payment` against that Customer, click "Suggest (FIFO)" (auto-fills the allocation
against the Approved Invoice), preview the GL posting before saving, Approve (real number
assigned, GL posts Debit Cash-in-Hand / Credit AR, balanced, allocation recorded); click "Convert
to Credit Note" on the Invoice (server pre-fills from the Approved Invoice), Approve the
`CreditNote` (GL posts the exact reverse of the Invoice's posting, balanced).

## Roadmap Phase 5 exit criteria — final status

- [x] `Quotation` aggregate + CRUD/Approve — `ApprovableTransaction` shape matching
      `JournalVoucher`'s precedent, no GL/stock side effect on Approve (confirmed live, same as
      `PurchaseOrder`'s "planning document" note)
- [x] `Invoice` aggregate + CRUD/Approve — first real use of `IGlPostingRule` for a
      non-`JournalVoucher` type, first `WarehouseId` requirement, `StockAvailabilityPolicy` stub
      (`AlwaysOkStockAvailabilityPolicy`) wired but inert per the roadmap's own sequencing
      recommendation (a)
- [x] `GetInvoiceConversionTemplateQuery` + Angular "Convert to Invoice" flow — server-computed
      pre-fill, `ReferrerType`/`ReferrerId` set on the new Invoice, ordinary `CreateInvoiceCommand`
      POST afterward (no separate conversion command/audit trail)
- [x] `Payment` (Direction=Received) + `PaymentAllocation` — FIFO-defaulted allocation suggestion
      (`GetDefaultPaymentAllocationsQuery`), manually overridable, live GL-preview-before-approve
      behavior confirmed in the hands-on pass
- [x] `SalesOrder` + `CreditNote` — built as the stretch slice (see scope decisions below for what
      shipped vs. what was deliberately deferred)
- [x] Permission keys: `Sales.Quotation.{View,Create,Edit,Approve}`, `Sales.Invoice.{...}`,
      `Sales.SalesOrder.{...}`, `Sales.CreditNote.{...}`, `Payments.Payment.{...}`, continuing
      Phase 4's maker-checker seed pattern (Admin all four, Member View+Create+Edit only)
- [x] Angular: Quotation/Invoice/Payment/CreditNote create/list/detail, cloning
      `journal-voucher-detail-page`'s chrome (Product picker + Qty/Rate/VAT columns, running
      Line/VAT/Grand Total, two-step Draft-save vs Approve, read-only GL Transactions section)
- [x] `dotnet build`, `dotnet test` (67 Domain + 87 Application, all still green — no new Phase 5
      unit tests were added this phase, see scope decisions), `Api.IntegrationTests` not run this
      pass (Docker Desktop wasn't started — see "What's next"), `ng build`, `ng test --watch=false`
      (7 tests, all still green) all pass
- [x] Manual E2E against real API/DB (see summary above) — reproduces the roadmap's own exit
      criteria: Quotation approved, converted to Invoice, Invoice approved (GL posted), Payment
      recorded and approved (GL posted, allocation applied), plus the CreditNote reversal

## Scope decisions

1. **TenantSettings-level fallback for Invoice/CreditNote's default GL accounts, not a
   per-Product-mandatory requirement.** `Product.SalesAccountId` (Phase 4 backfill, still commonly
   unset right after onboarding) is checked first; `TenantSettings.DefaultSalesAccountId` is the
   fallback. `DefaultAccountsReceivableId`/`DefaultVatPayableAccountId` have no per-Product
   equivalent at all (AR/VAT are tenant-wide control accounts, not product-specific) — both come
   from `TenantSettings` only. Approve fails with a friendly `ConflictException` (409, not a Domain
   `InvalidOperationException`/500) naming exactly which account is missing, resolved once by
   `InvoiceAccountResolver`/`CreditNoteAccountResolver` and reused by both the real Approve handler
   and the `PreviewGlPosting` query handler (no duplicated resolution logic, matching
   architecture-spec.md §3.4's "no duplicated debit/credit math" rule). A minimal Angular
   "Accounting Defaults" page (`accounting/defaults`) is the only way to set the three fields this
   phase — not a full `TenantSettings` editor (that stays deferred, unchanged from Phase 2's own
   note).
2. **`InvoicePostingRule`/`CreditNotePostingRule` take a resolved `*PostingInput` record, not the
   aggregate itself** — a deliberate departure from `JournalVoucherPostingRule`/
   `CashTransferPostingRule`'s shape (`IGlPostingRule<JournalVoucher>` etc.), where the document's
   own `Lines` already *are* its GL lines with nothing to resolve. An Invoice's GL lines need
   external Account lookups (Product/TenantSettings) that `IGlPostingRule.BuildLines`'s "no I/O"
   contract forbids performing inline — `InvoiceAccountResolver`/`CreditNoteAccountResolver` do
   that I/O once, up front, and hand back a plain, already-resolved record the rule computes
   against purely. `CreditNotePostingRule` is registered as a distinct `IGlPostingRule<
   CreditNotePostingInput>` (not reusing Invoice's registration) purely so DI can register two
   different rules; `CreditNoteAccountResolver` internally delegates to `InvoiceAccountResolver`
   rather than duplicating the resolution logic.
3. **`Warehouse` stays a minimal single-column lookup this phase** (`Id, OrganizationId, Name,
   IsActive, CreatedAt`, reusing the generic `ListLookupsQuery<TLookup>`/`DeleteLookupCommand
   <TLookup>` pair) — "build the seam, not the feature," per the roadmap brief's own explicit
   instruction. No address, default-location flag, or multi-warehouse stock-position UI.
4. **`Payment.Approve()` requires `Allocations` to sum to exactly `Amount`** — v1 doesn't model an
   unallocated "advance from customer" remainder. A Customer Payment for more than its allocated
   invoices' total isn't supported yet; the client's FIFO-suggest fills allocations until the
   Amount is exhausted, and manual entries must add up exactly, or Approve 409s with a clear
   message. Revisit once a real "customer credit/advance" concept exists.
5. **`SalesOrder` shipped backend-only this phase; `CreditNote` shipped full-stack (backend +
   Angular).** Both were originally the roadmap's stretch/second slice, built after 1–4 were
   confirmed working by hand. `CreditNote` got full Angular UI because it's the Invoice-conversion
   target (`GetCreditNoteConversionTemplateQuery` + a "Convert to Credit Note" button needed
   somewhere to land), so building its detail/list pages was effectively required to prove the
   conversion flow end-to-end anyway. `SalesOrder` is standalone (confirmed live: not a conversion
   source or target of anything, cross-referenced only via free-text Reference), so its Angular
   list/detail pages were cut to keep scope bounded — the full Application/Domain/Infrastructure/Api
   layer exists and is wired (`Commands/{Create,Update,Approve}SalesOrder`, `Queries/{Get,List}
   SalesOrders`, `sales.SalesOrders`/`SalesOrderLines` tables, `/sales-orders` endpoints,
   `Sales.SalesOrder.*` permission keys already seeded) — only the Angular screens are missing.
   Cloning `quotation-list-page`/`quotation-detail-page` (drop `ExpiryDate`, add `DeliveryDate`, no
   conversion buttons) is the next phase's fastest possible task if/when SalesOrder UI is wanted.
6. **No new unit tests added this phase.** Phase 4's precedent (`AccountingFlowTests`,
   `PreviewGlPostingQueryHandlerTests`, per-handler `Application.UnitTests`) wasn't repeated for
   Quotation/Invoice/Payment/CreditNote handlers — existing Domain (67) and Application (87) suites
   were re-run and confirmed still green (no regressions), but Phase 5's own handlers/domain
   methods are only covered by the manual E2E pass documented above. Worth backfilling before
   Phase 6 reuses `Payment`'s `Direction=Paid` path, so a regression there doesn't silently break
   Phase 5's `Direction=Received` behavior too.
7. **`Api.IntegrationTests` not run this phase** (Docker Desktop wasn't started during this build) —
   `dotnet build` and the InMemory-backed `Domain.UnitTests`/`Application.UnitTests` suites were
   confirmed green instead. Run `dotnet test ErpApp.slnx` with Docker Desktop running before
   merging to confirm the Testcontainers-backed SQL Server suite (FK enforcement, real
   `DocumentNumberGenerator` concurrency behavior) still passes against the new migration.
8. **Money precision**: `decimal(18,4)` for `Quantity`/`Rate`/`Amount`/`VatAmount`/`Payment.Amount`/
   `PaymentAllocation.Amount`, matching every prior phase's convention.
9. **FK delete behavior**: `Restrict` everywhere a Phase 5 document references another aggregate
   (`Contact`, `Product`, `Warehouse`, `Account`, `PaymentMode`), `Cascade` for aggregate-owned
   children (`Quotation`→`Lines`, `Invoice`→`Lines`, `SalesOrder`→`Lines`, `CreditNote`→`Lines`,
   `Payment`→`Allocations`) — same split every prior phase established.
   `PaymentAllocation.TargetDocumentType`/`TargetDocumentId` is a polymorphic reference (indexed,
   not FK-constrained), same precedent as `GlJournalEntry.SourceDocumentType`/`SourceDocumentId`.

## Bugs hit and fixed along the way

1. **A native `<select>`'s `[value]` property binding raced against its own `@for`-generated
   `<option>` children on a freshly-created row, silently defaulting to the first option instead of
   the bound value.** After reloading a saved Quotation, the VAT `<select>` visually (and in the
   DOM: `select.value`/`selectedIndex`) showed "NoVat" even though the line's actual `vatRate` was
   `ThirteenPercentVat` (confirmed correct end-to-end: the GET response body, the computed VAT
   Total of 390, and re-approving all used the correct value — only the `<select>`'s own displayed
   selection was wrong). Root cause: `@for (line of lines(); track line.key)` assigns every
   reloaded line a brand-new `key` (a module-level incrementing counter), so on `load()` Angular
   treats each row as an entirely new `@for` item and creates its whole subtree — `<select>` element
   plus its nested `@for`-generated `<option>`s — together in one pass. Angular applies a plain
   element's own property bindings (the `<select>`'s `[value]`) before finishing the child content
   from a nested structural directive, so `select.value = 'ThirteenPercentVat'` executes while the
   `<option>` elements still have no `value` attribute set, the browser can't match it to anything,
   and silently falls back to `selectedIndex = 0`. The Customer/Product `<select>`s at the same
   nesting level didn't show the bug because their options come from signals (`customers()`/
   `products()`) that had already resolved and rendered in an *earlier* change-detection cycle
   (from an unrelated subscribe in the constructor) by the time the line row was created — so their
   options already existed with real `value` attributes before the row-level bindings ran.
   **Fixed** by dropping the `[value]` binding on the `<select>` entirely and instead binding
   `[selected]="option === line.someField"` on each individual `<option>` — this makes the *correct*
   option mark itself selected as part of its own creation, independent of the parent `<select>`'s
   binding-evaluation order. Applied to every per-line/per-row `<select>` in
   `quotation-detail-page`, `invoice-detail-page`, `credit-note-detail-page` (Product + VAT
   selects) and `payment-detail-page` (the allocation row's Invoice select). Caught by hand during
   the manual E2E pass (screenshot showed "NoVat" selected with a VAT Total of 390, which is only
   possible if the underlying value was actually `ThirteenPercentVat` — worth remembering as the
   tell for this exact class of bug: computed totals and the visible `<select>` disagreeing after a
   reload). Not previously hit by `journal-voucher-detail-page`'s Account `<select>` for the same
   reason the Customer/Product selects above didn't show it — `accounts()` there is also populated
   by an earlier, unrelated subscribe.

## What's next

**Phase 6 — Purchase chain** (see `roadmap.md`): `PurchaseOrder` → `PurchaseBill` → Supplier
`Payment` (reusing this phase's `Payment` aggregate with `Direction=Paid` — the roadmap's own
"near-zero-new-code" expectation, though see scope decision #6 above about backfilling test
coverage first so a Phase 6 regression can't silently break Phase 5's `Direction=Received` path
too), `Expense`, `DebitNote`. Also the natural point to: run `Api.IntegrationTests` against this
phase's migration with Docker Desktop actually running (scope decision #7); decide whether
`SalesOrder`'s Angular UI (scope decision #5) is worth building now or stays cut; and reuse
`InvoiceAccountResolver`'s resolved-input-record pattern for `PurchaseBillPostingRule` if its GL
lines need the same kind of external Account resolution Invoice's did.
