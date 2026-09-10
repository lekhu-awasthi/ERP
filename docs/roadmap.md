# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–34b)

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
| 20d | Printing Templates / Custom Templates (FR-11.2/11.3): descoped by user decision to metadata-only lookups + `SetDefault`; the real deliverable is the QuestPDF print-to-PDF pipeline (2 shared layouts, 6 document types) | `phase-20d-status.md` |
| 20f | Tenant feature-flag enforcement (FR-2.6): `IRequireFeature` + `FeatureGateBehavior` (4th pipeline behavior); only `TrackInventory` and `MultipleWarehouses` (a cap at one, not a block) had a surface to gate | `phase-20f-status.md` |
| 20e | Alert Scheduler (FR-11.1), the first background job: `AlertSchedulerHostedService` driving `IAlertDispatcher`, `AlertDefinition` + `AlertSendLog` ledger whose unique index is the idempotency mechanism, Nepal wall clock via `NepalTime` | `phase-20e-status.md` |
| 21a | Async job foundation + bulk import (FR-2.9, NFR-4.3): `ImportJob`/`ImportJobRow` queue, template-based .xlsx import for Product/Customer/Supplier (create + update), the first job that writes (`IJobActingUser`) | `phase-21a-status.md` |
| 21b | Full-tenant data export (FR-2.8): `ExportJob` producing one multi-sheet .xlsx, shared `QueuedJobRunnerHostedService`, artifact-retention sweep; labelled an export, not a backup, because no restore path exists | `phase-21b-status.md` |
| 21c | Migrated tax-register import + migrated Sales/Purchase Register reports (FR-2.10, closing FR-9.4): two lifecycle-free aggregates seeded from .xlsx on `Configurations > Migration`, reusing the import job rather than adding a table | `phase-21c-status.md` |
| 22 | Document inbox (FR-10.3): `UploadedDocument`, `Workflow > Document` screen, conversion into four targets via a prefill gated by the target's own Create key, opt-in third-party extraction (withdrawable `TenantSettings` toggle + Admin-only key) | `phase-22-status.md` |
| 23 | Nepali localization & parity odds-and-ends (NFR-1.1/1.2, FR-5.8): dates stored AD, BS is presentation/entry only (`web/src/app/shared/formatting/`, range 2000–2092), `sweep-guard.spec.ts` enforces sweep completeness, dashboard (Decision F) | `phase-23-status.md` |
| 24 | Variant Products & Attributes (FR-8.3): live pass showed a variant is a Product with a parent pointer, so five nullable columns plus one rule instead of a stock-key change | `phase-24-status.md` |
| 25 | Manufacturing (FR-8.8/8.9, FR-9.5's slice), behind the Manufacturing flag: BOM → Production Order → costed Production Journal (Inventory-to-Inventory posting, perpetual; conservation law proven in SQL), Void unwinds both directions, three reports | `phase-25-status.md` |
| 26a | Report catalog completion, Accounting group (FR-9.1/9.6): Transaction list, Journal report, General Ledger Summary, Detail General Ledger, GL Master Report, plus FR-9.1's **Compare** column on Trial Balance / Balance Sheet / Income Statement. All read `GlJournalEntry`; nothing new stored, the only migration is ten permission-seed rows | `phase-26a-status.md` |
| 26b | Report catalog completion, Receivable/Payable and analytics (closing FR-9.2/9.3): Customer Receivable Summary, Supplier Payable Summary, Invoice Age, Purchase Bill Age, Sales/Purchase By Customer/Supplier and By Item, their four BS-fiscal-year Monthly crosstabs, Sales Summary Report — 13 reports over 7 shared handlers, plus the server-side `Domain/Common/BsCalendar` five of them are keyed by | `phase-26b-status.md` |
| 26c | Report catalog completion: inventory, tax, system, analytics (closing FR-9.4/9.5/9.7): Inventory Position / Movement / Ledger / Master, Sales & Purchase Return Registers, Net Trading Assets, Exceptional Report, User Log — 9 reports plus the `.xlsx` export the 3 manufacturing reports lacked. One new table (`UserLoginEvent`, written by the auth endpoints); the shared `StockFactReader` the four inventory reports agree through | `phase-26c-status.md` |
| 27a | Custom Fields, Custom Status, Reporting Tags and Tasks/Documents/Activity swept across every document type; `Comment` generalized to a polymorphic parent | `phase-27a-status.md` |
| 27b | Print/PDF for all 15 document types on one generic layout, BS dates in server-rendered PDFs/`.xlsx`, the last three pagers, wizard Turnstile, a feature-flag route guard, `CustomTemplate`'s first two consumers | `phase-27b-status.md` |
| 28 | Multi-currency (FR-2.5, NFR-1.3): a tenant `Currency` list seeded from a fixed catalog with NPR always present, `CurrencyCode` + `ExchangeRate` on 12 document types, the base-currency fold on each posting rule's **inputs** (so `GlLine` and every phase-8/19/26 report needed zero edits), two forex accounts and a realised-difference rule on Payment allocation. The entitlement is a **cap on the currency list**, not a gate on documents | `phase-28-status.md` |
| 29 | Landed cost: an Additional Cost section on the Purchase Bill, allocated by Value or Quantity at Approve and capitalised into the received FIFO layers, against a Landed Cost Clearing account | `phase-29-status.md` |
| 30 | Communications: Send Email on 6 document types + the Contact page (`EmailTemplate` aggregate, Email Logs, Email Templates config), `AlertMedium.Sms` | `phase-30-status.md` |
| 31 | Credit control (`Contact.CreditLimit`/`CreditTermId`, a Credit Limit Exceeds policy), the Configurations > General screen, Negative Cash Balance / Suggest Selling Price / Product Price Basis enforced, a stored `DueDate`, cheque bounce voids its payment, subscription expiry | `phase-31-status.md` |
| 32 | Billing Locations: `BillingLocation` with HeadOffice seeded, `MultipleLocations` as a cap at one, nullable `LocationId` on 17 types, the Advanced location-scope panel, location-wise numbering, location filter on the Invoice list and Sales Master Report | `phase-32-status.md` |
| 32b | Per-location permission scope: the role editor's second matrix, nullable `RolePermission.LocationId` (null = organization-wide), enforcement inside `AuthorizationBehavior` behind marker interfaces and a sweep guard | `phase-32b-status.md` |
| 33 | Platform chrome: global search (Ctrl + /), a History popover, the Quick Links tray, `UserPreference` per-user store | `phase-33-status.md` |
| 34a | WCAG 2.1 AA sweep over all 161 templates (titles, control names, `th scope`, icon names, contrast), pinned by `a11y-sweep-guard.spec.ts` | `phase-34a-status.md` |
| 34b | The shell (left nav, Create New flyout, company switcher, global date filter) on `NavigationCatalog` with zero page-template edits, a Reports index page, search on 25 `List*Query` types and a date range on 16 | `phase-34b-status.md` |

---

---

## Parity sequence (26–34) — complete except 34c

Phases 26–34b closed the 2026-09-02 gap analysis against the reference product; the per-phase
planning entries and the method write-up moved verbatim to `docs/roadmap-history.md` on 2026-09-10
(their outcomes are in each `docs/phase-N-status.md`). One entry is still open:

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

---

## Consolidation phases (35–41) — from the carried-item backlog of phases 25–34b (planned 2026-09-10)

**Method.** Every "Carried items / Known limitations / Follow-ups" section from `phase-25` through
`phase-34b` was read and each item placed where it is cheapest to close next to its neighbours;
items the reference product also lacks stay deferred rather than being built for their own sake.
The one new external input is the second reference tenant, `cadehi.tigg.app`, **read on 2026-09-10**
(findings in `erp-module-scan.md` under "Second reference tenant: Cadehi Enterprises"). It has
Billing Location enabled, which settled three phase-32 questions, showed the inventory-tracking-mode
setting Moonbeam lacks, and showed no native batch/lot/serial tracking despite the login banner.

**Ordering rule.** Bugs and consistency fixes first (35–36), Domain-invariant changes next (37),
then breadth (38–39), then the two passes that need a person or a product decision (40–41).

### 35. Ledger drill-down and the location dimension everywhere
- **An Account or Contact hit in global search has nowhere to go** (phase 33 #1): give Detail
  General Ledger an account route parameter and Contact Statement a contact one, link both from
  search results and from a **View Ledger** row action on Chart of Accounts (the reference product's).
- **Location reaches every report and picker** (phase 32 #2/#4/#6, 32b #1): the location
  filter/column on the remaining registers and Master reports (which also makes 32b's row-scoping
  real for them), the header picker on Sales Order, Credit Note and the eleven All-Transactions
  types, and `BillingLocation.WarehouseId` defaulting a document's warehouse. **Quotation's
  membership of the sales-only scope** (32 #3) is settled: Cadehi's own label names Invoice, Sales
  Order, POS and Credit Note, and Quotation shows the picker only under All Transactions — the
  exclusion stands. New from the same read: **a Product carries a Location selector** (multi-select,
  default All), so product-to-location scoping joins this phase.
- Report filters seen live and not built: Reporting Tags on the Journal report (26a), Reporting
  Tags and group-by-warehouse on Inventory Position (26c), **Group By Bill** and the Include Credit
  Note toggle on the Sales Register (31 #7, 26c). Each is a query parameter over an existing reader.

### 36. Allocation, forex and ageing consistency
- **Bug first:** `featureGuard('MultipleWarehouses')` on `organizations/:id/warehouses` stops a
  flag-off tenant creating its *first* warehouse, which Invoice and Purchase Bill require —
  contradicting phase-20f Decision #4; the server-side cap is right, the client guard is stricter
  than intended (phase 28).
- `ApplyPaymentAllocationCommand` posts the **forex leg** that Approve-time allocation already
  posts, and the Allocate screens' lists filter by currency (phase 28); cross-currency settlement
  stays rejected (28 Decision F) unless the cadehi read shows the reference product allowing it.
- **Two ageing reports must agree:** align phase-9's `ContactAgeingSummaryQueryHandler` with 26b's
  `DocumentAgeQueryHandler` (JV-sourced allocations, buckets from the stored `DueDate`) (26b #4);
  decide whether Quick Payment/Receipt are ageable (26b #2, a phase-17 Decision #7 consequence);
  the credit-limit comparison converts currency (31 #3); Credit Terms → `DueDate` becomes a
  server-side default so API-created documents get it too (31 #4).

### 37. Inventory policy — negative stock, returns at cost, the clearing unwind
- **Negative Item Balance for real** (31 #1, 26c): Warn and Do Nothing need a negative FIFO layer
  and its later fill, which is a Domain-invariant change — `StockLedgerService.ConsumeAsync` throws
  today and phase-26c pinned that. Design the negative layer's cost catch-up before touching a line;
  the phase-25 conservation law is the acceptance test, and `StockFactReader`'s zero-value guard is
  waiting for it.
- **Debit Note relieves inventory at consumed FIFO cost, not return price** — the phase-6/7
  modelling choice phase 29 only stopped widening; and an unwind path for the Landed Cost Clearing
  account that matches the carrier's bill to the capitalised amount (29).
- Inventory Master gains WarehouseTransfer and OpeningStock rows after a live re-check of the
  reference report's Txn Type filter (26c Decision D); **multi-UOM × variants** (24) — a secondary
  unit's rates on a variant — designed once, with the sweep-guard allow-list reason retired.
- Traceability: the Cadehi product form has no batch, lot, serial or expiry field, so it stays a
  Custom Fields matter here as on Moonbeam; no 37b.

### 38. Import and export breadth
- Importers for Account, Product Category and Account Group (intra-file parent ordering and cycle
  detection), Contact Personnel, and variants (21a, 24); the product-wise landed-cost grid paste (29);
  a pre-commit dry-run review step (21a, a trade against NFR-4.3 stated as such).
- Export by date range and the categories beyond FR-2.8's five (21b); the OpenXml SAX streaming
  writer only if 34c's measurement or a real tenant hits the 25,000-row cap.

### 39. CRM and workflow as first-class screens, and the two editors
- **Deals and Tasks get standalone routes** under CRM and Workflow (34b #6 — the router-derived nav
  made the gap visible); Quick Links reorder by drag (33 #2); a search results page with a kind
  filter, once 34c says the cap of 5 bites (33 #4).
- **One rich-text editor**, sanitised, behind both `app-terms-editor` and the email body (27b
  Decision C, 30) — the reference product's TinyMCE; terms carried through document conversions
  (27b); the `BalanceConfirmation` email context gets its consumer, the phase-27b letter (30).
- **Organization logo** upload (the phase-1b wizard gap) and its use in the printed header (27b).

### 40. The human accessibility pass and the list-chrome leftovers
- Decision A's six non-mechanisable WCAG criteria — focus order and visibility, error-message
  quality, label-in-name, status messages, reflow — with a keyboard and a screen reader over the 140
  pre-existing templates (34a #1, 34b #1); an `aria-live` policy for async results (34a #5);
  `radiogroup` for the two radio sets (34a #2); the two `NG8113` warnings (34a #3).
- `sortOptions`' first consumer, on whichever list's default order is complained about first
  (34b #2); client-side filtering on the six unpaginated Configurations lists (34b #3); a decision
  on whether `transaction-list-page` and `alert-list-page` are lists (34b #4); `@defer` the shell
  if 34c has not (34b #5).

### 41. Subscription and plan model — needs a product decision before it is a phase
- Tigg Subscriptions' three dead fields (Amount, the quotas, IRD Verified) (33 Decision D), a plan
  catalogue behind Renew instead of free text (31 #9), the expiry gate over configuration writes
  (31 #6), and a confirmed expired-tenant behaviour (31 #5, derived, never observed).
- **Decision to make first:** is this product selling plans at all? If not, retire all four with a
  reason and keep `TenantSubscription` as the entitlement record it already is — phase-8f's "omit
  rather than fake".

**Recommended drop list (decided, not silently omitted):** `Organization > Developer Mode` and
`> Documents` (phase-25), `Product.PrintProfileId` (20d), the Marketplace flag, the Service Charge
column (no product flag to drive it; revisit only with POS), and **supplier credit-limit
enforcement** (31 #2 — stored, unenforced, matching the live form).

---

## Deferred beyond this roadmap (post-v1 — seams kept, no phases planned)
Explicit decisions (2026-08-18, revised 2026-09-02 and 2026-09-10), not omissions:
- **Delivery Note / Goods Received Note (physical-movement inventory).** The scan recorded a
  "Mode of Inventory Tracking" setting; the live General page no longer has it, while Document
  Numbering still carries DO and GRN rules at next-number 1. `TenantSettings.InventoryTrackingMode`
  stays as the seam. **Re-entry condition, now reachable:** Cadehi's General page offers *Physical
  Movement* ("based on Delivery Notes and Goods Received Notes"); with Accounting Movement selected
  no DO/GRN appears anywhere. Switching the mode is a config write on that fresh trial tenant — the
  user's call; once flipped and read, this becomes a phase of its own (FIFO consumption moves from Invoice/Bill Approve to DO/GRN Approve under a handler-level
  gate, plus a goods-received-not-billed default account).
- **Unrealised forex revaluation at period end** (28 Decision A): no revaluation document exists in
  the reference product; only the realised account does.
- **Per-user location assignment** (32b #4): both live tenants' Users screens have no location
  column; the role carries the scope. A second mechanism if a later reading finds one, not an extension.
- **Multi-level BOM explosion** (25): the live Planning report states "Multiple Level: No".
- **POS Retail / POS Restaurant** front-ends (PRD non-goal): Phase 32 models the location *types*
  so a POS phase is additive later.
- **IRD e-filing integration** (Annex 5's Sync-with-IRD columns): aspirational until committed; the
  Annex reports omit rather than fake those columns (Phase 8f precedent).
- **Marketplace / third-party app ecosystem**: a permission flag in the research, nothing more.

---

*Living doc — re-order/re-scope as real constraints surface. When picking up a phase: read its confirmed shape in `erp-module-scan.md` first; if the screen was never opened in the hands-on pass, confirm it against the live Tigg UAT tenant through the Browser pane (user logs in themselves) before writing code — the Phase 8f Annex 5 lesson: the speculative design and the real screen had nothing in common. Every phase ends with its own `phase-N-status.md`; CLAUDE.md's known-gotchas list is the pre-flight checklist for migrations, EF Core LINQ, and Angular selects.*
