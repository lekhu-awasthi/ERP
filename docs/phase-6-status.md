# Phase 6 status — Purchase chain

**Status: COMPLETE.** `PurchaseOrder` → `PurchaseBill` → Supplier `Payment` (Direction=Paid) is live
end-to-end, plus `Expense` (account-based lines, no Product) and `DebitNote` (full-stack, as the
PurchaseBill-conversion target), matching the hands-on pass's Purchase-side chain documented in
`erp-module-scan.md`. `PurchaseBillPostingRule`/`DebitNotePostingRule` are the first posting rules
with a TDS leg, and `DebitNotePostingRule` is a true mirror of `PurchaseBillPostingRule` (TDS
included) so a full reversal nets every account, including TDS Payable, back to zero (see bug #3 --
an earlier TDS-free DebitNote design shipped first and was caught and fixed after direct testing);
`Payment`'s `PaymentPostingRule`/`PaymentAccountResolver` gained a real `Direction` branch (Received
vs Paid), reused as-is rather than duplicated into a second command/handler set, confirming Phase
5's own "near-zero-new-code" prediction. `GetPurchaseBillConversionTemplateQuery`/
`GetDebitNoteConversionTemplateQuery` reuse architecture-spec.md §3.3's document-conversion pattern
for the third and fourth time.

Confirmed by hand end-to-end against the real API/DB: a fresh Admin can set up a Chart of Accounts
(Cash in Hand, VAT Receivable, Accounts Payable, TDS Payable, Purchase Expense), a Warehouse, a TDS
Type (1.5%), a Supplier, and a 13%-VAT Product; create a `PurchaseOrder`, Approve it (real number
assigned, no GL/stock side effect); click "Convert to Bill" (server pre-fills Supplier/Lines/
Reference from the Approved PurchaseOrder), pick a Warehouse and a TDS Type, preview the GL posting
before saving (Debit Purchase Expense/VAT Receivable, Credit TDS Payable/Accounts Payable, TDS
correctly reducing the AP credit), Approve the `PurchaseBill` (real number assigned, GL posts as
previewed, balanced); create a Supplier `Payment` against that Supplier, click "Suggest (FIFO)"
(auto-fills the allocation against the Approved PurchaseBill), preview the GL posting (Debit
Accounts Payable / Credit Cash-in-Hand, exact mirror of Customer Payment's posting), Approve (real
number assigned, allocation recorded); create and approve an `Expense` with TDS applied (GL posts
directly against its line Account plus AP/TDS Payable, balanced); click "Convert to Debit Note" on
the PurchaseBill, Approve the `DebitNote` (GL posts the exact reverse of the PurchaseBill, TDS
included -- a fresh PurchaseBill+DebitNote pair with 4.50 TDS confirmed by hand that the combined
net effect on Accounts Payable and TDS Payable is exactly zero across both documents).

## Roadmap Phase 6 exit criteria — final status

- [x] `PurchaseOrder` aggregate + CRUD/Approve — clones Quotation's `ApprovableTransaction` shape
      exactly, no GL/stock side effect on Approve (confirmed live, "No negative-stock validation
      triggered on PO approval")
- [x] `PurchaseBill` aggregate + CRUD/Approve — first real use of a TDS leg in an `IGlPostingRule`,
      `WarehouseId` required (same "first-required-on-stock-moving-documents" pattern Invoice
      established), `SupplierInvoiceReference`/`IsImport`+Import Details/`TdsTypeId`+`TdsAmount`/
      `ExpenditureClassification` per line all modeled
- [x] `GetPurchaseBillConversionTemplateQuery` + Angular "Convert to Bill" flow — same
      architecture-spec.md §3.3 pattern as Quotation→Invoice, confirmed live a second time on the
      Purchase side per the scan
- [x] `TdsType` Configuration lookup — reuses the generic `ListLookupsQuery<TLookup>`/
      `DeleteLookupCommand<TLookup>` pair, Code/Name/RatePct fields, no fiscal-year versioning
      (deliberately deferred, see scope decisions)
- [x] `Payment` reused with `Direction=Paid` (Supplier Payment) — near-zero-new-code as the roadmap
      predicted: no second command/handler pair, just a `Direction`-aware branch through
      `PaymentValidation`, `GetDefaultPaymentAllocationsQueryHandler`, `PaymentAccountResolver`/
      `PaymentPostingRule`, and the API/Angular request shapes
- [x] `Expense` aggregate + CRUD/Approve — account-based lines (no Product), its own
      `ExpensePostingRule` (simpler than `PurchaseBillPostingRule`, no per-line Product→Account
      resolution needed)
- [x] `DebitNote` aggregate + CRUD/Approve + `GetDebitNoteConversionTemplateQuery` — mirrors
      `CreditNote`'s shape plus its own `TdsTypeId`/`TdsAmount` (see scope decision #7), full
      Angular UI shipped (not cut, per the brief's explicit instruction since DebitNote is a
      conversion target); confirmed by hand that a full reversal nets Accounts Payable and TDS
      Payable to exactly zero across the PurchaseBill+DebitNote pair (see bug #3)
- [x] Permission keys: `Purchasing.PurchaseOrder.{View,Create,Edit,Approve}`,
      `Purchasing.PurchaseBill.{...}`, `Purchasing.Expense.{...}`, `Purchasing.DebitNote.{...}`,
      `Configuration.TdsType.{View,Manage}`, continuing Phase 5's maker-checker seed pattern (Admin
      all four, Member View+Create+Edit only). `Payments.Payment.*` reused as-is for Supplier
      Payment — no new permission rows needed (see scope decisions)
- [x] Angular: PurchaseOrder/PurchaseBill/Expense/DebitNote/SupplierPayment create/list/detail,
      cloning Phase 5's chrome; Accounting Defaults page extended with the four Purchase-side
      fallback accounts instead of a second settings page
- [x] `dotnet build`, `dotnet test` (67 Domain + 87 Application, all still green — no new Phase 6
      unit tests were added, see scope decisions) with Docker Desktop actually running this time
      (Api.IntegrationTests: 4 passed), `ng build`, `ng test --watch=false` (7 tests, all still
      green) all pass
- [x] Manual E2E against real API/DB (see summary above) — reproduces the roadmap's own exit
      criteria: PurchaseOrder approved, converted to PurchaseBill with TDS applied, PurchaseBill
      approved (GL posted, AP/Purchase/VAT/TDS balanced), Supplier Payment recorded and approved
      (GL posted, allocation applied), Expense created and approved (GL posted against its own line
      Accounts), PurchaseBill converted to DebitNote and approved (GL posts the exact reverse)

## Scope decisions

1. **TDS reduces the AP credit rather than posting as a separate line.** `PurchaseBillPostingRule`/
   `ExpensePostingRule` credit Accounts Payable for `grandTotal - TdsAmount` and separately credit
   TDS Payable for `TdsAmount` — the two credits still sum to `grandTotal`, keeping the entry
   balanced against the debit side (Purchase Account + VAT Receivable). The alternative considered
   was crediting AP for the full `grandTotal` and adding TDS as an unrelated third leg with no
   netting — rejected because withholding TDS genuinely means less cash will ultimately move to
   the supplier (the payable itself shrinks by the withheld amount), while a separate TDS Payable
   liability is owed to the government instead. This is also the shape that makes a Supplier
   Payment's allocation against the bill correctly total to the net (223 in the confirmed hands-on
   pass: 226 grand total − 3 TDS), not the gross.
2. **TDS base amount is the pre-VAT taxable amount** — `PurchasingValidation.ResolveTdsAmountAsync`
   computes `TdsAmount = TdsType.RatePct% × sum(Quantity×Rate)`, the same base every other tax
   computation in this codebase already uses (VAT is computed against the same pre-tax `Amount`).
   TDS is resolved server-side at Create/Update time (not just Approve) since the client needs to
   show the computed amount on the form before Approve is even possible — a DB read (fetching
   `TdsType.RatePct`) that Domain's "no I/O" aggregates can't perform themselves, so
   `PurchaseBill`/`Expense.Create`/`UpdateHeader` take an already-resolved `decimal tdsAmount`
   rather than a `TdsTypeId` alone.
3. **Purchase VAT gets its own `DefaultVatReceivableAccountId`, distinct from Sales'
   `DefaultVatPayableAccountId`.** These are genuinely different accounting concepts — output VAT
   collected on sales is a payable (liability) owed to the government, input VAT paid on purchases
   is a receivable (asset) representing a tax credit the tenant can claim back. Reusing Invoice's
   VAT Payable account for PurchaseBill's VAT leg would have been a silent accounting error, not a
   cosmetic shortcut. `TenantSettings` grew four new Purchase-side fields
   (`DefaultPurchaseAccountId`, `DefaultAccountsPayableId`, `DefaultVatReceivableAccountId`,
   `DefaultTdsPayableAccountId`) alongside Phase 5's three Sales-side ones, all edited from the
   same "Accounting Defaults" Angular page (extended, not duplicated) and the same
   `UpdateAccountingDefaultsCommand`/`GetAccountingDefaultsQuery` (extended to carry all seven
   fields in one round-trip).
4. **`Payments.Payment.*` stays a single shared permission set across `Direction=Received` and
   `Direction=Paid`, not split into `Sales.Payment.*`/`Purchasing.Payment.*`.** The aggregate,
   lifecycle (Draft→Approve, full-allocation requirement), and maker-checker story are identical
   between Customer and Supplier Payment — only the GL direction and the Contact/allocation-target
   type differ, both resolved from the Payment's own `Direction` field rather than from which
   permission the caller happened to hold. `CreatePaymentCommand` was parameterized with a
   client-supplied `Direction` instead of adding a second `CreateSupplierPaymentCommand` — safe
   specifically because the permission is shared, so a client picking one direction over the other
   isn't a privilege escalation. `PaymentValidation.EnsureContactExistsAsync` and
   `GetDefaultPaymentAllocationsQuery`/`PreviewPaymentGlPostingQuery` all gained a `Direction`
   parameter/branch instead. The Angular side still gives Supplier Payment its own
   `supplier-payment-detail-page` (cloned, not shared) since the Contact picker (Supplier vs
   Customer filter) and allocation-target labels ("Purchase Bill" vs "Invoice") genuinely differ
   enough to not be worth threading a mode flag through one shared component.
5. **`ListPaymentsQuery` no longer hardcodes `Direction == Received`.** Found while building the
   Supplier Payment list page (see bugs below) — this was a latent bug shipped in Phase 5 itself,
   invisible until Phase 6 introduced the first `Direction=Paid` rows. Fixed to list both
   directions; `payment-list-page`/`supplier-payment-list-page` each filter client-side by
   `direction` instead, since splitting the query itself would have meant adding a `Direction`
   query param nothing else needs (both pages already fetch a small per-tenant list, not a
   paginated one).
6. **`ExpensePostingRule` takes the resolved `ExpensePostingInput` record (not the `Expense`
   aggregate directly), despite being closer to `JournalVoucherPostingRule`'s "lines already are
   the GL lines" shape than `PurchaseBillPostingRule`'s.** An Expense line's Account is already
   known (no per-line Product→Account fallback resolution needed, unlike PurchaseBill), but the
   tenant's Accounts Payable/VAT Receivable/TDS Payable accounts still live on `TenantSettings`,
   not on the aggregate — that's still a DB read `IGlPostingRule`'s "no I/O" contract forbids
   inline. `ExpenseAccountResolver` is accordingly simpler than `PurchaseBillAccountResolver` (no
   per-line resolution loop) but the split itself is the same reasoning as Invoice's.
7. **`DebitNote` *does* carry its own `TdsTypeId`/`TdsAmount`, resolved server-side from its own
   lines** — reversed from an earlier version of this decision that deliberately left DebitNote
   TDS-free on the theory that "a reversal doesn't reverse the TDS withholding". That theory broke
   the books: `DebitNotePostingRule` still had to debit Accounts Payable for *something*, and
   without its own TDS figure it used the full grand total, while the source PurchaseBill had only
   ever credited AP net of TDS — a full reversal then left AP off by the TDS amount and TDS Payable
   permanently unresolved (see bug #3 below, caught by hand-testing exactly this scenario). Fixed
   by making `DebitNotePostingInput` an exact mirror of `PurchaseBillPostingInput` (TDS fields
   included) and `DebitNoteAccountResolver` delegate to `PurchaseBillAccountResolver` with the
   DebitNote's *own* `TdsAmount` (not the source bill's) — so a partial-quantity debit note
   correctly reverses only its proportional share, and a full reversal nets every account,
   including TDS Payable, back to exactly zero. `GetDebitNoteConversionTemplateQuery` pre-fills
   `TdsTypeId` from the source PurchaseBill (user-editable, same as every other conversion-template
   field), and `TdsAmount` is recomputed from the DebitNote's own lines via the same
   `PurchasingValidation.ResolveTdsAmountAsync` path PurchaseBill/Expense already use.
8. **`ExpenditureClassification` (Annex 13, Capital/Others) added speculatively per
   architecture-spec.md §4.5's explicit recommendation** — the scan never found this field's real
   UI location (still an open item), so no time was spent hunting for it; the field exists on
   `PurchaseBillLine` only (not `DebitNoteLine`, matching scope decision #7's "a reversal doesn't
   need its own classification" reasoning), defaulting to `Others`.
9. **No new unit tests added this phase**, continuing Phase 5's own precedent (see that phase's
   scope decision #6) — existing Domain (67) and Application (87) suites re-confirmed green, but
   Phase 6's handlers/domain methods are only covered by the manual E2E pass documented above.
   Unlike Phase 5, `Api.IntegrationTests` *was* run against the new migration this time with Docker
   Desktop actually running (4/4 passed) — the thing Phase 5 explicitly deferred and flagged as a
   "before merging" follow-up.
10. **Money precision**: `decimal(18,4)` for `Quantity`/`Rate`/`Amount`/`VatAmount`/`TdsAmount`/
    `Payment.Amount`, matching every prior phase's convention. `TdsType.RatePct` is
    `decimal(9,4)` (a percentage, not a money amount).
11. **FK delete behavior**: `Restrict` everywhere a Phase 6 document references another aggregate
    (`Contact`, `Product`, `Warehouse`, `Account`, `TdsType`), `Cascade` for aggregate-owned
    children (`PurchaseOrder`→`Lines`, `PurchaseBill`→`Lines`, `Expense`→`Lines`,
    `DebitNote`→`Lines`) — same split every prior phase established. New schema `purchasing` for
    `PurchaseOrder`/`PurchaseBill`/`Expense`/`DebitNote`; `TdsType` lives in the existing
    `configuration` schema alongside `CreditTerm`/`PaymentMode`.

## Bugs hit and fixed along the way

1. **A native `<select>`'s `[value]` binding raced against its own `@for`-generated `<option>`
   children a second way this phase — not the "freshly-created `@for` row" trigger Phase 5 found,
   but two independently-resolving async subscribes finishing in an order that left the binding
   stale.** `purchase-bill-detail-page`'s TDS Type `<select>` uses `[value]="tdsTypeId()"`, a
   static top-level select (not inside a per-line `@for` row) whose options come from
   `configurationService.listTdsTypes()` — the same shape as the Warehouse/Supplier selects that
   worked fine in Phase 5. But here the race triggers differently: `tdsTypeId()` gets set by
   `load()`'s `getPurchaseBill()` subscribe, and `tdsTypes()` gets set by an independent
   `listTdsTypes()` subscribe kicked off in the constructor — if `load()`'s response happened to
   resolve before `listTdsTypes()`'s (a real possibility, not a fixed ordering), Angular writes
   `select.value = '<tdsTypeId guid>'` while no matching `<option>` exists yet in the DOM, which
   the browser silently drops (falls back to the first/default option). Crucially, Angular's
   signal-based fine-grained reactivity means the later `tdsTypes()` signal update does **not**
   retrigger the earlier `[value]` binding (which only depends on the `tdsTypeId` signal, not
   `tdsTypes`) — so the mismatch never self-corrects once both signals have resolved. Confirmed via
   direct DOM inspection (`select.value` empty while the TDS Amount field, driven by the same
   loaded DTO, correctly showed 3.00) and via a raw API call proving the loaded `PurchaseBillDetailDto.tdsTypeId`
   was never null — ruling out a backend cause. **Fixed** using the same general remedy CLAUDE.md
   already documents for the per-row case — drop the `[value]` binding on the `<select>` entirely,
   bind `[selected]="option === boundValue"` on each `<option>` instead — applied preventively to
   every top-level Supplier/Warehouse/TDS-Type/Payment-Mode/Account select newly added this phase
   (`purchase-order-detail-page`, `purchase-bill-detail-page`, `expense-detail-page`,
   `debit-note-detail-page`, `supplier-payment-detail-page`), not just the one caught red-handed,
   since the same two-independent-subscribes race is latent in all of them and only needs
   unlucky timing to surface. **Worth knowing for Phase 7**: the "customer/product selects were
   safe because their options resolved in an earlier change-detection cycle" reasoning from
   phase-5-status.md's bug #1 is not a reliable guarantee — it happened to hold in Phase 5's actual
   call graph, but any two independent async subscribes racing against each other can trigger the
   same underlying `[value]`-vs-`@for` staleness. Prefer `[selected]` per-option by default for
   every select whose options come from any signal, not only ones inside a freshly-created `@for`
   row.
2. **`ListPaymentsQueryHandler` silently filtered every result to `Direction == PaymentDirection.Received`**,
   a hardcoded filter shipped in Phase 5 itself (present in the original commit, `git diff main`
   confirms this file was untouched by any Phase 6 edit before the fix) that had zero observable
   effect until Phase 6 introduced the first `Direction=Paid` rows. Caught when the new
   `supplier-payment-list-page` showed "No Supplier Payments Yet" immediately after approving one
   by hand — a raw `sqlcmd` query against `[payments].[Payments]` confirmed the row existed with
   the correct `Direction='Paid'`, and the API server's own EF Core command logging showed the
   generated SQL literally included `AND [p].[Direction] = N'Received'`, a predicate that doesn't
   exist anywhere in `ListPaymentsQuery`'s own fields — tracing it to the handler's `Where` clause
   confirmed the leftover filter. **Fixed** by removing the hardcoded predicate; both
   `payment-list-page` (Customer) and `supplier-payment-list-page` (Supplier) now filter
   client-side by `direction` on the unfiltered result, confirmed to correctly separate the two
   lists after the fix (a Direction=Paid row no longer appears under Customer Payments and
   vice versa). Worth flagging for anyone auditing Phase 5's original manual E2E pass: it only ever
   exercised `Direction=Received`, so this class of "hardcoded filter matching the only case ever
   tested" bug is easy to miss without a second call site exercising the other branch — a good
   argument for scope decision #9's note that Phase 6's handlers still aren't unit-tested.
3. **`DebitNotePostingRule` debited Accounts Payable for the full grand total while the source
   PurchaseBill had only ever credited AP net of TDS, leaving Accounts Payable permanently off by
   the TDS amount (and TDS Payable permanently unresolved) after a full-reversal debit note.**
   Not a build/test failure — `dotnet build`/`dotnet test` and the manual E2E pass in the original
   Phase 6 submission all passed, because nothing in that pass checked the *combined* GL effect of
   a bill-plus-its-reversal. Caught only when asked directly "what happens to the TDS on a debit
   note" and re-verified by hand: created a fresh PurchaseBill (300 pre-VAT, 39 VAT, 4.5 TDS at
   1.5%, so AP credited 334.50 net of TDS), approved it, converted it to a DebitNote for the exact
   same lines, and approved that — the DebitNote debited AP for the full 339 (not 334.50) and never
   touched TDS Payable at all, so the combined ledger showed a spurious 4.50 debit balance on AP
   and a stuck 4.50 credit balance on TDS Payable that a 100%-matching reversal should have
   cancelled to zero on both. Root cause was the scope decision itself (see scope decision #7's
   updated text) — "DebitNote doesn't reverse TDS" was a defensible-sounding simplification that
   didn't survive contact with the actual debit math once written down. **Fixed** by giving
   DebitNote its own `TdsTypeId`/`TdsAmount` (resolved from its own lines, not copied from the
   source bill) and making `DebitNotePostingRule` a true mirror of `PurchaseBillPostingRule`
   including the TDS leg; re-verified the same scenario by hand afterward and confirmed AP and TDS
   Payable both net to exactly zero. **Worth knowing for Phase 7 and beyond**: when a document type
   is explicitly modeled as "the reverse of" another one, verify the reversal by computing the
   *combined* net effect on every account touched by both documents, not just that each document's
   own entry is internally balanced — a rule can satisfy `sum(Debit)==sum(Credit)` for itself while
   still leaving a paired account permanently unbalanced across the two related postings.

## What's next

**Phase 7 — Inventory & stock ledger** (see `roadmap.md`): retrofits Phase 5/6's stubbed stock
behavior (`AlwaysOkStockAvailabilityPolicy`, and PurchaseBill's entirely-absent stock increment)
into real FIFO costing — `StockLedgerEntry`, a real `StockAvailabilityPolicy` replacing the stub,
wiring the actual decrement into `InvoicePostingRule`'s approval path and the actual increment into
`PurchaseBillPostingRule`'s. Also worth doing early: backfill unit tests for Phase 5/6's handlers
(scope decision #9) before Phase 7 touches the same `Payment`/posting-rule code paths again: the
`ListPaymentsQueryHandler` bug above is exactly the kind of regression a handler-level test suite
would have caught immediately instead of during manual E2E. Phase 8+'s Role Reference editor would
also be a natural point to revisit whether `Payments.Payment.*` staying unsplit (scope decision #4)
still holds once real per-tenant roles (not just the Admin/Member hardcoded stub) need to express
"can approve Customer Payments but not Supplier Payments" or similar splits a real business might
want.
