# Phase 15 status — Deals

**Status: COMPLETE.** `Deal` (`Domain.Crm`) is the CRM module's first feature (architecture-spec.md
§4.2 / product-requirements.md FR-4.7), scoped to Deals only this phase per the roadmap's Phase 8+
"CRM: Deals, SMS" bullet — SMS is deferred to its own Phase 16 (needs its own gateway/credit-ledger/
template infrastructure, the same reasoning that split the Reports module into 8a–8f rather than one
giant phase). `erp-module-scan.md`'s CRM section confirms the live shape: a pipeline tracker with 3
status tabs (Pending/Won/Lost), list columns Closing Date/Created At/Details/Stage (inline dropdown)/
Contact/Expected Revenue/Assigned To (multi-avatar), and a New Deal form with no Stage field at all
(Deal Contact\*, Title\*, Assign To, Lead Source, Description, Expected Revenue, Expected Closing
Date, "Make this deal private"). `LeadSource`/`DealStage` are confirmed separate CRM (config) lookup
screens.

## Roadmap/brief exit criteria — final status

- [x] `DealStage`/`LeadSource` modeled as real tenant-editable lookup entities (`configuration`
      schema), reusing the generic `ListLookupsQuery<T>`/`DeleteLookupCommand<T>` pair (scope
      decision #1)
- [x] `Deal.Assignees` is a genuine multi-valued encapsulated child collection (`DealAssignee`), not
      a scalar FK — internal `AddAssignee`/`RemoveAssignee`, diffed explicitly on Update rather than
      Clear+re-Add (scope decision #2)
- [x] `IsPrivate` enforcement extended to "any assignee, not just one" — real query-time filtering,
      confirmed by both a unit test and a curl-driven manual E2E run with two assignees and a third
      outsider (scope decision #3)
- [x] Won and Lost are both terminal, made explicit rather than defaulted — `UpdateDealCommand`/
      `MoveDealToStageCommand` both reject once `Status != Pending`; `DealStage.SortOrder` is
      display-ordering only, not an enforced state-machine sequence (scope decision #4)
- [x] Contact-Type restriction — Customer/Lead allowed, Supplier rejected with a 409 (scope
      decision #5)
- [x] `CreateDealCommand`/`UpdateDealCommand`/`MoveDealToStageCommand`/`MarkDealWonCommand`/
      `MarkDealLostCommand`/`ListDealsQuery` — the command/query surface the brief specified
- [x] `Crm.Deal.View`/`Crm.Deal.Manage` — a View/Manage pair, both granted to Member;
      `Crm.LeadSource.*`/`Crm.DealStage.*` follow every other Configuration lookup's Member-View-
      only/Admin-write split
- [x] Two Angular integration points sharing one component (`deal-list`), not a new page: Contact
      detail page's new Deals tab (hidden for Supplier) and the Organization dashboard's Deals
      section
- [x] `DealCommandHandlerTests` (9 tests) covering the terminal Won/Lost guard, `IsPrivate`
      visibility across multiple assignees, Contact-Type restriction, the `Status?` filter, and
      org-scope isolation
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged, Application.UnitTests 181 — 9 new
      + 172 pre-existing, all green) and `ng build`/`ng test` (7 pre-existing specs green, no new
      Angular specs) all pass
- [x] Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below)

## Scope decisions

1. **`LeadSource`/`DealStage` are real lookup entities, not hardcoded enums.** The brief flagged
   this as the default resolution from Phase 13's `TaskType` precedent, and no competing evidence
   argued otherwise: `erp-module-scan.md` line 311-312 confirms two separate CRM (config) management
   screens (`LeadSource { id, name }`, `DealStage { id, name, sortOrder, color? }`), the same
   "confirmed dedicated management screen → generic lookup entity" precedent every other
   Configuration lookup in this codebase already follows. `DealStatus` (Pending/Won/Lost) stayed a
   plain enum — a fixed 3-value lifecycle with its own dedicated domain methods (`MarkWon`/
   `MarkLost`), no competing lookup-screen evidence, exactly like `WorkTaskStatus`.

2. **`Deal.Assignees` is a genuine multi-valued encapsulated child collection.** Unlike `WorkTask`'s
   single scalar `AssignedToUserId`, `erp-module-scan.md`'s confirmed "Assigned To (multi-avatar)"
   list column and the data model's own `assignees[]` field are a real new shape for this codebase —
   no existing aggregate had a many-to-many assignee list before this phase. Modeled as an
   encapsulated, private-backing-field child collection (`DealAssignee { Id, DealId, UserId }`), the
   same "child line, called by its own parent aggregate" pattern `JournalVoucher.Lines`/
   `Product.SecondaryUnits` already use — internal `AddAssignee`/`RemoveAssignee` methods, not a
   wholesale-replace-on-every-save. `UpdateDealCommandHandler` diffs the desired assignee set against
   the currently-loaded one and calls `RemoveAssignee`/`AddAssignee` only for the actual deltas — the
   same discipline CLAUDE.md's own Clear+re-Add InMemory-provider-mistracking gotcha calls for,
   applied here even though `DealAssignee` isn't replaced via a blind collection swap the way that
   gotcha originally described. Each assignee is validated against Accepted `OrganizationMembership`
   rows via `CrmValidation.EnsureAssigneesAreAcceptedMembersAsync`, mirroring
   `WorkflowValidation.EnsureAssigneeIsAcceptedMemberAsync`'s precedent extended for a plural set; the
   Angular multi-select picker reuses Phase 13's `ListOrganizationMembersQuery` as-is (already gated
   on a Workflow-context key, left unchanged — CRM's own `Crm.Deal.View` gates the Deal list/create
   surface itself, and the member picker is "necessary supporting infrastructure" the same way Phase
   13 justified adding that query in the first place).

3. **`IsPrivate` enforcement extends to "any assignee," not just one.** `ListDealsQueryHandler`'s
   `Where` clause is `!x.IsPrivate || x.CreatedByUserId == userId || x.Assignees.Any(a => a.UserId ==
   userId)` — a straightforward extension of `ListTasksQueryHandler`'s single-assignee check to
   `Assignees.Any(...)`, which translates fine against SQL Server as an `EXISTS` subquery over the
   real `DealAssignees` table (confirmed by the same `.Include(x => x.Lines)`/`.Include(x =>
   x.SecondaryUnits)` precedent already established elsewhere in this codebase — a concrete
   navigation-collection `Where`, not the generic-`Func`-in-`.Where()` translation gotcha CLAUDE.md's
   known-gotchas list warns about). Confirmed by a unit test with two assignees plus a creator plus an
   outsider, and again in this phase's own manual E2E with three real invited Members.

4. **Won and Lost are both terminal.** The brief's own framing for this judgment call:
   `erp-module-scan.md`'s confirmed live UI never shows a "reopen"/"revert" action on a closed Deal,
   only a `Stage` inline dropdown while still Pending. Resolved as recommended — `Deal.MarkWon`/
   `MarkLost`/`Update`/`MoveToStage` all call a shared `EnsureOpen()` guard rejecting once `Status !=
   Pending`, mirroring `WorkTask`'s own "Done blocks further edits" guard shape but for two terminal
   states instead of one (no ordinal-comparison state machine the way `WorkTaskStatus` uses, since
   Pending can go to either Won or Lost, never between the two). `DealStage.SortOrder` is confirmed
   display-ordering-only, not an enforced sequence — the live UI shows a plain inline dropdown, not a
   per-row forward-only checkmark, so `Deal.MoveToStage` accepts any active `DealStageId` for this
   Organization with no ordering check at all.

5. **Contact-Type restriction: Customer and Lead allowed, Supplier rejected with a 409.** Not
   confirmed either way in `erp-module-scan.md`; resolved per the brief's recommended default and
   documented explicitly rather than left implicit — a Deal is a pre-sale/sales-pipeline concept.
   `CrmValidation.EnsureContactCanHaveDealAsync` loads the Contact first (404 if it doesn't exist at
   all — `NotFoundException`) and only then checks `Type != Supplier` (409 — `ConflictException`),
   the same "doesn't exist" vs. "exists but violates a business rule" distinction
   `SalesValidation`/`PurchasingValidation` draw elsewhere in this codebase, made explicit here since
   `SalesValidation.EnsureContactExistsAsync` itself collapses both cases into a single 404 (a
   deliberate divergence for this handler, not an oversight).

## Command/query surface

`Application.Crm` (new bounded context, per architecture-spec.md's own module map placement):

- `CreateDealCommand`/`UpdateDealCommand` — Contact/Title/AssigneeUserIds/LeadSourceId/Description/
  ExpectedRevenue/ExpectedClosingDate/IsPrivate, no Draft/Approve lifecycle, same as `WorkTask`.
- `MoveDealToStageCommand(DealId, DealStageId)` — separate from `UpdateDealCommand`, mirroring
  `UpdateTaskStatusCommand`'s own separation of the state-changing action from the general edit; named
  `MoveDealToStageCommand` rather than `UpdateDealStageCommand` specifically to avoid colliding with
  `Application.Configuration.Commands.UpdateDealStage` (the `DealStage` *lookup*'s own Update command)
  — the brief's own called-out rename.
- `MarkDealWonCommand(DealId)`/`MarkDealLostCommand(DealId)` — two separate commands (not one
  `UpdateDealStatusCommand` with an enum param), per architecture-spec.md's own named method shape.
- `ListDealsQuery(OrganizationId, ContactId?, Status?)` — `ContactId` is an optional filter (not
  required the way `ListTasksQuery`'s `ParentType`/`ParentId` are), since `Deal` is tied to a single
  Contact directly rather than a polymorphic parent, and the confirmed live UI shows Deals both scoped
  to one Contact and unscoped across the whole pipeline.
- `Application.Configuration`: `CreateLeadSourceCommand`/`UpdateLeadSourceCommand`,
  `CreateDealStageCommand`/`UpdateDealStageCommand` (the lookup CRUD, reusing the generic
  `ListLookupsQuery<T>`/`DeleteLookupCommand<T>` pair for List/Delete).

New permission keys: `Crm.Deal.View`/`Crm.Deal.Manage` (Member gets both — routine daily-use working
data, the same reasoning that earned `Workflow.TaskView`/`TaskManage` their Member grant per
`product-requirements.md`'s Sales Staff persona), `Crm.LeadSource.{View,Manage}`/
`Crm.DealStage.{View,Manage}` (ordinary Member-View-only/Admin-write Configuration-lookup pairs, same
shape as `TdsType`/`TaskType`). `RolePermissionConfiguration.HasData` was updated before scaffolding
the migration, per CLAUDE.md's own stale-`HasData`-produces-an-empty-migration gotcha.

## Angular

One `deal-list` component (`features/crm/deal-list`) shared across both integration points, the same
"don't duplicate, extract a shared reader" discipline Phase 13's `TaskList` established:

- **Contact detail page**: a new Deals tab alongside Overview/Tasks, hidden for a Supplier Contact
  (`contact()?.type !== 'Supplier'`) — the same restriction `CrmValidation.EnsureContactCanHaveDealAsync`
  enforces server-side, surfaced in the UI too rather than left to a 409 the user would only see after
  submitting. `contactId` is bound to the route's Contact id, so the create form's Contact field is
  implied and hidden.
- **Organization dashboard page**: a new full-width Deals card below the Tasks card, `contactId`
  unbound — the create form instead shows a Contact picker sourced from `ContactsService.listContacts`
  filtered client-side to exclude `Supplier` (the server still enforces this independently; the
  client-side filter is a UX convenience, not the actual guard).

Assignee selection is a checkbox list (`ConfigurationService`'s `listLeadSources`/`listDealStages` and
`OrganizationsService.listMembers` all feed the create form), not a native `<select multiple>` — a
multi-select native element would reintroduce a variant of CLAUDE.md's own `[value]`-vs-`@for` select
race for no benefit here, since a checkbox-per-member list needs no such binding at all. Every other
`<select>` this phase touches (Lead Source, Contact picker, the per-row inline Stage dropdown) follows
CLAUDE.md's `[selected]`-per-option convention throughout, including under `formControlName` (the
`organization-dashboard-page`'s own Phase 14 precedent for a `formControlName`-managed select whose
options still resolve on their own async signal timeline) — the per-row Stage dropdown specifically
uses a plain `(change)` handler reading `event.target.value` rather than any Angular value-binding at
all, sidestepping the whole class of bug outright since a fresh row's options are re-created on every
`@for` re-render.

No dedicated `LeadSource`/`DealStage` admin screens were built — mirrors this codebase's own
established precedent (`TaskType` shipped in Phase 13 with a working API and no screen); confirmed via
curl only, per the brief.

## Bugs hit and fixed

One compile-time type mismatch, caught immediately by `dotnet build` before any test ran, not a
codebase defect: `OrganizationMembership.UserId` is `Guid?` (nullable — an invite is addressed to an
email, not yet a resolved user, per that type's own doc comment), so
`CrmValidation.EnsureAssigneesAreAcceptedMembersAsync`'s first draft (`distinctIds.Contains(x.UserId)`
against a `List<Guid>`) failed `CS1503`. Fixed with an explicit `x.UserId != null &&
distinctIds.Contains(x.UserId.Value)` guard. No other build or test failures this phase.

## Manual E2E

Confirmed by hand end-to-end against the real API/DB/browser, seeded via curl + a cookie jar per this
session's own memory note (reserve browser clicks for this phase's own new UI): a fresh Admin
registered/verified (verification code read directly from `[identity].VerificationCodes` via
`sqlcmd`)/logged in, created an Organization, a `LeadSource` ("Referral"), two `DealStage`s
("Qualified"/"Negotiation"), a Customer Contact ("Acme Retail") and a Supplier Contact ("Acme
Supplies"). `POST /deals` against the Supplier returned a real `409`; the same call against the
Customer, with `isPrivate=true` and `assigneeUserIds` naming two not-yet-existing Members, succeeded
once both were invited/registered/verified/accepted. `GET /deals?status=Pending` returned the deal for
the Admin (creator) and both assignees, and an empty list for a third invited-and-accepted Member with
no relationship to the deal at all — the actual proof this phase's `IsPrivate`-across-multiple-
assignees scope decision exists for. The deal was moved through both stages via `PUT
/deals/{id}/stage`, then `POST /deals/{id}/mark-won` succeeded once — a further `PUT /deals/{id}`
and a further `PUT /deals/{id}/stage` both returned a real `409`, and `GET /deals?status=Won`
correctly listed it with `closingDate` populated. Finally, drove the real Angular UI in the Browser
tool: the Organization dashboard's new Deals card correctly showed the deal under its Won tab with the
right Stage/Expected Revenue/Closing Date/both assignee names/Won badge; the Customer Contact's new
Deals tab rendered the same shared component correctly scoped to that Contact; the Supplier Contact's
detail page correctly showed no Deals tab at all (Overview/Tasks only).
