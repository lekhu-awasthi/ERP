# Phase 13 status — Tasks

**Status: COMPLETE.** `WorkTask` (`Domain.Workflow`) is the second Workflow-context feature
(architecture-spec.md §4.9) and the first *real aggregate* in that bounded context — Phase 12's
`TransactionApprovalQuery` was a pure read with no schema of its own. Per product-requirements.md
FR-10.1, it's a general-purpose polymorphic task manager attachable to a Contact or the Organization
itself, with its own Pending/Started/Done lifecycle independent of whatever it's attached to.
`erp-module-scan.md` line 106-107 confirms the live field list and the "3 status tabs" UI shape; line
315 names a separate "Workflow (config) > Task Types" screen, the phase's one real modeling
ambiguity (see scope decision #1).

## Roadmap/brief exit criteria — final status

- [x] `WorkTask` aggregate (`workflow` schema) — `Create`/`Update`/`TransitionStatus`, no Draft/
      Approve lifecycle at all, `Update` guarded by `Status != Done` (scope decision #2)
- [x] `TaskType` modeled as a real tenant-editable lookup entity (`configuration` schema), not a
      hardcoded enum, reusing the generic `ListLookupsQuery<T>`/`DeleteLookupCommand<T>` pair
      (scope decision #1)
- [x] `TaskParentType` — a small dedicated enum (`Contact`, `Organization` only), not `DocumentType`
      and not a speculative broader set (scope decision #3)
- [x] `CreateTaskCommand`/`UpdateTaskCommand`/`UpdateTaskStatusCommand`/`ListTasksQuery` — the exact
      tight command/query surface the brief specified, no more
- [x] `IsPrivate` really enforced at query time (excluded unless the caller is the creator or
      assignee), not stored-but-inert (scope decision #4)
- [x] `TransitionStatus` only allows a strictly-forward move (Pending→Started, Pending→Done,
      Started→Done); backward and no-op transitions rejected with a 409 (scope decision #5)
- [x] `Workflow.TaskView`/`Workflow.TaskManage` — a View/Manage pair, not the four-key maker-checker
      shape, both granted to Member (scope decision #6)
- [x] Two Angular integration points sharing one component, not a new page: Contact detail page's
      first-ever tab-switching mechanism plus a Tasks tab, and the Organization dashboard's Tasks
      section
- [x] `TaskCommandHandlerTests` (12 tests) covering parent/assignee/type existence validation, the
      forward-only status state machine, `IsPrivate` visibility, the `Status?` filter, and
      parent-scope isolation (Contact vs Organization, and across Organizations)
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged, Application.UnitTests 156 — 12 new
      + 144 pre-existing, Api.IntegrationTests 4, all green, Docker Desktop running this session) and
      `ng build`/`ng test` (7 pre-existing specs green, no new Angular specs) all pass
- [x] Confirmed by hand end-to-end against the real API/DB/browser (see "Manual E2E" below)

## Scope decisions

1. **`TaskType` is a real lookup entity, not a hardcoded enum.** The brief flagged this as the one
   real judgment call: `erp-module-scan.md` line 106-107's Tasks-list data model names `type` as a
   fixed `FollowUp/Notify/Email/Other` enum, but line 315 separately documents a "Workflow (config) >
   Task Types" screen with its own `{id, name, color}` shape — the same structure `CreditTerm`/
   `PaymentMode`/`TdsType` already have their own dedicated management screens for. The competing
   evidence was weighed explicitly (see `WorkTask.cs`'s and `TaskType.cs`'s own doc comments) and
   resolved in favor of the lookup entity: this codebase's established precedent is "a confirmed
   dedicated management screen → a generic lookup entity," and Type is the only field among
   Type/Priority/Status with that kind of screen behind it. `Priority` and `Status` have no competing
   lookup-screen evidence anywhere in the scan, so both stayed plain C# enums (`TaskPriority`,
   `WorkTaskStatus`) as the brief expected.

2. **No Draft/Approve lifecycle at all — `Update()` is guarded by `Status != Done` instead of
   `EnsureDraft()`.** Every other aggregate in this codebase (`ApprovableTransaction`) has a
   Draft→Approve state machine with document numbering assigned at Approve. `WorkTask` has neither:
   no `Code`, no `IDocumentNumberGenerator` call, no `DocumentNumberingRule` row, and no `RowVersion`/
   GL/posting-rule machinery. The only terminal state is `Done`, so `Update` (and, implicitly,
   further status transitions) is blocked once a task reaches it — the same guard-method *shape*
   every `ApprovableTransaction`'s `EnsureDraft()` uses, just keyed off a different terminal state,
   per the brief's own framing.

3. **`TaskParentType` is a small dedicated enum (`Contact`, `Organization`), not `DocumentType`.**
   `DocumentType` (`Domain.Common`) already has 18 entries, but every one of them is either a real
   `ApprovableTransaction` type or a numbering-pool-only stub (`Account`/`Contact`/`Product`) — a
   different semantic than "what kind of entity can a Task attach to" — and it has no `Organization`
   entry at all (confirmed by reading the file, not assumed, per the brief). Both
   `erp-module-scan.md`'s "presumably others" and architecture-spec.md's "and likely others" are
   explicitly hedged, unconfirmed guesses about a third parent type, not a shape to build against —
   this repo's own precedent (e.g. Annex 5's status doc) is to ship only what's confirmed and leave
   the rest as a documented future seam. Stored as `(ParentType, ParentId)` — a generic `Guid`
   column, not per-parent-type nullable FKs — so a third parent type later is additive (a new enum
   value plus a new `WorkflowValidation.EnsureParentExistsAsync` branch), not a schema break. No real
   FK constraint exists against `ParentId` for either value: `Organization`'s only valid `ParentId`
   is the command's own (already-membership-checked) `OrganizationId`, so that branch is a plain
   comparison, not a query; `Contact`'s branch does query `Contacts`.

4. **`IsPrivate` is really enforced, not stored-but-inert.** The brief flagged this as worth a
   deliberate decision, citing this codebase's own "no silently-inert fields" ethos (e.g. Phase 8b's
   explicit omission of columns with no backing capability, rather than shipping always-zero
   placeholders). `ListTasksQueryHandler` filters `!x.IsPrivate || x.CreatedByUserId == userId ||
   x.AssignedToUserId == userId` directly in the LINQ `Where` (translates fine against SQL Server —
   this is a concrete, non-generic query, not the generic-`Func`-in-`.Where()` translation gotcha
   CLAUDE.md's known-gotchas list warns about), confirmed both by a unit test and by a real curl-
   driven manual E2E run (see below): a third invited Member correctly got an empty list for a
   private task, while the creator and the assignee both saw it.

5. **`TransitionStatus` only allows a strictly-forward move.** The brief asked explicitly whether
   backward transitions (e.g. Started→Pending) should be legal, and to document the decision either
   way rather than defaulting silently. Decided **no** — `erp-module-scan.md`'s confirmed live UI only
   ever shows a forward-moving per-row complete checkmark, never a "reopen"/"revert" action, so
   `WorkTaskStatus`'s declared enum order (`Pending < Started < Done`) is compared directly
   (`newStatus <= Status` is rejected) rather than modeling a full bidirectional state machine on
   spec. This also means a task can skip `Started` entirely and go straight from `Pending` to `Done`
   in one call — matching "per-row complete checkmark" being the only confirmed forward action, not
   a two-step "Start then Complete" requirement. `UpdateTaskStatusCommand` is one command covering
   every legal transition (mirroring how every `ApproveXCommand` is one command per document type,
   not per possible prior state), not separate `StartTask`/`CompleteTask` commands.

6. **`Workflow.TaskView`/`Workflow.TaskManage` — a View/Manage pair, both granted to Member.** Not
   the four-key `{View,Create,Edit,Approve}` maker-checker shape every `ApprovableTransaction` uses
   (`WorkTask` has no Approve concept), and not Member-View-only either: Task is routine daily-use
   working data any Member should be able to create/complete, the same reasoning that earned Contact/
   Product their Member-View+Manage grant in Phase 3, explicitly weighed against Phase 2's
   Member-View-only taxonomy-lookup precedent and decided the other way (a financial-document-style
   maker-checker split doesn't fit a task list). `TaskType` gets the opposite, ordinary Configuration-
   lookup split (Member View-only, Admin write) — the same shape as `CreditTerm`/`PaymentMode`/
   `TdsType`, since it's tenant-wide control-plane data, not routine working data.

7. **A small `ListOrganizationMembersQuery` (`Application.Tenancy.Queries.ListOrganizationMembers`)
   was added, beyond the brief's named command/query surface.** The Assigned-To picker Tasks need
   (both the confirmed live "Assigned To" column and `CreateTaskCommand`/`UpdateTaskCommand`'s own
   `AssignedToUserId` validation against Accepted `OrganizationMembership` rows) has no existing way
   to list an Organization's members with display names anywhere in this codebase — every prior
   phase either queries a single membership row or lists *other Organizations*, never *other
   members of this one*. Judged in-scope rather than a speculative addition: it's the minimum
   supporting infrastructure the Assigned-To field needs to be usable at all, not a standalone
   feature. Gated on `PermissionKeys.TaskView` rather than minting a new Tenancy-level "view members"
   key nothing else needs yet — its only consumer is the Task feature's picker.

## Angular

Two integration points share one component (`features/workflow/task-list`), not two separate
implementations — the same "don't duplicate, extract a shared reader" discipline Phase 10's
`ContactLedgerReader` established for query handlers, applied here to Angular. The component takes
`organizationId`/`parentType`/`parentId` as Angular 21 signal inputs (`input.required<T>()`) and owns
its own status sub-tabs, inline create form, and per-row Start/Complete actions.

- **Contact detail page** (`contact-detail-page`): built this page's first-ever tab-switching
  mechanism. Until this phase, the vertical tab list was a single hardcoded always-`active` "Overview"
  button with no click handler or switching signal at all (confirmed by reading the file before
  assuming otherwise, per the brief). Added a `activeTab = signal<'Overview' | 'Tasks'>('Overview')`,
  reset to `'Overview'` on every route-paramMap navigation (the same route-reuse-safe pattern this
  page already uses for every other piece of per-record state, per CLAUDE.md's own route-reuse
  gotcha), and a Tasks tab (hidden for `isNew()`, since a not-yet-created Contact has no id to scope
  tasks to).
- **Organization dashboard page** (`organization-dashboard-page`): a new full-width "Tasks" card
  below the existing Workspace Parameters/Invite Team Member row, parameterized with
  `parentType="Organization"` and `parentId = organizationId()`.

No dedicated Angular admin screen was built for `TaskType` CRUD — its Create/Update/Delete commands
and generic List endpoint all exist and work (confirmed via curl), but there's no
`task-type-list-page` the way `tds-type-list-page` exists for `TdsType`. This mirrors this
codebase's own established precedent: `configuration-shell.ts`'s own doc comment already notes that
`CustomStatus`/`ReportingTagCategory`/`ReportingTagOption`/`CustomFieldDefinition` have working APIs
but no Angular screen either. Building one was judged out of scope for a phase whose Angular brief
was explicitly "two integration points, not one new page."

## Bugs hit and fixed

**One real bug, caught by `dotnet build` before any test ran — not a codebase defect in prior code,
but a genuine gotcha worth recording for the next phase that touches `Application.Workflow`:**
naming the Domain aggregate the bare `Task` (as product-requirements.md/erp-module-scan.md and the
brief's own prose call it) collides with `System.Threading.Tasks.Task`/`TaskStatus`, both implicitly
in scope in every async C# file in this codebase (`ImplicitUsings`) and both used by literally every
MediatR handler's own `Task<TResult> Handle(...)` signature. The moment a handler file needs both
`ErpApp.Domain.Workflow`'s namespace and its own `async Task<TResult>` return type, "Task"/
"TaskStatus" become ambiguous. Fixed by naming the Domain type `WorkTask` and the status enum
`WorkTaskStatus` instead — every other layer (permission keys `Workflow.TaskView`/`TaskManage`, the
`Tasks` table/DbSet name, the `/tasks` routes, every DTO, every Angular symbol) still uses plain
"Task" throughout; only these two C# types needed the rename. Worth checking for again before naming
any future Domain type after a common BCL word — `TaskType`, `TaskPriority`, and `TaskParentType` all
checked clean against the BCL and needed no rename.

One Angular build-time mistake, also caught before any test ran: an early draft of
`core/workflow/workflow.models.ts`/`workflow.service.ts` was created via a blind `Write` (not `Read`-
then-`Edit`) and silently clobbered Phase 12's existing `TransactionApprovalRowDto`/
`TransactionApprovalQueueDto`/`getTransactionApprovalQueue` content that already lived at that exact
path — `ng build` immediately surfaced it as broken imports in
`transaction-approval-queue-page.ts`. Recovered the original content via `git show HEAD:...` and
merged both phases' content into the same two files, and renamed the new `TaskList` response DTO to
`TaskListDto` (matching the C# `TaskListDto` name) specifically to avoid it colliding with the new
`TaskList` Angular *component* class the same feature also introduces. Worth remembering before
writing to any `core/*` file without checking first whether a path already has content from an
earlier phase — `Glob`/`Read` first, even when a fresh scaffold seems likely.

## Manual E2E

Confirmed by hand end-to-end against the real API/DB/browser, seeded via curl + a cookie jar per this
session's own memory note (reserve browser clicks for this phase's own new UI): a fresh Admin
registered, verified (verification code read directly from `[identity].VerificationCodes` — no
console-log email stub exists in this codebase, real SMTP is configured, so the code was pulled via
`sqlcmd` against the local dev SQL Server, bracket-quoting the `identity` schema per CLAUDE.md's own
gotcha), logged in, and created an Organization, a Customer Contact, and a `TaskType` ("Follow up").
Created a Task on the Contact and a Task on the Organization via the API — `GET /tasks` scoped to
each parent returned exactly the one row for that parent, confirming parent-scope isolation.
Transitioned the Contact's task Pending→Started (200), attempted Started→Pending (409, "Cannot move a
task from Started to Pending"), then Started→Done (200) — the `Status?` filter then correctly
returned it under `status=Done` and excluded it under `status=Pending`. Invited two more users
(Member 2, Member 3), registered/verified/logged them both in, accepted both invitations, and
confirmed `GET /members` lists all three with correct names/roles. Admin then created a private Task
on the Contact assigned to Member 2 — the creator (Admin) and the assignee (Member 2) both saw it in
their own `GET /tasks?status=Pending` call, while the third Member's identical call returned an empty
list. Finally, drove the real Angular UI in the Browser tool: logged in as Admin, saw the Organization
dashboard's new Tasks card render "Renew subscription" and correctly move from the Pending tab to the
Started tab after clicking "Start" (with the Start button disappearing once Started, matching the
forward-only transition); navigated to the Contact detail page, clicked between the new
Overview/Tasks tabs (the first real tab-switching this page has ever had), saw the private
"Confidential negotiation" task with its lock icon and "Member 2" assignee correctly rendered, and
created a new task ("Send welcome package") through the inline form — it appeared in the Pending list
immediately, TaskType/Priority/Assignee dropdowns all correctly populated from the real API.
