# Product Requirements Document (PRD)

## [Product Name TBD] — ERP, CRM & Accounting Platform for Nepali SMEs

**Status:** Draft v1
**Author:** Compiled from live product research (see References)
**Related docs (this project):** `erp-module-scan.md` (source research), `architecture-spec.md` (technical design), `roadmap.md` (build sequencing)

---

## 1. Overview

### 1.1 Problem statement
Small and medium businesses in Nepal — traders, importers/exporters, manufacturers, retailers, service firms — run their operations across a patchwork of spreadsheets, WhatsApp, paper ledgers, and disconnected point tools. They need one system that handles the full commercial lifecycle — quoting and selling, procuring and paying suppliers, tracking stock, and keeping statutory-compliant books — without the cost or rigidity of enterprise ERP suites built for other markets.

Nepali accounting has specific requirements most generic (Western) accounting/ERP software doesn't handle natively: 13% VAT with mixed taxable/non-taxable line items, TDS (withholding tax) on specified purchase categories using IRD-published category codes, the Bikram Sambat (BS) calendar alongside Gregorian (AD) dates, fiscal-year-suffixed document numbering, IRD statutory reports (Annex 13, Annex 5, VAT Summary), and Nepali digit-grouping conventions (lakh/crore comma placement) in financial figures.

### 1.2 Product vision
A single web application where a Nepali SME can, from day one of signing up, register their company, invite their team, and start quoting, invoicing, purchasing, and tracking inventory — with every transaction automatically producing correct double-entry bookkeeping and statutory-ready reports, in both AD and BS calendars, without the user needing accounting expertise to get the postings right.

### 1.3 Product summary
A multi-tenant, cloud-hosted business-management platform covering:
- **CRM** — contact/customer/supplier directory, sales pipeline (Deals), SMS marketing.
- **Sales** — Quotation → Invoice → Payment, Sales Orders, Credit Notes.
- **Purchasing** — Purchase Order → Purchase Bill → Payment, Expenses, Debit Notes, TDS and import handling.
- **Accounting** — full Chart of Accounts, Journal Vouchers, Cash Transfers, Cheque tracking, automatic GL posting from every transaction.
- **Inventory** — product catalog (incl. variants), multi-warehouse stock with FIFO costing, warehouse transfers, adjustments, and a manufacturing sub-system (BOM → Production Order → Production Journal).
- **Reports** — financial statements, statutory tax reports, AR/AP aging, sales/purchase analytics, an audit log, and computed financial ratios.
- **Workflow** — task management, a maker-checker approval queue for every transaction type, and an AI-assisted document-inbox for converting scanned receipts/bills into structured transactions.
- **Configuration** — a full tenant-wide control plane (numbering rules, custom statuses, custom fields, printing templates, permissions).

This PRD describes the **full target product**. Actual build sequencing (what ships first) is governed separately by `roadmap.md` — this document does not repeat that sequencing, but flags v1-vs-later scope decisions where they materially affect requirements.

---

## 2. Goals & success metrics

| Goal | How we'd know |
|---|---|
| Replace spreadsheet/paper bookkeeping for a Nepali SME | A business can complete a full month's operations (quote→sale→collect, buy→pay, stock movement) without leaving the app or reconciling anything manually in a spreadsheet |
| Statutory-correct books with zero manual tax math | VAT, TDS, and Annex 13/5 reports reconcile to what a human accountant would compute by hand, for every transaction type that touches tax |
| Fast time-to-first-value | A new signup can create an Organization and record their first Invoice within one sitting, with no training required for the core Quote→Invoice→Payment flow |
| Auditable by design | Every create/update/approve action is attributable to a user and timestamped, and every approved document's GL impact is inspectable inline, without a separate reconciliation step |
| Safe multi-user collaboration | Two or more staff can work concurrently without stepping on each other's drafts, with a clear maker-checker approval boundary before anything is "final" |

### Non-goals (explicitly out of scope for this PRD's covered surface)
- Point-of-sale (till/counter) interfaces for retail or restaurant use — deferred; see §4.
- Direct government e-filing/submission (IRD CBMS sync) — the product should *produce* statutory-ready reports; automated *submission* to IRD systems is a later integration, not a v1 requirement.
- Payroll / HR.
- A public storefront or e-commerce integration.

---

## 3. Users & personas

- **Business Owner / Admin** — creates the Organization, invites staff, has full access, cares about the financial big picture (dashboard, reports) more than daily data entry.
- **Accountant / Bookkeeper** — the heaviest user of Accounting/Sales/Purchase modules; needs Approve rights on financial documents, cares deeply about VAT/TDS correctness and statutory reports; typically does *not* have User & Permissions access (separation-of-duties requirement, confirmed in the research).
- **Sales Staff** — creates Quotations/Invoices, manages Deals/Contacts, needs Create rights on Sales documents but not necessarily Approve.
- **Purchase Staff** — mirrors Sales Staff on the buying side; creates Purchase Orders/Bills, records supplier info.
- **Warehouse/Inventory Staff** — manages stock movements, warehouse transfers, adjustments; typically doesn't touch financial documents at all.
- **External accountant (view-only)** — a common Nepali SME pattern: an outsourced accountant who needs to view/export reports and possibly approve, without full operational access. Covered by a "View Only" role.
- **Multi-org user** — a person (e.g. a consultant, or an owner of multiple businesses) who belongs to more than one Organization under a single login and switches between them.

---

## 4. Scope

### 4.1 In scope (v1 target — the full back-office/ERP surface)
Everything under §1.3 except the exclusions below. This includes multi-currency, multi-warehouse, multi-location (HeadOffice-style) support, and the manufacturing sub-module, since the underlying platform (Tigg) treats these as core rather than exotic — though see `roadmap.md` for the actual build order, which may defer Manufacturing to a later phase pending a scope confirmation with the business owner.

### 4.2 Deferred (not in this PRD's v1 surface, but the data model reserves the seams so adding them later isn't a breaking change)
- **POS Retail / POS Restaurant** front-ends — till-style billing, table/KOT management, split payments. The permission model and Billing Location concept both already anticipate these as a location "type," so this is additive later, not a redesign.
- **IRD e-filing/CBMS direct integration** — v1 produces the correct statutory reports (VAT Summary, TDS Report, Annex 13/5) for a human to file; automated submission is a future integration point.
- **Marketplace / third-party app ecosystem** — referenced only as a permission flag in the research; not a v1 requirement.

### 4.3 Explicit non-requirements
- No requirement to import/migrate data from any *specific* named competitor product beyond a generic spreadsheet-based import (see §6.9).

---

## 5. Guiding principles (derived from what worked, and what should be preserved, in the reference product)

1. **Every optional capability is a tenant-level opt-in, not a code branch.** Inventory tracking, multi-warehouse, multi-currency, multi-location, and manufacturing are all things a tenant turns on (at signup or later) — a tenant that doesn't need them should never see their UI surface.
2. **Draft, then Approve, always.** No transaction — sales, purchase, accounting, or inventory — takes real effect (gets a real document number, posts to the ledger, moves stock) until an authorized user explicitly approves it. This is the maker-checker control that makes the audit trail trustworthy.
3. **Every document that can logically follow from another should offer a one-click, pre-filled conversion** (Quotation→Invoice, Purchase Order→Purchase Bill, Production Order→Production Journal) — but the resulting document is always a fully independent, editable record, never a locked mirror of its source.
4. **Nothing about tax/statutory correctness should require the user to know tax law.** VAT rates, TDS categories, and Annex 13 thresholds are system-configured reference data the user picks from a list; the system computes the numbers.
5. **Reports never mutate data.** The reporting surface is strictly read-only, with drill-down into the source transaction as the only "edit" path (which opens that transaction's own screen).
6. **Nepali business conventions are first-class, not localized afterthoughts** — BS calendar alongside AD, fiscal-year-suffixed numbering, lakh/crore digit grouping, and the specific statutory report formats (Annex 13/5) are baseline requirements, not stretch goals.

---

## 6. Functional requirements

Each subsection is a set of user-facing capabilities. Requirements are written as "the system shall..." statements grouped by module, in the same module boundaries used by `architecture-spec.md` so the two documents cross-reference cleanly.

### 6.1 Signup, Identity & Onboarding
- FR-1.1: A visitor shall be able to register a new account with Full Name, Email, Phone, and Password, protected by a bot-check (e.g. CAPTCHA/Turnstile) and a Terms-of-Service acceptance.
- FR-1.2: The system shall verify the registrant's email via a one-time code sent to that address before the account is fully active.
- FR-1.3: A registered user shall be able to log in centrally (independent of any specific Organization) and, once authenticated, see a list of Organizations they belong to, organized into "Your Organizations," "Pending Requests" (sent by them), and "Pending Invitations" (received).
- FR-1.4: A logged-in user shall be able to create a new Organization via a guided, multi-step setup: (a) organization profile (name, industry, address, logo, VAT-registration status, fiscal accounting start date, a unique workspace identifier with live availability checking), (b) opt-in selection of Accounting Features (Track Inventory, Multiple Locations, Multiple Warehouses, Multi-Currency, Manufacturing, POS Retail, POS Restaurant), (c) a review-and-confirm step.
- FR-1.5: Creating an Organization shall start the account on a free trial by default (length configurable; researched reference defaults to 15 days).
- FR-1.6: A user with sufficient permission shall be able to invite another user to their Organization by email + role; the invited user shall see and be able to accept the invitation from their own account, and shall NOT be auto-added before accepting.
- FR-1.7: A user shall be able to belong to more than one Organization simultaneously and switch between them without logging out.

### 6.2 Organization & Tenant Configuration
- FR-2.1: An authorized user shall be able to view and edit the Organization's profile (name, display name, contact details, PAN, VAT-registration flag, accounting start date).
- FR-2.2: An authorized user shall be able to set a "transaction lock date," after which no transaction dated on or before that date can be altered, cancelled, or backdated into — protecting closed accounting periods.
- FR-2.3: An authorized user shall be able to manage Billing Locations (branches/points of sale), each linked to a Warehouse, with a location "type" (e.g. Head Office, and reserved types for future POS locations).
- FR-2.4: An authorized user shall be able to manage the Warehouse list (for tenants with Track Inventory / Multiple Warehouses enabled).
- FR-2.5: An authorized user shall be able to manage the tenant's active Currency list (for tenants with Multi-Currency enabled), each with code/name/symbol and an active/inactive flag.
- FR-2.6: The system shall enforce, at the point of use, which document types and UI surfaces are available based on the tenant's opted-in features (e.g. a tenant without Multi-Currency enabled should not be prompted for exchange rates; a tenant without Manufacturing enabled should not see BOM/Production screens).
- FR-2.7: An authorized user shall be able to configure tenant-wide business rules: default price-suggestion behavior (recent price vs. fixed price), whether product prices are VAT-inclusive or -exclusive, the inventory-tracking mode, and the system's behavior when a transaction would push a cash/bank account or a stock quantity negative (block / warn-and-allow / allow silently).
- FR-2.8: An authorized user shall be able to trigger an on-demand full data backup/export of the tenant's data (products, contacts, chart of accounts, ledger transactions, stock movements) and download the result.
- FR-2.9: An authorized user shall be able to bulk-import Products, Customers, Suppliers, Contacts, Accounts, Product Categories, and Account Groups from a spreadsheet, using a downloadable template, in either create-new or update-existing mode.
- FR-2.10: An authorized user shall be able to import historical Sales/Purchase tax-register data from a prior system, for continuity of statutory tax reporting across a system cutover, without needing to recreate every historical transaction as a full document.

### 6.3 Users, Roles & Permissions
- FR-3.1: The system shall support a role catalog per Organization (e.g. Admin, Accountant, Sales, Purchase, View Only, and any custom roles the tenant defines), each role carrying a granular permission set.
- FR-3.2: For every transactional document type, a role's permissions shall be independently controllable across View, Create, Edit, Approve, and Void.
- FR-3.3: Permission scope shall be assignable per Billing Location (e.g. a role may have different Sales/Inventory permissions at Head Office vs. another location), in addition to the document-type-level permissions.
- FR-3.4: Settings-area permissions (App Configuration, Organization Configuration view vs. edit, Opening Balance view vs. edit, User & Permissions management, Import/Export, Subscription management) shall each be independently grantable, so that e.g. an Accountant role can be configured to view but not edit organization settings, and to be explicitly denied User & Permissions access — enforcing separation of duties.
- FR-3.5: Report visibility shall be independently grantable per individual report, not just per report category.
- FR-3.6: Every write action (create, edit, approve, void) shall be checked against the acting user's effective permission before being allowed, regardless of which client or API path is used to attempt it.

### 6.4 Contacts & CRM
- FR-4.1: The system shall maintain a single master directory of Contacts, each typed as Customer, Supplier, or Lead, so a business partner who is both a customer and a supplier is represented once with a unified transaction history.
- FR-4.2: A Contact shall support: name, address, an internal code, PAN, phone, email, group membership, and an active/inactive status.
- FR-4.3: Contacts shall be organizable into a hierarchical Contact Group structure.
- FR-4.4: A Contact's detail view shall show its running ledger balance (opening balance, period debits/credits, closing balance) and recent transactions, sourced live from Accounting — without requiring the user to separately run a report.
- FR-4.5: A Contact shall support attached sub-contacts ("Contact Personnel"), free-form tasks, file attachments, and a communication/activity log (comments, activity events, SMS history, email logs).
- FR-4.6: From a Contact's own screen, an authorized user shall be able to directly launch Record Payment, Create Invoice, Create Quotation, or Create Sales Order pre-filled with that contact.
- FR-4.7: The system shall provide a sales-pipeline (Deals) feature: each Deal linked to a Contact, with a configurable Stage, expected revenue, expected closing date, assignable to one or more team members, and lifecycle status (Pending/Won/Lost).
- FR-4.8: The system shall provide a credit-based SMS marketing capability: reusable templates with merge fields, send-to-audience (all contacts, a Contact Group, or a custom selection), and a history/credit-usage log.

### 6.5 Sales
- FR-5.1: A user with Create rights shall be able to create a Quotation for a Customer, with one or more line items (product/service, quantity, unit, rate, discount, tax rate), multi-currency support, and tenant-defined custom fields.
- FR-5.2: A Quotation, and every other transactional document type, shall be saved as a Draft (with a placeholder identifier) until an authorized user explicitly Approves it, at which point it receives its real, sequential, fiscal-year-aware document number.
- FR-5.3: An Approved Quotation shall offer a one-click "Convert to Invoice" action that pre-fills a new Invoice from the Quotation's data (customer, lines, totals), while keeping the resulting Invoice a fully independent, further-editable document that retains a visible link back to its source Quotation.
- FR-5.4: A Sales Order shall exist as an independently creatable document type (not a mandatory step between Quotation and Invoice), cross-referenceable to a Quotation or Purchase Order via a reference field.
- FR-5.5: An Invoice shall require a Warehouse selection (for tenants tracking inventory) and shall, on Approval, decrement stock for each line's product at that warehouse and generate the corresponding General Ledger postings.
- FR-5.6: If approving an Invoice would take a product's stock below zero at the selected warehouse, the system shall apply the tenant's configured Negative Stock Balance policy (reject the approval, or warn the user with the option to proceed anyway).
- FR-5.7: Every Invoice line shall support a per-line tax selection (e.g. no tax, zero-rated, or the standard VAT rate) and the document's totals (subtotal, non-taxable total, taxable total, tax amount, grand total) shall recalculate immediately as lines or tax selections change.
- FR-5.8: An Invoice shall support marking itself as an export sale, affecting its tax treatment.
- FR-5.9: An authorized user shall be able to issue a Credit Note against a specific Invoice (return/adjustment reducing what the customer owes).
- FR-5.10: An authorized user shall be able to record a Customer Payment, which may be initiated standalone or launched directly from an outstanding Invoice (pre-filled with the amount due and a suggested allocation). The payment form shall show a live preview of its resulting ledger postings before it is saved.
- FR-5.11: A single Customer Payment shall be allocatable — fully or partially, and across one or more outstanding Invoices — with the system defaulting to an oldest-first (FIFO) allocation that the user can override or clear.
- FR-5.12: The system shall provide a dedicated screen listing all unallocated and partially-allocated customer credits (from Payments, Journal Vouchers, or Quick Receipts) so staff can apply "payment on account" money to specific invoices after the fact.
- FR-5.13: Every approved Sales document shall expose its resulting General Ledger entries (account, debit, credit) inline on the document, for transparency without needing a separate report.

### 6.6 Purchasing
- FR-6.1 through FR-6.13 mirror FR-5.1–FR-5.13 exactly, substituting Purchase Order for Quotation, Purchase Bill for Invoice, and Supplier Payment for Customer Payment, with the following purchase-specific additions:
- FR-6.14: A Purchase Bill shall support recording the supplier's own invoice/bill reference number, separate from this system's internal reference number.
- FR-6.15: A Purchase Bill shall support marking itself as an import, capturing country of origin, import date, and customs document number when so marked.
- FR-6.16: A Purchase Bill (and Expense) shall support Nepal TDS (tax-deducted-at-source) calculation: selection of a government-published TDS category, with the corresponding withholding amount computed and posted to a TDS-payable account.
- FR-6.17: A Purchase Bill (or Expense) line shall support classification as Capital or Revenue ("Others") expenditure, required for statutory Annex 13 reporting.
- FR-6.18: The system shall provide a separate "Expense" document type for non-inventory spend, where each line debits a chosen General Ledger account directly rather than referencing a product — distinct from Purchase Bill, which is inventory/product-based and affects stock.
- FR-6.19: An authorized user shall be able to issue a Debit Note against a specific Purchase Bill (return/adjustment reducing what's owed to a supplier).
- FR-6.20: Approving a Purchase Order or Purchase Bill shall never itself trigger a negative-balance check on stock (a Purchase Bill increases stock); the negative-stock policy applies only to stock-decreasing transactions.

### 6.7 Accounting
- FR-7.1: The system shall maintain a hierarchical Chart of Accounts under the five canonical root types (Assets, Liabilities, Equity, Income, Expenses), with tenant-editable account groups (nestable to arbitrary depth) and leaf accounts.
- FR-7.2: An authorized user shall be able to record a manual Journal Voucher: multiple lines, each debiting or crediting a chosen account, with the system preventing approval unless total debits equal total credits.
- FR-7.3: An authorized user shall be able to record a Cash/Bank Transfer between the Organization's own accounts, including a single transfer fanning out to multiple destination accounts in one transaction.
- FR-7.4: The system shall provide simplified "Quick Payment" and "Quick Receipt" entry for one-off cash movements that don't need to be tied to a specific Customer/Supplier record or invoice allocation.
- FR-7.5: The system shall maintain a live, per-account running balance for every bank, cash, and digital-wallet account, viewable as a dashboard summary and as individually manageable account records.
- FR-7.6: The system shall track physical cheques (received and issued), each linked to the payment it belongs to, with a status lifecycle (e.g. pending, cleared, bounced).
- FR-7.7: Every transactional document type that has a financial effect (Invoice, Purchase Bill, Payment, Journal Voucher, Inventory Adjustment, Production Journal, etc.) shall, upon Approval, automatically generate balanced double-entry General Ledger postings — the user shall never need to manually journal the effect of another document.

### 6.8 Inventory & Manufacturing
- FR-8.1: The system shall maintain a Product catalog, each product typed as a physical Good (stock-tracked) or a Service (not stock-tracked), with a category, tax treatment, primary unit of measure, HS (customs) code, selling/purchase prices, and applicable General Ledger account mappings for sales/purchases/returns.
- FR-8.2: A Product shall support one or more secondary units of measure, each with its own conversion rate to the primary unit and its own pricing (e.g. sell by the piece or by the case).
- FR-8.3: The system shall support product variants (e.g. size/color combinations) generated from reusable, tenant-defined attribute definitions, each variant carrying its own SKU, barcode, and pricing.
- FR-8.4: Products shall be organizable into a hierarchical category structure.
- FR-8.5: The system shall value stock using FIFO (first-in-first-out) costing, maintained per product per warehouse.
- FR-8.6: For tenants with Multiple Warehouses enabled, the system shall support transferring stock between named warehouses, and shall support manual stock adjustments (with a monetary value and General Ledger impact) for corrections such as damage or physical count reconciliation.
- FR-8.7: A Product's detail view shall show a running stock position (opening/in/out/balance) and a full transaction-level stock ledger.
- FR-8.8: For tenants with Manufacturing enabled, the system shall support defining a Bill of Materials for a finished good: required raw materials (with consumption ratios), optional by-products (with cost-allocation percentages), and additional production cost terms.
- FR-8.9: The system shall support a two-stage production workflow: a Production Order (an uncosted plan, optionally defaulted from a Bill of Materials) that can be converted into a Production Journal (the actual, costed execution), which on Approval consumes raw-material stock at cost, computes a per-unit cost for the finished good (and any by-products), and creates the corresponding new stock at that computed cost.

### 6.9 Reports
- FR-9.1: The system shall provide standard financial statements — Trial Balance, Balance Sheet, Income Statement (Profit & Loss), and Cash Flow Summary — rendered as an expandable hierarchy matching the Chart of Accounts structure, for any selected date range, with optional period-over-period comparison.
- FR-9.2: The system shall provide Accounts Receivable and Accounts Payable reports: aging summaries (bucketed by days overdue), and full running-balance statements per contact.
- FR-9.3: The system shall provide Sales and Purchase analytics reports (by customer/supplier, by item, monthly rollups, and a full transaction-line-level export) suitable for pivot-table analysis.
- FR-9.4: The system shall provide Nepal statutory tax reports: Sales Register, Purchase Register (each with a "migrated" variant sourced from imported historical data — see FR-2.10), VAT Summary, TDS Report, and the Annex 13 and Annex 5 statutory formats — the Annex 13 report filtered to transactions above a configurable monetary threshold, split by Capital vs. Revenue purchase classification.
- FR-9.5: The system shall provide Inventory reports: stock position, stock ageing, stock movement history, a full inventory ledger, product profitability, and manufacturing-specific summary/variance/planning reports.
- FR-9.6: The system shall provide a System audit report: a full log of every create/update/approve action, filterable by user, action type, and document type, each entry linking to the affected record.
- FR-9.7: The system shall provide computed financial ratio analysis (liquidity, solvency, efficiency, profitability ratios) derived from the same underlying financial-statement data.
- FR-9.8: Every report shall support exporting either the currently-viewed (paginated/filtered) data or the complete underlying dataset, in a downloadable spreadsheet format, and a separate print-formatted output.
- FR-9.9: Reports shall be filterable by a tenant-defined, multi-dimensional "Reporting Tag" system (analogous to cost centers/tracking categories), which the same tags can be attached to at the point of transaction creation.
- FR-9.10: No report shall provide any means of editing underlying data; the only interactive action from a report shall be drilling down into the source transaction or account ledger.

### 6.10 Workflow & Approvals
- FR-10.1: The system shall provide a general-purpose Task feature (title, description, assignee, due date, type, priority) attachable to Contacts, the Organization itself, and (architecturally) any other entity, with its own Pending/Started/Done lifecycle independent of the entity it's attached to.
- FR-10.2: The system shall provide a unified Transaction Approval queue listing every Draft-status document (across all document types the current user is permitted to approve), allowing approval directly from the list without opening each document individually.
- FR-10.3: The system shall provide a document inbox for uploading scanned or photographed source documents (receipts, bills, invoices), which an authorized user can convert directly into a structured transaction of the appropriate type, with AI-assisted extraction that pre-fills the transaction's fields from the document image for common types (e.g. Invoice, Purchase Bill, Expense, Quick Payment).

### 6.11 Notifications & Templates
- FR-11.1: An authorized user shall be able to schedule recurring email alerts (e.g. a daily transaction summary, a CRM activity report) sent to specified recipients on a configurable schedule.
- FR-11.2: An authorized user shall be able to manage a library of print/PDF layout templates per document type, selecting one as the tenant's default for that document type.
- FR-11.3: An authorized user shall be able to manage merge-field-based text templates for balance confirmation letters, terms & conditions, and emails, reusable across the system.

### 6.12 Configuration & Extensibility
- FR-12.1: The system shall allow an authorized user to define custom fields (text, number, description, or multi-choice), each specifying which document types it should appear on, without requiring a code change.
- FR-12.2: The system shall allow an authorized user to configure custom status/stage pipelines per document type (e.g. Sales Order stage, Deal stage), independent of that document's underlying Draft/Approved lifecycle status.
- FR-12.3: The system shall allow an authorized user to configure document-numbering behavior per document type: prefix, starting/next number, automatic vs. manual numbering, whether the counter resets each fiscal year, and whether the fiscal year is appended to the generated code.
- FR-12.4: The system shall allow an authorized user to manage tenant-wide lookup lists referenced throughout the system: Credit Terms, Cost Terms, Payment Modes, and TDS categories (the latter maintained as government-published reference data).

---

## 7. Non-functional requirements

### 7.1 Localization & regional conventions
- NFR-1.1: Every date-bearing field shall support display and entry in both the Gregorian (AD) and Bikram Sambat (BS) calendars, switchable per user preference.
- NFR-1.2: Monetary figures shall be formatted using the Nepali/Indian digit-grouping convention (lakh/crore comma placement) wherever displayed.
- NFR-1.3: The system's default currency shall be Nepalese Rupee, with full multi-currency support (per-transaction exchange rate, tenant-managed currency list) for tenants that enable it.
- NFR-1.4: All statutory tax logic (VAT rate, TDS categories/rates, Annex 13 threshold) shall be configurable reference data, not hardcoded, to accommodate annual changes to Nepali tax schedules.

### 7.2 Multi-tenancy & data isolation
- NFR-2.1: A user's data shall never be visible to, or actionable by, a user of a different Organization, enforced at the data-access layer, not only the UI.
- NFR-2.2: A single user account shall be able to hold different roles/permissions in different Organizations they belong to.

### 7.3 Security & auditability
- NFR-3.1: All authentication shall require email verification before an account can transact.
- NFR-3.2: Passwords shall never be stored or transmitted in plain text.
- NFR-3.3: Every state-changing action shall be attributable to an authenticated user and timestamped, and this audit trail shall not be editable or deletable through normal application use.
- NFR-3.4: Financial documents dated before an Organization's configured lock date shall be immutable through normal application use, protecting closed accounting periods from retroactive tampering.

### 7.4 Reliability & data integrity
- NFR-4.1: The system shall never allow an unbalanced (debits ≠ credits) entry to post to the General Ledger.
- NFR-4.2: The system shall never assign the same document number twice within a tenant/document-type/fiscal-year combination, even under concurrent approvals by different users.
- NFR-4.3: Long-running operations (bulk import, full-tenant backup/export, large report exports) shall run asynchronously and not block the initiating user's session, with the user notified on completion.

### 7.5 Performance & scalability
- NFR-5.1: List and report screens shall remain responsive (paginated, not loading full datasets client-side) for tenants with tens of thousands of transactions/products/contacts.
- NFR-5.2: Line-item tax/total recalculation on transaction entry forms shall feel instantaneous (no perceptible network round-trip) to the user.

### 7.6 Usability & accessibility
- NFR-6.1: Every list screen, record-detail screen, and transaction-entry screen shall follow one consistent interaction pattern across all modules (search, pagination, filtering, row actions), so a user who learns one module's UI can operate any other module's equivalent screens without relearning the pattern.
- NFR-6.2: The application shall meet WCAG 2.1 AA accessibility standards for color contrast, keyboard navigation, and screen-reader compatibility.

### 7.7 Extensibility
- NFR-7.1: Adding a new optional module (e.g. a future POS front-end) shall not require restructuring existing tenant data — the permission model, location model, and feature-flag model shall anticipate this from v1.

---

## 8. Assumptions & constraints
- The initial target market is Nepal-based SMEs; internationalization beyond Nepal-specific compliance (VAT/TDS/Annex reports, AD/BS calendar) is not assumed for v1, though multi-currency support means non-NPR transactions are usable.
- The reference product researched for this PRD (a live commercial Tigg ERP tenant) is not being cloned pixel-for-pixel; its confirmed *behaviors* (draft/approve lifecycle, document conversion, tax calculation, permission model) are the requirement baseline, and this team is free to improve on its UI/UX where it was found lacking (e.g. normalizing the minor post-approval-modal inconsistencies noted in the research).
- Government-published reference data (TDS categories, VAT rate) will need a maintenance process to stay current with Nepali fiscal-year tax-schedule changes; this PRD assumes that data is tenant-visible but centrally maintained by the product team, not tenant-editable.
- Technical implementation (stack, architecture, build sequencing) is governed by `architecture-spec.md` and `roadmap.md`, not repeated here.

## 9. Open questions
- Should Manufacturing (BOM/Production Order/Production Journal) ship in v1, or be deferred as its own phase? (Flagged identically in `roadmap.md`.)
- What is the actual/desired default free-trial length and post-trial pricing model? (Referenced product defaults to a 15-day trial; not necessarily the right default for this product.)
- Is IRD e-filing integration a committed near-term goal, or purely aspirational? This affects whether `TenantSubscription`-style entitlement infrastructure needs to be built early.
- Should the Capital-vs-Revenue purchase-expenditure classification (FR-6.17) be a required field on every Purchase Bill/Expense line, or optional-with-a-default — the reference product's own UI location for this was never definitively found during research, so its real-world data-entry burden is unconfirmed.

---

## 10. Glossary
- **AD** — Gregorian ("Anno Domini") calendar.
- **BS** — Bikram Sambat, the Nepali calendar in official/common use.
- **TDS** — Tax Deducted at Source, Nepal's withholding-tax mechanism on specified purchase categories.
- **VAT** — Value Added Tax (Nepal's standard rate referenced throughout is 13%).
- **Annex 13 / Annex 5** — statutory report formats required by Nepal's Inland Revenue Department (IRD).
- **PAN** — Permanent Account Number, Nepal's tax-identification number for individuals/businesses.
- **FIFO** — First-In-First-Out, the stock costing method used throughout.
- **Maker-checker** — a control pattern where the person who creates/saves a transaction is not, by default, the same person who approves/finalizes it.
- **Draft → Approve** — the universal two-step lifecycle every transactional document follows before it affects the ledger or stock.

---

*References: `erp-module-scan.md` (the underlying live-product research this PRD is derived from, including the specific UI behaviors, field lists, and live-tested validation rules cited throughout §6), `architecture-spec.md` (technical/domain design), `roadmap.md` (phased build plan).*
