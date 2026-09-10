# Roadmap history — per-phase planning detail (phases 16–25)

Moved verbatim out of `docs/roadmap.md` on 2026-09-02 so the roadmap stays a short index. These are the
planning sections as they stood when each phase was picked up and closed; the authoritative record of
what shipped is each `docs/phase-N-status.md`.

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

## Phase 22 — Document inbox ✅ COMPLETE
**Goal:** FR-10.3 — upload scanned receipts/bills, convert to structured transactions. Reuses Phase 18's file storage.

All three items shipped, including the stretch goal.

1. ✅ Inbox upload + list — `UploadedDocument` (`Domain/Workflow`), `Workflow > Document` screen with Pending/Done tabs at `/organizations/:id/workflow/document-inbox`, beside the Approval queue. `IFileStorage` and `AttachmentValidation` reused unchanged.
2. ✅ Conversion into all four targets (Invoice, Purchase Bill, Expense, Quick Payment) — **prefill-and-submit**: the target's ordinary `new` form, the scan beside it, the user's own Save running the normal `CreateXCommand`. The source stays viewable from the saved transaction via `SourceDocumentPanel`.
3. ✅ AI-assisted extraction — official Anthropic C# SDK behind an Application-layer `IDocumentExtractor`, `claude-opus-5`, tenant opt-in **default off** plus an Admin-only `.Extract` key, synchronous with a 90s timeout, no new job table, audited. Every test uses a fake; none touches the network.

**Carried change:** Quick Payment/Receipt (Phase 17) used to create *and approve* on one click. Since the inbox can now pre-fill it from a scan a model read, it is **Save Draft then Approve** like every other document type, with the Draft landing in the Transaction Approval queue. Both steps stay on that page — `payment-detail-page`'s `canApprove()` requires allocations, which a Quick Payment has none of. See `phase-17-status.md`'s addendum.

*Exit criteria — met:* an uploaded scan converts into a **Draft** Purchase Bill (`sqlcmd`-verified: `Code=DRAFT`, and zero GL entries, stock ledger rows, stock movements and payments); the source file is found again *from the transaction* and streams back byte-identical; four 403s naming their exact keys, including `Purchasing.PurchaseBill.Create` from the prefill — the conversion has no key of its own on purpose.

The browser pass was completed and found three real defects (a `.table-responsive` overflow clipping
three of the four "+ Add as" targets, a notice contradicting the consent switch, and the source panel
missing from the two Payment detail pages) — all fixed and re-verified live.

**One follow-up carried forward, stated in `phase-22-status.md`:** the real Anthropic call has never
run against a live key on this deployment, so the extraction path proven end-to-end is the
*unconfigured* one. Does not block Phase 23.

---

## Phase 23 — Nepali localization & parity odds-and-ends ✅ COMPLETE
**Goal:** the cross-cutting Nepal-market NFRs plus small confirmed-parity gaps carried from earlier phases.

All five items shipped.

1. ✅ **BS calendar** (NFR-1.1) — a shared dual AD/BS date component swept across **every** date field
   (66 inputs, 42 files) plus 40 business-date renders, behind a global per-user AD/BS toggle. Live
   session confirmed the switch is a *global* profile setting showing **one** calendar at a time, not a
   per-field toggle. Supported range **BS 2000–2092** (AD 1943-04-14 … 2036-04-13); outside it,
   conversion returns null and the UI falls back to AD rather than guessing.
2. ✅ **Lakh/crore digit grouping** (NFR-1.2) — the app's first shared pipe, replacing 324 inline
   `.toFixed(2)` calls across 40 templates.
3. ✅ **SalesOrder Angular UI** — the page itself already existed (Phase 18 built and routed it as a
   mid-phase scope expansion); what was missing was the two `detailRoute` switches that still returned
   `null` for SalesOrder behind a now-false comment. Both fixed, and both are now driven over every
   member of their document-type union so the next added type cannot fall through.
4. ✅ **Home dashboard** — four KPI cards with prior-period comparison, the Bank and Cash Balance panel
   with its Total row, a date sub-filter, and the unified recent-activity feed with its five
   All/Sales/Purchase/Payment/Receipt tabs, at `/organizations/:id/home`. Every KPI figure comes from an
   existing query handler; the feed is the phase's **one** new Application-layer query, added
   deliberately (see Decision F) because a merged, ordered, paged stream cannot be composed
   client-side.
5. ✅ **Export-sale flag on Invoice** (FR-5.8) with its tax treatment — `IsExport` plus optional
   Country/Declaration No/Declaration Date, filling the four Sales Register columns that had been
   hardcoded empty since Phase 19.

**Fiscal-year note (grounded finding #8):** `ResetEveryFiscalYear`/`IncludeFiscalYearInCode` remain
stored-but-inert. The live product's fiscal year is a **BS** year starting **Shrawan 1**, and it does
appear in document codes (`JV0031/83-84`) — but implementing that would change document numbering,
which is not a localization concern. **Deliberately not done**, and recorded so a future phase picks it
up on purpose.

*Exit criteria — met:* a date entered in BS persists and reads back identically in both calendars
(asserted as a test through both binding shapes, and confirmed live); amounts show lakh/crore grouping
everywhere, proven mechanically by a guard test rather than by inspection; a SalesOrder is
creatable/approvable through the UI and its queue row links correctly.

The browser pass was completed and found a real defect every automated check had passed: **the live
Sales Register page had no columns for the four export fields** — the DTO carried them since Phase 19,
the handler now fills them, and the table simply never rendered them, so FR-5.8 would have shipped with
its data invisible. Fixed and re-verified live.

**Carried forward, not blocking:** server-rendered output (Phase 20d print/PDF, Phase 16c/21b `.xlsx`)
still renders dates in **AD** regardless of the user's calendar preference — conversion is client-side
by Decision A, and closing that gap needs either a C# conversion table or sending the preference to the
server. **The 21b/21c debt tracked here is closed by Phase 25** — all four unlooked-at screens
(`Configurations > Import / Export`'s Export half, `Configurations > Organization > Migration`, and
both migrated register report pages) were browser-passed with **no defects found**, and all three
reference-product screens (`Organization > Developer Mode`, `> Documents`, `> Migration`) were
confirm-lived. See `phase-25-status.md`'s Step 3, which also records how a browser pass is now
possible in a non-interactive session — the thing that kept this item alive for four phases.

---

## Phase 24 — Variant Products & Attributes ✅ COMPLETE
**Goal:** FR-8.3 (`erp-module-scan.md` Inventory §2–§3), deferred since Phase 3 as off the critical path.

1. ✅ Tenant-defined attribute definitions (Size, Colour, …) + options — a tenant-global catalog, confirmed live.
2. ✅ Variants with their own SKU/barcode/pricing, stock tracked per variant per warehouse — **via Decision A rather than via a stock-key change**. A variant is a `Product`, so the FIFO ledger already keys on it; the task's parenthetical ("the FIFO ledger keys extend from ProductId to variant identity") described work that the live model made unnecessary. Both creation affordances ship: `+ Add` (one at a time, matching the reference product) and `Generate All` (the matrix FR-8.3 asks for, which the reference product does not have).
3. ✅ Variant-aware product pickers — one line, because all fifteen already shared `CatalogService.listAllProducts`.

*Exit criteria — all met, verified with `sqlcmd` against real SQL Server on a fresh Organization:*
- ✅ A two-attribute product generated its 4-variant matrix; re-running skipped all 4 rather than duplicating.
- ✅ PurchaseBill 10 Blue-Large then Invoice 4 Blue-Large left **exactly 6** on the first FIFO layer at cost **600** (not the newer layer's 800, nor the sibling's 610), with **zero movement** on Red-Large and nothing at all on the parent. Kardex reconciles per variant; Trial Balance balances at 26,690.60.
- ✅ Non-variant products unaffected: a plain control product went through the same documents with identical stock and GL behaviour, and all four existing suites are green.

**New in the Known Gotchas** (see CLAUDE.md): a child newly appended to a **tracked** parent's encapsulated collection is picked up as `Modified`, not `Added` — the add-only sibling of phase-4's clear-and-re-add bug, and it presents identically to a `TestAppDbContext` that has not restated the collection.

**Deliberately out of scope, decided not forgotten:** multi-UOM × variants (`ProductSecondaryUnit` is catalog metadata, not a stock line — it is on the sweep guard's allow-list with that reason); bulk-importing variants through Phase 21a's `ProductImporter`; and the browser passes outstanding from 21b/21c at the time (**closed by Phase 25**).

---

## Phase 25 — Manufacturing ✅ COMPLETE
**Goal:** FR-8.8/8.9 + FR-9.5's manufacturing slice (`erp-module-scan.md` Inventory §8–§10), behind the tenant's Manufacturing feature flag (enforced since Phase 20).

1. ✅ **Bill of Materials**: finished good, raw-material lines, by-products with a cost-allocation %, production expense terms (Phase 20c's Cost Terms lookup, finally consumed). Master data — no lifecycle, no number, at most one per finished product (live-confirmed: there is no BOM picker anywhere, so "LOAD BOM" resolves the recipe from the product alone).
2. ✅ **Production Order**: the uncosted plan — Draft → Approved → Converted/Void, raw materials with **quantity only**, no warehouse, nothing touching stock or the ledger. Conversion source with phase-6 bug #4's enforcement applied from day one, which the reference product itself does not have.
3. ✅ **Production Journal**: the costed execution. On Approve it checks availability through the tenant's *real* `NegativeStockBalanceAction`, consumes raw-material FIFO stock recording each line's actual returned cost, computes the six-figure roll-up in the Domain, creates the finished-good and by-product layers at the computed costs, posts one balanced GL entry, and commits **all of it in one `SaveChangesAsync`**. Void unwinds both directions and is refused once the output has been partly consumed.
4. ✅ **All three manufacturing reports** — Summary, Variance and Planning. The Phase 8f Annex 5 trap did not apply: the live pass answered every question about all three, so none needed guessing.
5. ✅ Angular: BOM list/detail, Production Order and Production Journal on the transactional-document chrome, three report screens, six dashboard links behind the two feature flags.

*Exit criteria — all met, verified with `sqlcmd` against real SQL Server on fresh Organizations:*
- ✅ BOM → Production Order → Production Journal end to end. Raw material held in **two layers at different costs** (10 @ 100, 10 @ 200): consuming 15 emptied the first layer and took 5 from the second, at a true weighted average of **133.3333** — not the latest rate, not the oldest, not the BOM's planned rate.
- ✅ Finished stock created at the computed cost (`UnitCost 184.0000`, equal to the document's `FinishedGoodsUnitCost`); the by-product's own layer at `153.3333`.
- ✅ **Conservation, computed in the database**: value in `2300.0000`, stock value created `2299.99990000`, residue `.00010000` — exactly the `CostRoundingAdjustment` the document reports. A second, clean-numbered run conserved **exactly, to the cent, with a zero residue**.
- ✅ GL hand-verified and **traced per account**: Inventory's net movement `+299.9999`, which is the production expenses and nothing else; Production Cost Applied `-299.9999`; no other account touched; the organization's trial-balance difference `.0000`.
- ✅ Kardex reconciles: one Out for the raw material, one In each for the finished good and by-product.
- ✅ A Manufacturing-off tenant sees none of it — 403 on reads, on writes, and on a read **against a nonexistent id**, so the gate demonstrably fires before the handler.
- ✅ A Journal exceeding available raw stock hits the real availability policy (Reject → 409; Warn → a confirmable 422 that a second Approve overrides).
- ✅ Negative permission proof for all 13 new keys: 403 naming each key against a nonexistent id, then **404 for the identical ids** once the grant is restored.

**Two bugs found, both by things unit tests structurally cannot reach.** A shared FluentValidation helper took `Func` selectors instead of `Expression`, so **every manufacturing endpoint returned 500** on its first real call — handler tests never run `ValidationBehavior`. And the browser pass caught the cost roll-up's **"Rounding Adjustment" row rendering `0.00`**, because the residue is smaller than a cent and `AmountPipe` was fixed at two decimals; the pipe gained an optional precision argument (default unchanged, so none of phase-23's 324 call sites moved).

**Found in passing and fixed:** `GetAccountingDefaultsQuery` implemented neither `IRequirePermission` nor `IOrganizationScoped`, so `AuthorizationBehavior` never ran for it — **any authenticated user could read any tenant's accounting defaults**. One line, in a file already being edited.

**Deliberately out of scope, decided not forgotten:** Custom Status on Production Order (live-confirmed to exist; Phase 20b's machinery already does it, so adding the document type is a small follow-up); multi-level BOM explosion (the live Planning report states "Multiple Level: No", so single-level is parity); Reporting Tags and Custom Fields on production documents; print/PDF for production documents; and `.xlsx` export for the three manufacturing reports.


---

## Index-table entries as originally written (phases 20d–25)

The roadmap's index table carried these as multi-paragraph cells; shortened to one line each on 2026-09-02, the originals are kept here.

**Phase 20d.** Printing Templates / Custom Templates (FR-11.2/11.3): confirm-live found the reference product's template gallery is a real visual editor, descoped by user decision to metadata-only `PrintingTemplate`/`CustomTemplate` lookups + `SetDefault`; the real deliverable is a print-to-PDF pipeline (QuestPDF, 2 shared layouts) wired for 6 document types, closing Phase 16c's deferred print output
**Phase 20f.** Tenant feature-flag enforcement (FR-2.6): `IRequireFeature` + `FeatureGateBehavior` (4th pipeline behavior) make `TenantSubscription`'s flags a real gate, `FeatureNotEnabledException` → 403 naming the feature. Investigation found only 2 of 7 flags have a surface to gate (`TrackInventory`, `MultipleWarehouses`) — both of FR-2.6's own examples are unbuildable here; scope reduced accordingly. MultipleWarehouses is a **cap at one**, not a block (nothing seeds a default warehouse and Invoice requires one). Read-only Subscription & Features screen; flags stay immutable, live-confirmed as matching the reference product
**Phase 20e.** Alert Scheduler (FR-11.1) — this codebase's **first background-job infrastructure**: hand-rolled `AlertSchedulerHostedService` (`BackgroundService` + `PeriodicTimer` + `TimeProvider`, scope per tick, `IOptionsMonitor`) driving `IAlertDispatcher`; `AlertDefinition` + an `AlertSendLog` ledger whose unique index on (definition, local occurrence date, recipient) delivers idempotency, multi-instance safety and at-most-once delivery at once. **No authentication-bypass surface was introduced** — the job sends no MediatR request, reading through `IAlertContentBuilder` with an explicit `OrganizationId` instead, so `CurrentUserService` still throws outside HTTP. Nepal-local (UTC+05:45) scheduling. Confirm-live closed every open question and surfaced a screen the scan had missed (Email Logs)
**Phase 21a.** Async job foundation + bulk import (FR-2.9, NFR-4.3): durable `ImportJob`/`ImportJobRow` queue driven by a second `BackgroundService` (`ImportJobRunnerHostedService`), template-based .xlsx import for Product/Customer/Supplier in create and update modes with per-row error reporting, cancellation and completion notification. **The first background job that writes**, so it answers the identity question 20e sidestepped: it reuses the real Create/Update commands through the full pipeline under a scoped `IJobActingUser`, which means `AuthorizationBehavior` **re-checks permission on every row at execution time**; an `HttpContext` always wins, so a job identity can never serve a request. At-most-once **per row** via a `(ImportJobId, RowNumber)` unique index — partial success is a `Completed` job, `Failed` means the file could not be processed at all. E2E found and fixed a concurrency token that made the user's own Cancel wedge the running job
**Phase 21b.** Full-tenant data export (FR-2.8, NFR-4.3): tenant-scoped `ExportJob` producing one multi-sheet `.xlsx` over FR-2.8's five named categories, downloaded through an authenticated endpoint. **Decision A is the phase** — FR-2.8 says "backup" and this codebase has no restore path, so what ships is an honest **export** that says so on the button, on the workbook's own Summary sheet and in the completion email. **Decision D restores 20e's default that 21a had to abandon**: an export only reads, so it runs with *no ambient identity at all* — the permission check and the `Audit` row (new `DocumentType.DataExport`) live on the enqueue command in a real HTTP request. **Decision C** answers 21a's deferred job-table question: separate tables, shared loop — 21a's runner became `QueuedJobRunnerHostedService<TProcessor, TOptions>` over a new `IQueuedJobProcessor` seam (a shared timer host, not a job framework). **Decision E** adds 7-day retention via `SweepAsync` on that seam and fixes the blob leak 21a shipped. Stated 25,000-row-per-sheet cap, disclosed in three places when hit
**Phase 21c.** Migrated tax-register import + the migrated Sales/Purchase Register variants (FR-2.10, **closing FR-9.4** — Nepal's statutory report set is now complete): two tenant-scoped aggregates hold a prior system's filed Sales/Purchase Book rows, surfaced by two new Admin-only report screens and seeded from a template-based .xlsx on a new `Configurations > Migration` screen. **Decision A is the phase** — a migrated row is *real enough for a tax report and deliberately not real enough to be anything else*: no `GlJournalEntry`, no stock movement, no payment, no document number, no Draft/Approve/Void lifecycle, no lock-date gate, and presence in exactly two reports. Two tables (the column sets diverge after five fields), a **free-text party** with an optional exact-PAN link that never mints a Contact (so both register row DTOs' `ContactId` widens to nullable), two appended `DocumentType` members, and a return modelled as a **negative row** exactly as the live registers render a CreditNote/DebitNote. **Decision C is 21b's Decision C run again and coming out the other way**: no new job table — every `ImportJob` column applies and the loop is the same loop, so the two migrated types are `ImportEntityType` members, create-only, with a new `EntityTypes` filter keeping the two screens' histories apart. **Decision D re-argues 21a's identity rule from scratch** (21a's *reason* does not transfer, since the create handler did not exist) and lands the same way for different reasons: per-row permission re-check, validation and audit. **Decision F** gives migrated rows reach into the two register variants only, with VAT Summary / Annex 5 / Annex 13 / TDS each opened, each found structurally unable to consume a register-level row, and each given a test proving it is unaffected. Confirm-live was not possible (non-interactive session), so **Decision E derives the template columns from Phase 19's live-confirmed statutory register** rather than guessing at an unseen screen
**Phase 22.** Document inbox (FR-10.3): `UploadedDocument` in `Domain/Workflow` reusing Phase 18's `IFileStorage`/`AttachmentValidation` unchanged, a `Workflow > Document` screen with Pending/Done tabs beside the Approval queue, conversion into all four of FR-10.3's targets, and a permanent viewable link from the produced transaction back to the scan. **Decision A states the invariant**: an inbox document is *evidence* — no number, no Draft/Approve/Void lifecycle, no GL entry, no stock movement, no lock-date gate — and the link to the transaction lives on the **document**, not on the transaction's `ReferrerType`/`ReferrerId` (Phase 6's bug #4 catalogues what that pair actually requires, none of which applies here), costing zero change to any transactional aggregate. One document, one transaction: a second link is refused, and so is the prefill. **Decision B is prefill-and-submit** — converting creates *nothing*; it opens the target's ordinary `new` form with the scan beside it and the user's Save runs the normal `CreateXCommand` through the full pipeline, so numbering, validation, lock-date, posting rules and audit stay untouched. The document id rides the **URL**, not `PendingTemplateStore`, which is read-once, dies on reload and never covered two of the four targets. **Decision C is this codebase's first egress of customer data to an LLM and is a product decision**: what leaves is the bytes of the one clicked file plus a fixed prompt — no contacts, no catalogue, no identity, no other document — behind two default-closed gates (a withdrawable tenant opt-in on `TenantSettings`, **default off**, plus an Admin-only `.Extract` key), never automatic, synchronous with a timeout and **no new job table** (21c's test again: not rows, not a loop), audited via a new `DocumentType.DocumentExtraction`, and failing as an *outcome* that leaves the document exactly as convertible by hand. The **conversion has no key of its own** — the prefill query resolves to the target type's own Create key, so the inbox can never be a side door
**Phase 23.** Nepali localization & parity odds-and-ends (NFR-1.1, NFR-1.2, FR-5.8). **What is stored never changed: every date is AD, always** — BS is presentation and entry only, converted at one client edge (`web/src/app/shared/formatting/`), so no column, DTO, report window or migration gained a second meaning. **Decision B is the phase's risk and was treated as data under test**: BS month lengths are not computable, so the table was cross-checked across four independent implementations — all four agree through BS 2083, two of them then emit filler rows (`…,30,30,30`) where their real data ran out, and the two with genuine data agree through **BS 2092**, which is the supported range (AD 1943-04-14 … 2036-04-13). Outside it the functions return null: never a guess, never a clamp. **Decision D made completeness mechanical rather than asserted** — 324 inline `.toFixed(2)` money renders across 40 files and 66 native `<input type="date">` across 42 were swept, and `sweep-guard.spec.ts` reads every template off disk at test time and fails the build on a new one, with a reasoned allow-list. Both sweeps are complete, because a half-converted app is worse than an unstarted one. Preference lives in `localStorage` (**Decision C**: no migration, but explicitly no cross-device sync — and server-rendered PDFs/`.xlsx` stay AD, stated rather than discovered). **FR-5.8's tax treatment was live-confirmed in the DOM, not inferred**: ticking export disables the per-line Tax selector and pins every line to `0 Vat`, so `Invoice.AddLine`/`SetExport` enforce zero-rating in the aggregate — `ZeroVat`, not `NoVat`, since both compute zero but file under different statutory headings. **Decision F held the dashboard to queries that already existed** — the four KPI cards and the balance panel add no Application-layer aggregation — and then **overrode its own rule once, on purpose**: the recent-activity feed is the phase's only new query, because no existing one returns a mixed transaction stream and a client-side merge of five per-type endpoints cannot page a merged stream correctly (the first stated reason for omitting it, phase-16c's bug #1, did not actually apply — a feed has no footer total). **One new permission key (Decision G)**, `Workflow.RecentTransaction.View`, blanket in `TransactionApprovalQuery`'s exact sense: its job is to make `AuthorizationBehavior` verify org membership, while the real gating is per document type inside the handler against each type's own `*.View` grant — so a partial grant yields a partial feed rather than a 403 or a leak, and **the key alone shows nothing**, which is asserted. Every other surface in the phase adds no key: each dashboard card rides the key of its own query, so a Member sees a smaller dashboard rather than a broken one
**Phase 24.** Variant Products & Attributes (FR-8.3), deferred since Phase 3. **The live pass changed the central decision, and that is the phase**: confirmed against the reference tenant, **a variant IS a Product** — a parent and its four variants are five rows in the same Products list, each with its own Code and prices, and the invoice picker lists them flat. So `ProductId` already means "the sellable, stockable thing": the FIFO ledger, all twelve `ProductId`-bearing entities, both composite indexes and every one of the 25 report handlers are **untouched**, and the roadmap's own "the FIFO ledger keys extend from ProductId to variant identity" described a change that turned out not to be needed (**Decision A**). The **migration is purely additive** — five nullable columns on `catalog.Products`, three tables, no `DropColumn`/`DropIndex`/`DropTable` anywhere in `Up`, nothing touching the stock tables — and was proven on real SQL Server against 1,089 live products and 17 live FIFO layers by SHA-256 fingerprinting both stock tables before and after (**Decision B**). **One new rule is the whole sweep**: a variant *parent* may never reach a document line, because a parent stock bucket reconciles against nothing — the reference product does offer it and we deliberately refuse. Server-side that is four call sites, client-side **one line** (all fifteen pickers already shared `CatalogService.listAllProducts`), and both halves have a guard test that reads the tree off disk and fails the build on a new bypass (**Decision D**). **The conversion cap needed no change and that is a finding, not an omission** — Phase 6's `(ProductId, Rate, VatRate, DiscountPct)` key already discriminates two variants sharing a rate, asserted rather than reasoned about (**Decision E**). Generation ships even though the live product has none (it adds variants one at a time), because FR-8.3 and this exit criterion both ask for it; re-running skips rather than duplicates, and overshooting the 200-per-run cap is **refused, never truncated** (**Decision C**). **No feature flag** — `TenantFeature` is immutable after Organization creation, and having variants is a property of a Product, not an entitlement — and **no key for variants themselves**: creating one is creating a product, so it rides `Catalog.Product.Manage`. The only new pair is the attribute catalog, on `ProductCategory`'s exact split (**Decision F**). **No report changed** (**Decision G**): a variant is a Product row with its own Code, so every inventory and Master report shows one row per variant automatically, and a non-variant tenant sees no change at all
**Phase 25.** Manufacturing (FR-8.8/8.9 + FR-9.5's manufacturing slice), behind the Manufacturing feature flag: BOM, Production Order and the costed Production Journal, plus all three manufacturing reports. **The confirm-live pass closed the scan's own open item, and the answer was "nothing"**: a Production Journal was created and approved in the reference tenant and does not appear in a **199-row Journal report covering that exact date**, while its stock moved — that tenant runs *periodic* inventory, under which production genuinely has nothing to post (**Decision A**). We post anyway, because this codebase is *perpetual* since the post-Phase-19 fix: posting nothing would leave the Inventory account understating stock by the capitalised expenses, permanently and silently. The entry is Inventory-to-Inventory gross with **one new tenant default account** taking the difference, and its **net effect on Inventory is exactly the production expenses added** — traced per account before a line was written, per phase-6 bug #3. **The conservation law is the phase and is proven in the database**: 15 units held in two layers at different costs consumed for a true weighted average of 133.3333 (not the latest rate, not the BOM's), value in 2300.0000 against stock value created 2299.99990000 with the **0.0001 residue the document itself reports** — and a clean-numbered run conserving exactly, to the cent. Three more decisions came from the live pass rather than from reasoning: the by-product's "% of Cost" is a percentage **of the Total Cost of Production**, verified to the penny on two real journals (**C**); "LOAD BOM" is an explicit template load that scales by output ratio and leaves the percentage alone, so a BOM **defaults and never binds** (**D**); and Production Order's native lifecycle **is** Draft → Approved — the scan's "Planned/InProgress/Completed" is Phase 20b's Custom Status in the list grid, orthogonal to it (**E**). The reference product lets one order convert to a journal **repeatedly** (phase-6 bug #4 in the wild); we refuse. Void unwinds **both directions** and is refused once the finished goods have been partly sold. **Two bugs, both found by things unit tests structurally cannot reach**: a shared FluentValidation helper taking `Func` instead of `Expression` made *every* manufacturing endpoint 500 on its first real call, and the browser pass caught the roll-up's rounding-residue row rendering `0.00` because it is smaller than a cent

---

## Parity-sequence planning entries (phases 26–34), moved out of `docs/roadmap.md` on 2026-09-10

Verbatim. The 34c entry is repeated in the live roadmap because it is still open; everything else here is history — outcomes are in each `docs/phase-N-status.md`.

## The planned v1 sequence is complete

Phase 25 was the last v1 phase, so every phase in the index table above is done, and **no confirm-live
or browser-pass debt is outstanding** (`phase-25-status.md`'s Step 3 records how a browser pass is run
in a non-interactive session). What follows is the second sequence: parity with the reference product.

---

## Parity phases (26–34) — from a gap analysis against the reference product (2026-09-02, confirm-lived the same day)

**Method.** `erp-module-scan.md`'s module-by-module inventory of Tigg was diffed against what the
codebase now has (report pages, endpoint groups, Domain aggregates, the wiring of each cross-cutting
editor, and which `TenantSettings` are actually *read* by a handler). Every screen the plan depends
on that the scan had never opened was then read live on the Moonbeam UAT tenant — the findings are
in `erp-module-scan.md` under "Confirm-live pass for the parity plan (2026-09-02)" and are cited
below as **(live)**. Three kinds of gap came out:

1. **Catalog gaps** — Tigg lists 40 reports; 24 exist here (plus 3 manufacturing). 27 are missing,
   and two PRD requirements are only partly met by them (FR-9.2's receivable/payable summaries,
   FR-9.3's by-customer/by-item/monthly analytics). All 27 were opened live; none is a surprise
   of the Annex 5 kind, two need data this codebase does not store (below).
2. **Rollout gaps** — mechanisms that exist but reach a fraction of their surface: Custom Fields on
   2 of 17 document types, Custom Status on 2 of ~5, Reporting Tags on 2, print/PDF on 6 of 15,
   Custom Templates with **no consumer at all**, import on 3 of 7 entity types. Three
   `TenantSettings` (`SuggestSellingPriceMode`, `ProductPriceBasis`, `NegativeCashBalanceAction`)
   are stored and edited but **read by nothing**; `TrialEndsAt` likewise.
3. **Structural gaps** — Tigg-core features the v1 roadmap deferred or never named: multi-currency,
   landed cost, outbound email, customer credit control, Billing Locations, global search.

**What the live pass changed.** Multi-currency and landed cost moved *up* (both fully observable
here, shapes recorded); Billing Locations moved *down* (the feature is an entitlement that is off on
this tenant, so its screens cannot be read); Delivery Note / GRN moved to the deferred list (the
"Mode of Inventory Tracking" setting the scan recorded is gone from the General page, though the DO
and GRN numbering rules remain at next-number 1); and a **Credit Limit** feature the scan missed
entirely was added (contact-level limit plus a Reject/Warn/Do-Nothing policy).

**Ordering rule.** Cheapest-per-parity first, and nothing that changes a stored shape before the
reports that will read it exist. Every phase keeps the v1 exit bar (build/test green, curl-seeded
E2E, one proven negative path, a status doc) and the confirm-live rule.

### 26. Report catalog completion (FR-9.1/9.2/9.3/9.5/9.6, three sub-phases)
- **26a — Accounting. DONE (`docs/phase-26a-status.md`).** Transaction list (live: Txn Type + Status filters; Created/Approved By/At
  columns), Journal report, General Ledger Summary, Detail General Ledger, GL Master Report; and the
  **Compare** (period-over-period) column on Trial Balance / Balance Sheet / Income Statement that
  FR-9.1 names and Phase 8a never built. All read `GlJournalEntry`; nothing new is stored.
- **26b — Receivable/Payable and analytics. DONE (`docs/phase-26b-status.md`).** All thirteen
  built, over **seven** handlers (each mirrored pair answered once, discriminated by a side the
  route hardcodes). Confirmed live 2026-09-03: age runs from the **Due Date**; a contact-tagged
  Journal Voucher really is an ageable document; and **all four Monthly variants are keyed by a BS
  fiscal-year picker**, not a date range — so `Domain/Common/BsCalendar` arrived with five consumers
  rather than the one predicted here. Service Charge omitted with a note, as directed; Quick
  Payment/Receipt omitted too (phase-17 made it a `Payment`, not a document type). Twenty-six
  permission-seed rows are the only migration.
- **26c — Inventory, tax, system, analytics. DONE (`docs/phase-26c-status.md`).** All nine built,
  plus the manufacturing exports. Two live findings reversed this bullet's own predictions:
  the main Sales/Purchase Registers **keep** their credit/debit notes (the same notes appear in both
  registers, negative in the main one and positive in the return one, with the main footer net of
  them — phase 19's folding was parity, not a simplification), and the Purchase Return Register is
  **not** the Sales Return Register's mirror but the *Purchase* Register's, with seven money columns
  to the sales side's four. `Inventory.Reports.StockFactReader` is the shared reader the four
  inventory reports and Net Trading Assets' Inventory Items row agree through; `UserLoginEvent` is
  the only new table, deliberately carrying no `OrganizationId`.
- Each report gets its own `Reports.*` key (Admin-only where it exposes per-transaction rows or
  identity, per the standing rule — User Log is Admin-only), `.xlsx` export via
  `ReportSpreadsheetExporter`, and the manufacturing reports get the export they still lack. Exit:
  every card on Tigg's Reports landing page has a counterpart here or a recorded reason not to.

### 27. Cross-cutting rollout sweep (two sub-phases; mechanical, guarded by sweep tests)
- **27a — Document-level mechanisms. Done, `phase-27a-status.md`.** Custom Fields to 11 more
  document types (13 total, not the assumed 15 — Configurations > Custom Fields live-confirmed 16
  sections, four payment kinds collapsing onto this codebase's one `Payment`; Warehouse Transfer and
  Inventory Adjustment carry no such section at all); Custom Status to Sales Order and Production
  Order (20b's machinery, unchanged); Reporting Tags to every transactional type plus both Opening
  Balances kinds, tagged by each row's own line id; Overview/Tasks/Documents/Activity tabs on all 15
  transactional detail pages via one shared `app-document-tabs` component (Comments lives as an
  Activity sub-tab, not a top-level tab — the roadmap's tab list here was wrong). `Comment` went
  polymorphic (`CommentParentType`), the trigger phase-18 Decision #2/#3 deferred that generalization
  to. One shared `DocumentMechanisms` classification table plus a server guard test
  (`DocumentMechanismSweepGuardTests`) and a client guard spec
  (`document-mechanism-sweep-guard.spec.ts`) prove all four sweeps complete.
- **27b — Output. Done, `phase-27b-status.md`.** Print/PDF for the 9 unwired `DocumentType`s (all 15
  now; live-confirmed present on every one, including both production documents) — and the live pass
  reshaped it: the reference product prints one frame with a *varying number of titled tables*, so
  `PrintableDocumentDto` became a section list and the renderer went from two layouts to one that
  switches on no `DocumentType` at all. BS dates in server-rendered PDFs and `.xlsx` (phase-23
  Decision A closed, via an `X-Calendar` header and an ambient `RequestCalendar`, `Domain/Common/BsCalendar`
  reused as 26b left it). The three pagers, Turnstile on the wizard (one server call behind three
  steps, so one check), and a feature-flag route guard — buildable after all, because Phase 25 gave
  `Manufacturing` a real surface 20f could not gate. **Terms and Conditions is 5 document types, not
  2** (Quotation, Sales Order, Invoice, Credit Note, Purchase Order; absent from Purchase Bill,
  Expense, Debit Note).
- **Custom Templates got their first consumers here (done).** `TermsAndConditions` seeds an editable
  terms block on the five document types that carry one live;
  `CustomerBalanceConfirmation`/`SupplierBalanceConfirmation` render as a PDF letter from the Contact
  statement, agreeing with it by construction (both read `ContactLedgerReader`). The `Email` type
  waits for Phase 30, alongside the `Send Email` action live-confirmed on Invoice/Credit Note/Payment.

### 28. Multi-currency (FR-2.5, NFR-1.3). **DONE (`docs/phase-28-status.md`).**
- Live: **Currency (default Nepalese Rupee) + Exchange Rate To NPR\*** sit on the Invoice and
  Purchase Bill add forms even on an NPR-only tenant; the Opening Balances row form carries
  **Currency + Conversion Rate**; `Organization > Features > Multiple Currency` lists NPR with an
  ADD NEW CURRENCY action; the Chart of Accounts has a **"Forex Gain"** account (Income, group
  "Foreign Exchange Gain") — the product realises exchange differences.
- **Scope.** `Currency` list seeded from the standard catalog with NPR fixed active; `MultiCurrency`
  flag as a cap (NPR only when off, exactly the 20f pattern); `CurrencyCode` + `ExchangeRate` on
  every document that shows them live (Quotation, Sales Order, Invoice, Credit Note, Purchase Order,
  Purchase Bill, Expense, Debit Note, Journal Voucher, Cash Transfer, Payment, opening-balance
  line); amounts stored in transaction currency with the NPR figure folded onto `GlLine` at Approve
  (the phase-16b discount pattern — reports need zero change); two new tenant defaults, Forex Gain
  and Forex Loss accounts, and a posting rule on **Payment allocation** that books the difference
  between the invoice's booked NPR value and the payment's NPR value. Decision A of the phase is
  whether unrealised revaluation at period end is in scope (recommend no: Tigg shows no revaluation
  document, only the realised account).
- **The decisive experiment could not be run, and that is a finding.** The reference product's own
  "Add New Currency" catalog picker returns **"No data"** on the UAT tenant (two 400s in its
  console), so no second currency can be activated there and no foreign-currency document can exist.
  The allocation posting rule is therefore **reasoned from first principles and recorded as
  reasoned**, in `PaymentForexCalculator`'s own doc comment as well as the status doc, with one
  strong corroboration: that tenant's chart carries a *realised* Forex Gain account under Indirect
  Income and no unrealised or revaluation account of any kind.
- **What the live pass did settle, all of which changed the design.** The Multi-Currency switch is
  self-service and on; a document's Currency picker reads **the tenant's own active list** and its
  Exchange Rate input is **disabled and pinned to 1 on the base currency** — which is why the
  entitlement became a cap on the *currency list* and **no document command is feature-gated**;
  Opening Balances' Conversion Rate is the identical control, so it is a document rate, not an as-at
  one; the chart ships **Forex Gain with no loss counterpart** (we ship two anyway, on phase-6's
  VAT-Receivable-vs-Payable precedent); and the printed document carries **one money column, a
  currency-coded Net Total and no NPR equivalent at all** — a layout with no column for it cannot
  print one, so the printed figure is the transaction currency.
- **Decision A resolved: no.** Unrealised period-end revaluation is out of scope, corroborated live.
  The fold went on the posting rule's *inputs*, not its finished lines — every rule derives its
  balancing leg as a sum, so converting afterwards breaks the balanced-entry invariant
  intermittently. Zero report changes, as predicted.

### 29. Landed cost (FR-6.15, Cost Terms' other half) — **COMPLETE** (see `docs/phase-29-status.md`)
Shipped: `PurchaseBillAdditionalCost` + `PurchaseBillAdditionalCostAllocation`, allocated at Approve
by Value or Quantity across the bill's **goods** lines and capitalised into the received FIFO layers'
unit cost, with the phase-25 conservation law proven in SQL and the residue named
(`AdditionalCostRoundingAdjustment`). The decisive experiment turned out to be unnecessary: two
already-approved reference bills answered it read-only, and the answer was that the reference product
posts **no GL at all** (it is periodic) while fully capitalising the cost into stock. We post anyway —
Debit Inventory / Credit a new `DefaultLandedCostClearingAccountId` — on phase-25 Decision A's
argument. Debit Note gained a release leg; Void needed none. The original plan, for the record:
- Live, on the Purchase Bill itself: an **Additional Cost** section with an "Add product-wise"
  toggle and rows of *Cost Term × Product ("All Product" or one product) × Method (Value |
  Quantity) × Amount (NPR)*. `CostTerm.AdditionalCost` (20c) is the lookup; Phase 25 consumed only
  the `ProductionCost` half.
- **Scope.** `PurchaseBillAdditionalCost` lines; at Approve, allocate each amount across the bill's
  goods lines by value or by quantity (or to the one named product), and capitalise the allocation
  into the received FIFO layers' unit cost — the phase-25 conservation law again
  (`bill goods value + additional cost = layer value created + residue`) with the same
  `UnitCostScale` rounding and a named residue. GL: the additional cost debits Inventory and credits
  the supplier (same bill) or a separate payee — confirm live which, by approving one bill with a
  Freight row and reading its GL Transactions. Debit Note / Void must unwind the capitalised cost
  (phase-16a mirror rule).

### 30. Communications — outbound email, SMS medium, email logs (FR-11.1, FR-4.5's Email Logs) — **DONE**

Shipped; see `docs/phase-30-status.md`. The confirm-live pass corrected this heading's own scope
three times, so the original text is kept below struck through rather than silently edited:

- ~~That dialog on every printable document and on the Contact statement~~ — **6 of 15 document
  types** (Quotation, Sales Order, Invoice, Credit Note, Payment, Purchase Order), **not** the
  Contact statement report, **but** the Contact detail page, which the heading did not anticipate.
- ~~merge fields from the `Email` Custom Template~~ — email templates are **their own aggregate**
  (`EmailTemplate`), served by a different resource in the reference product, carrying six fields
  `CustomTemplate` does not have and a disjoint type vocabulary. `CustomTemplateType.Email` is
  deleted.
- ~~`AlertMedium.Sms` … (20e listed it as one enum member and a branch)~~ — it was four changes;
  the one nobody predicted is that it spends SMS credit.
- The rest held: the PDF comes from 20d/27b's pipeline and is attached by default, the Email Logs
  tab now has data behind phase 27b's pager, and every send goes through a claim-then-act ledger
  where a resend is a new row.

### 31. Credit control, dead settings, and the small carried items — **DONE** (see `docs/phase-31-status.md`)
- Shipped: `Contact.CreditLimit` (0 = no limit) / `CreditTermId` / `AcceptsReverseTransactions` (one
  field, labelled "Accept Purchase" on a Customer and "Accept Sales" on a Supplier; a Lead carries
  none of the three), a tenant **Credit Limit Exceeds** policy enforced **at Invoice Approve** against
  the customer's `ContactLedgerReader` balance, and the **Configurations > General** screen the other
  four settings needed to exist at all.
- **Negative Cash Balance** on Payment / CashTransfer / JournalVoucher Approve; **Suggest Selling
  Price** and **Product Price Basis** decided server-side at the line picker, mirroring the reference
  product's own `get_recent_selling_price` call; **`TrialEndsAt`** as read-only-for-documents via a
  fifth pipeline behavior reusing the lock-date markers, plus the `TenantSubscription` mutator 20f
  left out.
- Carried items closed: **Cheque Bounced** now voids the payment it settled and reverses its GL entry
  (phase-17 decision #4); a **stored `DueDate`** on Invoice and PurchaseBill, which also closed
  26b's carried item, 30's `$[DUE_DATE]$` and 26b's ageing follow-up #4; and the **Include Credit
  Note In Calculation** toggle, which the confirm-live pass proved is *not* inert (19 rows to 8 on the
  live tenant), correcting phase 26c.
- **Deliberately not taken** (agreed with the user, recorded rather than omitted): import for
  Account / Product Category / Account Group / Contact Personnel and for variants (21a and 24's
  deferred lists), export date range and extra categories (21b), and the newly-found **Group By
  Bill** toggle on the Sales Register. Negative Item Balance's Warn/DoNothing branches stay
  fictional — making them real is a Domain invariant change (negative FIFO layers), not a setting.

### 32. Billing Locations (FR-2.3, FR-3.3) — **DONE** (see `docs/phase-32-status.md`)
- **The premise of this entry was wrong, and that is its main lesson.** It said the
  location-enabled screens "cannot be read here" and pre-authorised phase-21c's derive-instead
  precedent. A second tenant (`cadehi.tigg.app`, `Location Enabled = Yes`) was available the whole
  time, and the 2026-09-07 pass over it made the phase **observed, not derived** — correcting four
  things this entry had assumed. *Before invoking "derive because we cannot observe", ask whether a
  different tenant can observe it.*
- Shipped: `BillingLocation { Code, Name, Address, WarehouseId, LocationType }` under `Tenancy` with
  its Features card; **HeadOffice seeded unconditionally** at Organization creation and
  `MultipleLocations` as a **cap at one** (phase-20f Decision #4's shape, third instance after 20f's
  warehouses and 28's currencies); nullable `LocationId` on **all 17** location-bearing types in one
  migration with a hand-written seed-and-backfill; **location-wise numbering pools** consumed at last
  (`DocumentNumberingRule` gained a nullable `LocationId` — null is the settings row); the location
  filter and column on the Invoice list and the Sales Master Report.
- **What the live pass changed.** `LocationType` is **system-assigned** — the create dialog is Code,
  Name, Address, Warehouse and nothing else. The picker is a document **header** control rendering
  `Name (Code)`, not a body field beside Warehouse. And the scope is a **runtime tenant setting** (the
  Advanced panel: *Sales Transactions Only* by default, *All Transactions* opt-in), which is why the
  schema is sized for the widest setting rather than the default one.
- **Split to 32b:** the per-location permission matrix, whose shape is now confirmed exactly — the
  **Transactions group alone** (94 keys) replicated per location, with General/Settings/Reports
  org-wide, which is also why *Implement Location Wise Permission for Report View* is a separate
  toggle. That toggle ships stored, editable and enforced by nothing, recorded as such.
- POS location *types* stay modelled, not built.

### 32b. Per-location permission scope (FR-3.3, `architecture-spec.md` §3.7) — **DONE** (see `docs/phase-32b-status.md`)
Split out of 32 by agreement, because it is the largest single piece and it touches
`AuthorizationBehavior` — the one mechanism verifying org membership at all. Its shape is **already
confirmed live** (2026-09-07), so this phase starts from an observed design, not a guess:

- The role editor is **two** top-level sections, not N scope groups: *Organization-wide* (General 20
  + Transactions 94 + Settings 9 + Reports 52 = 175) and *Location-specific*, which is the
  **Transactions group alone** replicated per location — 94 × N, breaking down identically as Sales
  25 / Purchase 25 / Accounting 24 / Inventory 20.
- So a location-scoped key is a location segment prefixed onto a **transaction** key
  (`"HeadOffice.Sales.Invoice.Approve"`) — §3.7's guess, confirmed and **narrowed**: only transaction
  keys ever carry one.
- The real key depends on the loaded row's own `LocationId`, which `IRequirePermission.PermissionKey`
  cannot express. That is phase-27a's `AttachmentAccess` pattern a third time (after 27a and 31): a
  blanket key on the request, the real key re-checked inside the handler with the identical
  `ForbiddenException` shape. The E2E must prove it in **both** directions — 404 on a nonexistent
  document (so the caller does hold the pipeline key) and 403 naming the location key on a real one.
- Also here: the consumer for `TenantSettings.LocationWiseReportPermission`. **This entry's own
  claim that turning it on "pulls the 52 Reports keys into location scope" was wrong**, and the
  2026-09-09 confirm-live pass proved it: with the toggle ON and persisted across a hard reload, the
  live matrix stayed at 0 of 282 = 94 × 3 with no Reports group. It narrows report *rows* to the
  locations a role holds transaction grants at, which is what shipped -- on the Sales Master Report,
  the only report with a location dimension (phase 32's carried item #4 gates the rest).
- The seed-size warning **dissolved**: an absent row is a denial and the live default is 0 of 94 at
  every location, so only *granted* rows exist and nothing is seeded per location at all.

### 33. Platform chrome — global search, history, Quick Links ✅ (see `phase-33-status.md`)
- **Done.** What this entry said, and what the 2026-09-09 confirm-live pass on both tenants found:
  - "global search across contacts, products and document numbers" — right about those three, but it
    omits **Accounts** (a fourth `collection`) and, more importantly, the **navigation half**:
    screens and `Add X` create actions, which on a typical query are the majority of the ten results.
    It is half a command palette. A document is matched on its **number alone** — searching a
    customer's name does not surface that customer's invoices.
  - "a History/Browse list of recently opened **records**" — wrong twice. It is **client-only**
    (`localStorage`, no request on open *or* on navigate), and it lists **screens, one per module**;
    it stores a record url but derives the label from the route, so a contact detail page renders as
    "CRM · Contacts". Decision C was settled by observation, not by costing a write-per-open.
  - "the per-user Quick Links tray" — right, and server-stored, which is what made the per-user store
    worth building. **Two day-one consumers, not three**, once History dropped out.
  - "the Tigg Subscriptions read-only screen" — **carried, deliberately.**
    `organizations/:id/features` already carries the plan, the expiry and seven entitlement flags;
    the three fields it lacks (Amount, the two quotas, IRD Verified) have no writer and no reader,
    which is phase-31's lesson in reverse. Re-entry condition: a billing feature that sets them.

### 34. Hardening — accessibility, consistency, scale — **SPLIT into 34a / 34b / 34c**

This entry was three sweeps and a ~130-route re-layout in one phase. It was split by agreement on
2026-09-10; `phase-34a-status.md`'s Decision B records why, and why accessibility went first even
though the re-layout comes later: **the template-level sweep is layout-independent and survives 34b
untouched, while the landmark-level work is small and is better built *into* the new shell than
retrofitted onto it.**

#### 34a. Accessibility (NFR-6.2) — **DONE** (see `docs/phase-34a-status.md`)

#### 34b. Consistency and the shell (NFR-6.1) — **DONE** (see `docs/phase-34b-status.md`)

**What shipped.** The four shell controls phase 33 deferred by name — left nav, Create New flyout,
company switcher, global date filter — plus a **Reports index page** (52 routes were unreachable from
a nav that renders Reports as one leaf, which is what the reference product does) and the list chrome
NFR-6.1 names.

| | before | after |
|---|---|---|
| list screens with a search box | 1 of 46 | 21 of 21 paginated ones |
| `List*Query` accepting a search term | 2 of 47 | 25, with 9 exempt and reasons stated |
| lists scoped by a date range | 1 | 16 document lists |
| page templates edited for the re-layout | — | **0** |

**Decision A's dichotomy was false**, and that is the entry worth remembering: all 130
in-organization templates already open with `<div class="container py-5">`, so a fixed rail plus a
left inset gave a permanent nav for no page edits at all. Decision B took the full server-side search
sweep over a client-side filter of the current page, which on `Skip`/`Take` lists would have been a
worse feature wearing the same chrome. Decision C kept `list-group` rows and moved sorting into the
chrome, stating the price. Decision D scoped the date range to aggregates with a business `Date`
rather than copying the reference product's own inconsistency.

**The confirm-live pass falsified the module scan again** (fourth phase running): the global date
filter scopes *list queries*, not dashboard figures. The Create New flyout, by contrast, matched the
scan verbatim.

#### 34c. Scale (NFR-5.1/5.2) — after 34b
- The dataset and the measurement are already decided in `phase-34a-status.md`'s Decision C:
  **50,000 invoices / 50,000 contacts / 20,000 products**, seeded by direct `INSERT` (not through the
  API — that would measure the seeder), with p95 taken for each list's first *and last* page, the
  three financial statements, the two heaviest registers, and global search.
- **The export rewrite is conditional and the condition is still unmet.** The 25,000-row cap and the
  OpenXml SAX streaming writer wait on a tenant having hit the cap; there are no tenants. 34c decides
  it *after* the measurement — phase-8f's "omit rather than fake" in a new shape.
- Phase-33 carried item #4 (global search's per-collection cap of 5 and its 18-query fan-out) is
  measured in the same pass, and stays carried until then: against today's two invoices any number
  would be meaningless.

**Recommended drop list (decided, not silently omitted):** `Organization > Developer Mode` and
`> Documents` (phase-25's recommendation), `Product.PrintProfileId` (20d), the Marketplace flag,
and the Service Charge column (no product flag to drive it; revisit only with POS).

---

## Index-table entries as originally written (phases 27a–34b)

Shortened to one line each in `docs/roadmap.md` on 2026-09-10; the originals are kept here.

**Phase 27a.** Cross-cutting rollout sweep, document-level mechanisms: Custom Fields to 11 more types (13 total, not the assumed 15), Custom Status to Sales Order + Production Order, Reporting Tags to every transactional type plus Opening Balances, Tasks/Documents/Activity tabs on all 15 transactional detail pages. `Comment` generalized to a polymorphic `CommentParentType` (phase-18 decision #3's deferred trigger). One shared `DocumentMechanisms` classification table plus a server guard test and a client guard spec prove the sweep complete

**Phase 27b.** Cross-cutting rollout sweep, output: print/PDF for the 9 unwired document types (all 15 now, live-confirmed universal) on **one generic section-based layout** replacing phase-20d's two; **Bikram Sambat in server-rendered PDFs and `.xlsx`** via an `X-Calendar` header + ambient `RequestCalendar`, closing phase-23 Decision A; pagers on Email Logs / import history / export history; Turnstile on the New Organization wizard; a feature-flag route guard (3 real flags, 13 routes). `CustomTemplate`'s first two consumers: Terms and Conditions on 5 document types (not the 2 assumed) and the Customer/Supplier Balance Confirmation letter

**Phase 29.** Landed cost (FR-6.15, Cost Terms' other half): an Additional Cost section on the Purchase Bill (Cost Term x Product x Method x Amount, plus the product-wise matrix), allocated at Approve by Value or Quantity across the bill's **goods** lines and capitalised into the received FIFO layers' unit cost — conservation law proven in SQL, residue named. The reference product posts no GL at all (it is periodic); we post Debit Inventory / Credit a new Landed Cost Clearing account, on phase-25 Decision A's argument. Debit Note gained a release leg

**Phase 30.** Communications (FR-11.1, FR-4.5's Email Logs): a **Send Email** dialog on 6 document types and the Contact detail page, an Email Logs tab with data behind it, an Email Templates config page, and `AlertMedium.Sms`. Confirm-live corrected the scope three times — Send Email is on 6 of 15 types, not the statement report but yes the Contact page, and email templates are **their own aggregate**, not a `CustomTemplateType` (that member is deleted). Sends go through a claim-then-act ledger and a fourth background job; idempotency is a client-minted request id, so a double-click is one email and a reopened dialog is a new row

**Phase 31.** Credit control, dead settings, and the small carried items: `Contact.CreditLimit`/`CreditTermId`/`AcceptsReverseTransactions`, a **Credit Limit Exceeds** policy enforced at Invoice Approve, and a **Configurations > General** screen that makes all five behaviour settings reachable for the first time — four of them had no command, no endpoint and no screen at all. Plus **Negative Cash Balance** enforced on the three documents that can take money out, **Suggest Selling Price / Product Price Basis** decided server-side at the line picker, a **stored Due Date** on Invoice and Purchase Bill (closing 26b's carried item, 30's `$[DUE_DATE]$` and 26b's ageing follow-up), a **bounced cheque** that voids the payment it settled, subscription expiry as read-only-for-documents plus the renewal that lifts it, and the **Include Credit Note In Calculation** toggle — which the confirm-live pass showed 26c had wrongly recorded as inert

**Phase 32.** Billing Locations (FR-2.3/FR-3.3) — **and the phase this roadmap said could not be confirm-lived, was.** A second tenant with `Location Enabled = Yes` turned the whole phase from derived to observed, correcting four things this entry had assumed. A `BillingLocation` aggregate with a **HeadOffice row seeded unconditionally** and `MultipleLocations` as a **cap at one** (phase-20f Decision #4's shape, third instance); the **Advanced** panel that makes location scope a *runtime tenant setting* (Sales-only by default, All-Transactions opt-in) — which is why nullable `LocationId` went onto **all 17** location-bearing types in one hand-written seed-and-backfill migration rather than the four the default names; a document-**header** location picker (not a body field); **location-wise numbering** consumed at last; and the location filter/column on the Invoice list and Sales Master Report. `LocationType` is system-assigned — the live create dialog has no such field

**Phase 32b.** Per-location permission scope (FR-3.3) -- the role editor's **second matrix**: *Organization-wide Permissions* ("Apply across all billing locations") and *Location-specific Permissions*, the **transaction keys alone** replicated per location (77 of this codebase's 222, derived from `DocumentMechanisms.LocationBearing`). A grant is a `RolePermission` row with a nullable **`LocationId` FK** -- null is the organization-wide grant, so no backfill and nothing seeded per location, which dissolves this roadmap's own seed-size warning. Enforced by one extra branch in **`AuthorizationBehavior`** (phase-27a's `AttachmentAccess` pattern, third use, promoted to the pipeline because it spans ~120 requests) plus four marker interfaces and a sweep guard. **And the confirm-live pass falsified what four documents recorded**: turning `LocationWiseReportPermission` ON does *not* pull the 52 Reports keys into the matrix -- it narrows report *rows*, and that is the consumer that shipped

**Phase 33.** Platform chrome (the top bar this app never had): a **global search** (Ctrl + /) in the shell, a **History** popover, the personalisable **Quick Links** tray, and the thing all of it exists to decide -- **`UserPreference`, the per-user server store** (a row per `(OrganizationId, UserId, Key)` with an opaque JSON value), which also closes phase-23 Decision C's `localStorage`-only calendar choice. **Three of the four things this entry recorded were wrong**, and the confirm-live pass on both tenants is what showed it: History is **client-only `localStorage`** and lists **screens, one per module** rather than opened records; global search is **half a command palette**, whose navigation and create-action half is usually the majority of a result set and whose record half omits a whole collection (Accounts) from this entry's list; and a document is matched on its **number alone**, never through its contact's name. Search is filtered per collection *and* per billing location through 32b's existing `LocationAccessScope` seam -- building which surfaced a real defect: the two pre-32b inlined permission joins in the approval queue and the recent-activity feed still read location-scoped grants as organization-wide ones

**Phase 34a.** Accessibility (NFR-6.2, WCAG 2.1 AA): the mechanisable half of the standard swept across all 161 templates and pinned by a guard spec — **page titles derived from the router** (0 of 141 routes had one; the app was `<title>Web</title>` throughout), 348 unnamed controls, **152 orphan labels of which 121 were date fields whose control lives inside a component and so was invisible to any scan for `<input>`**, 25 unnamed icon-only controls, 1035 unhidden decorative icons, 653 `<th>` with no scope, and 161 badge pairings at 3.4–3.9:1 — plus a skip link, the `<main>`/`<search>` landmarks and the ARIA combobox pattern global search had the keyboard model for but never exposed. **Bootstrap's stock brand tones fail AA on this app's own `#f8f9fa` body** (they clear 4.5:1 against pure white and only just), so the four text utilities are re-pointed at its `-600` shades. Decision A records which criteria a machine can decide and which need a person, and why the line falls there

**Phase 34b.** Consistency and the shell (NFR-6.1): the four controls phase 33 deferred by name — **left nav, Create New flyout, company switcher, global date filter** — plus a **Reports index page** (52 report routes, one nav leaf, which is what the reference product does) and the list chrome NFR-6.1 enumerates. Search went from **1 of 46 list screens to 21 of 21 paginated ones** and from **2 of 47 `List*Query` types to 25** (9 exempt *with reasons*, enforced by `SearchSweepGuardTests`), with a date range on the 16 that have a business `Date`. **The re-layout cost zero page templates**: all 130 already open with the same `<div class="container py-5">`, so a fixed rail plus a left inset was the whole layout change — the plan's choice between re-laying out 130 pages and an overlay was a false dichotomy that one grep dissolved. Three defects came from the browser pass and none from tests, the sharpest being **a chrome that displayed a date range it had not applied** (the range loads from the per-user store after the page has fetched), which is a wrong answer that looks checked. **The confirm-live pass falsified the module scan for the fourth phase running** — the global date filter scopes *list queries*, not dashboard figures — while the Create New flyout matched it verbatim
