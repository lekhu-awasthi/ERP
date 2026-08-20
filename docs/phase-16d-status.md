# Phase 16d — System Audit report

## TL;DR
Append-only Audit trail (`workflow.Audits`) written by a new `AuditBehavior` pipeline step (5th,
after `LockDateBehavior`) for every Create/Update/Approve/Void of the 13 ApprovableTransaction
document types. Two new marker interfaces (`IAuditableRequest`/`IAuditableRequestWithId`) let
Create/Update commands declare their DocumentType without a 50-branch switch in the behavior;
Approve/Void reuse the existing `ILockDateSensitiveDocument` interface instead of adding a
redundant one. Immutability is enforced twice: Domain-level (private ctor, no mutators) and a real
mechanism (`AppDbContext.SaveChangesAsync` override throws on any tracked `Modified`/`Deleted`
`Audit` entity, proven by a unit test). A new paginated, filterable `Reports.SystemAudit.View`
(Admin-only) report screen mirrors the Phase 16c report shape, including spreadsheet export.
Tests: Domain.UnitTests 76 (unchanged), Application.UnitTests 216 (4 new), Api.IntegrationTests
+3 new InMemory-provider tests for the immutability interceptor (the pre-existing 5
Testcontainers-based tests weren't re-run this session — Docker Desktop wasn't running locally),
Angular 7 specs (unchanged). Full real-API manual E2E below. All green.

## Why this phase exists
FR-9.6 / NFR-3.3: "Auditable by design." `LoggingBehavior`'s own doc comment had pointed at this
exact phase since Phase 0; architecture-spec.md §3.9 specifies the shape (`IPipelineBehavior`
logging `UserId, Action, DocumentType, DocumentId, Timestamp`), which this phase implements as
written.

## Scope decisions (with reasoning)

### 1. Audited commands: the 13 ApprovableTransaction types' Create/Update/Approve/Void only
Administrative actions (InviteUser, UpdateRolePermissions, SetOrganizationLockDate, lookup CRUD,
etc.) are real NFR-3.3 candidates too, but they have no DocumentType/DocumentId at all — a
materially different shape. Explicitly **out of scope** this phase, matching the roadmap's own
document-centric framing ("document type/id ... each row linking to the affected record"). Flagged
as a follow-up via `spawn_task` rather than left silently unstated, the same discipline Phase 16c
used for its picker-dropdown gap.

"Convert" is not a distinct audited action: every conversion in this codebase (e.g.
`CreateInvoiceCommand`'s optional `ReferrerType`/`ReferrerId`) is a plain Create under the hood, so
it's already covered by the "Create" action prefix. No command in this codebase implements a
separate Convert verb.

### 2. The marker-interface shape
- **Approve/Void**: reuse `ILockDateSensitiveDocument` (`LockDateDocumentType`/`LockDateDocumentId`)
  directly — every one of the 13 types' Approve/Void commands already implements it (Phase 16a), and
  it carries exactly what's needed. No new interface.
- **Update**: new `IAuditableRequestWithId : IAuditableRequest` — `AuditDocumentType` (computed
  property, one line) + `AuditDocumentId => Id` (the command already carries `Id`). Confirmed the
  shape generalizes cleanly across Sales/Purchasing/Payments/Inventory/Accounting modules (not just
  Invoice) before committing to it — all 13 Update commands have the same `Guid Id` positional
  parameter.
- **Create**: new `IAuditableRequest` only (`AuditDocumentType`) — the new document's Id isn't known
  pre-handler. `AuditBehavior` resolves it via reflection on the handler's response
  (`typeof(TResponse).GetProperty("Id")`) instead of a second marker interface requiring an edit to
  ~50 `CreateXResult`/`UpdateXResult`/`ApproveXResult`/`VoidXResult` records that already all share a
  leading `Guid Id` property by convention. This is a plain post-execution read of a CLR object, not
  an EF LINQ query — the "generic `Func` selector fails EF translation" gotcha (phase-9-status.md)
  doesn't apply, since nothing here touches `IQueryable`.

Net: 26 files touched (13 Create + 13 Update commands, one interface + one-or-two computed
properties each); 0 files touched for Approve/Void (already compliant) and 0 for the ~50 Result
records (reflection instead).

### 3. Audit-write timing and atomicity
`AuditBehavior` calls its own explicit `db.SaveChangesAsync()` after `next()` completes
successfully, uniformly for every audited command type (including Approve/Void, whose DocumentId
is already known pre-handler) — one code path, not perfectly atomic with the handler's own commit.
Accepted trade-off: a crash in the vanishingly small window between the handler's own
`SaveChangesAsync` and the behavior's own `SaveChangesAsync` would lose that one audit row while the
business document itself still committed. No cross-transaction coordination was built to close this
gap — not justified for an internal admin audit trail with no compliance requirement for perfect
atomicity (architecture-spec.md §3.9 doesn't call for it either).

### 4. Action derivation
`typeof(TRequest).Name`'s prefix (`Create`/`Update`/`Approve`/`Void`) — `LoggingBehavior` already
reflects over the same property for its own purposes, so this isn't a new pattern in this codebase.

### 5. Immutability enforcement
Two layers, per the roadmap's explicit "not just an absence of an Update/Delete handler" exit
criterion:
1. Domain-level: private constructor + `Create` factory, no public setters (the standard discipline
   every entity in this codebase already follows).
2. A real mechanism: `AppDbContext.SaveChangesAsync` override inspects
   `ChangeTracker.Entries<Audit>()` and throws `InvalidOperationException` if any is
   `Modified`/`Deleted`. Proven by 3 new tests in `Api.IntegrationTests/AuditImmutabilityTests.cs`
   (InMemory provider — no Testcontainers/Docker needed, since the override only inspects
   change-tracker state, identical across providers): forcing `EntityState.Modified` throws, calling
   `.Remove()` throws, a plain insert succeeds.

### 6. DocumentType filter scope
The report's Document Type dropdown offers only the 13 ApprovableTransaction values, not all 18
`DocumentType` enum members — the other 5 (`Account`/`Contact`/`Product` numbering-pool-only
entries, `ProductionOrder`/`ProductionJournal`) can never appear in an Audit row per this phase's own
audited-commands scope decision, so listing them would be a dead-end filter option.

### 7. Export
Added now, not deferred — FR-9.8 is exactly the kind of record an Admin would want for compliance,
and `ReportSpreadsheetExporter`'s generic `ExportTable` helper made it a ~10-line addition (no new
per-report plumbing). Reuses the report's own `Reports.SystemAudit.View` permission key, same as
every other Phase 16c report's export endpoint.

### 8. Schema
`workflow` schema (existing `Audits` table alongside `Tasks`) — the same cross-cutting-territory
reasoning the roadmap flagged (no dedicated cross-cutting schema exists; `workflow` already hosts
`WorkTask` and `TransactionApprovalQuery`'s read model). Confirmed rather than defaulted: no
stronger candidate schema exists among the 12 bounded-context schemas.

### 9. Row-linking (Angular)
`detailRoute(row)` on the new `SystemAuditReportPage` is a direct copy of
`transaction-approval-queue-page.ts`'s own 13-branch switch (Phase 12) — same known gap (SalesOrder
has no Angular detail page, degrades to plain text `—`, not a broken link) and same Payment
Direction split. `AuditRowDto`'s `Direction` field (populated only for `DocumentType.Payment` rows,
via a small follow-up query against `Payments` in the handler, not stored on `Audit` itself) exists
solely to make that split possible from the report row alone, the same reason
`TransactionApprovalRowDto.Direction` exists.

## Bugs hit and fixed
None — the marker-interface design generalized cleanly across all 13 document types on the first
pass (confirmed by a full solution build after each module's edits), and the migration scaffolded
in the correct order (table create before the seed `INSERT`) with no manual reordering needed.

## Manual E2E (real API calls, fresh Organization `ae0d8016-...`, reused `Testing:*` Admin login)
- Created a JournalVoucher, Approved it, Voided it → exactly 3 Audit rows, correct `UserId`,
  `Action` (`Create`/`Approve`/`Void` in that order), `DocumentType.JournalVoucher`, and
  `DocumentId` matching the voucher — confirmed via the report endpoint itself.
- Each of the 4 filters (`userId`, `action`, `documentType`, `fromDate`/`toDate`) independently
  narrowed the result set correctly against the seeded rows.
- Zero false positives: an unbalanced-lines Create that actually succeeded (201) added exactly 1
  row; an Approve-an-already-Void 409, an Approve-a-nonexistent-id 404, and a malformed-JSON 400 each
  added **zero** rows (verified via before/after `totalCount`).
- Registered a second throwaway user, verified via a DB-read verification code (no email
  round-trip), invited as Member into the same organization, accepted the invite. Both
  `GET /reports/system-audit` and `GET /reports/system-audit/export` returned
  `403 {"title":"You do not have permission to perform this action (Reports.SystemAudit.View)."}`.
- Created 8 more JournalVouchers (12 total Create rows); paged with `pageSize=6` across 2 pages —
  zero duplicate/skipped rows, exact union of both pages equals the full set.
- Export endpoint returned a real `.xlsx` (`file` confirmed "Microsoft Excel 2007+").
- Browser (Angular dev server): report page renders with the User/Action/DocumentType/date filters
  populated (User dropdown pulled both real org members), Action=Void filter correctly narrowed to
  1 row live in the UI, and clicking that row's "View" link navigated to the real JournalVoucher
  detail page showing the correct Code/Status/lines.

## Not run this session
The 5 pre-existing `Api.IntegrationTests` that use Testcontainers (`AccountingFlowTests`,
`DocumentNumberGeneratorTests`, `ExceptionHandlingTests`, `HealthEndpointTests`) weren't re-run —
Docker Desktop wasn't running locally during this session. `AuditBehavior`'s insertion into the
pipeline (5th, after `LockDateBehavior`) doesn't touch any code path those tests exercise, but they
should be re-run before merge to confirm no regression.
