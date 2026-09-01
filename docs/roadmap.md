# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–20c, 20b, 20g, 20d, 20f)

Detail lives in each phase's own status doc — this table is the index, not the history.

| Phase | Shipped | Status doc |
|---|---|---|
| 0 | Clean Architecture scaffold, CI, test harness (incl. Testcontainers SQL Server) | `phase-0-status.md` |
| 1a | User registration, email verification, login (JWT in httpOnly cookie), password reset | `phase-1a-status.md` |
| 1b | Organization aggregate, 3-step wizard, memberships, invites/requests, workspace-name check | `phase-1b-status.md` |
| 1c | Role/RolePermission stub (system Admin/Member), `AuthorizationBehavior` pipeline | `phase-1c-status.md` |
| 2 | Generic lookup CRUD (CreditTerm, PaymentMode, CustomStatus, ReportingTags), TenantSettings, DocumentNumberingRule + race-safe generator, CustomFieldDefinition/Value (EAV, definitions only) | `phase-2-status.md` |
| 3 | Contact (Customer/Supplier/Lead) + ContactGroup tree, Product (Goods/Service) + ProductCategory/UoM/secondary units, list + record-detail Angular chrome | `phase-3-status.md` |
| 4 | AccountGroup/Account chart, JournalVoucher (first ApprovableTransaction), GL posting engine (`IGlPostingRule<T>`/`GlJournalEntry.Post`), CashTransfer, transactional-document Angular chrome | `phase-4-status.md` |
| 5 | Sales chain: Quotation → Invoice → Customer Payment, document conversion pattern, Warehouse lookup, TenantSettings GL-account defaults, SalesOrder (backend-only) + CreditNote | `phase-5-status.md` |
| 6 | Purchase chain: PurchaseOrder → PurchaseBill → Supplier Payment, Expense, DebitNote, TDS (TdsType lookup, TDS-nets-AP posting); post-phase conversion-enforcement fix across all 4 conversion flows | `phase-6-status.md` |
| 7 | Real FIFO stock ledger, availability policy (Reject/Warn/override), Invoice COGS leg, WarehouseTransfer, InventoryAdjustment, stock position/kardex queries; follow-up: CreditNote/DebitNote FIFO reversal | `phase-7-status.md` |
| 8a–8f | Reports: Trial Balance, Balance Sheet, Income Statement; Sales/Purchase Master; VAT Summary; TDS Report; Annex 13; Annex 5 | `phase-8a`–`8f-status.md` |
| 9 | Customer/Supplier Ageing Summary + Statement (the real running-balance engine) | `phase-9-status.md` |
| 10 | Contact Overview tab (shared `ContactLedgerReader`) | `phase-10-status.md` |
| 11 | Payment allocation suggestion fixed to net TDS + linked reversals | `phase-11-status.md` |
| 12 | Transaction Approval queue across all 13 ApprovableTransaction types (Workflow context) | `phase-12-status.md` |
| 13 | Tasks (`WorkTask`, polymorphic Contact/Organization parent, TaskType lookup) | `phase-13-status.md` |
| 14 | Role Reference full editor: per-org custom roles, permission-matrix UI, invite-by-RoleId | `phase-14-status.md` |
| 15 | CRM: Deals (`Deal`/`DealAssignee`, `LeadSource`/`DealStage` lookups) | `phase-15-status.md` |
| 16a | Void lifecycle (all 13 ApprovableTransaction types) + Organization.LockDate enforcement | `phase-16a-status.md` |
| 16b | Discounts retrofit: line/header `DiscountPct` across all 7 Product-line document types | `phase-16b-status.md` |
| 16c | Pagination (`PagedResult<T>`, shared Angular pagination component) + report export (ClosedXML, current view/full dataset) | `phase-16c-status.md` |
| 16d | System Audit report: append-only `Audit` trail via `AuditBehavior` pipeline step, `Reports.SystemAudit.View` report screen | `phase-16d-status.md` |
| 17 | Accounting breadth: Quick Payment/Receipt, Bank Accounts, Cheque Register, Allocate Customer/Supplier Payment, Opening Balances; `PaymentAllocation` generalized to a polymorphic Payment/JournalVoucher source | `phase-17-status.md` |
| 18 | CRM completion: `IFileStorage` (local-disk), `Attachment`/Contact Personnel/Comment (Contact-scoped), Activity feed (reused `Audit`/`AuditBehavior`), SMS (`SmsTemplate`/`SmsLog`/`SmsCreditLedgerEntry`, `ISmsSender`), quick-action prefill, Sales Order Angular UI (a pre-existing Phase 5 gap, closed here) | `phase-18-status.md` |
| 19 | `TransactionReportingTag` (document-level, Quotation/Invoice) + tag-filtered Sales Register; Cash Flow Summary, Sales/Purchase Register, Stock Ageing, Product Profitability, Ratio Analysis reports, closing FR-9.1/9.4/9.5/9.7's non-migrated catalog | `phase-19-status.md` |
| 20a | Custom Fields reach the forms: `SetCustomFieldValuesCommand`/`GetCustomFieldValuesQuery` + `CustomFieldDefinition.ChoiceOptions`, shared `app-custom-fields-editor` wired into Quotation/Invoice (FR-12.1) | `phase-20a-status.md` |
| 20c | `CostTerm` lookup (Additional Cost / Production Cost categories) + Configurations screen — prerequisite reference data for Phase 25's Manufacturing, nothing consumes it yet | `phase-20c-status.md` |
| 20b | Custom Status wiring: `SetCustomStatusCommand` (nullable `CustomStatusId` on Quotation/PurchaseOrder) + shared `app-custom-status-picker`, live-confirmed as a list-grid-only control orthogonal to Draft/Approved (FR-12.2); Cheque excluded (its pipeline drives the native lifecycle, not orthogonal to it) | `phase-20b-status.md` |
| 20g | Turnstile bot-check on registration (FR-1.1): `RegisterUserCommand.TurnstileToken` verified server-side by `ITurnstileVerifier` against Cloudflare's `siteverify`, `app-turnstile-widget` wired into the registration page only (New Organization wizard's two checks stay out of scope) | `phase-20g-status.md` |
| 20d | Printing Templates / Custom Templates (FR-11.2/11.3): confirm-live found the reference product's template gallery is a real visual editor, descoped by user decision to metadata-only `PrintingTemplate`/`CustomTemplate` lookups + `SetDefault`; the real deliverable is a print-to-PDF pipeline (QuestPDF, 2 shared layouts) wired for 6 document types, closing Phase 16c's deferred print output | `phase-20d-status.md` |
| 20f | Tenant feature-flag enforcement (FR-2.6): `IRequireFeature` + `FeatureGateBehavior` (4th pipeline behavior) make `TenantSubscription`'s flags a real gate, `FeatureNotEnabledException` → 403 naming the feature. Investigation found only 2 of 7 flags have a surface to gate (`TrackInventory`, `MultipleWarehouses`) — both of FR-2.6's own examples are unbuildable here; scope reduced accordingly. MultipleWarehouses is a **cap at one**, not a block (nothing seeds a default warehouse and Invoice requires one). Read-only Subscription & Features screen; flags stay immutable, live-confirmed as matching the reference product | `phase-20f-status.md` |
| 20e | Alert Scheduler (FR-11.1) — this codebase's **first background-job infrastructure**: hand-rolled `AlertSchedulerHostedService` (`BackgroundService` + `PeriodicTimer` + `TimeProvider`, scope per tick, `IOptionsMonitor`) driving `IAlertDispatcher`; `AlertDefinition` + an `AlertSendLog` ledger whose unique index on (definition, local occurrence date, recipient) delivers idempotency, multi-instance safety and at-most-once delivery at once. **No authentication-bypass surface was introduced** — the job sends no MediatR request, reading through `IAlertContentBuilder` with an explicit `OrganizationId` instead, so `CurrentUserService` still throws outside HTTP. Nepal-local (UTC+05:45) scheduling. Confirm-live closed every open question and surfaced a screen the scan had missed (Email Logs) | `phase-20e-status.md` |
| 21a | Async job foundation + bulk import (FR-2.9, NFR-4.3): durable `ImportJob`/`ImportJobRow` queue driven by a second `BackgroundService` (`ImportJobRunnerHostedService`), template-based .xlsx import for Product/Customer/Supplier in create and update modes with per-row error reporting, cancellation and completion notification. **The first background job that writes**, so it answers the identity question 20e sidestepped: it reuses the real Create/Update commands through the full pipeline under a scoped `IJobActingUser`, which means `AuthorizationBehavior` **re-checks permission on every row at execution time**; an `HttpContext` always wins, so a job identity can never serve a request. At-most-once **per row** via a `(ImportJobId, RowNumber)` unique index — partial success is a `Completed` job, `Failed` means the file could not be processed at all. E2E found and fixed a concurrency token that made the user's own Cancel wedge the running job | `phase-21a-status.md` |
| 21b | Full-tenant data export (FR-2.8, NFR-4.3): tenant-scoped `ExportJob` producing one multi-sheet `.xlsx` over FR-2.8's five named categories, downloaded through an authenticated endpoint. **Decision A is the phase** — FR-2.8 says "backup" and this codebase has no restore path, so what ships is an honest **export** that says so on the button, on the workbook's own Summary sheet and in the completion email. **Decision D restores 20e's default that 21a had to abandon**: an export only reads, so it runs with *no ambient identity at all* — the permission check and the `Audit` row (new `DocumentType.DataExport`) live on the enqueue command in a real HTTP request. **Decision C** answers 21a's deferred job-table question: separate tables, shared loop — 21a's runner became `QueuedJobRunnerHostedService<TProcessor, TOptions>` over a new `IQueuedJobProcessor` seam (a shared timer host, not a job framework). **Decision E** adds 7-day retention via `SweepAsync` on that seam and fixes the blob leak 21a shipped. Stated 25,000-row-per-sheet cap, disclosed in three places when hit | `phase-21b-status.md` |
| 21c | Migrated tax-register import + the migrated Sales/Purchase Register variants (FR-2.10, **closing FR-9.4** — Nepal's statutory report set is now complete): two tenant-scoped aggregates hold a prior system's filed Sales/Purchase Book rows, surfaced by two new Admin-only report screens and seeded from a template-based .xlsx on a new `Configurations > Migration` screen. **Decision A is the phase** — a migrated row is *real enough for a tax report and deliberately not real enough to be anything else*: no `GlJournalEntry`, no stock movement, no payment, no document number, no Draft/Approve/Void lifecycle, no lock-date gate, and presence in exactly two reports. Two tables (the column sets diverge after five fields), a **free-text party** with an optional exact-PAN link that never mints a Contact (so both register row DTOs' `ContactId` widens to nullable), two appended `DocumentType` members, and a return modelled as a **negative row** exactly as the live registers render a CreditNote/DebitNote. **Decision C is 21b's Decision C run again and coming out the other way**: no new job table — every `ImportJob` column applies and the loop is the same loop, so the two migrated types are `ImportEntityType` members, create-only, with a new `EntityTypes` filter keeping the two screens' histories apart. **Decision D re-argues 21a's identity rule from scratch** (21a's *reason* does not transfer, since the create handler did not exist) and lands the same way for different reasons: per-row permission re-check, validation and audit. **Decision F** gives migrated rows reach into the two register variants only, with VAT Summary / Annex 5 / Annex 13 / TDS each opened, each found structurally unable to consume a register-level row, and each given a test proving it is unaffected. Confirm-live was not possible (non-interactive session), so **Decision E derives the template columns from Phase 19's live-confirmed statutory register** rather than guessing at an unseen screen | `phase-21c-status.md` |

---

## Phase 16 — Platform hardening (a–d)
**Goal:** close the QA-critical gaps every existing screen shares, *before* adding more surface area. Each sub-phase is independently shippable.

### 16a. Void lifecycle + lock-date enforcement — **COMPLETE**, see `phase-16a-status.md`
All 13 `ApprovableTransaction` types have a real `VoidXCommand`; GL reverses via a mirror-image
`GlJournalEntry.PostReversalOf` entry, stock reverses via `IStockLedgerService.ReverseIncrementAsync`
(reject-if-partly-consumed) plus restock-at-original-cost (`ConsumedUnitCost` fields); dependent-
document guards block Invoice/PurchaseBill voiding until their CreditNote/DebitNote/Payment
dependents are voided first; `LockDateBehavior` enforces `Organization.LockDate` across every
create/edit/approve/void via `ILockDateSensitive`/`ILockDateSensitiveDocument`; a new Admin-only
Lock Date settings page. 13 new `*.Void` keys + `Tenancy.Organization.LockDateManage`.

### 16b. Discounts retrofit — **COMPLETE**, see `phase-16b-status.md`
Line-level + header-level `DiscountPct` on all 7 Product-line document types (Quotation/SalesOrder/
Invoice/CreditNote/PurchaseOrder/PurchaseBill/DebitNote). Confirmed live against the reference
product: discount reduces the taxable base before VAT, and nets straight into Sales Revenue/
Purchase Expense with **no separate Discount account** — `Line.Amount`/`VatAmount` are stored fully
netted (line discount, then header discount), so every GL posting rule and VAT/Annex report needed
zero code changes. Conversion-cap key grew a 4th component (line `DiscountPct`) plus a new
document-level header-`DiscountPct` equality check. PurchaseBill/DebitNote's TDS base switched from
pre-discount `Quantity*Rate` to the discounted `Amount`. Sales/Purchase Master Report gained
`ItemDiscount`/`TransactionDiscount`/`NetSales`.

### 16c. Pagination + report export — **COMPLETE**, see `phase-16c-status.md`
Every one of the 22 document-list queries and 7 of the 8 reports (VAT Summary excluded — fixed
2×3-bucket cardinality) return a shared `PagedResult<T>` envelope; a new shared Angular
`<app-pagination-control>` is wired into all 27 non-lookup screens (the 14 types sharing the
generic lookup query keep their bare-array contract, no visible pager — bounded master data).
Every report gained a ClosedXML spreadsheet export ("current view"/"full dataset") behind its
existing permission key. Two real bugs fixed along the way: report footer totals silently
breaking under pagination (now computed server-side over the full filtered set), and ClosedXML's
synchronous `SaveAs` needing a `MemoryStream` buffer since Kestrel disallows sync writes to the
live response stream.

*Exit criteria: confirmed live against a 105-row seeded Invoice table and a 60-row seeded
PurchaseBill table (`TotalCount` matched `sqlcmd COUNT(*)`, zero duplicate/skipped rows across
pages, real `OFFSET`/`FETCH NEXT` SQL); exported spreadsheets unzipped and diffed cell-by-cell
against the on-screen/API rows; export endpoints confirmed 403 with the same key as their report,
for a Member, on two reports.*

### 16d. System Audit report — **COMPLETE**, see `phase-16d-status.md`
An append-only `Audit` entity (`workflow.Audits`) is written by a new `AuditBehavior` pipeline
step (5th, after `LockDateBehavior`) for every Create/Update/Approve/Void of the 13
ApprovableTransaction document types, via two new marker interfaces
(`IAuditableRequest`/`IAuditableRequestWithId`) plus reuse of the existing
`ILockDateSensitiveDocument` for Approve/Void. Immutability enforced twice: Domain-level (private
ctor) and a real `AppDbContext.SaveChangesAsync` override throwing on any tracked
`Modified`/`Deleted` `Audit` row. `Reports.SystemAudit.View` (Admin-only) report screen mirrors the
Phase 16c report shape — paginated, filterable by User/Action/DocumentType/date range, spreadsheet
export, row-linking via a copy of the Transaction Approval Queue's `detailRoute` switch.
Administrative (non-document) actions explicitly out of scope this phase.

*Exit criteria: confirmed live — Create→Approve→Void a JournalVoucher produced exactly 3 audit rows
with the right actor/order; all 4 filters narrow correctly; a failing 400/404/409 call produces
zero audit rows; a real invited Member gets 403 naming `Reports.SystemAudit.View`; a direct
`Modified`/`Deleted` attempt on an `Audit` entity throws (unit-tested); pagination clean across
seeded rows.*

---


## Phase 19 — Reporting Tags + remaining reports
**Goal:** complete the report catalog (FR-9.1, FR-9.3, FR-9.5, FR-9.7, FR-9.9). Tags first — they're a write-side change the reports then consume.

1. **Reporting Tags on transactions** (FR-9.9): attach `ReportingTagOption`s (lookups exist since Phase 2) at document-line or document level (confirm live), thread tag filters through existing GL reports.
2. **Cash Flow Summary** (FR-9.1's fourth statement) — derived from GL like Phase 8a's three; same permission reasoning pass.
3. **Sales Register / Purchase Register** (FR-9.4's non-migrated variants — migrated variants landed with **Phase 21c**, closing FR-9.4).
4. **Stock Ageing** + **Product Profitability** (FR-9.5) over the FIFO layers/`StockMovement` history.
5. **Ratio Analysis** (FR-9.7) computed from the same statement data as 8a.
6. Every new report ships with the established per-report permission-key derivation (rollup vs flat register, PAN exposure) written down, not defaulted.

*Exit criteria: every remaining catalog report renders with hand-verified numbers against seeded data; tag filtering narrows a GL report correctly; each report's permission decision recorded and its 403 proven.*

---

## Phase 20 — Configuration & extensibility completion
**Goal:** make the Phase 2 extensibility foundations actually reach the UI, plus the notification/template surface (FR-11.x, FR-12.x). Split into seven independently shippable sub-phases; one sub-phase = one session.

**Locked execution order: 20a ✅ → 20c ✅ → 20b ✅ → 20g ✅ → 20d ✅ → 20f ✅ → 20e ✅ — Phase 20 is complete.** Reasoning (this is
*not* the original list order — deliberately resequenced):
- **20b next** because it is the same shape and size as 20a (extend a Phase 2 lookup onto real documents, confirm-live step, shared editor component). Running that pattern again while the muscle memory is fresh is lower-risk than pivoting to something structurally new.
- **20g early** because it is small and isolable — a good pairing candidate with leftover budget or a short session of its own, but never the main event.
- **20d and 20e are both greenfield-and-risky**, and 20e in particular is an architecture decision (background-job infra, plus how a jobless command authenticates itself — a new authentication-bypass surface) that deserves a session where it is the *only* thing being decided — so it goes last, treated as an architecture review rather than "whatever's next."
- **20f is a sweep across already-built surfaces**, easiest to scope correctly once more document types exist and have settled shapes, so it waits until fewer sub-phases are still landing and re-doing gating work is less likely.

**20d's confirm-live pass is done** — it found the reference product's Printing Templates screen is a
genuine visual template-authoring surface, not a fixed catalog; the user chose a metadata-only
descope rather than building that editor. See `phase-20d-status.md`'s TL;DR.

### 20a. Custom fields rendered on forms (FR-12.1) — **COMPLETE**, see `phase-20a-status.md`
The deferred write-side half of Phase 2's EAV: `SetCustomFieldValuesCommand`/`GetCustomFieldValuesQuery`
(riding on the target document's own Edit/View permission), a `ChoiceOptions` field
`CustomFieldDefinition` never had, and a shared `app-custom-fields-editor` rendering a document type's
applicable fields inline in its create/edit form. Wired to Quotation and Invoice only; the other 15
applicable document types and a `CustomFieldDefinition` admin screen are mechanical follow-up.

### 20b. Custom Status wiring (FR-12.2) — **COMPLETE**, see `phase-20b-status.md`
`SetCustomStatusCommand` (nullable `CustomStatusId` on Quotation/PurchaseOrder, riding on the target
document's own Edit permission) plus a shared `app-custom-status-picker`. Confirm-live reshaped the
plan on three counts: the picker lives only in the LIST grid (a "Stage" column, applying instantly on
selection) with no presence on the detail page at all — a third shape distinct from both 20a's inline-
form and Phase 19's sidebar-action patterns; Invoice has no Custom Status section in the real product,
so Quotation+PurchaseOrder were wired instead (spanning Sales and Purchasing, not both-Sales like
20a); Cheque was excluded outright (not deferred) since its pipeline appears to drive the native
`ChequeStatus` lifecycle rather than sit orthogonal to it. SalesOrder (identical shape) is mechanical
follow-up. No `CustomStatus` admin screen was built — the third consecutive lookup-CRUD-with-no-UI
deferral (`CustomFieldDefinition` in 20a, now this), flagged as worth a dedicated follow-up session
covering all three at once rather than a fourth one-off next time.

### 20c. Cost Terms lookup — **COMPLETE**, see `phase-20c-status.md`
Configurations §7 — the `CostTerm` lookup (`AdditionalCost`/`ProductionCost` categories, uniqueness per
`(Organization, Category, Name)`) plus its Configurations screen. Prerequisite reference data for Phase
25's Manufacturing; nothing consumes it yet, by design.

### 20d. Printing Templates / Custom Templates (FR-11.2/11.3) — **COMPLETE**, see `phase-20d-status.md`
Confirm-live found the reference product's Printing Templates screen is a genuine visual
template-authoring surface (toggle/canvas editor), not a fixed catalog picker — the user chose to
descope it to a metadata-only `PrintingTemplate` lookup (Name + `IsDefault` per DocumentType, no
layout-definition field) rather than build that editor. `CustomTemplate` (merge-field text, 4 types)
shipped as originally scoped. The real deliverable is the print-to-PDF pipeline this closes Phase
16c's deferral with: a generic print endpoint rendering via QuestPDF (2 shared layouts — line-item
and ledger — not one per document type), wired for 6 of the ~15 printable document types (Invoice,
Quotation, SalesOrder, PurchaseOrder, PurchaseBill, JournalVoucher); the rest are mechanical
follow-up (a new handler case reusing the existing shared layout, no new design). No admin screen
gap this time — both lookups got real Angular CRUD+SetDefault screens.

### 20e. Alert Scheduler (FR-11.1) — **COMPLETE**, see `phase-20e-status.md`
This codebase's first background-job infrastructure. `AlertSchedulerHostedService` (Infrastructure) owns a
`PeriodicTimer` built from `TimeProvider`, creates a **DI scope per tick** (both `IAppDbContext` and
`IEmailSender` are scoped), reads its interval through `IOptionsMonitor` (not `IOptions` — the phase-20g
caching gotcha), and swallows tick failures so the loop survives. It holds **no business decision**: those all
live in `IAlertDispatcher` (Application) behind an injected clock, which is why the whole suite runs on
`FakeTimeProvider` with no `Task.Delay` anywhere.

**Decision A — hand-rolled, not Hangfire/Quartz/Coravel.** Everything a scheduler library sells is already
covered: the schedule is durable because it is tenant data (`AlertDefinitions`), catch-up and multi-instance
locking fall out of the `AlertSendLog` unique index this phase needs anyway, and the reference product's own
Email Logs screen is the operational visibility. A library would have added a second schema and a dashboard to
secure in exchange for machinery this design does not use.

**Decision B — the anticipated authentication-bypass surface was not built, because it was not needed.** The
dispatcher sends **no MediatR request**; it reads through `IAlertContentBuilder` implementations taking an
explicit `OrganizationId`. `CurrentUserService` still throws outside an HTTP context, and no system principal,
ambient user, or "runs as" field exists. Access control is entirely at *definition* time
(`Configuration.AlertDefinition.Manage`, Admin-only), which is the right place because the real risk is data
**egress** to unvalidated free-text recipient addresses — mitigated further by keeping every alert body to
bounded aggregates (counts and totals; no PAN, contact names, or per-transaction rows).

**Decision C — at-most-once, ledger-first.** The `AlertSendLog` row is committed *before* SMTP is called, under
a unique index on `(AlertDefinitionId, OccurrenceDate, Recipient)`. Crash-between-the-two leaves a visible
`Pending` row and is never retried; a second instance's duplicate insert is rejected; a missed slot fires late
the *same local day* but a multi-day outage never backfills. A duplicate daily summary to a real customer is
worse than a missing one, and a missing one is visible in Email Logs.

Confirm-live closed every open question (Alert Type has exactly two options, Medium exactly one, Schedule
exactly one plus an HH:mm picker), ruled out `CustomTemplateType.Email` (no template picker exists on the alert
form) and a "Run now" action (the product has none), established that the time picker is **Nepal-local
UTC+05:45** — and surfaced a screen the module scan had missed entirely: **Email Logs**, behind the panel's own
kebab menu, which turned the send ledger from testing scaffolding into a real feature.

### 20f. Tenant feature-flag enforcement (FR-2.6) — **COMPLETE**, see `phase-20f-status.md`
The wizard's Accounting Features checkboxes (recorded since Phase 1b, read nowhere in the twelve phases
since) are now enforced at point of use by a fourth pipeline behavior, `FeatureGateBehavior`, keyed by a
new `IRequireFeature` marker and slotted between `AuthorizationBehavior` and `LockDateBehavior`;
`FeatureNotEnabledException` maps to 403 naming the feature in the wizard's own wording.

The mandatory scope investigation found **only 2 of the 7 flags have a real surface in this codebase to
gate** — `TrackInventory` (the Inventory context: WarehouseTransfer, InventoryAdjustment, Opening Stock,
Stock Position, Inventory Ledger — 16 requests) and `MultipleWarehouses`. The other five have nothing
built to gate, *including both examples FR-2.6 itself gives* (no `Currency` domain class exists;
BOM/Production is Phase 25; POS is out of the whole rebuild's scope). Scope was sized to what is real
rather than padded to match the FR's illustrations; the `TenantFeature` enum still covers all seven, so
Phase 25's Manufacturing gate is a one-line declaration.

Two findings reshaped the design. **`MultipleWarehouses` is a cap at one, not an on/off block** — nothing
seeds a default Warehouse at Organization creation and Invoice/PurchaseBill both require a `WarehouseId`,
so blocking creation outright would leave a flag-off tenant unable to invoice; the *second* warehouse is
what the entitlement buys. Being conditional, it lives in `CreateWarehouseCommandHandler`, the one
deliberate exception to the one-behavior rule, and it needs no backfill migration. And **`Track Inventory`
cannot gate "the Inventory module"** — confirm-live showed the reference product files Products/Categories/
Units under its Inventory nav, so the gate lands on the Inventory bounded context only and Catalog is
untouched. The FIFO/GL engine is never gated (proven live: a Track-Inventory-off tenant still approves
Invoices with balanced GL).

Confirm-live also settled the mutability question outright: the reference product's own subscription screen
is read-only and its disabled-feature panel says to contact vendor support, so immutable-at-creation is
*not* a divergence and no Update path was built. A read-only Angular **Subscription & Features** page
mirrors that shape, and the dashboard's three Inventory nav entries render conditionally.

### 20g. Turnstile bot-check on registration — **COMPLETE**, see `phase-20g-status.md`
`RegisterUserCommand` gained a required `TurnstileToken`, verified server-side by a new
`ITurnstileVerifier` (Infrastructure: a typed `HttpClient` against Cloudflare's `siteverify`) before
any uniqueness check — a failed/missing token never reaches user creation. A shared
`app-turnstile-widget` (no new npm dependency) wired into the registration page only, per the
roadmap title and FR-1.1 — the New Organization wizard's two additional Turnstile checks
(module-scan §5) stay out of scope, mechanical follow-up reusing the same component. Proving the
negative path required temporarily swapping to Cloudflare's always-*fails* dummy secret key (the
always-*passes* dummy secret ignores the token value entirely, so it can't itself prove the
server-side check rejects a bad token).

*Phase exit criteria: a custom field defined for Invoice appears on the Invoice form and round-trips; a printed Invoice renders through the selected template; a scheduled alert email actually arrives on schedule; a tenant without Track Inventory no longer sees warehouse-dependent surfaces.*

---

## Phase 21 — Import/Export & backup — **COMPLETE**
**Goal:** FR-2.8/2.9/2.10 — the data-migration story, on Phase 20's async-job infrastructure.

**Split into three independently shippable sub-phases; one sub-phase = one session. All three are COMPLETE.**
The original four numbered items are three deliverables, not one phase: 21b and 21c both need a job
runner, and 21a is the only one that forces the identity decision — so it went first, alone, the same
reasoning that put 20e last in Phase 20.

### 21a. Async job foundation + bulk import (FR-2.9, NFR-4.3) — **COMPLETE**, see `phase-21a-status.md`
Durable `ImportJob`/`ImportJobRow` queue, a second `BackgroundService` copying 20e's shape (scope per
job, `IOptionsMonitor`, swallowed tick failures) but draining per tick, and template-based .xlsx
import for **Product, Customer and Supplier** in both create and update modes, matched on the **Code**
column (live-confirmed as the reference product's own update key). Row-level errors carry the
spreadsheet's own row number and the offending column.

**Decision B is the one to carry forward.** Unlike an alert, an import writes, so the job reuses the
real Create/Update commands through the full six-behavior pipeline under a scoped `IJobActingUser` —
which makes permission **re-checked per row at execution time** for free, and attributes every
imported record to the initiating user in the audit trail. `CurrentUserService` prefers `HttpContext`
unconditionally, so a background identity can never serve an HTTP request. **Decision C** is
at-most-once *per row* (claim-then-act under a unique index), so a crash at row 500 of 1,000 resumes
at 501 and creates nothing twice; partial success is a `Completed` job. Confirm-live corrected the
brief on one point that would have produced the wrong importer: the product's "Contact" upload type
is `ContactPersonnel`, not `Contact`. Account / Product Category / Account Group / ContactPersonnel
are deferred as mechanical follow-up (the two tree types additionally need intra-file parent ordering).

### 21b. Full-tenant data export (FR-2.8) — **COMPLETE**, see `phase-21b-status.md`
A tenant-scoped `ExportJob` produces one multi-sheet `.xlsx` — Summary plus FR-2.8's five named
categories (products, contacts, chart of accounts, ledger transactions, stock movements) — downloaded
through an authenticated, permission-checked endpoint. Admin-only `Configuration.ExportJob.View` /
`.Manage`; View gates the download itself, so it is the key that controls whether the file leaves the
system.

**It is called an export, never a backup, and that was the phase's first and largest decision.**
FR-2.8 says "backup/export"; this codebase has no restore path and none is planned, so the artifact
says outright what it cannot do — on the screen, on the workbook's first sheet, and in the completion
email. **Decision D got 20e's "no ambient identity" default back**: an export only reads, through
hand-filtered org-scoped queries, so the job has no acting user; `IJobActingUser` exists and is
deliberately unused, and the permission check plus the `Audit` row live on the enqueue command.
**Decision C** answered 21a's deferred job-table question with separate tables and a shared loop:
`ImportJobRunnerHostedService` became `QueuedJobRunnerHostedService<TProcessor, TOptions>` over a new
`IQueuedJobProcessor` seam — a shared timer host, explicitly not the generic job framework 21a
declined, with one hosted service per processor so a long import cannot hold up an export.
**Decision E** is 7-day retention swept from that seam's `SweepAsync`, which also fixes the blob leak
21a shipped (nothing had ever deleted an uploaded workbook). A stated 25,000-row-per-sheet cap,
disclosed on the job row, the Summary sheet and the email when hit — not hidden in a status.

Confirm-live was **not** performed (non-interactive session, and CLAUDE.md's rule is that the user
signs in themselves): `Organization > Developer Mode` and `Organization > Documents` remain unopened,
and the **browser pass on the new screen is outstanding**. Neither blocks the phase — 21a had already
established the decisive fact that there is no backup screen to mirror.

### 21c. Migrated tax-register import + the migrated Sales/Purchase Register variants (FR-2.10) — **COMPLETE**, see `phase-21c-status.md`
**FR-9.4 is now fully closed: Nepal's statutory report set is complete.** Two tenant-scoped
aggregates (`MigratedSalesRegisterEntry`, `MigratedPurchaseRegisterEntry`) hold a prior system's
filed register rows, read by two new Admin-only report screens and seeded from a template-based .xlsx
on a new `Configurations > Migration` screen, separate from Import / Export as the reference product
files it.

**Decision A is the phase, and it is a domain question nothing in this tree had answered.** A
migrated row is *real enough to appear in a tax report and deliberately not real enough to be
anything else*: it posts no `GlJournalEntry`, creates no stock movement or payment, draws no document
number, has no Draft/Approve/Void lifecycle, is not lock-date sensitive, and appears in exactly two
reports. Two tables rather than one (the column sets share five fields and then diverge completely —
21b's Decision C reasoning applied unchanged); a **free-text party** with an optional exact-PAN link
that never mints a `Contact`, whose consequence is taken rather than dodged (both register row DTOs'
`ContactId` widens to nullable); two appended `DocumentType` members rather than borrowing
`Invoice`/`PurchaseBill`; and a return modelled as a **negative row**, exactly as the live registers
already render a CreditNote/DebitNote.

**Decision C is 21b's own test run again, coming out the other way: no new job table.** Every
`ImportJob` column applies to a migrated upload and the loop is the same loop, so the two migrated
types are simply `ImportEntityType` members — create-only, with a new `ListImportJobsQuery.EntityTypes`
filter keeping the Migration and Import / Export histories apart. The cost was two classes and two DI
lines. **Decision D re-argued 21a's identity rule from scratch**, since 21a's justification (the rules
already live in the Create handler) does not transfer when you are writing that handler yourself, and
landed the same way for different reasons: per-row permission re-check at execution time, validation,
and audit attribution. **Decision F** gives migrated rows reach into the two register variants only —
VAT Summary, Annex 5, Annex 13 and TDS were each opened, each found structurally unable to consume a
register-level row, and each given a test proving it is unaffected rather than left implicit.

Confirm-live was **not** possible (non-interactive session), so **Decision E derives the template
columns from Phase 19's live-confirmed statutory registers** rather than guessing at an unseen screen
— defensible here in a way it would not have been in 21a, because the migrated variants must match
the statutory form by construction. `Organization > Migration`, `> Developer Mode` and `> Documents`
stay unopened, and the browser pass on this phase's three new screens is outstanding.

*Exit criteria: a template-based Product import creates and then updates rows correctly with per-row errors surfaced (**21a — done**); a data export downloads and contains the seeded data, and no other tenant's (**21b — done**); a migrated-register import shows up only in the migrated report variants, never in live GL (**21c — done**, verified live with `sqlcmd`: zero GL journal entries, stock ledger entries, stock movements and payments after a real import, and a Trial Balance still at 0/0).*

---

## Phase 22 — Document inbox
**Goal:** FR-10.3 — upload scanned receipts/bills, convert to structured transactions. Reuses Phase 18's file storage.

1. Inbox upload + list (Workflow context, alongside Tasks/Approval queue).
2. "Convert to transaction" flow: pick a target type (Invoice, Purchase Bill, Expense, Quick Payment), land on that form with the image side-by-side.
3. AI-assisted field extraction pre-filling the form (Claude API) — a stretch goal per the original roadmap framing: the inbox + manual conversion ships regardless; extraction quality does not block the phase.

*Exit criteria: an uploaded bill photo converts into a Draft Purchase Bill through the real UI; the source image stays linked and viewable from the resulting document; permission checks proven.*

---

## Phase 23 — Nepali localization & parity odds-and-ends
**Goal:** the cross-cutting Nepal-market NFRs plus small confirmed-parity gaps carried from earlier phases.

1. **BS calendar** (NFR-1.1): shared dual AD/BS date component (entry + display, per-user preference), swept across every date field; fiscal-year display in BS where the reference product shows it.
2. **Lakh/crore digit grouping** (NFR-1.2) in one shared currency-format pipe, swept across the UI.
3. **SalesOrder Angular UI** — the Phase 5 backend-only gap (its Approval-queue rows have had no "Open" link since Phase 12).
4. **Home dashboard** (`erp-module-scan.md` Home Tab): summary cards over existing queries.
5. **Export-sale flag on Invoice** (FR-5.8) with its tax treatment — confirm live first.

*Exit criteria: a date entered in BS persists and reads back identically in both calendars; amounts show lakh/crore grouping everywhere; a SalesOrder is creatable/approvable through the UI and its queue row links correctly.*

---

## Phase 24 — Variant Products & Attributes
**Goal:** FR-8.3 (`erp-module-scan.md` Inventory §2–§3), deferred since Phase 3 as off the critical path.

1. Tenant-defined attribute definitions (e.g. Size, Color) + values.
2. Variant generation from attribute combinations, each variant with its own SKU/barcode/pricing, stock tracked per variant per warehouse (the FIFO ledger keys extend from ProductId to variant identity — migration reviewed by hand per the CLAUDE.md migration gotchas).
3. Variant-aware product pickers on document lines.

*Exit criteria: a two-attribute product generates its variant matrix; a PurchaseBill/Invoice cycle on one specific variant moves only that variant's stock, verified against the DB; existing non-variant products unaffected (regression suite green).*

---

## Phase 25 — Manufacturing
**Goal:** FR-8.8/8.9 (`erp-module-scan.md` Inventory §8–§10), behind the tenant's Manufacturing feature flag (enforced since Phase 20).

1. **Bill of Materials**: finished good, raw-material lines with consumption ratios, optional by-products with cost-allocation %, additional cost terms (Phase 20's Cost Terms lookup).
2. **Production Order**: uncosted plan, optionally defaulted from a BOM; conversion source per the §3.3 pattern *with* the full conversion-enforcement lessons from Phase 6 bug #4 applied from day one.
3. **Production Journal**: the costed execution — on Approve, consumes raw-material FIFO stock at cost, computes per-unit finished-good (and by-product) cost, creates new stock at that computed cost, posts balanced GL. Work the net GL/stock effect out on paper first (Phase 7's InventoryAdjustment discipline).
4. Manufacturing reports (summary/variance/planning — FR-9.5's manufacturing slice).
5. Angular: BOM list/detail, Production Order/Journal on the transactional-document chrome.

*Exit criteria: BOM → Production Order → Production Journal end-to-end: raw stock consumed at FIFO cost, finished stock created at the computed cost, GL balanced and hand-verified, kardex reconciles exactly; a tenant without the Manufacturing flag sees none of it; a Journal exceeding available raw stock hits the real availability policy.*

---

## Deferred beyond this roadmap (post-v1 — seams kept, no phases planned)
Explicit decisions (2026-08-18), not omissions:
- **Multi-currency** (FR-2.5, NFR-1.3): NPR-only for v1. The data model keeps the seam per `product-requirements.md` §4.2.
- **Billing Locations + POS** (FR-2.3, FR-3.3, POS Retail/Restaurant): no `BillingLocation` backing exists (Phase 14 scoped the permission sections out for exactly this reason). The permission/feature-flag model already anticipates it (NFR-7.1).
- **IRD e-filing integration** (Annex 5's Sync-with-IRD columns): aspirational until committed; the Annex reports omit rather than fake those columns (Phase 8f precedent).

---

*Living doc — re-order/re-scope as real constraints surface. When picking up a phase: read its confirmed shape in `erp-module-scan.md` first; if the screen was never opened in the hands-on pass, confirm it against the live Tigg UAT tenant through the Browser pane (user logs in themselves) before writing code — the Phase 8f Annex 5 lesson: the speculative design and the real screen had nothing in common. Every phase ends with its own `phase-N-status.md`; CLAUDE.md's known-gotchas list is the pre-flight checklist for migrations, EF Core LINQ, and Angular selects.*
