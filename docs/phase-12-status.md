# Phase 12 status — Transaction Approval Queue

**Status: COMPLETE.** `TransactionApprovalQuery` (`Application.Workflow.Queries.TransactionApproval`)
is the first Workflow-context feature (architecture-spec.md §4.9) — a read-only v1 unifying every
Draft-status row across all 13 `ApprovableTransaction` document types this codebase has into one
list, per product-requirements.md FR-10.2 ("a unified Transaction Approval queue listing every
Draft-status document... the current user is permitted to approve"). `erp-module-scan.md` line 113
confirms this as a real sub-module of the reference product. Each row links into that document's own
existing detail page, where the existing Approve button already works — no bulk-approve-from-the-list
action this phase (see scope decision #1). No new commands, aggregates, or schema tables — one
permission-seed-only migration (`AddPhase12WorkflowPermissions`).

## Roadmap/brief exit criteria — final status

- [x] `TransactionApprovalQuery(OrganizationId)` under `Application.Workflow.Queries.TransactionApproval`,
      13 concrete per-document-type `Where` blocks, unioned in-memory (not one generic
      `Func<TDocument,...>`-parameterized helper — see scope decision #3)
- [x] Read-only v1: no bulk-approve action, documented as a deferred stretch goal (scope decision #1)
- [x] The one real judgment call — whether the query/endpoint needs its own blanket
      `IRequirePermission` gate, or whether per-type filtering down to zero rows is sufficient —
      decided and documented, not defaulted either way (scope decision #2)
- [x] `TransactionApprovalQueryHandlerTests` (4 tests) covering: only-Draft rows across multiple
      types with an Approved row of the same type excluded; a document type excluded entirely when
      the seeded Role lacks that type's own `.Approve` permission even though a Draft row exists
      (the behavior the whole feature exists for); multi-type union; organization-scoping
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged, Application.UnitTests 147 — 3 new +
      144 pre-existing[^1], Api.IntegrationTests 4, all green, Docker Desktop running this session) and
      `ng build`/`ng test` (7 pre-existing specs green, no new Angular specs) all pass
- [x] Angular: `transaction-approval-queue-page` under `features/workflow/`, a flat table (Document
      Type / Reference / Date / Open link), dashboard nav link
- [x] Manual E2E against the real API/DB/browser: an Admin sees a Draft Invoice and a Draft
      PurchaseBill; a Member granted `Invoice.Approve` but not `PurchaseBill.Approve` sees only the
      Invoice — confirmed both via direct API call and through the real Angular page in both sessions

[^1]: 147 total includes this phase's 3 new tests; the jump from Phase 11's 138 to a 144 pre-existing
      baseline reflects tests added in the working tree between phases (not part of this phase's own
      diff).

## Scope decisions

1. **No bulk-approve-from-the-list action this phase — read-only v1, exactly as scoped.** The brief
   named this explicitly as a real stretch goal, not a silent omission: a second "approve without
   opening the document" code path per document type would roughly double this phase's surface area
   for a first cut of a brand-new bounded context. Every row instead links into that document type's
   own existing detail page, where the existing Approve button (proven since Phase 4) already works.
   Deferred, not forgotten — a natural Phase 13+ candidate once the read-only queue itself is proven
   useful.
2. **`TransactionApprovalQuery` implements `IRequirePermission` with a new blanket
   `PermissionKeys.TransactionApprovalView` key, granted Admin+Member — not because a Member needs
   gating down from something they'd otherwise over-see, but because `AuthorizationBehavior` turned out
   to be the *only* mechanism in this codebase that verifies the acting user actually belongs to
   `OrganizationId` at all.** This was the one real judgment call the brief asked to make explicitly
   rather than default. Investigation: `AuthorizationBehavior`'s own doc comment says it checks a
   request's `PermissionKey` against `OrganizationMemberships`/`RolePermissions` for the *organization
   the request targets* — but a request that implements `IOrganizationScoped` without also implementing
   `IRequirePermission` skips that check entirely (`AuthorizationBehavior.Handle` returns `await next()`
   immediately for any non-`IRequirePermission` request, before ever looking at `OrganizationId`).
   Grepping every `IOrganizationScoped` type in `Application` (128 files) confirmed every single one also
   implements `IRequirePermission` — no exception exists anywhere in this codebase. So skipping a
   permission key here wouldn't just be a looser exposure policy the way most `Reports.*.View` keys are
   — it would mean *no* code anywhere checks that the querying user belongs to the requested
   Organization at all, a real violation of NFR-2.1 ("A user's data shall never be visible to... a user
   of a different Organization, enforced at the data-access layer"), not merely a stylistic gap. Once a
   key is required for that reason, its Admin/Member grant question is separate and answered
   differently than every prior `Reports.*.View` key: this key doesn't itself gate exposure — the
   query's own per-document-type `*.Approve`-key filtering (mirroring `AuthorizationBehavior`'s exact
   join, resolved once as a set) is what actually determines which rows a Member sees, row-type by
   row-type. A Member holding zero `*.Approve` grants anywhere just sees an empty queue — functionally
   identical to an Admin-only gate's outcome — without blocking a Member who legitimately holds one or
   more `*.Approve` grants (e.g. a Sales Staff persona with `Invoice.Approve`) from using the screen at
   all. Confirmed directly in manual E2E: the default system Member role (all `*.Approve` keys denied)
   got a real `200` with an empty `rows: []`, not a `403` — proving the blanket key isn't doing exposure
   work, the per-type filtering is.
3. **13 separate concrete `db.Xs.Where(...)` blocks, not one generic `Func<TDocument,...>`-parameterized
   LINQ helper.** CLAUDE.md's own known-gotchas list (and `phase-9-status.md`'s bug #1, a smaller
   five-type union) already document why: a captured delegate inside `.Where()` doesn't translate
   against a real SQL Server provider, only ever proven working against the InMemory test provider. Each
   block queries its own document type independently, materializes to an anonymous projection via
   `ToListAsync`, then builds the shared `TransactionApprovalRowDto` in memory — the same pattern
   `TdsReportQueryHandler`/`AnnexThirteenReportQueryHandler` already established for a multi-type union.
4. **`TransactionApprovalRowDto` carries `Direction` (nullable `PaymentDirection`), used only for the
   `Payment` document type.** Payment is the one document type among the 13 whose Angular detail page
   isn't determined by `DocumentType` alone — Customer Payment (`Direction=Received`) and Supplier
   Payment (`Direction=Paid`) share one backend aggregate (Phase 6's "near-zero-new-code" precedent) but
   route to two separate existing Angular pages (`/payments/:id` vs
   `/purchasing/supplier-payments/:id`). Rather than inventing a new routing-hint field, the DTO exposes
   the one field that already discriminates this exact case elsewhere in the codebase, and the Angular
   page's own `detailRoute()` switches on it only for `Payment`.
5. **`SalesOrder` rows render with no "Open" link at all** (`detailRoute()` returns `null`, the template
   shows "No detail page" instead of a button). Phase 5 shipped `SalesOrder`'s backend fully but
   deliberately cut its Angular UI (`docs/phase-5-status.md`'s own scope decision, never retrofitted) —
   there is no `/organizations/:id/sales/sales-orders/:id` route to link to. Rather than fabricate a
   route that would 404, or silently omit Draft SalesOrders from the queue entirely (which would violate
   FR-10.2's "every Draft-status document" for a type whose `.Approve` permission a Role can still hold),
   the row still appears — proving the query itself is correct for all 13 types — with an honest "no
   detail page yet" affordance instead of a broken link.
6. **`Expense` rows use `SupplierInvoiceReference` for the DTO's shared `Reference` field, not a second
   dedicated column.** `Expense` is the one document type among the 13 with no plain `Reference`
   field on its own aggregate (confirmed by reading `Expense.cs` directly, not assumed from the other
   12's consistent shape) — only `SupplierInvoiceReference`. Reusing the shared field keeps
   `TransactionApprovalRowDto` a single flat shape across all 13 types rather than adding a
   type-specific column for one row out of thirteen.
7. **Rows sort oldest-first (`Date` then `CreatedAt`), matching a work-queue's natural
   longest-waiting-first processing order** — not a confirmed live-screen convention (no prior phase's
   scan notes describe Tigg's own Transaction Approval Queue sort order), but the same default every
   other "list of pending work" convention in this codebase implicitly favors (e.g. FIFO payment
   allocation, Phase 5/6/11).
8. **Manual E2E's partial-visibility proof required directly seeding a third, custom `Role`/
   `RolePermission` row set via SQL, not through the product's own Invite flow.** This is a real,
   documented limitation of Phase 1c's role stub, not of this phase's own query logic (which is already
   proven at the unit level against arbitrary grant combinations via `TransactionApprovalQueryHandlerTests`).
   Today's Invite screen only offers the two hardcoded system roles (`Admin` = every permission granted,
   `Member` = every `*.Approve` key denied, confirmed in `RolePermissionConfiguration`'s own seed data)
   — there is no way, through any UI or command this codebase currently ships, to grant a Member
   `Invoice.Approve` without also granting every other `*.Approve` key, since both keys are denied
   identically for every Member in every Organization. To prove the query's real per-type behavior
   end-to-end against the real API/DB/browser (not just the InMemory unit-test provider), a third `Role`
   row was inserted directly (`Workflow.TransactionApproval.View` + `Sales.Invoice.Approve` granted,
   `Purchasing.PurchaseBill.Approve` explicitly denied) and the test Member's `OrganizationMembership.RoleId`
   was pointed at it — the same shape roadmap's own "Role Reference full editor" (Phase 8+ backlog item)
   will eventually let a real Admin do through a UI. Not a workaround around this phase's own code; a
   workaround around a known, already-flagged gap in a different, earlier phase's deliberately-thin stub.

## Manual E2E

Confirmed against the real API/DB/browser (Docker Desktop running, `dotnet run --project src/Api
--launch-profile https`, `ng serve`). A fresh Admin was seeded via direct API calls (curl + cookie
jar, per this codebase's established manual-E2E-seeding convention — reserve browser clicks for this
phase's own new list page): a Warehouse, a Customer (PAN), a Supplier (PAN), a Service Product, a
Draft Invoice (200.00, unapproved), and a Draft PurchaseBill (80.00, unapproved) — neither approved,
by design, since this phase's query only cares about Draft-status rows.

- **Admin, direct API call**: `GET /api/organizations/{id}/workflow/transaction-approval-queue`
  returned both rows — the Draft Invoice (`contactName: "Kathmandu Retail Pvt Ltd"`) and the Draft
  PurchaseBill (`contactName: "Everest Wholesalers"`).
- **Admin, real browser**: logged into the Angular app, clicked the dashboard's new "Approval Queue"
  nav link, saw both rows rendered with working "Open" buttons; clicking the Invoice row's "Open"
  button landed on the real, existing Invoice detail page (Draft, Grand Total 200.00, Approve button
  present) — proving the row-to-detail-page link is real navigation, not a dead affordance.
- **Member, default system role, direct API call**: a second user was invited (Phase 1c's standard
  Member role, every `*.Approve` key denied by seed), accepted the invitation, and called the same
  endpoint — got a real `200` with `rows: []`, not a `403`, confirming `TransactionApprovalView`'s
  blanket grant lets the screen load while per-type filtering correctly narrows it to nothing.
- **Member, custom role granting only `Invoice.Approve`, direct API call**: the same Member's
  `OrganizationMembership.RoleId` was repointed at a custom Role granting
  `Workflow.TransactionApproval.View` + `Sales.Invoice.Approve` but explicitly denying
  `Purchasing.PurchaseBill.Approve` (see scope decision #8) — the endpoint returned exactly one row,
  the Draft Invoice, with the Draft PurchaseBill correctly absent.
- **Member, custom role, real browser**: re-loaded the same Angular Approval Queue page (same
  session, no re-login needed) — showed exactly the Invoice row, matching the API response, with no
  PurchaseBill row anywhere on the page. This is the actual point of the phase, confirmed both via
  direct API call and through the real UI, not just a "the list isn't empty" happy-path check.

## Bugs and gotchas hit along the way

None in the shipped handler/endpoint/permission wiring itself — `dotnet build` was clean on the first
pass after the query/handler/endpoint were written, and all 4 new tests passed on the first real run.
One test-authoring slip caught and fixed before the first test run (not a codebase defect): the initial
`CreateWarehouseTransferAsync` test helper passed `seed.ServiceProductId` for the transfer line, which
would have failed `InventoryValidation.EnsureProductsAreGoodsAsync` at runtime (Phase 7's own
Goods-only rule for stock-moving documents) — caught by re-reading `CreateWarehouseTransferCommandHandler`
before running the tests, fixed by seeding a second, Goods-type product for that one call site.

## What's next

`roadmap.md`'s Phase 8+ Workflow bullet also names Tasks (polymorphic, attachable to Contacts/
Organization/other entities) and a Document inbox (AI-extraction can be a stretch goal) as the
remaining Workflow-context features — neither touched this phase. Within this feature's own scope,
the deferred bulk-approve-from-the-list action (scope decision #1) and a real Angular UI for
`SalesOrder` (scope decision #5, a pre-existing Phase 5 gap this phase's queue simply surfaced rather
than caused) are the two most natural follow-ups. Separately, this phase's manual E2E leaned directly
on SQL to prove partial-type visibility (scope decision #8) precisely because Phase 1c's Role Reference
stub has no UI for granting a custom permission combination — the roadmap's own "Role Reference full
editor" backlog item is the real fix for that gap, not specific to this phase.
