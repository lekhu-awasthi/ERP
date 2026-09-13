# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–39)

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
| 34c | Scale (NFR-5.1/5.2): the 50,000-invoice dataset seeded by direct `INSERT` (`tools/scale/`), a p95 budget per class of screen, and **50 indexes from a rule** — `TenantIndexConvention` found **18 tenant-scoped tables with no index leading on `OrganizationId`**, exactly the transactional documents plus `GlJournalEntry`, because every other table got one free from its per-tenant uniqueness rule. List first pages **469 ms → 49 ms**; the shell `@defer`ed for **724 kB → 649 kB**. Three findings outlive the speed-up: an index added for one access path made *search* on the same table worse; a plausible multi-tenancy inference (`GlLine` has no tenant column, so statements must pay for other tenants) was **refused by a second-tenant experiment**; and the report layer's cost is the **period**, not the page size — with `JournalReportQueryHandler` already in the repo as the shape that fixes it. The export cap's re-entry condition is **met by specification** and the constant deliberately unchanged | `phase-34c-status.md` |
| 35a | Ledger drill-down (`?accountId=` + a View Ledger row action closing phase-33 #1) and the location dimension on documents: the picker extracted and swept onto all 15 forms, the LOCATION cell and filter onto all 15 lists via `ListChrome`/`ListFilter`, `BillingLocation.WarehouseId` as a prefill. Found phase 32's read-path gap — **14 of 15 detail DTOs and all 5 conversion templates dropped `LocationId`** — and corrected a `known-gotchas.md` generalisation by experiment | `phase-35a-status.md` |
| 35b–38 | The location dimension in the reports; allocation/forex/ageing consistency; inventory policy (negative stock as a layer); import/export breadth | `phase-35b`–`phase-38-status.md` |
| 39 | CRM and workflow as first-class screens, and the two editors: one sanitised rich-text editor (`RichText` — re-emission, not filtering, enforced in the Domain setters) behind both `app-terms-editor` and the email body; Organization logo, with `ImageHeader` reading the format from the bytes, in the printed header; `EmailTemplateContext.BalanceConfirmation`'s first consumer; standalone `CRM > Deals` and `Workflow > Tasks` routes; Quick Links drag-to-reorder; the search results page with a kind filter | `phase-39-status.md` |

---

---

## Parity sequence (26–34) — complete except 34c

Phases 26–34b closed the 2026-09-02 gap analysis against the reference product; the per-phase
planning entries and the method write-up moved verbatim to `docs/roadmap-history.md` on 2026-09-10
(their outcomes are in each `docs/phase-N-status.md`). One entry is still open:

#### 34c. Scale (NFR-5.1/5.2) — **DONE** (see `docs/phase-34c-status.md`)

**What shipped.** The dataset and measurement Decision C fixed, executed and committed
(`tools/scale/`: an API-driven master seeder, a 190,000-row `INSERT` seeder that derives its columns
from the reference rows the handlers wrote, a statistics refresh, a 33-endpoint timing harness and a
summariser). Decision A set a p95 budget per class of screen — **500 ms** for a list page or the
search box, **2 s** for a statement or a register — because NFR-5.1/5.2 name no number and a
measurement without a threshold is a table nobody can act on.

**The finding that made the fix a rule.** `TenantIndexConvention` found **18 tenant-scoped tables
with no index leading on `OrganizationId`** — exactly the transactional documents plus
`GlJournalEntry`, because every other tenant table already had one *free* from its per-tenant
uniqueness rule and a document number is not unique-indexed. 50 indexes in three families, derived
from what the handlers do, with the convention throwing at model build for any tenant entity it
cannot classify. List first pages **469 ms → 49–97 ms**; write cost measured at **+32.8 %** on a
pure-`INSERT` workload.

**Three results outlive the speed-up.** An index added for the list path made *search* on the same
table **worse** (651 → 1,173 ms — one scan replaced by a seek plus a key lookup per row). A plausible
multi-tenancy inference — `GlLine` has no tenant column, so every statement pays for every other
tenant's ledger — was **refused by a second-tenant experiment**, and survives only as a precise
statement with a re-entry condition. And the report layer's cost is the **period**, not `pageSize`:
0.65 s for a month against 2.1–3.5 s for three years, with `JournalReportQueryHandler` already in the
repo as the shape that fixes it.

**Answers owed to phases 38–40**, which name 34c by name:
- **38** — the export cap's condition is **met**: NFR-5.1's own top-of-range tenant truncates
  `Ledger Transactions (25,000 of 210,006)`. But the SAX writer is not the first move; the complete
  export was measured at 280,024 rows / 12.1 MB / 15.8 s / ~700 MB, i.e. **~2.5 kB of working set per
  row**, so *raising the cap to a measured level* comes first. The constant was deliberately left at
  25,000 — a server-safety limit should not move on one machine's run.
- **39** — global search's cap of 5 **does** bite, and the 18-query fan-out costs 595–1,106 ms p95,
  over budget in every pass before any UI is added.
- **40** — **`@defer` the shell is done here** (724.2 kB → 649.5 kB raw, 141.0 → 127.3 kB transfer,
  pinned by two `DeferBlockBehavior.Playthrough` tests). Strike it from 40.

---

## Consolidation phases (35–41) — from the carried-item backlog of phases 25–34b (planned 2026-09-10)

**Method.** Every "Carried items / Known limitations / Follow-ups" section from `phase-25` through
`phase-34b` was read and each item placed where it is cheapest to close next to its neighbours;
items the reference product also lacks stay deferred rather than being built for their own sake.
The one new external input is the second reference tenant, `cadehi.tigg.app`, **read on 2026-09-10**
(findings in `erp-module-scan.md` under "Second reference tenant: Cadehi Enterprises"). It has
Billing Location enabled, which settled three phase-32 questions, showed the inventory-tracking-mode
setting Moonbeam lacks, and showed no native batch/lot/serial tracking despite the login banner.

**Ordering rule.** Bugs and consistency fixes first (35a/35b–36), Domain-invariant changes next (37),
then breadth (38–39), then the two passes that need a person or a product decision (40–41).

### 35a. Ledger drill-down and the location dimension on documents — **DONE** (see `docs/phase-35a-status.md`)

Phase 35 was split with the user before any code was written, on the 34a/b/c precedent, because the
survey found far more than the entry assumed: **15 document forms and 15 lists** had no location
picker at all (only Invoice did), the Sales Master Report's filter exists **server-side only**, and
43 of 49 live reports carry the filter — **including every GL report**, over a `GlJournalEntry` that
has no `LocationId`.

**What shipped.** `?accountId=` on Detail General Ledger, reached from a **View Ledger** row action
on the Chart of Accounts and from the global-search Account hit — phase 33 carried item #1 closed
without building a per-account page, because the reference product does not have one either; its
result row offers View Ledger and this codebase already had the report. The picker extracted into
`app-document-location-picker` and swept onto all 15 forms; the LOCATION cell and a Billing Location
filter onto all 15 lists, the filter living in `ListChrome` and its value in `ListFilter` so a page
opts in with three bindings. `BillingLocation.WarehouseId` became a prefill (32 #6 answered:
the live dialog's placeholder is literally `Select Default Warehouse`, two of three seeded locations
have no warehouse, and switching a document's location leaves its warehouse untouched).

**The defect it existed to find:** phase 32's write-path sweep guard was green while **14 of 15
detail DTOs and all 5 conversion templates** dropped `LocationId` on the way back out — a form could
store a branch, never show it, and overwrite it on the next save. `LocationReadPathSweepGuardTests`
now asserts all three read paths from `DocumentMechanisms.LocationBearing`.

**A correction carried forward:** five handlers matching `known-gotchas.md`'s forbidden
`x == null || …` predicate were rewritten to compose — and were **not** broken. Injecting the old
shape and running it against SQL Server returned 200 and the right row. The gotcha's generalisation
holds for a captured bool, not for a captured collection compared to null.

### 35b. The location dimension in the reports — **done** (`docs/phase-35b-status.md`)

- **Decision B settled three columns, not one.** `GlJournalEntry` gained a `LocationId` stamped at
  post time rather than every GL report joining back across 11 types — and `StockMovement` and
  `StockLedgerEntry` turned out to have the identical gap, so all three follow one rule: an
  append-only fact row carries the billing location of the document that created it, with reversals
  inheriting. Two backfill migrations (11 and 8 producing types), no index (34c's re-measure rule).
- The filter reached **36 report queries, their handlers and 86 endpoint constructions**, five shared
  readers, and **43 report screens** through one `app-report-location-filter`. Nine exemptions with
  reasons: the census's six, plus the two migrated registers and System Audit, whose rows carry no
  location at all. Annex 5 keeps the filter, as observed.
- The Sales Master Report's filter and LOCATION column are live (35a carried item #3), and the
  Purchase Master Report gained the same column — inferred from the two being mirrors, and named as
  the one place this phase went past the census.
- `TenantSettings.LocationWiseReportPermission` now governs every report (32b carried item #1). The
  E2E established what the mechanism actually is: a `Reports.*` key cannot be granted per location,
  so the scope comes from the caller's **transaction** grants.
- **Deferred with the user into phase 36:** Product-to-location (what the selector restricts is
  unobserved; separating "filters the product picker" from "stored and unenforced" needs two writes
  on the live tenant) and the three Moonbeam-only filters — Reporting Tags on the Journal report,
  Reporting Tags + group-by-warehouse on Inventory Position, Group By Bill / Include Credit Note on
  the Sales Register. `sales-summary`'s **Group Wise location** control (a group-*by*) is also still
  unbuilt; 35b gave that report the filter, not the grouping.

### 36. Allocation, forex and ageing consistency — **done** (`docs/phase-36-status.md`)

- **The warehouse-guard bug is fixed.** The route no longer carries
  `featureGuard('MultipleWarehouses')`; the page shows the cap the server actually enforces. Proved
  both ways: a flag-off tenant's first warehouse is a 201, its second a 403.
- **`ApplyPaymentAllocationCommand` posts the realised forex leg** on both its branches (a Payment
  and a contact-tagged Journal Voucher line), as its own entry — so both settlement doors agree and
  the control account is left flat. Cross-currency settlement stays rejected (28 Decision F), and
  the Allocate screen plus both approve-time pickers now filter their targets by currency instead of
  offering a choice the server refuses. The Supplier Payment form gained the Currency + Exchange Rate
  control phase 28 missed.
- **The two ageing reports read one `OutstandingDocumentReader`** — a bucket total is a partition of
  the rows Invoice Age lists, asserted on shared data. The Ageing Summary gained contact-tagged
  Journal Vouchers and contact opening balances as a stated consequence. Quick Payment/Receipt stay
  un-ageable (26b #2, a phase-17 Decision #7 consequence, re-examined and kept). Credit Terms are a
  server-side `DueDate` default (31 #4) and the credit-limit comparison converts currency (31 #3),
  which meant folding the whole contact-ledger family to base — a settlement at the rate of what it
  settles.
- **Product-to-location is built**, settled by one write on the live tenant: the restriction filters
  the line picker server-side by the document's location, an empty set means everywhere, and the
  Products grid is unfiltered. Enforcement at *save* is deliberately not built (unobserved).
- **The Moonbeam filters are built** — Reporting Tags on the Journal report, Reporting Tags + Group
  by Warehouse on Inventory Position, Group By Bill on the Sales Register (27 rows → 50, three item
  columns, same total). **Carried forward:** *Display Warehouse in Column* (indistinguishable from
  Group by Warehouse on a single-warehouse tenant) and `sales-summary`'s Group Wise location
  grouping.
- **Found on the way:** editing an Opening Balance line twice had been a 500 since phase 17.

### 37. Inventory policy — negative stock, returns at cost, the clearing unwind — **done** (`docs/phase-37-status.md`)
- **Negative Item Balance is real.** An oversell leaves a **shortfall layer** (a `StockLedgerEntry`
  negative on both quantities, carried at the product's last known cost in that warehouse, zero when
  it has never been received there); the next receipt fills it before the goods become stock on hand.
  Only Invoice and Production Journal can produce one — the two that already consult
  `IStockAvailabilityPolicy`; Warehouse Transfer, Inventory Adjustment's Decrease side and Debit Note
  stay hard rejects with a reason each.
- **The cost catch-up** — `filled x (real cost - assumed cost)` — reaches all three views of stock
  value, because any two can be patched into agreement: the FIFO layers, the Inventory account (its
  **own** GL entry against the covering document, on phase 36's `SourceDocumentGlEntries`) and the
  movement history (a value-only `StockMovement.ValueAdjustment` row, quantity zero). One migration.
- **A purchase return credits Inventory what the layers gave up**, not the price the supplier
  credits, with the difference derived as the plug that balances the entry and posted to the tenant's
  Inventory Adjustment account. Phase 29's release leg keeps its clearing debit and loses its
  Inventory credit (the single credit now carries the freight). Full return -> clearing zero; partial
  -> the unreturned units' share.
- **The Credit Note needed nothing, and the phase says why**: a sales return adds stock at the cost
  its source invoice recorded, so ledger and COGS agree by construction. The problem is specific to
  relieving, where FIFO chooses the layer.
- **Swept on the way:** every void reverses what is *outstanding* and every detail query reads every
  entry, so `GlJournalEntry.PostReversalOf` was deleted with its last caller; `WarehouseTransfer` and
  `OpeningStock` joined `GlSourceDocumentResolver` (they can now post). Phase 26c's pinned oversell
  test was **replaced** deliberately, and its replacement says what changed.
- **Carried forward, named:** Inventory Master gaining WarehouseTransfer and OpeningStock rows (needs
  a live re-check of the reference report's Txn Type filter, 26c Decision D); **multi-UOM x variants**
  (24) — a secondary unit's rates on a variant, with the sweep-guard allow-list reason retired; and a
  standalone Debit Note's Goods line, which still credits Inventory without touching the ledger
  because `DebitNote` carries no warehouse of its own.
- Traceability stays a Custom Fields matter (the Cadehi product form has no batch, lot, serial or
  expiry field), as recorded before the phase: no 37b.

### 38. Import and export breadth — **done** (`docs/phase-38-status.md`)
- **Eight upload types, not three.** Account, Product Category, Account Group and Contact Personnel
  are the reference product's own deferred types (its "Contact" is `ContactPersonnel`, the
  correction phase 21a's confirm-live pass caught); the **variant importer is an addition**, labelled
  as one, with three fixed Attribute/Value slots and a stated requirement that the parent's attribute
  pool already exists. No new permission keys: every importer sends the target type's own
  Create/Update command, which `AuthorizationBehavior` re-checks per row.
- **Intra-file ordering is solved once and used twice, not three times.** The roadmap called `Account`
  a tree and it is a **leaf** — its "Account Group" column points at a different aggregate the file
  cannot contain. `ImportRowSequencer` runs before the first row is planned in both passes; a cycle
  or a duplicate key fails the **whole file** with its rows named, while an unknown parent is one
  row's error.
- **The pre-commit review is real, and needed no new table.** `IEntityImporter.ApplyAsync` became
  **`PlanAsync`** (the command built but not sent), which is what made a dry run possible at all; the
  validate pass claims **only the rows it rejects** in the existing row ledger, so the apply pass
  skips them through the same mechanism that makes a crashed import resumable. It defaults **on**,
  matching the reference product, and the throughput it trades is stated with its number. The
  kickoff called this the phase's unconfirmed shape; phase 21a had **already** read the whole wizard
  live, including its wording and buttons.
- **Export gained three categories by a rule** — a category earns its place when its rows cannot be
  reconstructed from the five already there — plus a per-category selection and a date range, with
  every reader publishing whether a range applies to it so a short sheet is never ambiguous.
- **The row cap moved, and changed shape.** 25,000 → **50,000** per category on phase 34c's measured
  ~2.5 kB/row, joined by a workbook-wide **150,000** — which is what 34c said the cap should have
  been, and which this phase needed because it took the category count from five to eight. **The SAX
  rewrite is still not the move**: selection and a date range let a large tenant take the ledger out
  a quarter at a time.
- **The landed-cost grid's Import was read live, and phase 29 had it wrong**: a template-based `.xlsx`
  drawer, not a clipboard paste, whose template is generated *from the bill in front of you* (one
  column per tenant cost term, one row per bill line). It is therefore neither an
  `ImportTemplateDefinition` nor an `ImportJob`.
- **Found on the way**: a Minimal API binds an **array** parameter from the body on a POST, so
  `?categories=…` silently arrived null and every export ran all eight sheets while the screen said
  otherwise — caught by nothing but the manual E2E.
- **Carried forward**: the dry run cannot see what only writing can (uniqueness, lifecycle); there is
  no bulk way to configure a product's Attributes Used, which the variant importer requires; the
  landed-cost drawer replaces rather than merges per product; and `MaxRowsPerWorkbook` is still one
  machine's memory law.

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
