# Phase 11 status — Payment Allocation Suggestion: outstanding-amount fix

**Status: COMPLETE.** `GetDefaultPaymentAllocationsQueryHandler`
(`Application.Payments.Queries.GetDefaultPaymentAllocations`) — the Payment-recording screen's
FIFO-oldest-first "suggest which outstanding bills to allocate this payment against" query
(architecture-spec.md §4.6) — now computes "outstanding" the same way `ContactAgeingSummaryQueryHandler`
already does (phase-9-status.md), instead of the `PurchaseBill.GrandTotal`-only figure phase-9-status.md's
scope decision #7 flagged as a pre-existing latent gap. No new commands, aggregates, schema tables, or
migrations — a pure bug fix to one existing query handler's internal computation, confirmed before
starting (see "Investigation" below).

## What was fixed

`SuggestAsync<TDocument>`'s per-document `outstanding` computation was `grandTotalSelector(document) -
allocated`, and the PurchaseBill branch passed `x => x.GrandTotal` — the gross bill total, not net of TDS.
`PurchaseBillPostingRule` (Phase 6) and `ContactStatementQueryHandler`/`ContactAgeingSummaryQueryHandler`
(Phase 9) all already encode `GrandTotal - TdsAmount` as the real amount owed to a Supplier; this handler
was the one place in the codebase still using the gross figure, letting it suggest allocating more of a
Payment than a TDS-bearing bill's actual net payable.

Fixed with the smallest correct change: the PurchaseBill branch's `netAmountSelector` argument became
`x => x.GrandTotal - x.TdsAmount` instead of `x => x.GrandTotal` — no change to the generic helper's
signature was needed for this half of the fix, since `PurchaseBill.TdsAmount` is already a queryable
property reachable from the existing selector `Func`.

## Investigation: the reversal-netting gap (in scope, fixed)

The brief required an explicit in-scope-vs-deferred call on a second gap this handler's own code raised
but no prior phase had flagged: `outstanding = grandTotalSelector(document) - allocated` never nets an
Approved CreditNote/DebitNote reversal linked to that specific document at all, on either side — an
Invoice with a linked CreditNote, or a PurchaseBill with a linked DebitNote, both still showed their full
gross-minus-payments figure as outstanding, over-suggesting on *both* Sales and Purchase sides.

**Decision: in scope, fixed this phase — not deferred.** Reasoning:

1. It's the same class of bug as the TDS gap (an incomplete "outstanding" computation), arguably a wider
   blast radius since it affects both Direction branches, not just Purchase.
2. The fix is genuinely small under this handler's existing shape: `SuggestAsync<TDocument>` already took
   selector `Func`s per architecture-spec.md's own generic-helper pattern; the natural extension was one
   more parameter (`Func<IReadOnlyList<Guid>, Task<Dictionary<Guid, decimal>>> reversalsByDocumentIdAsync`),
   not a rewrite of the helper's shape or a new abstraction.
3. `ContactAgeingSummaryQueryHandler`'s `LoadCreditNoteReductionsAsync`/`LoadDebitNoteReductionsAsync`
   (phase-9-status.md) already established exactly the query shape needed — match on
   `ReferrerType`/`ReferrerId` pointing at an in-scope document, sum `(Amount + VatAmount)` per source
   document, net a DebitNote's own `TdsAmount` off its gross. This phase's two new private methods
   (`LoadCreditNoteReversalsAsync`/`LoadDebitNoteReversalsAsync`) mirror that pattern directly rather than
   inventing a new one — the only structural difference is no `AsOfDate` cutoff, since this handler (unlike
   Ageing) has never date-filtered its outstanding-documents query either, and adding one would have been
   scope creep beyond fixing the flagged computation.
4. Unlike Ageing's own standalone-reversal exclusion (phase-9-status.md scope decision #9), there is no
   "which bucket does this belong to" ambiguity here to resolve — a payment-allocation suggestion only
   ever needs to know one thing per document: how much of *this specific bill* is still open. A standalone
   (unlinked) CreditNote/DebitNote naturally contributes nothing here for the same reason it contributes
   nothing to Ageing's per-bill buckets: it has no `ReferrerId` matching any candidate document, so it's
   invisible to `LoadCreditNoteReversalsAsync`/`LoadDebitNoteReversalsAsync`'s own `Where` clause — no
   special-case code was needed to reproduce that exclusion.

## Scope decisions

1. **Both halves of the fix (TDS-netting and reversal-netting) shipped together, not split into two
   phases.** They're both instances of the same underlying defect — an incomplete `outstanding`
   computation — surfaced by the same investigation, and the reversal-netting fix reuses infrastructure
   (`SuggestAsync`'s selector-`Func` pattern) that the TDS fix didn't need to touch. Splitting them would
   have meant re-deriving the same context in a follow-up phase for no isolation benefit.
2. **No `AsOfDate`/cutoff-date parameter was added to either new reversal-loading method**, even though
   `ContactAgeingSummaryQueryHandler`'s equivalents take one. This handler's own `outstandingQuery` (the
   candidate Invoices/PurchaseBills) has never date-filtered either — it suggests against every Approved
   document regardless of date, by design (a Payment can be recorded against a bill of any age). Threading
   a cutoff date through only the new reversal logic while the rest of the query has none would have been
   an inconsistent half-measure with no real requirement behind it.
3. **`PurchaseBill.TdsAmount` net-of-TDS change is a one-line selector swap, not a new field or query.**
   `TdsAmount` was already a mapped column on the entity Include-loaded in `Handle`; no schema or migration
   changes were needed anywhere in this fix.

## Tests

`GetDefaultPaymentAllocationsQueryHandlerTests` (`tests/Application.UnitTests/Payments/`) is this
handler's first unit test coverage (confirmed zero prior coverage by grep before starting), seeding real
Contact/Warehouse/Product/TdsType/Account/TenantSettings rows and real Invoice/CreditNote/PurchaseBill/
DebitNote/Payment documents through their real Create/Approve command handlers, per this codebase's
established test pattern (same seeding style as `ContactAgeingSummaryQueryHandlerTests`). Four tests:

- `Handle_nets_tds_off_purchase_bills_and_orders_fifo_oldest_first` — an older TDS-bearing bill (gross
  1000, TDS 100 → net 900) and a newer non-TDS bill (gross 500, unaffected — regression guard) both
  outstanding; a 1200 payment suggests the older bill's full 900 first, then 300 of the newer bill's 500,
  proving both the TDS-net figure and FIFO-oldest-first ordering in one scenario.
- `Handle_reduces_purchase_bill_outstanding_by_a_prior_approved_payment_allocation` — a 900-net bill with
  an existing Approved Payment already allocating 300 against it suggests only the remaining 600.
- `Handle_reduces_purchase_bill_outstanding_by_a_linked_debit_note_net_of_its_own_tds` — a 900-net bill
  with a linked DebitNote (gross 200, its own TDS 20 → net reversal 180) suggests 720 — a `(ProductId,
  Rate, VatRate)`-exact-match partial reversal, expressed as a fractional Quantity at the source line's own
  Rate per Phase 6's conversion-cap enforcement (phase-9-status.md's own test-authoring gotcha).
- `Handle_reduces_invoice_outstanding_by_a_linked_credit_note` — the Sales-side mirror: a 1000-gross
  Invoice with a linked CreditNote (gross 300) suggests 700, proving the reversal-netting fix on the
  Received-direction branch too, not just Paid.

`dotnet test` results this phase: Domain.UnitTests 67 unchanged, Application.UnitTests 138 (4 new + 134
pre-existing, all green), Api.IntegrationTests 4 (Docker Desktop running this session, all green).

## Angular

**No frontend changes.** Confirmed by reading both `payment-detail-page.ts` (Customer/Received) and
`supplier-payment-detail-page.ts` (Supplier/Paid, phase-6-status.md's near-zero-new-code mirror pattern):
both call `PaymentsService.getDefaultAllocations(...)` and render whatever `PaymentAllocationInput[]` the
API returns — the DTO shape (`targetDocumentType`, `targetDocumentId`, `amount`) is unchanged by this fix,
only the *value* of `amount` for a TDS-bearing or reversal-linked document is now correct. Neither page
needed a code change; both automatically show the corrected figure since they were already pure
formatters of this endpoint's response.

## Manual E2E

Confirmed against the real API/DB and the real Angular UI. A fresh Admin was seeded end-to-end via direct
API calls (curl-equivalent Python script, cookie-based auth, per this codebase's established
manual-E2E-seeding convention — reserve browser clicks for the phase's own UI): Chart of Accounts
(Accounts Payable/VAT Receivable/TDS Payable/Purchase Expense), a Warehouse, a Service Product, a Supplier
(PAN-bearing), a TDS Type (10%), and a PurchaseBill (gross 1000, TDS 100 → net 900), approved.

- **Direct API call**: `GET /api/organizations/{id}/payments/default-allocations?contactId={supplierId}
  &amount=1000&direction=Paid` returned exactly one suggestion — `{targetDocumentType: "PurchaseBill",
  targetDocumentId: <bill>, amount: 900.0}` — matching the expected net-of-TDS figure exactly, not the
  gross 1000.
- **Real UI**: logged into the Angular app, opened the Supplier Payment "New" screen, selected the seeded
  Supplier, entered Amount 1000, and clicked "Suggest (FIFO)". The Allocations table populated with
  Purchase Bill 0001 at **900** (Allocated 900.00, Remaining 100.00) — the exact same net-of-TDS figure
  the API returned, confirmed on-screen, not just inferred from the response body.

## Bugs and gotchas hit along the way

None in the shipped handler/test code itself — `dotnet build` was clean on the first pass after the fix,
and all four new tests passed on the first real run. Several environment/tooling gotchas surfaced while
setting up this phase's manual E2E script, worth keeping for the next one:

- **SQL Server's `identity` schema name needs bracket-quoting** (`[identity].Users`, not
  `identity.Users`) — `identity` is a reserved keyword in T-SQL, so an unquoted schema-qualified reference
  fails with `Msg 156: Incorrect syntax near the keyword 'identity'` even though the same query pattern
  works fine for every other (non-reserved-word) schema name in this codebase.
- **`dotnet run --project src/Api` must be run from the repository root, not from inside `src/Api`
  itself** — an earlier `cd src/Api && dotnet user-secrets list` in the same shell session left the working
  directory changed for every subsequent command, so a later `dotnet run --project src/Api` failed with
  "The provided file path does not exist: src/Api" until the shell was returned to the repo root.
- **This repo's default `dotnet run --project src/Api` (no `--launch-profile`) only binds the `http`
  profile (`http://localhost:5155`), not `https://localhost:7104`** — `launchSettings.json` has two
  profiles (`http` and `https`), and `https` is the one binding both ports, matching what
  `.claude/launch.json`'s `erp-api` configuration already runs. A plain `dotnet run --project src/Api`
  picked the `http`-only profile, and since ASP.NET Core's `/login` cookie is `Secure` (phase-1a-status.md's
  own documented reasoning — required for `SameSite=None`), an `http`-only session's cookie jar silently
  refuses to send it back on later requests, surfacing as a confusing `401 Unauthorized` on every
  authenticated call *after* a successfully-`200` login. Always pass `--launch-profile https` (or use
  `.claude/launch.json`'s `erp-api` entry) when scripting an authenticated E2E flow against this Api.

## What's next

`roadmap.md`'s Phase 8+/Reports sequence and any further cross-cutting fixes should be consulted for
what's next; this phase closed out the one specific latent gap phase-9-status.md's scope decision #7
flagged, plus the reversal-netting gap its own investigation surfaced.
