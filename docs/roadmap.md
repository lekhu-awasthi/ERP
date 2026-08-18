# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–14)

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

---

## Phase 15 — CRM: Deals **(IN PROGRESS — branch `feature/phase-15-deals`)**
**Goal:** the sales-pipeline feature (FR-4.7): each Deal linked to a Contact, with a configurable Stage, expected revenue, expected closing date, one or more assignees, and a Pending/Won/Lost lifecycle. Confirmed shape: `erp-module-scan.md` CRM §1 (Deals) and Configurations §4 (CRM config: Deal Stages / Lead Sources).

1. `DealStage` + `LeadSource` Configuration lookups reusing the generic lookup pair (started on branch).
2. `Deal` aggregate (`Domain.Crm`, new `crm` schema): ContactId, Title, ExpectedRevenue, ExpectedClosingDate, StageId, LeadSourceId, `DealStatus` (Pending/Won/Lost), `DealAssignee` child collection (started on branch). No Draft/Approve lifecycle — like `WorkTask`, status transitions replace it; no document number.
3. Commands/queries: Create/Update/Delete, a status-transition command (decide explicitly whether Won/Lost are terminal and whether reopening is allowed — record the call in the status doc), `ListDealsQuery` filterable by stage/status/assignee.
4. Permission keys `Crm.Deal.View`/`Crm.Deal.Manage`, Member-granted (routine daily-use working data — Phase 13's Task precedent), stage/source lookups on the ordinary Member-View/Admin-write Configuration split.
5. Angular: Deals list grouped by stage (or status tabs — confirm against the live screen), deal create/edit form (`[selected]`-per-option on every signal-fed select), a Deals tab on the Contact detail page.

*Exit criteria: an Admin creates Deal Stages + Lead Sources, creates a Deal on a Contact with two assignees, edits it, moves it through stages, marks it Won; an illegal transition 409s; a Member with `Crm.Deal.*` denied gets a real 403 naming the key; deal visible from the Contact's Deals tab. All builds/tests green; `phase-15-status.md` written.*

---

## Phase 16 — Platform hardening (a–d)
**Goal:** close the QA-critical gaps every existing screen shares, *before* adding more surface area. Each sub-phase is independently shippable.

### 16a. Void lifecycle + lock-date enforcement
The two integrity guarantees the PRD promises that don't exist yet: no command can produce a `Void` status (flagged in `phase-8f-status.md`), and `Organization.LockDate` (schema'd in Phase 1b) is enforced nowhere.
1. `VoidXCommand` per ApprovableTransaction type (or a shared pattern — decide against the Phase 12 "13 concrete blocks, not one generic" precedent): Approved → Void, reversing the GL posting **and** any FIFO stock effect. Trace the *net* effect on every account/stock layer the original touched (the Phase 6 bug-#3 lesson) — a voided Invoice must restore consumed FIFO layers at original cost, a voided PurchaseBill must be blocked (409) if its layers are already partly consumed.
2. Guards: block voiding a document that has non-Void allocations, conversions, or reversals against it; existing `Status != Void` filters in reports/conversion-caps become live behavior — re-run those test suites.
3. `Tenancy.*.Void` permission keys per type (the maker-checker matrix already anticipated in FR-3.2), Admin-granted, Member-denied by default.
4. Lock date: enforcement in one shared place (a pipeline behavior or shared validator, not per-handler copy-paste) rejecting any create/edit/approve/void whose business `Date` ≤ `LockDate` (NFR-3.4); Organization settings UI to view/set the lock date, Admin-only.

*Exit criteria: void an approved Invoice → GL nets to zero, stock restored, it disappears from VAT Summary/Master Reports, a Payment allocation against it is rejected; voiding a partly-consumed PurchaseBill 409s; any backdated write at-or-before the lock date 400/409s with a clear message, proven on at least 3 document types; Member void attempt 403s.*

### 16b. Discounts retrofit
FR-5.1 requires per-line discount; no discount field exists anywhere (`phase-8b-status.md` scope decision #3). Confirm the live Tigg discount model first (item-level %, transaction-level, and which GL account discount posts to) before writing code.
1. Line-level discount on Quotation/SalesOrder/Invoice/CreditNote/PurchaseOrder/PurchaseBill/DebitNote lines; transaction-level discount on the document; totals math (discount before VAT — confirm live).
2. GL posting rules updated (discount account via TenantSettings default, same fallback pattern as Phase 5); verify the reversal documents' *net* effect again (Phase 6 bug #3).
3. Conversion-cap enforcement: decide whether the `(ProductId, Rate, VatRate)` match triple must grow a discount component — record the call.
4. Master Reports gain the `ItemDiscount`/`TransactionDiscount`/`NetSales` columns 8b deliberately omitted; VAT Summary/Annex math re-verified by hand.

*Exit criteria: an Invoice with line + transaction discounts approves with balanced, hand-verified GL; its CreditNote reversal nets every touched account to zero; Master Report columns match hand arithmetic; all pre-existing report tests still green.*

### 16c. Pagination + report export
NFR-5.1 (every list unpaginated today) and FR-9.8 (no export anywhere).
1. Shared server-side pagination contract (page/pageSize/total) + one shared Angular pagination component; retrofit the highest-row-count screens first (document lists, Master Reports, Statements), then sweep the rest.
2. Spreadsheet export per report: "current view" and "full dataset" variants (FR-9.8), download endpoint per the established permission key of each report. Print-formatted output deferred to Phase 20's Printing Templates.

*Exit criteria: a seeded 100+-row list pages correctly through the real UI; exported spreadsheet of a filtered Master Report matches the on-screen rows exactly; export endpoint honors the same 403 as its report.*

### 16d. System Audit report
FR-9.6/NFR-3.3: an append-only audit trail + report.
1. Audit entity written from a pipeline behavior (one place, all commands), capturing user, action, document type/id, timestamp — not editable/deletable through the app.
2. Audit report screen filterable by user/action/document type, each row linking to the affected record. `Reports.SystemAudit.View` Admin-only (flat per-user activity register — the Phase 8b discriminator applies).

*Exit criteria: create/edit/approve/void actions each produce exactly one audit row with the right actor; the report filters correctly; a Member gets 403; no code path can update or delete an audit row.*

---

## Phase 17 — Accounting breadth
**Goal:** finish the Accounting module to reference parity. Confirmed screens: `erp-module-scan.md` Accounting §3–§7, Sales §7, Purchase §7, Configurations §3/§18.

1. **Quick Payment / Quick Receipt** (FR-7.4): simplified one-off cash movement documents, no Contact/allocation required — likely thin variants over the existing posting path (the CashTransfer precedent).
2. **Bank Accounts** (FR-7.5): bank/cash/wallet account records (Banks config lookup §3) with live running balances, dashboard summary card.
3. **Cheque Register** (FR-7.6): cheques received/issued linked to their Payment, pending/cleared/bounced lifecycle; decide whether a bounce reverses GL (confirm live).
4. **Allocate Customer/Supplier Payment screens** (FR-5.12/FR-6.12): list unallocated/partially-allocated credits (Payments, JVs, Quick Receipts) and apply them to open documents after the fact — reuses Phase 11's netting logic.
5. **Opening Balances screen** (Configurations §18): view/edit per-account and per-contact opening balances, with the edit permission split FR-3.4 describes.

*Exit criteria: each of the five surfaces works end-to-end through the real UI with hand-verified GL where applicable; an allocation made from the Allocate screen shows up identically in the Contact Statement; negative permission checks per new key.*

---

## Phase 18 — CRM completion
**Goal:** the rest of the Contact/CRM story (FR-4.5, FR-4.6, FR-4.8). This phase introduces the codebase's first file-storage infrastructure — design it once, here, since Phase 22's Document inbox reuses it.

1. **Contact Personnel** (sub-contacts child collection) + **comments/activity log** on the Contact detail page.
2. **File attachments**: storage abstraction (`IFileStorage`, local-disk dev implementation, cloud-swappable), attachment entity polymorphic like `WorkTask`'s parent, upload/download endpoints, Contact detail Attachments tab.
3. **Quick actions from Contact** (FR-4.6): Record Payment / Create Invoice / Create Quotation / Create Sales Order buttons pre-filling the target form with that Contact — routing + prefill only, no new commands.
4. **SMS** (FR-4.8): `ISmsSender` abstraction (log-to-console dev implementation, real gateway later), merge-field templates, send-to-audience (all/ContactGroup/custom selection), history + credit-usage ledger. Confirm the live screen's shape first (scan CRM §4 was a category listing, not a hands-on pass).

*Exit criteria: personnel/comments/attachments round-trip on a real Contact; a quick-action lands on the target form correctly pre-filled; an SMS send to a ContactGroup writes one history row per recipient and decrements credits; Member/Admin permission splits proven.*

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
**Goal:** make the Phase 2 extensibility foundations actually reach the UI, plus the notification/template surface (FR-11.x, FR-12.x).

1. **Custom fields rendered on forms** (FR-12.1): the deferred half of Phase 2's EAV — a shared Angular component rendering a document type's `CustomFieldDefinition`s inline, values saved via `CustomFieldValue`.
2. **Custom Status wiring** (FR-12.2): per-document-type custom status/stage pipelines (the lookup exists; the assignment on documents doesn't).
3. **Cost Terms** lookup (Configurations §7) — prerequisite reference data for Phase 25's Manufacturing.
4. **Printing Templates / Custom Templates** (FR-11.2/11.3): print/PDF layout per document type with a tenant default; merge-field text templates. This closes 16c's deferred print-formatted output.
5. **Alert Scheduler** (FR-11.1): first background-job infrastructure (scheduled recurring emails) — design the job runner once; Phase 21's async import/export reuses it (NFR-4.3).
6. **Tenant feature-flag enforcement** (FR-2.6): the wizard's Accounting Features checkboxes (recorded since Phase 1b) actually gate document types and UI surfaces at point of use.
7. **Turnstile bot-check** on registration — the Phase 1 hardening deferral.

*Exit criteria: a custom field defined for Invoice appears on the Invoice form and round-trips; a printed Invoice renders through the selected template; a scheduled alert email actually arrives on schedule; a tenant without Track Inventory no longer sees warehouse-dependent surfaces.*

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
