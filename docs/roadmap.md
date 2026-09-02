# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md` (what to build) and `product-requirements.md` (why). This doc says *in what order*, broken down small enough to actually pick up and work. The reference product is a live Tigg UAT tenant; when a screen's shape is unconfirmed, it is read live through the Browser pane before building (the user logs in themselves — credentials are never entered by the agent and never committed to this repo; see `phase-8f-status.md` for the established workflow).

Guiding rule for phase sizing: each phase ends with something *runnable and demonstrable* (an API you can hit, a screen you can click through), not just "code exists." Every phase's exit criteria include: `dotnet build`/`dotnet test`/`ng build`/`ng test` all green; a hand-driven E2E pass against the real API/DB/browser (seed master data via curl + cookie jar, reserve UI clicks for the phase's own new screens); at least one **negative** check (a permission `403` naming the exact key, a lifecycle `409`, or a validation `400`) proven against the real API, not just the happy path; and a `docs/phase-N-status.md` history doc recording scope decisions and bugs before the phase is called done.

---

## Completed phases (0–25)

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

---

---

## The planned v1 sequence is complete

Phase 25 was the last v1 phase, so every phase in the index table above is done, and **no confirm-live
or browser-pass debt is outstanding** (`phase-25-status.md`'s Step 3 records how a browser pass is run
in a non-interactive session). What follows is the second sequence: parity with the reference product.

---

## Parity phases (26–35) — from a gap analysis against the reference product (2026-09-02)

**Method.** `erp-module-scan.md`'s module-by-module inventory of Tigg was diffed against what the
codebase now has (report pages, endpoint groups, Domain aggregates, the wiring of each cross-cutting
editor, and which `TenantSettings` are actually *read* by a handler). Three kinds of gap came out:

1. **Catalog gaps** — Tigg lists 40 reports; 24 exist here (plus 3 manufacturing). 27 are missing,
   and two PRD requirements are only partly met by them (FR-9.2's receivable/payable summaries,
   FR-9.3's by-customer/by-item/monthly analytics).
2. **Rollout gaps** — mechanisms that exist but reach a fraction of their surface: Custom Fields on
   2 of 17 document types, Custom Status on 2 of ~5, Reporting Tags on 2, print/PDF on 6 of 15,
   Custom Templates with **no consumer at all**, import on 3 of 7 entity types. Four `TenantSettings`
   (`SuggestSellingPriceMode`, `ProductPriceBasis`, `NegativeCashBalanceAction`,
   `InventoryTrackingMode`) are stored and edited but **read by nothing**; `TrialEndsAt` likewise.
3. **Structural gaps** — Tigg-core features the v1 roadmap deferred or never named: Billing
   Locations, multi-currency, physical-movement inventory (Delivery Note / GRN), landed cost, outbound
   email, per-location permission scope, global search.

**Ordering rule.** Cheapest-per-parity first, and nothing that changes a stored shape before the
reports that will read it exist. Catalog and rollout (26–27) reuse machinery and touch no schema of
consequence; the structural phases (28–31) each add a column to many documents, so they follow and
are ordered by how many later phases depend on them (Location before Currency, because per-location
numbering, opening balances, permission scope and reports all wait on it). Every phase keeps the v1
exit bar (build/test green, curl-seeded E2E, one proven negative path, a status doc) and the
confirm-live rule: **a screen the scan never opened is read live before a line is written**.

### 26. Report catalog completion (FR-9.1/9.2/9.3/9.5/9.6, three sub-phases)
- **26a — Accounting.** Transaction list, Journal report, General Ledger Summary, Detail General
  Ledger, GL Master Report; and the **Compare** (period-over-period) column on Trial Balance / Balance
  Sheet / Income Statement that FR-9.1 names and Phase 8a never built. All read `GlJournalEntry`;
  nothing new is stored.
- **26b — Receivable/Payable and analytics.** Customer Receivable Summary, Supplier Payable Summary,
  Invoice Age, Purchase Bill Age (closing FR-9.2); Sales/Purchase By Customer/Supplier, By Item,
  their Monthly variants, Sales Summary Report (closing FR-9.3). Reuse `ContactLedgerReader` and the
  Master-report handlers' line projections.
- **26c — Inventory, tax, system, analytics.** Inventory Position / Movement / Ledger / Master as
  *report* pages (the queries exist behind the Product detail), Sales Return Register and Purchase
  Return Register (phase-19 chose to fold returns into the main registers — confirm live whether the
  Return variants differ by more than a filter), User Log, Net Trading Assets, Exceptional Report.
  The last two were **never opened in the scan** — confirm live first; expect an 8f-style surprise.
- Each report gets its own `Reports.*` key (Admin-only where it exposes per-transaction rows, per
  the standing rule), `.xlsx` export via `ReportSpreadsheetExporter`, and the manufacturing reports
  get the export they still lack. Exit: every card on Tigg's Reports landing page has a counterpart
  here or a recorded reason not to.

### 27. Cross-cutting rollout sweep (two sub-phases; mechanical, guarded by sweep tests)
- **27a — Document-level mechanisms.** Custom Fields to the remaining 15 applicable document types
  (Phase 20a's editor, per type); Custom Status to Production Order and Sales Order (20b's
  machinery); Reporting Tags to every transactional type plus Opening Balances (Phase 19's
  `TransactionReportingTag`); Tasks / Documents / Comments / Activity tabs on transactional
  document detail pages (Tigg's Invoice detail has them; ours are Contact-scoped — extend
  `WorkTask`/`Attachment` parent types rather than a new polymorphic entity, per phase-18 Decision #2).
  A phase-23-style guard spec must prove each sweep complete.
- **27b — Output.** Print/PDF for the 9 unwired `DocumentType`s and both production documents
  (20d's pipeline); **BS dates in server-rendered PDFs and `.xlsx`** (phase-23 Decision A's carried
  limitation — port `web/src/app/shared/formatting/` to a Domain `BsDate` converter, same 2000–2092
  range, same null-outside-range rule); the three missing pagers (Email Logs, import history, export
  history); Turnstile on the New Organization wizard (20g); a feature-flag route guard (20f).
- **Custom Templates get their first consumers here:** the `TermsAndConditions` type pre-fills the
  Quotation/Invoice terms field; `CustomerBalanceConfirmation`/`SupplierBalanceConfirmation` render
  as a PDF from the Contact statement. The `Email` type waits for Phase 32.

### 28. Billing Locations (FR-2.3, FR-3.3 — the PRD's v1 item the roadmap parked with POS)
- **Why now, and why it is not POS.** Tigg's HeadOffice is itself a Billing Location; `Location`
  appears on Opening Balances, Document Numbering ("Enable Location-wise Next Number" — the flag is
  already on `DocumentNumberingRule` and read by nothing), the Sales Master report, and the
  permission matrix (Phase 14 scoped the HeadOffice section out for exactly this reason).
- **Scope.** `BillingLocation { Code, Name, Address, WarehouseId, LocationType }` under `Tenancy`;
  **seed a HeadOffice location at Organization creation** and make `MultipleLocations` a *cap at one*
  (the phase-20f lesson: nothing may block a flag-off tenant); nullable-then-backfilled `LocationId`
  on every `ApprovableTransaction`, Payment and opening-balance line; location-wise numbering pools;
  location filter on registers and Master reports; per-location permission scope
  (`scope ∈ {default, HeadOffice, …}` per `architecture-spec.md` §3.7) as a second matrix in the
  role editor. POS location *types* are modelled, not built.
- **Confirm live:** whether a document's location defaults from the user, the warehouse, or the
  last-used value; whether stock is location- or warehouse-keyed (the scan pairs each location with
  one warehouse — if that is a hard 1:1, the FIFO key does not change, which is the phase-24 style
  outcome to hope for).

### 29. Multi-currency (FR-2.5, NFR-1.3)
- Tigg puts **Currency + Exchange Rate to NPR** on Quotation, Expense, Journal Voucher, Cash
  Transfer and Opening Balances; NPR stays the functional currency and the GL posts in NPR.
- **Scope.** `Currency` list seeded from the standard catalog with NPR fixed active; `MultiCurrency`
  flag as a cap (NPR only when off); `CurrencyCode` + `ExchangeRate` on the document, `Amount` stored
  in transaction currency with the NPR-converted figure carried onto `GlLine` at Approve (fold the
  conversion into the posted lines exactly as phase-16b folded discounts — reports need zero change).
- **Confirm live before any posting code:** whether Tigg realises FX gain/loss on settlement at all
  (a Payment in USD against an invoice booked at a different rate). If it posts nothing, Decision
  A of this phase is whether *we* do — phase-25's perpetual-vs-periodic argument is the template.

### 30. Physical-movement inventory — Delivery Note and Goods Received Note
- `TenantSettings.InventoryTrackingMode` exists with `PhysicalMovement`/`AccountingMovement` and
  **no handler reads it**; the scan called this "the single most architecturally important setting".
  Tigg's numbering prefixes DO and GRN confirm the two document types exist.
- **Scope.** `DeliveryNote` (Invoice → DO moves stock; the Invoice then posts value only) and
  `GoodsReceivedNote` (PO → GRN receives stock; the Bill posts value), both `ApprovableTransaction`s
  with conversion enforcement (phase-6 bug #4's four-part fix), FIFO consumption moved from
  Invoice/Bill Approve to DO/GRN Approve **only when the mode is PhysicalMovement** — a handler-level
  gate, not a marker interface (phase-20f). GL rule for goods received-not-billed needs a new
  tenant default (the twelfth).
- **Risk:** the UAT tenant runs Accounting Movement, so the DO/GRN screens may be unobservable
  there. If no PhysicalMovement tenant is available the phase is built to the scan plus the
  document-numbering evidence and says so, the phase-21c "derive when confirm-live is impossible"
  precedent — or is descoped by explicit decision, the 20d precedent.

### 31. Landed cost and import completeness (FR-6.15, Cost Terms' other half)
- `CostTerm.AdditionalCost` (Freight, Insurance, Customs Duty) was built in 20c for a consumer that
  never came; Phase 25 consumed only the `ProductionCost` half. An **Additional Cost** section on the
  Purchase Bill that capitalises those amounts into the received FIFO layers' unit cost — the
  phase-25 conservation law again (`bill value + additional cost = layer value created + residue`),
  with the same `UnitCostScale` rounding and named residue.
- **Confirm live first:** the scan never saw the section on a Purchase Bill; if it lives elsewhere
  (a separate Landed Cost document) the shape changes entirely.

### 32. Communications — outbound email, SMS medium, email logs (FR-11.1, FR-4.5's Email Logs)
- Send a document PDF (20d) or Contact statement by email using the `Email` Custom Template's
  merge fields; `EmailLog` under the Contact Activity tab (the tab exists, the data does not);
  `AlertMedium.Sms` through the existing `ISmsSender` (20e listed it as one enum member and a
  branch). Every send goes through phase-20e's claim-then-act ledger — a resend is a new row, never
  a retry of the same one.

### 33. Settings that exist but do nothing, and small carried items
- **Enforce the four dead `TenantSettings`:** Suggest Selling Price (recent vs fixed, on the line
  picker), Product Price Basis (VAT-inclusive rates — a display and back-calculation rule, stored
  amounts stay exclusive), Negative Cash Balance (mirror of the stock policy, on any document
  crediting a Bank/Cash account), and `TrialEndsAt` (read-only past expiry, plus the
  `TenantSubscription` mutator 20f deliberately left out).
- Cheque **Bounced** reverses the receipt's GL via `PostReversalOf` (Phase 17 recorded "no automatic
  reversal" as a gap); early-payment discount on `CreditTerm` (the scan's `earlyPaymentDiscountPct`,
  never modelled); import for Account / Product Category / Account Group / Contact Personnel and
  for variants (21a and 24's deferred lists); export date range and extra categories (21b).

### 34. Platform chrome — global search, history, Quick Links
- Tigg's top bar: global search across contacts, products and document numbers; a History/Browse
  list of recently opened records; the per-user **Quick Links** tray on Home (phase-23 declined
  per-user server storage for one boolean — this is the phase that decides the per-user store, once,
  for all three). Also the Tigg Subscriptions read-only screen and the User Log if 26c did not take it.

### 35. Hardening — accessibility, consistency, scale
- NFR-6.2 (WCAG 2.1 AA) and NFR-6.1 (one interaction model across every list, detail and entry
  screen) have never had a phase; NFR-5.1/5.2 get a measured pass on a tenant-sized dataset. The
  streaming export writer (OpenXml SAX) replaces the 25,000-row cap if any tenant has hit it.

**Recommended drop list (decided, not silently omitted):** `Organization > Developer Mode` and
`> Documents` (phase-25's recommendation), `Product.PrintProfileId` (20d), the Marketplace flag.

---

## Deferred beyond this roadmap (post-v1 — seams kept, no phases planned)
Explicit decisions (2026-08-18, re-affirmed 2026-09-02), not omissions:
- **POS Retail / POS Restaurant** front-ends (PRD non-goal): Phase 28 models the location *types*
  so a POS phase is additive later.
- **IRD e-filing integration** (Annex 5's Sync-with-IRD columns): aspirational until committed; the
  Annex reports omit rather than fake those columns (Phase 8f precedent).
- **Marketplace / third-party app ecosystem**: a permission flag in the research, nothing more.

---

*Living doc — re-order/re-scope as real constraints surface. When picking up a phase: read its confirmed shape in `erp-module-scan.md` first; if the screen was never opened in the hands-on pass, confirm it against the live Tigg UAT tenant through the Browser pane (user logs in themselves) before writing code — the Phase 8f Annex 5 lesson: the speculative design and the real screen had nothing in common. Every phase ends with its own `phase-N-status.md`; CLAUDE.md's known-gotchas list is the pre-flight checklist for migrations, EF Core LINQ, and Angular selects.*
