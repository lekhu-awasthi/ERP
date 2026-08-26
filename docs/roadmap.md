# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–20a)

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
| 20a | Custom Fields reach the forms: `SetCustomFieldValuesCommand`/`GetCustomFieldValuesQuery` + `CustomFieldDefinition.ChoiceOptions`, shared `app-custom-fields-editor` wired to Quotation/Invoice | `phase-20a-status.md` |

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
3. **Sales Register / Purchase Register** (FR-9.4's non-migrated variants — migrated variants land with Phase 21's import).
4. **Stock Ageing** + **Product Profitability** (FR-9.5) over the FIFO layers/`StockMovement` history.
5. **Ratio Analysis** (FR-9.7) computed from the same statement data as 8a.
6. Every new report ships with the established per-report permission-key derivation (rollup vs flat register, PAN exposure) written down, not defaulted.

*Exit criteria: every remaining catalog report renders with hand-verified numbers against seeded data; tag filtering narrows a GL report correctly; each report's permission decision recorded and its 403 proven.*

---

## Phase 20 — Configuration & extensibility completion
**Goal:** make the Phase 2 extensibility foundations actually reach the UI, plus the notification/template surface (FR-11.x, FR-12.x). Split into seven independently shippable sub-phases; one sub-phase = one session.

**Locked execution order: 20a ✅ → 20c (in progress) → 20b → 20g → 20d → 20f → 20e.** Reasoning (not the
list order — deliberately resequenced):
- **20b after 20c** because it is the same shape and size as the just-finished 20a (extend a Phase 2 lookup onto real documents, confirm-live step, shared editor component). Running that pattern again while the muscle memory is fresh is lower-risk than pivoting to something structurally new.
- **20g early** because it is small and isolable — a good pairing candidate with leftover budget or a short session of its own, but never the main event.
- **20d and 20e are both greenfield-and-risky**, and 20e in particular is an architecture decision (background-job infra, a new authentication-bypass surface) that deserves a session where it is the *only* thing being decided — so it goes last, treated as an architecture review rather than "whatever's next."
- **20f is a sweep across already-built surfaces**, easiest to scope correctly once more document types exist and have settled shapes, so it waits until fewer sub-phases are still landing and re-doing gating work is less likely.

### 20a. Custom fields rendered on forms (FR-12.1) — **COMPLETE**, see `phase-20a-status.md`
The deferred write-side half of Phase 2's EAV: `SetCustomFieldValuesCommand`/`GetCustomFieldValuesQuery`
(riding on the target document's own Edit/View permission), a `ChoiceOptions` field
`CustomFieldDefinition` never had, and a shared `app-custom-fields-editor` rendering a document type's
applicable fields inline in its create/edit form. Wired to Quotation and Invoice only; the other 15
applicable document types and a `CustomFieldDefinition` admin screen are mechanical follow-up.

### 20b. Custom Status wiring (FR-12.2) — **NEXT** (after 20c merges)
Per-document-type custom status/stage pipelines. The same shape of gap 20a just closed for Custom Fields,
and the *third* instance of "cross-cutting data attached to a document" after Phase 19's
`ReportingTagsEditor` and 20a's `app-custom-fields-editor` — read both predecessors before designing this
one rather than assuming it repeats either.

Verified current state (grepped this session, not assumed):
- The lookup entity is **`CustomStatus`** (`src/Domain/Configuration/CustomStatus.cs`), *not*
  `CustomStatusDefinition` — an `ITenantLookupEntity` with `Name` + a `DocumentType` discriminator, with
  full Create/Update/List/Delete through the generic lookup CRUD since Phase 2.
- **No document aggregate references it.** `grep -rin customstatus src/Domain` hits only the entity's own
  file; no `CustomStatusId` exists anywhere. The assignment-onto-a-document half is 100% unbuilt.
- **There is no Angular screen for `CustomStatus`** — `configuration-shell.ts`'s own comment records that
  only ReportingTags got one (Phase 19); `CustomStatus` and `CustomFieldDefinition` are API-only. Defining
  a status is `curl`-only today. Decide up front whether 20b also builds that admin screen or keeps
  definition-seeding on `curl` (as 20a did for `CustomFieldDefinition`) — don't discover it mid-session.

**Confirm live before coding** (the scan lists Sales Order, Purchase Order, Quotation, Cheque, Production
Order as status-pipeline-*definable* — a candidate list, not a confirmed one):
- Which document types actually show a status-picker control on the real document form. "The lookup can be defined for this type" and "the form has the control" are two different confirmations — the same distinction Phase 19 drew between `ReportingTagCategory` (Phase 2) and `TransactionReportingTag` (Phase 19).
- Whether changing a custom status has any side effect (GL, stock, notification) or is purely informational — if purely informational this is a much smaller sub-phase.
- Whether a Kanban/board view grouped by status exists at all. Don't build one speculatively.
- Whether the picker is a plain `<select>` (native-`<select>` `[selected]`-per-option gotcha applies immediately) or something richer.

**Backend:** nullable `CustomStatusId` on whichever aggregates are confirmed to carry it; a
`SetCustomStatusCommand`; validation that the assigned `CustomStatus.DocumentType` matches the
target document's own type (**400**, not a silent accept); a `GetCustomStatusOptionsQuery` (or fold into the
document type's existing lookup-loading call) returning only options valid for that type; permission shape
derived the way 20a did it (rides on the document's own Edit permission, or its own key — reasoning recorded
either way).

**Frontend:** the picker on the confirmed document types' detail pages; a Kanban board grouped by status
*only* if live-confirmed to exist.

*Exit criteria: a status persists and reads back through the real Angular form; a status defined for the
wrong document type is rejected with 400; permission-key derivation recorded with reasoning; manual E2E
(seed a `CustomStatus` via curl, set via curl, confirm via `sqlcmd`, then confirm in the real
form) plus a 403 naming the exact key; if a board view is in scope, dragging between columns persists.*

### 20c. Cost Terms lookup — *in progress* (`feature/phase-20c-cost-terms`)
Configurations §7 — prerequisite reference data for Phase 25's Manufacturing.

### 20d. Printing Templates / Custom Templates (FR-11.2/11.3)
Print/PDF layout per document type with a tenant default; merge-field text templates. Closes 16c's deferred
print-formatted output. Greenfield — treat the template model as its own design decision.

### 20e. Alert Scheduler (FR-11.1)
First background-job infrastructure (scheduled recurring emails) — design the job runner once; Phase 21's
async import/export reuses it (NFR-4.3). **Highest-risk sub-phase**: a job-runner architecture choice plus a
new authentication-bypass surface. Scheduled last, for a session that treats it as an architecture review.

### 20f. Tenant feature-flag enforcement (FR-2.6)
The wizard's Accounting Features checkboxes (recorded since Phase 1b) actually gate document types and UI
surfaces at point of use. A sweep across already-built surfaces — scoped after the other build-out
sub-phases land, so gating work isn't re-done.

### 20g. Turnstile bot-check on registration
The Phase 1 hardening deferral. Small and isolable.

*Phase exit criteria: a custom field defined for Invoice appears on the Invoice form and round-trips; a printed Invoice renders through the selected template; a scheduled alert email actually arrives on schedule; a tenant without Track Inventory no longer sees warehouse-dependent surfaces.*

---

## Phase 21 — Import/Export & backup
**Goal:** FR-2.8/2.9/2.10 — the data-migration story, on Phase 20's async-job infrastructure.

1. Bulk import (Products, Customers, Suppliers, Contacts, Accounts, Product Categories, Account Groups) from downloadable spreadsheet templates, create-new and update-existing modes, row-level error reporting.
2. Full-tenant backup/export download (FR-2.8).
3. Historical Sales/Purchase tax-register import (FR-2.10) + the **migrated** Sales/Purchase Register report variants, closing FR-9.4 completely.
4. All long-running operations async with completion notification (NFR-4.3).

*Exit criteria: a template-based Product import creates and then updates rows correctly with per-row errors surfaced; a migrated-register import shows up only in the migrated report variants, never in live GL; a backup export downloads and contains the seeded data.*

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
