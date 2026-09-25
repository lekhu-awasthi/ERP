# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–58)

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
| 43 | Aggregate completions: `UpdateOrganizationCommand` (8 editable fields, 3 refused by name), Deal/WorkTask as record parents with detail pages, `TrialStartsAt`/`TrialEndsAt` → `OriginatedAt`/`TermEndsAt`, `DebitNote.WarehouseId`; product-to-location enforcement retired on live evidence | `phase-43-status.md` |
| 44 | Report semantics read live first: four statutory reports folded to base currency, Reporting Tags corrected to OR-within/AND-across, Display Warehouse in Column, System Audit's stamped location, the billing-location backfill — plus the location filter both registers accepted and never applied | `phase-44-status.md` |
| 45 | Multi-UOM × variants: a variant **owns** its unit matrix and a variant parent is refused one, `Update`/`DeleteSecondaryUnitCommand`, the `ProductAttributePool` importer (a ninth upload type), the landed-cost drawer's replace semantics | `phase-45-status.md` |
| 46 | Metered add-on axes: the AI-scan ceiling (20/day, counted from the append-only `Audit` rows), billing locations as a purchased **record** that caps nothing, SMS declared already-metered by phase 18's ledger, the transaction window corrected from the term to the allowance **year** | `phase-46-status.md` |
| 47 | Accessibility completion: `Sort by` on all 16 document lists, the `<select>`-inside-`<a>` nesting removed app-wide with a derived guard, per-field `aria-invalid`/`aria-describedby` on 13 forms, the lookup/server search parity table, the drop list decided item by item. **The NVDA hour is still not done** | `phase-47-status.md` |
| 48 | Shared display components and the record pages: every date the app **outputs** through `NepaliDatePipe` (23 `DatePipe` uses across 19 templates, where the roadmap named 4), and the pipe made instant-aware so a timestamp's day is its **Nepal** day; `app-deal-form`/`app-task-form` serving create *and* edit | `phase-48-status.md` |
| 49 | The expiry decision: the reference product removes an expired **trial** from the namespace list and this phase **declined to match it** (one sample of trial behaviour, zero of paid) — the organization stays, stays readable and is **marked** (`TermEndsAt`/`IsExpired`, *Expired — read-only*; 17 of 139 dev organizations were silently read-only). Plus `IsTrialActive` → `IsActive` and term ends anchored to the Nepal day | `phase-49-status.md` |
| 50 | Measured indexes: `Cheque` gains `(OrganizationId, ChequeDate DESC)` + `ToKeyPagedResultAsync` (non-matching search 430,084 logical reads → 3,957), **three configurations measured and refused** including the phase's own idea, and `tests/Infrastructure.UnitTests` settling where a cross-assembly invariant lives | `phase-50-status.md` |
| 51 | Batch and serial tracking: **both are keys on the FIFO layer and neither carries a quantity** — `ProductBatch` has no `Quantity` column, a serial is a layer of quantity one. Control on the Invoice and Purchase Bill grids; everywhere else derives or refuses with a named 409. The two reports' columns were never read (phase-8f rule, invoked explicitly) | `phase-51-status.md` |
| 52 | A unit on the document line: it stores `UnitId` (the unit **lookup**, never the product's row) and `ConversionFactor`, frozen at Create/Update, with `PrimaryQuantity` derived and never a column. The money is untouched. 8 line types carry it — *every line naming a product and a quantity* — and 4 are deferred **unread** | `phase-52-status.md` |
| 53 | **A re-planning phase, not a feature phase.** Instead of reading screens one at a time it censused the vendor's **whole permission-key catalogue** from its own JS bundle — 166 keys. Result: we hold **20 of its 22 document types** and all 51 reports, so **bank reconciliation is the only substantial gap**; Recurring Invoices is a route with no server and no key; phase 52's four unread line types now have evidence. No code | `phase-53-status.md` |
| 54 | Phase 52's four deferred line types, settled by a **row-present** read: Inventory Adjustment carries a unit (a real select, markup identical to the Invoice grid's), Bill of Materials / Production Order / Production Journal carry a **display element** and no input, Opening Stock has no line grid. `fg_measurement_unit_id` is the product's primary unit stored and never chosen, so the header is **not a unit mechanism**; BOM's `Qty/Unit` does not exist and the question is deleted. Both outstanding **measurement debts paid** with numbers | `phase-54-status.md` |
| 55 | Bank statement import. **The kickoff's premise was falsified by the screen**: `/config/import-statement` is a generic CSV staging editor for Delivery Note / GRN / Inventory Adjustment, not the bank importer — which is `/accounting/bank-accounts/:id/import`, read in full and probed with twelve files. An ordinary tenth `ImportEntityType` (phase 21c's precedent beats the "resolves into no command" doubt), one signed `StatementAmount` rather than the vendor's dr/cr pair, Status derived and deferred to 56, deletion by row id **plus an undo-this-import** the vendor lacks, and the account as **per-run context** on `ImportJob`. **Phase 56 is unblocked** — its start condition was wrong | `phase-55-status.md` |
| 56 | Bank reconciliation — the two-pane N:M matcher, Book Statement, the reconciliation detail page with Unreconcile, the Reconciliation Report, and phase 55's deferred Status column and filter. The live read settled what inference could not: the right-hand pane is **`/gl-transactions`** (GL rows, not documents — so a reconciliation joins a `GlLine`, the only thing that is *one movement of money*), N:M is real, and the gate is **sum equality enforced by the server**. Membership is a nullable key on each side, not a link table; the aggregate carries no amount; **it posts nothing**, proven in SQL before and after. No new permission keys. The phase's own bug — an `OrderBy` over a projected record, green on InMemory and **500** on SQL Server — was the fourth trip through one door and is now fixed structurally | `phase-56-status.md` |
| 57 | **Finishing the bank module.** Quick Approve (a statement line becomes a document and is matched against itself in one action), the reconciliation report's `.xlsx`, and the 30-day Balance History chart. The vendor's **four** executors are **two** documents here — its picker is one list of ledger accounts in a chart where a customer *is* an account, so a Contact becomes a `Payment` and an Account a `JournalVoucher`. Reusing the Create/Approve commands through `ISender` **derives** the permission answer (the target document's own keys, on top of `.Manage`) instead of minting one. **The census re-run reproduced phase 53's 166 exactly**; its one diff, the Inventory Variance Report, is gated on *Physical* Inventory Tracking and so belongs to the DN/GRN deferral. `is_bank_user` is a read-only lender's report shell — a different product. **Phase 56's index debt paid, and it was booked against the wrong column**: `ReconciliationId` in the key is worth one logical read, while a covering index on `AccountId` takes the matcher's pane from 153,470 reads to 553 | `phase-57-status.md` |
| 58 | **Physical-movement inventory** — Delivery Note, Goods Received Note and the Inventory Variance Report. **The kickoff's premise was falsified by the first live write**: the vendor does not move FIFO consumption to DN/GRN, it keeps **two stock ledgers always** (physical: GRN/DN; accounting: bill/invoice/returns; both: adjustments and the other shared types) and the setting only picks which one the inventory screens read. Neither document posts, so the GRNI account dissolved. Here the physical ledger is **derived and quantity-only** (a new append-only table plus the shared types' existing rows — no dual writes, no backfill), every rule is per ledger under one Negative Item Balance verdict, a GRN's Void is refused once its goods have left (the vendor carries −3 at rate 0), and conversions are parallel and one-shot. Eleven keys, all Admin+Member. **Two indexes made covering with numbers**: the physical balance check 318 → 5 logical reads on its own table and 319 → 5 on `StockMovements`' shared half, the untargeted paths flat, the report load a full read in every variant and refused | `phase-58-status.md` |

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

## Continuation phases (48–52) — complete

Planned 2026-09-16 from the carried items of phases 42–47 plus a live read that found genuinely new
scope (batch and serial tracking, which became phase 51) — so the sequence was never only a backlog.
Every entry is done. The planning entries moved verbatim to `docs/roadmap-history.md` on 2026-09-20;
the outcomes are in each `docs/phase-N-status.md`, and the index table above carries the one-liners.

---

## Forward plan (54–56) — planned 2026-09-20 in phase 53, **all done**

Archived to `roadmap-history.md` ("Bank-module phases (54–57)") on 2026-09-21, when phase 57 closed
the last of them. It is worth knowing where it went: that section holds **the phase-53 census result
of record** — 166 permission keys, 20 of 22 document types ours, 51 reports — which phase 57 re-ran
and reproduced exactly.

---

## Forward plan (58) — planned 2026-09-22, **done**

Archived to `roadmap-history.md` ("Physical-movement phase (58)") on 2026-09-25. Its structural
paragraph — *FIFO consumption moves to DN/GRN Approve, plus a goods-received-not-billed account* —
is kept there as written and is **wrong**: the first live write showed two ledgers and no GL posting,
and `phase-58-status.md` is the record of what was built instead.

---

## Forward plan — **empty** (2026-09-25, after phase 58)

The last two of the vendor's 22 document types are built, and the census that produced 54–57 was
reproduced exactly on 2026-09-21. **Nothing is scheduled**, and padding past this point would be
inventing work (phase 53's rule). What remains, each with its start condition:

- **Phase 58's carried items** (`phase-58-status.md`, "Carried into a later phase"): the Mode filter
  on Movement / Ledger / Ageing, partial receipts and deliveries (per-line counters), Mark as
  Delivered / Processed, batch/serial and a per-line warehouse on DN/GRN. Parity work with the
  vendor's shape already read — a phase if wanted, not a discovery.
- **The auto-match suggestion engine** — a *product decision* first: the vendor returns
  `account_suggestions: null` on every row, so its rule would be invented, not copied.
- The three items under *Outside the sequence*, and the vendor-side actor.

**Plan the next phase the way 53 planned 54–57: from a fresh read, against evidence** — a census
re-run is cheap and is the right first step of any session that wants one.

### Read, and absent — recorded so no future session re-opens it

**Recurring Invoices is not a feature.** `/sales/recurring-invoices` is a real client route rendering
real Approved/Draft list chrome with `+ ADD NEW`; it calls `recurring-invoices?limit=20`, and the
server answers **404 on both builds** (`api-v2.tigg.app` and `api-v2.tigg.dev`) while the screen
renders `404 Not Found` in place of the grid. **No `recurring-invoice-*` key exists among the 166**,
and the 2026-09-21 re-run confirms it still does not. A route is not a feature, and the vendor's own
key catalogue is the check.

**`is_bank_user` is a different product.** Noticed in phase 55, carried through 56, chased in 57: it
replaces the vendor's entire router with fourteen **report** routes and a catch-all redirect to
`/reports/new/bank-view`, reached through its own `validate_login_bank_user` sign-in. A read-only
lender's or analyst's window on a tenant's financial reports, with no path to the reconciliation
module it was noticed beside. Closed, with no carried item.

---

## Outside the sequence — three items a session cannot schedule

Re-confirmed 2026-09-20. Phase 52 had added a fourth (its four unread line types); the 2026-09-20 read
supplied their evidence, so that item is now **phase 54** and has left this list.

**An hour with NVDA**, on Windows, over the live regions, the rich-text toolbar's roving tabindex, the
three phase-46 subscription panels and phase 47's field-level errors. Phases 40 and 47 both built
everything derivable and both recorded plainly that no screen reader was run, because none is
available in this environment; it was not simulated. It is the only thing standing between this
codebase and a finished WCAG 2.1 AA story. **Start condition:** a person with headphones. Record what
is heard verbatim before changing anything — phase 40's own rule, and the reason its first focus sweep
nearly reported an app-wide 2.4.7 failure that did not exist.

**The two traceability reports' real column sets** (51 carried #1). Phase 51 built the Product Batch
Report and the Product Serial No Report from the product detail tabs and the catalogue's filter
lists, because the vendor gates both reports behind permission keys its demo Admin does not hold —
the phase-8f rule, invoked explicitly. One page load each would settle whether this codebase's column
order, grouping and totals match. **Start condition:** an account or tenant holding those two keys.
Ask before falling back again — phase 32's lesson is that a second tenant existed and four written
scope decisions were wrong.

**Full-text search** (42 carried #1). List search on a term matching nothing is fixed — a count of
zero is now a complete answer — but a term matching many rows still costs what a `LIKE` costs. Phase
42 was explicit that the remedy is a *semantics* change, not a performance fix, and so not something
to do because it is next. **Start condition:** a tenant complaining about search latency, or a product
decision about what "search" should mean.

---

## Deferred beyond this roadmap (post-v1 — seams kept, no phases planned)

Explicit decisions (2026-08-18, revised 2026-09-02, 2026-09-10, 2026-09-14, 2026-09-15, 2026-09-16 and
**re-confirmed 2026-09-20**), not omissions. The 2026-09-20 permission-key census touched two of them and
strengthened both: the DN/GRN and marketplace seams are demonstrably real in the vendor, not aspirational.

- **Delivery Note / Goods Received Note (physical-movement inventory).** `TenantSettings.InventoryTrackingMode`
  is the seam. Cadehi's General page offers *Physical Movement*; with Accounting Movement selected no
  DO/GRN appears anywhere, and both tenants still read `inventoryTrackingMode: "Accounting Movement"`.
  **The 2026-09-20 census confirms the feature is fully built behind that flag** — the vendor carries
  `delivery-note-view/add/edit/approve/void` and the same five for GRN, so these are the only two of
  its 22 document types we lack. **Phase 57 adds a third item to this deferral:** its report
  catalogue diff is the **Inventory Variance Report** (`/reports/new/inventory-variance`), which
  opens live as *"Inventory Tracking **and Physical Inventory Tracking** Not Enabled"* — the same
  flag. Scoping this phase from the two document types alone would ship it without the report. **PROMOTED to phase 58 on 2026-09-22 and BUILT** (`phase-58-status.md`) — no longer deferred; the start condition and the FIFO sentence after it are the pre-read plan, and the second is wrong. **Start condition:** the user flips that setting on a tenant and the screens
  are read; then it is a phase of its own (FIFO consumption moves from Invoice/Bill Approve to DO/GRN
  Approve under a handler-level gate, plus a goods-received-not-billed default account).
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
  August 2026 release notes name "e-commerce sales"): a public storefront is a PRD non-goal. The
  2026-09-20 read found the integration is **live, not aspirational** — both tenants call
  `general-settings/daraz/access-token` on every page load — which strengthens the deferral rather
  than reopening it: it is a named third-party marketplace, i.e. exactly the storefront the PRD excludes.
- **POS Retail / POS Restaurant** front-ends (PRD non-goal): Phase 32 models the location *types*
  so a POS phase is additive later.
- **IRD e-filing integration** (Annex 5's Sync-with-IRD columns): aspirational until committed; the
  Annex reports omit rather than fake those columns (Phase 8f precedent).
- **Marketplace / third-party app ecosystem**: a permission flag in the research, nothing more.

## Dropped, with the reason (phase 47, Decision G)

Not deferred — decided against, so that no future session has to re-open them from a list. The full
reasoning and the re-entry condition for each is in `docs/phase-47-status.md`:
`Organization > Developer Mode` (API credential management — a platform feature, confirm-lived in
phase 25), `Organization > Documents` (a bare upload zone over phase 18's `Attachment`),
`Product.PrintProfileId` (live-confirmed to do nothing observable), the Marketplace flag, the Service
Charge column (a product flag this codebase does not model, printing `-` on every live row), supplier
credit-limit enforcement (the setting's own wording says Customer), `customtags` in the rich-text
toolbar, and rich-text tables/images (a *renderer* capability before a toolbar one).

## Settled, not carried

Recorded here so no future reading re-opens them: **keyset pagination** was retired rather than
deferred (42 — the tail's cost was the row fetch, and `ToKeyPagedResultAsync` fixes it inside
`PagedResult<T>`); **product-to-location** is a picker filter and always will be (43 #2 — the
reference product saves *and* approves a document naming an out-of-location product); and **a
standalone Credit Note putting no stock back** is deliberate asymmetry, not a divergence, because its
GL posts no Inventory leg either (43 #3).

---
*Living doc — re-order/re-scope as real constraints surface. When picking up work: read its confirmed shape in `erp-module-scan.md` first; if the screen was never opened in the hands-on pass, confirm it against the live Tigg UAT tenant through the Browser pane (user logs in themselves) before writing code — the Phase 8f Annex 5 lesson: the speculative design and the real screen had nothing in common. Every phase ends with its own `phase-N-status.md`; CLAUDE.md's known-gotchas list is the pre-flight checklist for migrations, EF Core LINQ, and Angular selects.*
