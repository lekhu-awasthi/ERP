# Tigg-Clone ERP+CRM+Accounting — Architecture & Domain Specification

Companion to `erp-module-scan.md` (the raw findings doc). That doc records *what Tigg does and how its UI behaves*; this doc translates those findings into *what we build*: bounded contexts, aggregates, the CQRS command/query surface, and the cross-cutting engines every module leans on.

**Confirmed stack**
- Backend: **.NET (latest LTS)**, Clean Architecture, **CQRS** (MediatR), **EF Core** on **SQL Server**.
- Frontend: **Angular (latest LTS)**.
- Scope: **ERP/back-office only** for v1 — POS Restaurant/POS Retail explicitly deferred (see Signup & Onboarding / Organization > Features in the scan doc), but the domain model reserves the seams (`BillingLocation.LocationType`) so it isn't a breaking change to add later.

---

## 1. Solution layout

```
src/
  Domain/                     # Entities, value objects, aggregates, domain events, domain services. Zero framework deps.
    Identity/
    Tenancy/                  # Organization, BillingLocation, Warehouse, Currency, Subscription
    Contacts/                 # Contact, ContactGroup (Customer/Supplier/Lead)
    Catalog/                  # Product, ProductVariant, VariantAttribute, ProductCategory, UnitOfMeasurement
    Sales/                    # Quotation, SalesOrder, Invoice, CreditNote
    Purchasing/                # PurchaseOrder, PurchaseBill, Expense, DebitNote
    Payments/                  # Payment (Customer/Supplier/Quick), PaymentAllocation, Cheque
    Accounting/                 # Account, AccountGroup, JournalVoucher, CashTransfer
    Inventory/                  # WarehouseTransfer, InventoryAdjustment, StockLedgerEntry (FIFO layers)
    Manufacturing/               # BillOfMaterials, ProductionOrder, ProductionJournal
    CRM/                          # Deal, Task, SmsCampaign
    Workflow/                      # UploadedDocument, ApprovalQueueEntry
    Configuration/                  # Lookup lists: CustomStatus, CreditTerm, PaymentMode, TdsType, ReportingTag, CustomField, DocumentNumberingRule, PrintingTemplate, AlertDefinition
    Common/                          # TreeEntity<T>, ApprovableTransaction base, IDocumentNumberGenerator, value objects (Money, TaxRate, FiscalPeriod)

  Application/                 # CQRS command/query handlers, validators, DTOs, MediatR pipeline behaviors. Depends on Domain only.
    <mirrors Domain's module folders, one Commands/ + Queries/ + Validators/ per module>
    Common/Behaviors/           # LoggingBehavior, ValidationBehavior, AuthorizationBehavior, TransactionBehavior, AuditBehavior

  Infrastructure/               # EF Core DbContext(s), repositories, external services (SMS gateway, email, IRD sync stub, file storage).
    Persistence/
      Configurations/            # IEntityTypeConfiguration<T> per aggregate, one file per entity
      Migrations/
    Identity/                     # ASP.NET Core Identity or custom, JWT issuing
    ExternalServices/

  Api/                            # ASP.NET Core Web API (or minimal API), controllers/endpoints thin — just MediatR.Send()
    Program.cs, appsettings, Swagger

  Web/                             # Angular workspace (separate top-level folder, not under src/ if using an Nx/monorepo layout — TBD by repo conventions)

tests/
  Domain.UnitTests/
  Application.UnitTests/
  Api.IntegrationTests/          # WebApplicationFactory + Testcontainers (SQL Server container) for realistic EF Core tests
```

Dependency rule: `Api → Application → Domain`, `Infrastructure → Application/Domain` (implements interfaces Domain/Application define). Nothing depends on `Infrastructure` or `Api` except the composition root (`Api/Program.cs` wires DI).

> **Actual repo note:** the built repo uses `web/` at the top level (not under `src/`), and `ErpApp.slnx` (the new .NET 10 solution format) instead of `.sln`. Everything else matches this layout.

---

## 2. Multi-tenancy strategy

The scan confirms Tigg is **shared-identity, per-Organization-tenant**: one user account can belong to N Organizations (`me.tiggapp.com` root identity host vs. tenant subdomains).

**Recommendation: single database, shared schema, discriminator column (`OrganizationId`) + EF Core global query filter.**

Rationale: SQL Server + EF Core makes database-per-tenant operationally expensive at this scale (backups, migrations ×N tenants), and Tigg's own UI (company switcher, "belongs to multiple Organizations") implies a single logical database is entirely workable. Every tenant-scoped entity carries `OrganizationId`; a global query filter (`modelBuilder.Entity<T>().HasQueryFilter(e => e.OrganizationId == _currentTenant.OrganizationId)`) makes tenant isolation automatic and hard to forget. `Organization` itself and `User`/`OrganizationMembership` (Identity/Tenancy contexts) are the only tables *not* filtered this way, since they're the root of the tenant relationship.

If regulatory/compliance needs later demand physical isolation for specific large tenants, this can be revisited without changing the domain model — it's purely an Infrastructure-layer decision (`DbContext` connection resolution).

**Identity split**: mirrors the scan's finding of two bounded contexts — `Identity` (User, email verification, login — tenant-agnostic) and `Tenancy` (Organization, OrganizationMembership, BillingLocation, Warehouse, Currency, Subscription). `OrganizationMembership { UserId, OrganizationId, RoleId, Status(Requested/Invited/Accepted) }` is the join.

---

## 3. Cross-cutting engines

These aren't bounded contexts on their own — they're services/behaviors every module calls into. Getting these right early avoids re-deriving them per module.

### 3.1 Document numbering (`IDocumentNumberGenerator`)
- Confirmed live: numbering is assigned **at Approve, not at Save/Create**. Every document sits at literal placeholder `"DRAFT"` until approved.
- One `DocumentNumberingRule` row per `DocumentType` enum value: `Prefix, NextNumber, Mode(Auto/Manual), ResetEveryFiscalYear, IncludeFiscalYearInCode, LocationWiseNumbering`.
- Implementation: a domain service called from each module's `ApproveXCommandHandler` (never from `CreateXCommandHandler`). Needs an `IUnitOfWork`-scoped increment (SQL Server `UPDLOCK`/`ROWLOCK` hint or a `HiLo`/sequence-per-tenant-per-doctype to avoid contention) — do **not** rely on optimistic concurrency here; number gaps are usually tolerated but *duplicates* are not (fiscal/tax-audit implications).
- Separate numbering pools for Account codes and Contact/Item codes reuse the same service with `DocumentType = "Account"|"Contact"|"Product"`.

### 3.2 Approve / Draft lifecycle (`ApprovableTransaction`)
- Shared base for every transactional aggregate (Quotation, Invoice, PurchaseBill, Payment, JournalVoucher, WarehouseTransfer, InventoryAdjustment, ProductionOrder/Journal, …): `Status(Draft/Approved/Void)`, `ApprovedByUserId?`, `ApprovedAt?`.
- `Approve()` domain method encapsulates: assign real number (3.1), run any type-specific side effects (GL posting — 3.4, stock movement — 3.5), raise a domain event (`DocumentApprovedEvent`) that the Activity Log / Transaction Approval read models subscribe to.
- **Maker-checker**: authorization for `Approve` is a distinct permission (`{Module}.{DocumentType}.Approve`) from `Create`/`Edit` — confirmed by Role Reference's per-document-type checkbox matrix (View/Create/Edit/Approve/Void). A single `Transaction Approval` query unions Draft-status rows across all `ApprovableTransaction` types the current user has `.Approve` rights on.

### 3.3 Document conversion (`?form_data=` pattern → `ConvertDocumentCommand`)
- Confirmed live twice (Quotation→Invoice, PurchaseOrder→PurchaseBill): conversion is **not** a distinct "convert" domain command with its own audit trail. It's "pre-fill a normal Create command from the source document's data, plus set `ReferrerType`/`ReferrerId` on the new aggregate."
- Recommend: every convertible target aggregate (Invoice, PurchaseBill, ProductionJournal, CreditNote, DebitNote) carries nullable `ReferrerType` (enum) + `ReferrerId` (Guid) columns. A shared `IDocumentConverter<TSource, TTarget>` maps source → target-creation-DTO; the Angular client calls a `GET /api/{module}/{sourceType}/{id}/conversion-template?target={targetType}` endpoint (replacing the Tigg SPA's client-side URL-payload trick, which doesn't translate well to a real API — better to compute the pre-fill server-side) that returns the pre-filled DTO, then the user still POSTs a normal `CreateXCommand`.
- Two documents remain fully independent aggregates post-creation; only `ReferrerType`/`ReferrerId` + the reciprocal read-model card ("linked document") connect them.

### 3.4 GL posting engine
- Confirmed live: `Approve()` on Invoice/PurchaseBill/Payment/JournalVoucher immediately produces `GlJournalEntry` rows (Account, Debit, Credit), and — newly confirmed — the **payment form previews the GL entries before the user even saves**, meaning the debit/credit computation must be a pure, side-effect-free function callable both for preview (query) and for actual posting (command), not something only the command handler can compute.
- Recommend: `IGlPostingRule<TDocument>` per document type (`InvoicePostingRule`, `PurchaseBillPostingRule`, `PaymentPostingRule`, …), each a pure function `TDocument → IReadOnlyList<GlLine>`. The **query** side (`PreviewGlPostingQuery`) and the **command** side (`Approve` handler) both call the same rule — no duplication, no drift.
- Invariant: `sum(Debit) == sum(Credit)` per document, enforced once in a shared `GlJournalEntry.Post(IReadOnlyList<GlLine> lines)` factory that throws if unbalanced — this is the Journal Voucher's live "Difference: Rs. 0" check, generalized to every document type that posts to GL, not just manual JVs.
- `PaymentAllocation { SourceType, SourceId, TargetType, TargetId, Amount }` is a separate, generic join table — confirmed live to support Invoice/PurchaseBill as targets and CustomerPayment/SupplierPayment/JournalVoucher/QuickReceipt/QuickPayment/Expense/ProductionJournal as sources (the Allocate Customer/Supplier Payment screens' full observed Type list). Default allocation is FIFO-ordered (oldest outstanding target first) but manually overridable per line — confirmed live via the "Clear" / "Clear All" actions on the allocation table.

### 3.5 Stock / FIFO costing engine
- Confirmed: `ValuationMethod = FIFO` on every Product. Stock-affecting documents (Invoice=decrement, PurchaseBill=increment, WarehouseTransfer=move, InventoryAdjustment=correct, ProductionJournal=consume raw material + create finished-good layer) all need to consume/create `StockLedgerEntry` (FIFO layer) rows on `Approve()`, scoped by `(ProductId, WarehouseId)`.
- **Confirmed live**: Warehouse is required specifically on Invoice and PurchaseBill (not on Quotation/SalesOrder/PurchaseOrder) — this is the exact seam where FIFO layers get touched. Model `WarehouseId` as required on the aggregate only for document types that actually move stock; don't add it to planning documents.
- **Confirmed live**: a Negative Stock Balance check runs on Invoice approval (decrement side only — Purchase Bill approval, an increment, triggered no such check). On this tenant it behaves **Warn-and-allow**: a confirmable warning, not a hard block. Recommend a `StockAvailabilityPolicy` returning `Ok | Warn(shortfallDetails) | Reject`, mirroring the tenant-configurable `NegativeStockBalanceAction` setting (Reject/Warn/DoNothing) inferred to live alongside the documented `NegativeCashBalanceAction` under `TenantSettings`. The command should accept an `overrideWarning: bool` flag so the client can resubmit after the user clicks "Continue" — avoids a second round-trip just to ask "are you sure."
- Two-mode inventory tracking (`Physical Movement` via DeliveryNote/GRN vs. `Accounting Movement` via Invoice/PurchaseBill directly, confirmed under Configurations > General) should gate, at the **domain/handler level**, whether `DeliveryNote`/`GoodsReceivedNote` command handlers are even registered/reachable for a tenant — not just a UI-side menu hide. v1 can ship Accounting Movement only (matches both scanned tenants) and add Physical Movement as a phase-2 mode if a customer needs it.

### 3.6 Custom fields (EAV)
- Confirmed: one field definition can apply to any subset of 17 document types. `CustomFieldDefinition { Name, Type(Text/Number/Description/Choices/…), ApplicableDocumentTypes[] }` + generic `CustomFieldValue { FieldDefinitionId, DocumentType, DocumentId, Value }`.
- SQL Server-specific note: store `Value` as `nvarchar(max)` with a `ValueType` discriminator for query-ability, or use a `sql_variant`/JSON column if EF Core's JSON column support (EF Core 8+) fits — recommend starting with the simple `nvarchar` + typed-cast-at-read-time approach; it's the least clever option and custom fields are rarely queried at scale (they're display/print concerns, per the scan's finding that they feed Print/Custom Templates).

### 3.7 Authorization
- Confirmed: permission matrix is `(scope, module, documentType, action)` where `scope ∈ {default location, HeadOffice, PosRestaurant, PosRetail}` — all four really `BillingLocation` instances with a `LocationType` tag.
- Recommend a single `RolePermission { RoleId, PermissionKey, IsGranted }` table, `PermissionKey` a stable string (`"HeadOffice.Sales.Invoice.Approve"`, `"Settings.UserPermissions"`), evaluated by one `IAuthorizationBehavior` in the MediatR pipeline (a `MustHavePermissionAttribute`-style marker on each command/query, resolved generically) rather than per-handler `[Authorize]` — the matrix is too large (150+ checkboxes per role observed) for hand-written policies per capability.

### 3.8 Reporting tags
- `ReportingTagCategory { Name }` + `ReportingTagOption { CategoryId, Value }`, many-to-many `TransactionReportingTag { DocumentType, DocumentId, TagOptionId }` — referenced from Quotation/Invoice forms and every Reports-module filter drawer.

### 3.9 Audit / Activity Log
- Confirmed: every CREATE/UPDATE/APPROVE emits an event automatically (519 events / ~3 weeks observed). Implement as a MediatR `IPipelineBehavior<TRequest, TResponse>` that logs `(UserId, Action, DocumentType, DocumentId, Timestamp)` for every command — not bespoke logging per handler. This single behavior also backs the Contact/Organization/Product "Activity" tabs (filtered by `DocumentId`).

---

## 4. Bounded contexts — aggregates & key commands/queries

For each context: the aggregate roots, their key invariants, and the primary command/query surface. (Value objects and simple lookup tables are omitted here — they're in the scan doc's per-module "Data model implication" notes; this section is the *aggregate-level* view.)

### 4.1 Identity & Tenancy
- **User** (aggregate root): Register, VerifyEmail, Login, RequestPasswordReset.
- **Organization** (aggregate root): CreateOrganization (seeds Subscription, Accounting Features flags, VAT flag, Accounting Start Date in one command — confirmed as a single wizard flow), UpdateOverview, SetLockDate.
- **OrganizationMembership**: RequestToJoin, InviteUser, AcceptInvitation, AcceptRequest.
- **BillingLocation**, **Warehouse**, **Currency**: simple CRUD aggregates under Organization > Features.
- **TenantSubscription**: read-mostly; entitlement flags gate command availability elsewhere (`LocationEnabled`, `WarehouseEnabled`, `IrdSyncEnabled`).

### 4.2 Contacts (CRM)
- **Contact** (aggregate root, `Type ∈ {Customer, Supplier, Lead}`): CreateContact, UpdateContact, Deactivate, plus child collections (ContactPersonnel, Task, Document via the shared polymorphic Task/Document mechanism — 4.9).
- **ContactGroup**: self-referencing tree, standard CRUD.
- **Deal**: CreateDeal, UpdateStage, MarkWon, MarkLost, AssignTo.
- Query side: `ContactStatementQuery` (the running-balance ledger, joins Accounting), `ContactOverviewQuery` (opening/DR/CR/closing balance).

### 4.3 Catalog (Inventory master data)
- **Product** (aggregate root, `Type ∈ {Goods, Service}`): CreateProduct, UpdateProduct, AddSecondaryUnit. Full field list confirmed live — see scan doc's Inventory > Products section for the captured JSON shape (includes `ReOrderLevel`, `TrackInventory`, `PrintProfileId`).
- **VariantProduct**: a Product subtype; CreateVariantProduct (defines AttributesUsed), GenerateVariantCombinations, AddVariant.
- **VariantAttribute**, **ProductCategory** (tree), **UnitOfMeasurement**: lookup aggregates.
- Query side: `ProductStockPositionQuery` (Opening/In/Out/Balance), `InventoryLedgerQuery` (kardex, per product+warehouse).

### 4.4 Sales
- **Quotation**, **SalesOrder** (standalone, cross-referenced by free-text Reference No only — confirmed live, not a conversion target of Quotation), **Invoice**, **CreditNote** — all `ApprovableTransaction`.
- Commands: `CreateQuotation`, `ApproveQuotation`, `GetInvoiceConversionTemplate(quotationId)` → `CreateInvoice(fromTemplate)`, `ApproveInvoice` (triggers 3.4 GL posting + 3.5 stock decrement + Negative Stock Balance policy check), `CreateCreditNote(fromInvoiceId)`, `VoidInvoice`.
- Query side: `SalesMasterReportQuery` (line-item fact table — confirmed live shape: Contact, Type, Warehouse, Product, Qty, Rate, Amount, VatType, VatAmount, …), Customer Ageing/Statement queries.

### 4.5 Purchasing
- **PurchaseOrder**, **PurchaseBill**, **Expense** (account-based, no ProductId on lines — confirmed distinct from PurchaseBill), **DebitNote**.
- Commands mirror Sales exactly: `CreatePurchaseOrder`, `ApprovePurchaseOrder` (no stock/GL side effect — confirmed), `GetBillConversionTemplate(purchaseOrderId)` → `CreatePurchaseBill`, `ApprovePurchaseBill` (GL post + stock increment, TDS calculation if applicable, Import Details capture when `IsImport`).
- TDS: `PurchaseBill`/`Expense` carry optional `TdsTypeId` (FK to system-seeded `TdsType` reference data — versioned per fiscal year) + computed `TdsAmount`; posting rule (3.4) must add the TDS-payable GL line.
- **Open item carried over from the scan**: Annex 13 Capital-vs-Others classification has no confirmed UI location. Recommend adding an explicit `ExpenditureClassification(Capital/Others)` field on `PurchaseBillLine`/`ExpenseLine` now, defaulting to `Others`, rather than waiting to reverse-engineer it — cheap to add, expensive to retrofit into historical data later, and the Annex 13 statutory report needs it unconditionally.

### 4.6 Payments (shared Sales+Purchase)
- **Payment** (aggregate root, `Direction ∈ {Received, Paid}`, unifies Customer Payment/Supplier Payment/Quick Payment/Quick Receipt — confirmed to be the same underlying shape across all four in the scan): CreatePayment (server computes the conversion-template pre-fill per 3.3 when arriving from a Record Payment action, including `IncludedAllocationIds`), ApprovePayment (GL post per 3.4 — live-previewable before approval).
- **PaymentAllocation**: `Allocate(sourceId, targetId, amount)`, `Clear(allocationId)`, `ClearAll(paymentId)` — FIFO-defaulted, confirmed live.
- **Cheque**: `Direction(Received/Issued)`, lifecycle `Issued/Received → Presented → Cleared/Bounced`, linked to a Payment via `PaymentMode = Cheque`.

### 4.7 Accounting
- **Account** (leaf) / **AccountGroup** (tree, 5 root types: Asset/Liability/Equity/Income/Expense) — the target of every "Select Account" picker across the system.
- **JournalVoucher**: `AddLine(accountId, debit, credit)`, invariant `sum(debit)==sum(credit)` (3.4), `Approve`.
- **CashTransfer**: simplified UI over JournalVoucher — one `FromAccountId`, N `(ToAccountId, Amount)` rows (fan-out confirmed live); internally still posts as a balanced multi-line GL entry.
- **OpeningBalanceLine** / **OpeningStockLine**: one-time "day zero" JournalVoucher-equivalent / InventoryAdjustment-equivalent, per-location, multi-currency, scoped to the Organization's Accounting Start Date.

### 4.8 Manufacturing (phase-2 candidate — confirm with user before committing v1 scope)
- **BillOfMaterials** (master data/template): RawMaterials[], ByProducts[] (with cost-allocation %), ExpenseTerms[].
- **ProductionOrder** (planning, uncosted): CreateFromBom, `ConvertToProductionJournal` (same 3.3 conversion pattern).
- **ProductionJournal** (executed, costed): on Approve, consumes raw-material FIFO layers at actual cost, computes `CostPerUnit = FinishedGoodsCost / OutputQuantity` (confirmed formula: `TotalCostOfProduction = RawMaterialCost + ProductionExpenses`; `FinishedGoodsCost = TotalCostOfProduction − CostAllocatedToByProduct`), creates a new FIFO layer for the finished good at that cost, posts GL (not yet independently confirmed which accounts — flagged in the scan as a remaining open item).

### 4.9 Workflow (cross-cutting, but its own context)
- **Task** (polymorphic: `ParentType/ParentId` — attaches to Contact, Organization, and likely others): CreateTask, CompleteTask, AssignTo.
- **UploadedDocument**: Upload, `ConvertToTransaction(targetType)` (AI-extraction-assisted for the ✨-marked subset: Quick Payment, Invoice, Expenses, Purchase Bill).
- Read-model: `TransactionApprovalQuery` — unions Draft-status rows across every `ApprovableTransaction` type the current user holds `.Approve` on (3.2).

### 4.10 Configuration (tenant control plane)
All lookup-list contexts (`CustomStatus`, `CreditTerm`, `CostTerm`, `PaymentMode`, `TdsType`, `ReportingTagCategory`, `CustomFieldDefinition`, `PrintingTemplate`, `CustomTemplate`, `DocumentNumberingRule`, `AlertDefinition`) share a generic `LookupList<T>` CRUD pattern at the Application layer (one generic command/handler pair parameterized by lookup type) even though each gets its own Angular screen. `TenantSettings` (General tab — Suggest Selling Price mode, Product Price Basis, Inventory Tracking mode, Negative Cash/Stock Balance actions) is a single-row-per-tenant aggregate read by nearly every other context's command handlers.

---

## 5. EF Core / SQL Server specifics

- **Schemas**: one SQL Server schema per bounded context (`sales.Invoices`, `purchasing.PurchaseBills`, `accounting.Accounts`, …) — keeps the DbContext's model organized and makes it trivial to see which context owns which table in SSMS, without needing separate databases.
- **Tenant isolation**: global query filter on `OrganizationId` (§2) — apply via a shared `ITenantEntity` marker interface + a single `modelBuilder.ApplyGlobalFilters<ITenantEntity>()` extension rather than repeating the filter per entity config.
- **Concurrency**: `rowversion` (`byte[]`) column on every aggregate root for optimistic concurrency on Edit — except the document-numbering counter (§3.1), which needs pessimistic locking or a `SEQUENCE` object per (tenant, doctype) to avoid duplicate-number races under concurrent approvals.
- **Money**: `decimal(18,4)` for line amounts/rates (Nepali Rupee doesn't need more, but exchange-rate math benefits from the extra scale before rounding to display precision), `decimal(18,6)` for `ExchangeRate`/`ConversionRate`.
- **Trees** (AccountGroup, ContactGroup, ProductCategory, CustomStatus-ordering): recommend the **adjacency list** (`ParentId` self-FK) over SQL Server `HIERARCHYID` — simpler EF Core mapping, and the observed depth (a few levels) doesn't need `HIERARCHYID`'s query-performance advantages at this scale. Add a recursive CTE-backed query (`ITreeQuery<T>`) for "get full subtree" reads (Trial Balance's group rollups need this).
- **Audit**: consider **SQL Server temporal tables** (`SYSTEM_VERSIONING`) on high-value aggregates (Invoice, PurchaseBill, JournalVoucher, Account) as a belt-and-suspenders complement to the Activity Log event stream (§3.9) — the event stream answers "who did what when," temporal tables answer "what did this row look like at time T" for point-in-time reconstruction (useful for the Lock Date / period-close feature).
- **Migrations**: one `DbContext` for v1 is fine given the shared-schema tenancy model (§2); split into multiple `DbContext`s only if a bounded context's migration history needs to move independently (unlikely at this stage).

---

## 6. What this spec deliberately defers

Per the scan's confirmed scope decision and remaining open items:
- **POS Restaurant / POS Retail**: `BillingLocation.LocationType` reserves the seam; no POS-specific aggregates (KOT, table billing, split payment) are modeled yet.
- **IRD e-filing / CBMS sync**: `TenantSubscription.IrdSyncEnabled` flag reserved; actual integration not designed (Tigg's own trial tenant had it disabled, so the real submission flow was never observed to spec against).
- **Manufacturing** (§4.10): included in the domain model since it's architecturally analogous to Purchase Order→Bill, but confirm with the user whether it's in v1 scope before building it — it's a meaningfully sized sub-system (BOM + 2-stage production documents + cost roll-up) that could reasonably be its own phase.
- **Physical Movement inventory mode** (Delivery Note / Goods Received Note): both scanned tenants run Accounting Movement; v1 ships Accounting Movement only.
- **Annex 13 Capital-vs-Others**: modeled speculatively (§4.5) pending confirmation of Tigg's actual UI location for this field.

---

*Source: `erp-module-scan.md` in this project (full UI/workflow findings across 9 modules, signup/onboarding, and a live hands-on transaction-creation pass). This spec should be revisited once the MVP scope/phasing decision (a separate, not-yet-made decision) is finalized, since phasing may reorder which bounded contexts get built first.*
