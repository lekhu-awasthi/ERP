# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–29)

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
| 27a | Cross-cutting rollout sweep, document-level mechanisms: Custom Fields to 11 more types (13 total, not the assumed 15), Custom Status to Sales Order + Production Order, Reporting Tags to every transactional type plus Opening Balances, Tasks/Documents/Activity tabs on all 15 transactional detail pages. `Comment` generalized to a polymorphic `CommentParentType` (phase-18 decision #3's deferred trigger). One shared `DocumentMechanisms` classification table plus a server guard test and a client guard spec prove the sweep complete | `phase-27a-status.md` |
| 27b | Cross-cutting rollout sweep, output: print/PDF for the 9 unwired document types (all 15 now, live-confirmed universal) on **one generic section-based layout** replacing phase-20d's two; **Bikram Sambat in server-rendered PDFs and `.xlsx`** via an `X-Calendar` header + ambient `RequestCalendar`, closing phase-23 Decision A; pagers on Email Logs / import history / export history; Turnstile on the New Organization wizard; a feature-flag route guard (3 real flags, 13 routes). `CustomTemplate`'s first two consumers: Terms and Conditions on 5 document types (not the 2 assumed) and the Customer/Supplier Balance Confirmation letter | `phase-27b-status.md` |
| 28 | Multi-currency (FR-2.5, NFR-1.3): a tenant `Currency` list seeded from a fixed catalog with NPR always present, `CurrencyCode` + `ExchangeRate` on 12 document types, the base-currency fold on each posting rule's **inputs** (so `GlLine` and every phase-8/19/26 report needed zero edits), two forex accounts and a realised-difference rule on Payment allocation. The entitlement is a **cap on the currency list**, not a gate on documents | `phase-28-status.md` |
| 29 | Landed cost (FR-6.15, Cost Terms' other half): an Additional Cost section on the Purchase Bill (Cost Term x Product x Method x Amount, plus the product-wise matrix), allocated at Approve by Value or Quantity across the bill's **goods** lines and capitalised into the received FIFO layers' unit cost — conservation law proven in SQL, residue named. The reference product posts no GL at all (it is periodic); we post Debit Inventory / Credit a new Landed Cost Clearing account, on phase-25 Decision A's argument. Debit Note gained a release leg | `phase-29-status.md` |

---

---

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

### 30. Communications — outbound email, SMS medium, email logs (FR-11.1, FR-4.5's Email Logs)
- Live: the Invoice detail's **Send Email** opens a dialog — Template\* (an Email-type Custom
  Template), To\* with More / CC / BCC, Reply To\* defaulting to the user, Subject\*, an "Attach
  Invoice PDF" checkbox (on) and a drop zone for extra files.
- **Scope.** That dialog on every printable document and on the Contact statement; merge fields
  from the `Email` Custom Template; the PDF from 20d's pipeline attached by default; `EmailLog`
  rows under the Contact Activity tab (the tab exists, the data does not); `AlertMedium.Sms` through
  the existing `ISmsSender` (20e listed it as one enum member and a branch). Every send goes through
  phase-20e's claim-then-act ledger — a resend is a new row, never a retry of the same one.

### 31. Credit control, dead settings, and the small carried items
- **Credit control (live, missed by the scan):** `Contact.CreditLimit` and `CreditTermId` (the
  New Contact modal's "Add More Details" block, alongside an **Accept Purchase** toggle and Email),
  plus a tenant policy **Credit Limit Exceeds = Reject / Warn / Do Nothing** on Configurations >
  General, applied at Invoice Approve against the customer's ledger balance — the same
  `BalanceAction` shape phase-7 built for stock, so reuse the Warn-and-override flow.
- **Enforce the three dead `TenantSettings`:** Suggest Selling Price (recent vs fixed, on the line
  picker), Product Price Basis (VAT-inclusive rates — a display and back-calculation rule, stored
  amounts stay exclusive), Negative Cash Balance (mirror of the stock policy, on any document
  crediting a Bank/Cash account); and `TrialEndsAt` (read-only past expiry, plus the
  `TenantSubscription` mutator 20f deliberately left out; live: the Subscriptions screen shows
  "will expire in N days").
- Cheque **Bounced** reverses the receipt's GL via `PostReversalOf` (Phase 17 recorded "no automatic
  reversal" as a gap); import for Account / Product Category / Account Group / Contact Personnel and
  for variants (21a and 24's deferred lists); export date range and extra categories (21b).

### 32. Billing Locations (FR-2.3, FR-3.3) — not observable on the UAT tenant
- Live: `Organization > Features` shows Billing Location **Disabled** with "reach out to Tigg
  Support" — an entitlement, not a toggle — and, consistently, no Location field on any form,
  no Location column on Opening Balances, and Location Enabled = No on the Subscriptions screen.
  So the location-enabled screens cannot be read here, which is why this phase now follows the
  observable ones rather than leading them.
- **Scope (to the scan, stated as such).** `BillingLocation { Code, Name, Address, WarehouseId,
  LocationType }` under `Tenancy`; **seed a HeadOffice location at Organization creation** and make
  `MultipleLocations` a *cap at one* (the phase-20f lesson); nullable-then-backfilled `LocationId`
  on every `ApprovableTransaction`, Payment and opening-balance line; location-wise numbering pools
  (`DocumentNumberingRule.LocationWiseNumbering` already exists and is read by nothing); location
  filter on registers and Master reports; per-location permission scope (`scope ∈ {default,
  HeadOffice, …}` per `architecture-spec.md` §3.7) as a second matrix in the role editor. POS
  location *types* are modelled, not built. If a location-enabled tenant becomes available, read
  it first; otherwise the phase-21c "derive when confirm-live is impossible" precedent applies and
  the status doc says so.

### 33. Platform chrome — global search, history, Quick Links
- Tigg's top bar: global search (Ctrl + /) across contacts, products and document numbers; a
  History/Browse list of recently opened records; the per-user **Quick Links** tray on Home
  (phase-23 declined per-user server storage for one boolean — this is the phase that decides the
  per-user store, once, for all three). Also the Tigg Subscriptions read-only screen (live: plan,
  amount, expiry, four entitlement flags).

### 34. Hardening — accessibility, consistency, scale
- NFR-6.2 (WCAG 2.1 AA) and NFR-6.1 (one interaction model across every list, detail and entry
  screen) have never had a phase; NFR-5.1/5.2 get a measured pass on a tenant-sized dataset. The
  streaming export writer (OpenXml SAX) replaces the 25,000-row cap if any tenant has hit it.

**Recommended drop list (decided, not silently omitted):** `Organization > Developer Mode` and
`> Documents` (phase-25's recommendation), `Product.PrintProfileId` (20d), the Marketplace flag,
and the Service Charge column (no product flag to drive it; revisit only with POS).

---

## Deferred beyond this roadmap (post-v1 — seams kept, no phases planned)
Explicit decisions (2026-08-18, revised 2026-09-02), not omissions:
- **Delivery Note / Goods Received Note (physical-movement inventory).** The scan recorded a
  "Mode of Inventory Tracking" setting; the live General page no longer has it, while Document
  Numbering still carries DO and GRN rules at next-number 1. `TenantSettings.InventoryTrackingMode`
  stays as the seam. **Re-entry condition:** a tenant on which a Delivery Note can actually be
  created; then it is a phase of its own (FIFO consumption moves from Invoice/Bill Approve to
  DO/GRN Approve under a handler-level gate, plus a goods-received-not-billed default account).
- **POS Retail / POS Restaurant** front-ends (PRD non-goal): Phase 32 models the location *types*
  so a POS phase is additive later.
- **IRD e-filing integration** (Annex 5's Sync-with-IRD columns): aspirational until committed; the
  Annex reports omit rather than fake those columns (Phase 8f precedent).
- **Marketplace / third-party app ecosystem**: a permission flag in the research, nothing more.

---

*Living doc — re-order/re-scope as real constraints surface. When picking up a phase: read its confirmed shape in `erp-module-scan.md` first; if the screen was never opened in the hands-on pass, confirm it against the live Tigg UAT tenant through the Browser pane (user logs in themselves) before writing code — the Phase 8f Annex 5 lesson: the speculative design and the real screen had nothing in common. Every phase ends with its own `phase-N-status.md`; CLAUDE.md's known-gotchas list is the pre-flight checklist for migrations, EF Core LINQ, and Angular selects.*
