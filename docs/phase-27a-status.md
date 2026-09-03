# Phase 27a status — Cross-cutting rollout sweep: document-level mechanisms

**TL;DR.** Swept four cross-cutting mechanisms — Custom Fields, Custom Status, Reporting Tags, and
the Tasks/Documents/Activity detail-page tabs — across the document types that previously lacked
them. All four sweeps are backed by one shared classification table
(`DocumentMechanisms` in `Domain/Common`) and one server-side guard test
(`DocumentMechanismSweepGuardTests`) plus one client-side guard spec
(`document-mechanism-sweep-guard.spec.ts`) that fail the build if a document type is left
unclassified or a template is missing a mechanism its type is supposed to carry. `Comment` became
polymorphic (`CommentParentType`, mirroring `TaskParentType`/`AttachmentParentType`) — the trigger
phase-18 decision #3 set for that generalization ("only if/when a second parent type is actually
needed") has now been met. A shared `DocumentPermissions`/`ParentPermissions` map replaced three
near-identical permission switches, and a shared `DocumentExistenceReader` replaced two
near-identical existence-check switches. Domain 323 (+6), Application 706 (+35), Angular 165, Api.
IntegrationTests 18 (unchanged); `dotnet build` / `dotnet test` / `ng build` / `ng test` /
`tsc --noEmit` all clean.

**The confirm-live pass corrected the roadmap's own numbers.** Custom Fields applies to **13**
document types, not the roadmap's "remaining 15" — Configurations > Custom Fields renders exactly 16
live sections (the four payment kinds collapse onto this codebase's one `Payment` type), and
**Warehouse Transfer and Inventory Adjustment have no Custom Fields section at all**, despite both
being ordinary transactional documents. Custom Status widened by exactly two: **Sales Order** and
**Production Order** join Quotation and Purchase Order (Cheque stays excluded, per 20b's finding,
unchanged). Reporting Tags is the widest sweep — every transactional type *plus* **OpeningBalance
and OpeningStock**, which the roadmap's "plus Opening Balances" undersold: both Opening Balances
tabs carry an inline "Add Reporting Tags" link in their own row form, tagging that row by its own
line id. And the roadmap's "Tasks / Documents / Comments / Activity" tab list is wrong on the detail
page: the live shape is **Overview / Tasks / Documents / Activity**, with Comments living as a
sub-tab *inside* Activity (alongside Activities and Emails) — there is no top-level Comments tab.

## Confirm-live decisions

All confirmed against the real Tigg UAT tenant (`moonbeamtradingandsuppliers.tigguat.com`) on
2026-09-03, per the phase-8f discipline. Full trace kept in this session's scratchpad; the findings
that changed scope are recorded here.

1. **Custom Fields: 13 types, not 15.** Configurations > Custom Fields lists one section per
   applicable type: Sales Invoice, Quotation, Sales Order, Credit Note, Customer Payment, Quick
   Receipt, Purchase Order, Purchase Bill, Expense, Debit Note, Supplier Payment, Quick Payment,
   Journal Voucher, Cash Transfer, Production Order, Production Journal (16 live sections; the four
   payment kinds collapse onto our one `Payment`, giving 13). Warehouse Transfer and Inventory
   Adjustment are absent from that list entirely — confirmed by their absence from the section list,
   not inferred. `DocumentMechanisms.CustomFields` encodes exactly these 13 and both absences are
   pinned by a dedicated guard-spec assertion (`keeps the custom-fields editor off the two types...`)
   so a future "just widen it" edit fails loudly.
2. **Custom Status: Sales Order and Production Order, live-confirmed as the third shape's third and
   fourth members.** Sales Orders list grid carries a STAGE column with a per-row "Select Status"
   popover (seeded pipeline Pending/Confirmed/Packaged/Delivered/Cancelled/…), settable on Draft and
   Approved rows alike — same orthogonality Quotation/Purchase Order already had. Production Order's
   grid carries the identical control under a STATUS column (not STAGE — a label difference, not a
   different mechanism), with real assigned values already on two of four seeded rows ("Completed").
   Production *Journal*'s grid has no such column (Date/Code/Reference/Product/Quantity only) — it
   was never a candidate, and no assertion pretends it is.
3. **Reporting Tags: all 15 transactional types plus OpeningBalance/OpeningStock — the widest
   sweep.** Sampled the detail-page chrome across three different bounded contexts (Invoice/Sales,
   Journal Voucher/Accounting, Warehouse Transfer/Inventory) and found byte-identical shape: a
   REPORTING TAGS block with an Add/Edit action in the left profile panel. Warehouse Transfer
   carried six real tags (BUSINESS/SERVICE/VEHICLE/IMPORT/DTRG/DSPL), proving the live feature is in
   active use, not vestigial. Separately, both Opening Balances tabs (Account and Product) carry an
   inline "Add Reporting Tags" link in their own row-edit form — a fact the roadmap's parenthetical
   "plus Opening Balances" didn't specify the shape of. Because neither `OpeningBalanceLine` nor
   `OpeningStockLine` has its own detail page, tagging is keyed by the line's own Id — the same
   identity `GlJournalEntry.SourceDocumentId` already uses for these two.
4. **The detail-page tab set is Overview / Tasks / Documents / Activity — four, not the roadmap's
   five.** Confirmed identical across Invoice, Journal Voucher and Warehouse Transfer: a vertical tab
   list with those four entries. Tasks renders the same DUE/DETAILS/PRIORITY/STATUS/CREATED
   BY/ASSIGNED TO table with a "+ ADD TASK" action our `app-task-list` already builds; Documents
   renders the same bare drag-and-drop dropzone our `app-attachment-list` already builds. **Activity
   opens with a real comment composer** ("Write comment here…", ADD COMMENT) **above three sub-tabs:
   Comments / Activities / Emails** — one fewer than the Contact tab's four (no SMS History, since a
   document has no phone number). There is no standalone top-level Comments tab anywhere. This
   directly falsified the kickoff prompt's assumed tab list, which is why `Comment` needed
   generalizing rather than a fifth tab needing building.

## Scope decisions

- **One shared `DocumentTabs` component, not fifteen per-type ones.** The three non-Overview panes
  are identical in every sampled module; a per-type component for each of 15 detail pages would have
  been 15 copies of one thing, which is exactly the drift a sweep phase must not introduce. Hosts
  drop in one element (`<app-document-tabs #docTabs documentType="..." [documentId]="...">`) and wrap
  their existing body in `@if (docTabs.isOverview())`. A null `documentId` (an unsaved `.../new` form)
  renders no tab strip and reports Overview, so a host needs no second condition for the create path.
- **`Comment` generalized to `CommentParentType` rather than gaining a second, document-scoped
  entity.** Phase 18 decision #3 explicitly deferred this ("generalize only if/when a second parent
  type is actually needed") — the live Activity tab's comment composer on every transactional
  document is that trigger. The migration renames `ContactId` to `ParentId`, adds `ParentType`
  defaulted to `"Contact"` for every pre-existing row (every comment ever written was on a Contact,
  by construction — the column being renamed *is* `ContactId`), and drops the FK to `Contacts` since
  a polymorphic pair cannot carry one (matching `WorkTask`/`Attachment`'s existing shape). The
  scaffolded migration's own default value was the empty string, which would have made every
  pre-existing comment invisible to any `ParentType`-filtered query — hand-corrected before applying
  it; see the migration file's own comment.
- **`TaskParentType` and `AttachmentParentType` widened by name-matching `DocumentType`, bridged by
  `DocumentParentTypes.For<T>()` — never by ordinal cast.** The phase-26a lesson applied
  structurally: the three parent enums cannot share an ordinal order (`TaskParentType` leads with
  `Contact`/`Organization`, `DocumentType` leads with `Quotation`), so the bridge is
  `Enum.TryParse<TParentType>(documentType.ToString())`, and a guard test
  (`Enum_mapping_is_by_name_and_not_by_ordinal`) asserts a member picked to have divergent ordinals
  round-trips correctly, so a future "simplify to a cast" edit fails immediately rather than shipping
  a silent misattribution.
- **One shared `DocumentPermissions`/`ParentPermissions` map, replacing three near-identical
  switches.** `CustomFieldValuePermissions`, `TransactionReportingTagPermissions` and
  `CustomStatusPermissions` each independently switched `DocumentType` to a permission key; sweeping
  four mechanisms across 17 document types would have meant four parallel ~17-arm switches that must
  agree by inspection. They now all delegate to `DocumentPermissions` (keyed by `DocumentType`) or
  `ParentPermissions` (keyed by the parent enums, resolving through `DocumentPermissions` for a
  document parent and to Contact's own pre-split pair otherwise). Same reasoning as phase-26b's
  shared readers: agreement by construction, not by someone checking four call sites stay in sync.
- **One shared `DocumentExistenceReader`, replacing two near-identical existence switches.**
  `SetCustomFieldValuesCommandHandler` and `SetTransactionReportingTagsCommandHandler` each
  hand-wrote a `EnsureDocumentExistsAsync` switch covering only Quotation/Invoice; extending both to
  17 types would have doubled that duplication. `WorkflowValidation.EnsureParentExistsAsync` (used by
  `WorkTask`/`Attachment`/`Comment`) also now delegates to it for a document parent, so there is
  exactly one 17-arm existence switch in the whole codebase.
- **The two id-only attachment operations (download, delete) needed a genuinely new pattern: a
  blanket declared key plus an in-handler re-check against the parent's real key.**
  `IRequirePermission.PermissionKey` is a property evaluated *before* the handler runs, but the real
  key for `GetAttachmentForDownloadQuery`/`DeleteAttachmentCommand` depends on a column
  (`Attachment.ParentType`) of the row the handler is about to load — a chicken-and-egg the other
  five mechanisms don't have, since they all take their parent in the request itself. The fix is the
  `TransactionApprovalView`/`RecentTransactionsQuery` blanket-key pattern for a third time: a new
  `PermissionKeys.AttachmentAccess` (Admin+Member, seeded, gates nothing on its own) gets the request
  through `AuthorizationBehavior`, and the handler then loads the row and calls the new
  `GrantedPermissionReader.EnsureGrantedAsync` against `ParentPermissions.{Edit,View}PermissionFor`
  the row's actual parent — throwing the identical `ForbiddenException` shape
  `AuthorizationBehavior` would have, so a caller cannot tell which layer refused them. Proven by two
  new handler tests plus the E2E's View/Edit split (denying Edit still permits reads; denying View
  too then refuses reads by name).
- **`SalesOrder`/`ProductionOrder` gained `CustomStatusId` as a plain nullable column, not gated by
  `EnsureDraft`** — identical reasoning to Quotation/Purchase Order in 20b: no GL or stock weight,
  confirmed settable on Draft and Approved rows alike.
- **`ListProductionOrdersQuery`'s DTO gained `CustomStatusId`**, since the Production Order list page
  needed it to feed the picker — the same shape `ListSalesOrdersQuery`'s existing `SalesOrder` model
  already carried it through (this phase added the field to that model too).
- **`AccountOpeningBalanceDto`/`ProductOpeningBalanceDto` both gained `LineId`** so the Angular row
  forms know which id to tag — `null` until a balance has actually been set for that row, since
  there's nothing to tag before then.
- **The client-side sweep used a single generator script per language (Python, over `Bash`), not
  sixteen hand edits.** A sweep phase's whole point is uniform treatment; hand-editing sixteen
  near-identical detail pages is sixteen chances for one to diverge. Every anchor the script matched
  against (the header `<div>`, the route-id field declaration, the `.subscribe({` call sites) was
  asserted to occur exactly the expected number of times, so a page whose shape didn't match would
  have failed loudly rather than being silently skipped or malformed.
- **Custom field commits ride the document's own Save via a new `commitCustomFieldsThen` rxjs
  operator**, not a hand-nested `subscribe` inside `subscribe` repeated 13 times (phase 20a's
  original two-page pattern). A failed commit never turns a successful document save into an
  apparent failure — the document really was created; the operator reports the custom-field error
  separately and still lets the page's own `next` handler navigate or reload.

## Permission-key derivation

No new document-attached permission keys were added beyond the one blanket key described above
(`Workflow.Attachment.Access`). Every mechanism continues riding the target document's own existing
`{Module}.{DocumentType}.{Edit,View}` pair — the decision Phase 19 made and 20a/20b reaffirmed, now
applied uniformly via `DocumentPermissions`. `ParentPermissions` additionally special-cases `Contact`
to its own pre-split `Contact.{View,Manage}` pair, since Contacts predate the Edit-key convention.
`Organization` (a `TaskParentType`-only, non-document parent) is unreachable from `ParentPermissions`
by design, with a guard test (`The_only_non_document_parents_are_contact_and_organization`) pinning
that these two are the only non-document members across all three parent enums.

## Manual E2E (fresh Organization, 2026-09-03)

Master data (warehouses, a category, a UOM, two contacts, two products, four accounts) seeded via
direct API calls; every call's status code printed rather than piped to `/dev/null` (the phase-26c
lesson). One DRAFT document of every newly-swept type created — SalesOrder, CreditNote, Payment,
PurchaseOrder, PurchaseBill, Expense, DebitNote, JournalVoucher, CashTransfer, WarehouseTransfer,
InventoryAdjustment, ProductionOrder, ProductionJournal (13/13 created on the first clean run).

**Positive path, 61/61 automated checks passed**, each a genuine round-trip (write via the real
command, then read back via the real query — never trusting the write's own 200):
- Custom fields on all 11 applicable types among those 13 (SalesOrder through ProductionJournal,
  excluding WarehouseTransfer/InventoryAdjustment as confirmed).
- Reporting tags on all 13 created types, **plus** OpeningBalance and OpeningStock (set via
  `PUT /opening-balances/accounts` and `/products`, tagged by the returned row's `lineId`, verified
  separately with 2/2 passing).
- Comments on all 13 (posted, then confirmed present exactly once in the list — re-run-safe).
- Tasks on all 13 (same shape).
- The Activity tab's audit feed reachable on three sampled types (SalesOrder, PurchaseBill,
  ProductionOrder).
- Custom status assigned on both new types (SalesOrder, ProductionOrder).

Every write was also verified via `sqlcmd` directly against the tables (`configuration.
CustomFieldValues`, `configuration.TransactionReportingTags`, `contacts.Comments`,
`workflow.Tasks`, and `CustomStatusId` on `sales.SalesOrders`/`manufacturing.ProductionOrders`),
confirming persisted rows per `DocumentType`/`ParentType` group — not just the API's own read-back
(the select-race-family lesson: a UI or API response can lie about what actually persisted).

**Negative path, a purpose-built custom Role (`NoJournalVoucherEdit`) denying exactly
`Accounting.JournalVoucher.Edit`**, fired against a document id guaranteed not to exist:
- Custom-field write, reporting-tag write, and comment-post on JournalVoucher **all refuse with 403
  naming `Accounting.JournalVoucher.Edit`** — proving `AuthorizationBehavior` fired before any
  handler could even attempt a 404.
- With only Edit denied, attachment-list and comment-list reads **still succeed (200)** — proving
  View and Edit are genuinely separate keys, not one blanket gate.
- Denying `Accounting.JournalVoucher.View` too then makes those same three reads **refuse with 403
  naming that key** — proving the View half of the split actually gates something.
- The same role, still holding `Sales.SalesOrder.Edit`, **can** attempt the identical reporting-tags
  write against a nonexistent SalesOrder id and gets **404** (not 403) — proving the refusal above
  was permission-specific to JournalVoucher, not a broken pipeline.

9/9 negative-path checks passed. The tester's role was restored to system Admin afterward (this
identity persists across phases per CLAUDE.md's working-practices note).

## What's next

Phase 27b (Output): print/PDF for the 9 unwired `DocumentType`s and both production documents,
BS dates in server-rendered PDFs/`.xlsx`, the three missing pagers, Turnstile on the New Organization
wizard, a feature-flag route guard. Then phases 28+ per `docs/roadmap.md`.

Nothing from 27a was flagged or deferred — all four mechanisms are fully wired for their confirmed
scope, both guard tests pass, and the manual E2E round-tripped every mechanism on every newly-swept
type plus the negative path.
