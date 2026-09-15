# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–41)

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
| 34c | Scale: `tools/scale/` (API master seeder + 190k-row INSERT seeder + 33-endpoint timing harness), p95 budgets per screen class, `TenantIndexConvention` (50 indexes from a rule), the shell `@defer`ed | `phase-34c-status.md` |
| 35a | Ledger drill-down (`?accountId=` + View Ledger row action) and the location picker/filter swept onto all 15 document forms and lists | `phase-35a-status.md` |
| 35b–38 | The location dimension in the reports; allocation/forex/ageing consistency; inventory policy (negative stock as a layer); import/export breadth | `phase-35b`–`phase-38-status.md` |
| 39 | The two editors: one sanitised rich-text control (re-emission in the Domain setters), the Organization logo in the printed header, `BalanceConfirmation`'s first consumer, standalone Deals/Tasks routes, Quick Links drag, the search results page | `phase-39-status.md` |
| 40 | The human WCAG pass (keyboard census, one focus ring, `app-status-banner`'s always-present live region) + 34b's list-chrome leftovers | `phase-40-status.md` |
| 41 | Subscription and plan model: the seeded `SubscriptionPlan` catalogue, commercial terms on `TenantSubscription`, quota enforcement on both axes (`SubscriptionQuotaBehavior`), the shell subscription banner | `phase-41-status.md` |
| 42 | Performance follow-through: `ToKeyPagedResultAsync` on 16 list handlers (keyset retired, not deferred), Detail General Ledger paged by row with the account boundary disclosed, the period-length id lists removed from five readers, the quota count's covering columns, a measured bundle budget | `phase-42-status.md` |
| 44 | Report semantics, read live first: the last four statutory reports folded to base currency, Reporting Tags corrected to OR-within/AND-across, Inventory Master's Warehouse Transfer + Opening Stock, Display Warehouse in Column, `sales-summary`'s Group Wise location, System Audit's stamped location, the billing-location backfill command; plus the Billing Location filter both statutory registers accepted and never applied | `phase-44-status.md` |
| 43 | Aggregate completions: `UpdateOrganizationCommand` (8 editable fields, 3 refused by name), Deal/WorkTask as record parents with detail pages, `TrialStartsAt`/`TrialEndsAt` → `OriginatedAt`/`TermEndsAt`, `DebitNote.WarehouseId` so a standalone Goods return consumes FIFO, the Sales Register folded to base currency, Quick Payment/Receipt's currency control; product-to-location enforcement retired on live evidence | `phase-43-status.md` |
| 46 | Metered add-on axes, where the roadmap premise that each axis is "a reader plus a ceiling" was wrong for two of three: the **AI-scan** ceiling (20/day on every tier and term length, sold by no add-on, per Nepal-local day, counted from the append-only `Audit` rows because a re-scan overwrites the document timestamp); **billing locations** as a purchased *record* that refuses nothing, on live evidence that the reference product caps them nowhere; **SMS** declared already-metered by phase 18's credit ledger; the transaction window corrected from the term to the allowance **year**; the entitlement mismatch surfaced and compared server-side; master-data creation gated past expiry | `phase-46-status.md` |
| 45 | Multi-UOM × variants settled by live read — a variant **owns** its unit matrix, and phase 24's identity decision meant no schema change; a variant parent refused one (the sweep-guard exemption retired); `UpdateSecondaryUnitCommand`/`DeleteSecondaryUnitCommand` plus the primary-unit and duplicate-unit refusals; the `ProductAttributePool` importer (a ninth upload type whose rows add to a set its command replaces); `ListSmsLogsQuery`'s search term; the landed-cost drawer's replace semantics and the dry-run-in-a-transaction question decided and pinned | `phase-45-status.md` |
| 47 | Accessibility completion: `Sort by` swept to all 16 document lists (the derived number, not the roadmap's 18) with a guard whose behavioural half drives every handler through both orderings; the four grids' `<select>`-inside-`<a>` removed rather than defended, with a whole-app nesting guard derived from which components render a control; per-field `aria-invalid`/`aria-describedby` plus focus movement on 13 document forms; the lookup/server search parity table pinned against real SQL Server; the drop list decided item by item; the AI-scan allowance shown where scanning happens. **The NVDA hour is still not done** — no screen reader is available in this environment, and it was not simulated | `phase-47-status.md` |

---

---

## Parity (26–34c) and consolidation (35–41) sequences — complete

Every entry of both sequences is done. The planning entries, the method write-ups and the "what
shipped" summaries moved verbatim to `docs/roadmap-history.md` on 2026-09-14; the outcomes are in
each `docs/phase-N-status.md`, and the 2026-09-02 and 2026-09-10 confirm-live appendices remain in
`docs/erp-module-scan.md`.

---

## Completion phases (42–47) — complete

**Method.** Every "Carried items / Known limitations / Follow-ups" section from `phase-34c` through
`phase-41` was read, each item was checked against the later phases that claimed it, and what is
still open was grouped by the code it touches. Items the reference product also lacks, or that need
an actor this codebase does not model, stay deferred with their re-entry condition. Two phases (44,
45) named the read they needed before coding, and both reads overturned recorded decisions.

The planning entries for 42–47 moved verbatim to `docs/roadmap-history.md` on 2026-09-15; the
outcomes are in each `docs/phase-N-status.md`, and the index table above carries the one-line
summaries. **Phase 47 was the last planned phase.** What follows replaces the forward plan.

---

## What remains (2026-09-15)

Nothing is planned. What is left falls into three kinds, and each says what would start it.

### One item that needs a person, not a decision

**An hour with NVDA**, on Windows, on the live regions, the rich-text toolbar's roving tabindex, the
three phase-46 subscription panels and phase 47's field-level errors. Phases 40 and 47 both built
what could be derived and both recorded plainly that no screen reader was run, because none is
available in this environment. This is the only item in this section, and it is the only thing
standing between the codebase and a finished WCAG 2.1 AA story. **Start condition:** a person with
headphones. Record what is heard verbatim before changing anything — phase 40's own rule, and the
reason its first focus sweep nearly reported an app-wide 2.4.7 failure that did not exist.

### One re-entry condition with a date

**Cadehi's trial ends 2026-09-22.** It is the first observable expiry this project has ever had a
date for, and one read settles what phases 31 and 46 both had to derive: what a tenant can and cannot
do once its term is over. Phase 46 narrowed the behaviour rather than guessing at it whole — a third
marker gates master-data creation past expiry while settings edits and the renewal command stay open
— and named this read as the thing that would confirm or correct it.

### Deferred beyond this roadmap (post-v1 — seams kept, no phases planned)

Explicit decisions (2026-08-18, revised 2026-09-02, 2026-09-10, 2026-09-14 and 2026-09-15), not
omissions:

- **Delivery Note / Goods Received Note (physical-movement inventory).** `TenantSettings.InventoryTrackingMode`
  is the seam. Cadehi's General page offers *Physical Movement*; with Accounting Movement selected no
  DO/GRN appears anywhere. **Re-entry:** the user flips that setting on the Cadehi trial tenant and
  the screens are read; then it is a phase of its own (FIFO consumption moves from Invoice/Bill
  Approve to DO/GRN Approve under a handler-level gate, plus a goods-received-not-billed default
  account).
- **A vendor-side actor** (41 Decision G): every subscription ceiling is self-liftable until an actor
  outside `OrganizationId` exists. A console, a support role, or an API key — a phase of its own.
  Phase 46 confirmed this live on all three metered axes, and phase 47's `LocationQuota` decision
  (Decision H #2) waits on the same thing.
- **Unrealised forex revaluation at period end** (28 Decision A): no revaluation document exists in
  the reference product; only the realised account does.
- **Per-user location assignment** (32b #4): both live tenants' Users screens have no location
  column; the role carries the scope.
- **Multi-level BOM explosion** (25): the live Planning report states "Multiple Level: No".
- **E-commerce / marketplace SKUs** (`marketplace_skus`, `sku_id` on the product JSON; the vendor's
  August 2026 release notes name "e-commerce sales"): a public storefront is a PRD non-goal.
- **POS Retail / POS Restaurant** front-ends (PRD non-goal): Phase 32 models the location *types*
  so a POS phase is additive later.
- **IRD e-filing integration** (Annex 5's Sync-with-IRD columns): aspirational until committed; the
  Annex reports omit rather than fake those columns (Phase 8f precedent).
- **Marketplace / third-party app ecosystem**: a permission flag in the research, nothing more.

### Dropped, with the reason (phase 47, Decision G)

Not deferred — decided against, so that no future session has to re-open them from a list. The full
reasoning and the re-entry condition for each is in `docs/phase-47-status.md`:
`Organization > Developer Mode` (API credential management — a platform feature, confirm-lived in
phase 25), `Organization > Documents` (a bare upload zone over phase 18's `Attachment`),
`Product.PrintProfileId` (live-confirmed to do nothing observable), the Marketplace flag, the Service
Charge column (a product flag this codebase does not model, printing `-` on every live row), supplier
credit-limit enforcement (the setting's own wording says Customer), `customtags` in the rich-text
toolbar, and rich-text tables/images (a *renderer* capability before a toolbar one).

### Carried engineering items, each named in a status doc

Small, specific and each with its own re-entry condition — read the phase's own doc rather than this
list: `Cheque`'s missing date index (47 #2, wants a `tools/scale/` measurement first), the Attributes
Used editor's flat checkbox list (45 #1 / 47 #3), two banner-only messages that name no single
control (47 #4), `role="toolbar"`'s roving tabindex with one consumer (40 #6 / 47 #5), and the
`ListSort`-to-`TenantIndexConvention` correspondence that no test asserts because the two live in
different assemblies (40 / 47 #6).

---
*Living doc — re-order/re-scope as real constraints surface. When picking up work: read its confirmed shape in `erp-module-scan.md` first; if the screen was never opened in the hands-on pass, confirm it against the live Tigg UAT tenant through the Browser pane (user logs in themselves) before writing code — the Phase 8f Annex 5 lesson: the speculative design and the real screen had nothing in common. Every phase ends with its own `phase-N-status.md`; CLAUDE.md's known-gotchas list is the pre-flight checklist for migrations, EF Core LINQ, and Angular selects.*
