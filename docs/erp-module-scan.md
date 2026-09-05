# Moonbeam ERP (Tigg) — Module Scan

Source: https://moonbeamtradingandsuppliers.tigguat.com/erp/#/ (a tenant instance of the "Tigg" ERP/POS/CRM+Accounting platform, tigg.app). Logged in with demo credentials. This doc is built incrementally, module by module, to spec out a similar Clean-Architecture (.NET LTS + Angular LTS, CQRS) application.

**Scope decision (confirmed with the user 2026-08-07):** the rebuild targets the **ERP/back-office only** for now. The platform also includes POS Restaurant and POS Retail point-of-sale front ends (discovered via the permission system and confirmed as actual Billing Location types under Organization > Features — see below), but those are explicitly deferred to a later phase and are documented here only where they surfaced incidentally.

**Note on tenants**: from the Configurations section onward, scanning switched to a second tenant, https://abcagro.tigg.app/erp/#/ ("Abc Agro Trading"), because the original Moonbeam UAT tenant's demo user lacked permission to view Users & Permissions. Both tenants run the same Tigg platform/UI; findings below apply generally unless a tenant is called out specifically. The hands-on transaction-creation pass (below) was performed back on the Moonbeam tenant, since Abc Agro's product catalog turned out to be empty at that point in the session.

---

## Signup & Onboarding

Tigg splits identity from tenancy: signup/login happens once, centrally, at the platform's root domain (**me.tiggapp.com** — separate from any tenant subdomain like `abcagro.tigg.app` or `moonbeamtradingandsuppliers.tigguat.com`), and a single logged-in account can then belong to **multiple Organizations** (create your own, request to join one, or accept an invite to one) — a model much like Slack workspaces. This is a foundational architectural finding: the rebuild needs a tenant-agnostic **Identity/Account** bounded context plus a separate **Organization/Tenant** context, joined by membership.

### 1. Registration — `me.tiggapp.com/erp/#/register`
"Create New Account" form: **Full Name\***, **Email\***, **Phone Number\***, **Password\*** + a second masked confirm-password field (both with show/hide toggles), a **Cloudflare Turnstile** bot-check widget (must resolve to green "Success" before the form can submit), and a required **"I agree to Tigg's Term of Service and Privacy Policy"** checkbox. Page subtitle: *"Let's get started with your 15 days free trial"* — confirms the default onboarding plan is a 15-day free trial (ties to Configurations > Tigg Subscriptions found earlier in this doc). "Create Account" stays disabled until every required field + the captcha + the checkbox all pass. Footer: "Already a user? Log in".

### 2. Email verification
Per the user's own first-hand account-creation experience: after submitting Create Account, a verification code is emailed to the address entered, and the user enters that code back into the app to confirm the address (standard OTP-style email verification) before proceeding. *(Not independently reproduced in this session — creating a real account/using a real verification code is an action I don't perform on the user's behalf per policy; documented here from the user's direct report. If a screenshot of this step is shared later, this section should be updated with the exact field/screen details.)*

### 3. Login — `me.tiggapp.com/erp/#/`
Returning users log in centrally at `me.tiggapp.com`, not at any tenant subdomain: Email + Password fields, "Forgot Password?" link, "Log in" button, and "Need an account? Sign Up" for new users. The subdomain shown under the "Sign in to Tigg" heading (e.g. "me.tiggapp.com") confirms this is the identity host, distinct from a tenant's own subdomain reached only after selecting an Organization.

### 4. Organization List (post-login landing page) — `me.tiggapp.com/erp/#/`
Three tabs, confirming the membership model described above:
- **Your Organization** — for a brand-new account: an illustrated empty state ("Create Your First Organization — Let's create your first organization together") plus an "Add New Organization" button; once Organizations exist they're listed here (list/grid view toggle present, plus a search box), alongside an always-visible "ADD NEW ORGANIZATION" button top-right.
- **Requests** — outgoing requests to join an existing Organization (empty state: "No Pending Requests").
- **Invitation** — incoming invitations from other Organizations' admins (empty state: "No Pending Invitations!") — this is the receiving end of the Users & Permissions "+ INVITE USER" flow documented later in this doc: an invited user's invite lands here to accept, rather than the user being auto-added to that Organization.

### 5. New Organization wizard — `me.tiggapp.com/erp/#/new-company` (3 steps)

**Step 1 — Set Up Your Organization**
- **Organization Name\*** (text)
- **Industry\*** — a searchable single-select combobox, alphabetically sorted, seeded with an extensive catalog (~70 entries, heavily oriented toward Nepali SME sectors) ending in a catch-all **"Other"**.
- **Organization Address** (optional)
- **Company Logo** upload (optional; min 300×300px, max 5MB, JPG/PNG/GIF)
- **Accounting Start Date\*** — a **BS (Bikram Sambat)** date picker, DD-MM-YYYY format — confirms the fiscal go-live date is captured at org-creation time and feeds directly into Opening Balances.
- **Registered with VAT?\*** — Yes/No dropdown, becoming the org-level VAT flag later surfaced on Organization > Overview.
- **Workspace Name\*** — the subdomain slug, rendered as `[input].tigg.app`, with **live async availability checking**: typing triggers a debounced "Checking duplicate slug ⟳" indicator, resolving to a green **"Congratulations! The workspace name X is available. Click on the Next button to proceed."** confirmation. This becomes the tenant's actual subdomain — a uniqueness-checked reservation, ideally implemented as its own idempotent "check availability" query independent of the final create-organization command.
- Collapsible **"Add more organization info"**: Email, Organization Phone Number, PAN Number, Website — all optional.
- Collapsible **"Have a referral code?"**: a code field + "Apply" button.
- Required **"I agree to Tigg's Terms of Service and Privacy Policy"** checkbox before Next enables. A second Cloudflare Turnstile check also appears on this step.

**Step 2 — Accounting Features**
An opt-in feature-selection screen — checkbox cards, each with a name and a plain-English description, none pre-checked, all can be "enabled later":
- **Track Inventory**, **Multiple Locations**, **Enable Manufacturing**, **Multiple Warehouses**, **Point of Sale (Retail)**, **Multi-Currency**, **Point of Sale (Restaurant)**.

"Save and Continue" (+ "Back"). This step is the direct origin of every feature toggle later found under Configurations > Organization > Features (Billing Location, Multiple Warehouse, Multiple Currency), the manufacturing sub-module, and the POS location scopes seen in Role Reference — confirming all of these are opt-in choices made once at org creation, not always-on.

**Step 3 — Review Your Organization**
A read-only confirmation screen: Organization Details (Name, Workspace URL, Industry, Address, Accounting Start Date, VAT Registered), Selected Features (or "No additional features selected - you can always enable them later"), another Cloudflare Turnstile check, and **"Confirm & Submit"** (+ "Back").

### 6. Post-creation
A one-time celebratory **"Welcome to Tigg 🎉"** modal with an "Add New Organization" call-to-action, then the user lands back on the Organization List, now showing the newly created Organization as an entry to click into.

### Signup & Onboarding — architectural implications
- Confirms a clean two-bounded-context split for the rebuild: an **Identity/Account** context (signup, email verification, login, password reset — tenant-agnostic, lives at a root/identity host) and an **Organization/Tenant** context (Organization, BillingLocation, Warehouse, Currency, Subscription). A join entity, `OrganizationMembership { userId, organizationId, roleId, status(Requested/Invited/Accepted) }`, ties the two together and directly backs the Your Organization / Requests / Invitation tabs.
- Org creation seeds several things in one shot: the Subscription record (15-day trial default), the org's VAT-registered flag and Accounting Start Date, and the initial Accounting Features selections. In the rebuild, these should be set once at creation via a single `CreateOrganizationCommand`, not left as ambient settings discoverable only through separate screens.
- Confirms the invite-only user-provisioning model is symmetric with a request-to-join / invite-to-join model at the Organization level itself.
- The Workspace Name uniqueness check is a good template for other "reserve a unique identifier live" needs in this codebase.

---

Left sidebar (global nav, present on every screen): **Home, CRM, Workflow, Sales, Purchase, Accounting, Inventory, Reports, Configurations**, plus a **Create New** button pinned above the nav and a company switcher ("Change Company") pinned at the bottom. Each top-level nav item with sub-modules expands inline (accordion) into an indented list of sub-pages when clicked.

Top bar (global, present on every screen): logo, History/Browse icon, global search bar, Support link, date-range filter (global period filter that scopes dashboard figures), and a profile avatar dropdown (email, My Tigg Profile, Help, **Calendar format toggle AD/BS**, Logout).

List-page chrome pattern (repeats across nearly every module): breadcrumb-style header, search box, pager, view-toggle + OPTIONS link, "+ ADD NEW" green button, sortable/filterable column headers, row-level "⋮" action menu.

Record-detail-page chrome pattern (seen on Contact detail; reused for Customer/Supplier/Deal/Product/BOM/Production etc.): left mini-profile panel with vertical tab list, right content pane, top-right "OPTION ⋮" menu, universal **Activity** tab pattern (comment box + Comments/Activities/SMS History/Email Logs sub-tabs).

## Create New (global quick-create menu)
4-column flyout: **General** (Customer, Supplier, Products, Accounts, Accounts Group), **Sales** (Quotation, Sales Order, Invoice, Customer Payment, Credit Note), **Purchase** (Purchase Order, Purchase Bill, Expenses, Supplier Payment, Debit Note), **Accounting** (Journal Voucher, Cash Transfer, Quick Payment, Quick Receipt).

## Home Tab (dashboard)
Sections: Quick Links (personalizable shortcut tray, per-user), date sub-filter, **KPI cards row** (Sales, Purchase, Receipt, Payment — each with % change vs prior period), **Bank and Cash Balance** (every bank/cash/wallet account with running balance, Total Balance row), **Transactions** (unified recent-activity feed, tab filters All/Sales/Purchase/Payment/Receipt).

Data model implications: multi-tenant/multi-company (company switcher); fiscal-year-aware numbering with dual AD/BS calendar support; core transactional documents each with own auto-incrementing number series; Chart of Accounts includes Bank/Cash/Wallet sub-types with running balances; user-level personalization; global activity/history log.

---

## CRM Module
Sub-modules: **Deals, Contacts, Contact Group, SMS**.

### 1. Deals
Pipeline tracker. 3 status tabs: Pending/Won/Lost. Columns: Closing Date, Created At, Details, Stage (inline dropdown), Contact, Expected Revenue, Assigned To (multi-avatar). New Deal form: Deal Contact*, Title*, Assign To (multi-user), Lead Source, Description, Expected Revenue, Expected Closing Date, "Make this deal private" toggle.
Data model: Deal { id, contactId, title, assignees[], leadSource, description, expectedRevenue, expectedClosingDate, stage, status(Pending/Won/Lost), isPrivate, closingDate, createdAt }.

### 2. Contacts
Master directory unifying Customers, Suppliers, Leads with a `Type` discriminator. 342 total contacts vs 225 filtered "Customer" — confirms Contacts is the superset. New Contact modal: Type* (radio tabs), Name*, Address, Code, PAN, Phone, Group, "+ Add More Details".
Contact detail tabs: Overview (Opening Balance, DR/CR, Closing Balance, Recent Transactions, "View Full Statement"), Contact Personnel, Tasks, Deals, Documents, Activity (Comments/Activities/SMS History/Email Logs). OPTION menu: Edit, Make Inactive, Send SMS, Record Payment, Create Invoice, Create Quotation, Create Sales Order.
Data model: Contact { id, type(Customer/Supplier/Lead), name, address, code, pan, phone, email, groupId, organisationId, isActive, openingBalance } + ContactPersonnel, Task, Document, Comment/Activity/SmsLog/EmailLog children, computed running ledger balance from Accounting.

### 3. Contact Group
Hierarchical grouping. Columns: Name, Parent (self-referencing FK). Data model: ContactGroup { id, name, parentGroupId }.

### 4. SMS
Credit-based bulk SMS. Tabs: Overview, SMS History, Templates (merge-field `$[placeholder]$` syntax), Credit History. Data model: SmsCampaign/SmsLog, SmsTemplate, SmsCreditLedger.

---

## Workflow Module
Sub-modules: **Tasks, Document, Transaction Approval**.

### 1. Tasks
General-purpose task manager. 3 status tabs: Pending/Started/Done. Columns: Due, Created At, Title, Type, Priority, Created By, Assigned To, per-row complete checkmark. Confirms Task is a universal polymorphic child entity (attaches to Contact, Organization, presumably others).
Data model: Task { id, title, description, assignedToUserId, dueDate, type(FollowUp/Notify/Email/Other), priority(Normal/Urgent), status(Pending/Started/Done), isPrivate, createdByUserId, createdAt, linkedContactId? }.

### 2. Document
Inbound receipt/document inbox with AI-assisted OCR extraction. Drag-and-drop upload. Tabs: Pending/Done. "+ ADD AS" converts an uploaded doc directly into any of: Quick Payment✨, Customer Payment, Invoice✨, Expenses✨, Supplier Payment, Purchase Bill✨, Quick Receipt, Credit Note, Cash Transfer, Debit Note, Journal Voucher, Purchase Order, Sales Order, Quotation, Warehouse Transfer, Inventory Adjustment (✨ = AI-assisted extraction).
Data model: UploadedDocument { id, fileUrl, fileType, description, label, uploadedByUserId, uploadedAt, status(Pending/Done), aiExtractionStatus?, linkedTransactionId?, linkedTransactionType? }.

### 3. Transaction Approval
Maker-checker queue: any draft transaction (across nearly all types) lands here for a second person to approve. Columns: Txn Date, Saved Date, Txn Type, Description, Entry No (shows "DRAFT" until approved), Saved By, Amount, per-row approve checkmark. 49 pending items observed — heavily used control feature.
Data model: every transactional aggregate needs `Status` including at least Draft → PendingApproval → Approved/Posted, plus an approval audit trail. Strong argument for a shared `ApprovableTransaction` base with a single generic Transaction Approval read-model query unioning across all types.

---

## Sales Module
Sub-modules: **Quotations, Sales Orders, Invoice, Credit Notes, Customer Payment, Customers, Allocate Customer Payment**.

### 1. Quotations
Approved/Draft tabs. Columns: Customer, Quote No, Date, Amount, Expiry Date, Stage.
New Quotation full-page form (template reused for Sales Order/Invoice/Purchase Bill/etc.): Customer Name*, Code (auto DRAFT), Date*, Expiry Date*, Credit Terms, Currency + Exchange Rate To NPR*, line items table (product search-select, Qty/Rate/Discount/Tax/Amount per line), Custom Fields section (tenant-configurable dynamic fields), Terms and Conditions, Reporting Tags.
Data model: Quotation { id, customerId, code, date, expiryDate, creditTerms, currency, exchangeRate, lines[], customFieldValues{}, termsAndConditions, reportingTags[], stage, status(Draft/Approved) }; QuotationLine { productId, qty, unit, rate, discount, taxRateId, amount }.

### 2. Sales Orders
Same pattern. Columns: Customer, Order No, **Reference No** (free text), Date, Amount, Delivery Date, Stage. **Confirmed live**: an approved Quotation's only system-offered conversion target is Invoice — Sales Order is standalone, cross-referenced only via free-text Reference No, not a mandatory intermediate step.

### 3. Invoice
Core sales document. Detail page: left panel (number, status badge, customer, total, Reporting Tags, tabs Overview/Tasks/Documents/Activity); Details panel (Customer Name, Reference No, Invoice Code, Invoice Date, Due Date, **Is Export** Yes/No, **Warehouse\*** required — confirmed live, dropdown Kathmandu/Patan/Lalitpur, only appears once a document affects stock); line items table (Product, Qty+unit, Rate, Discount, **Tax** — confirmed 3-option dropdown No Vat/0 Vat/13% Vat, Amount); Notes; **Totals panel** (Sub Total, Discount%, Non-Taxable Total, Taxable Total, VAT, Grand Total — confirmed live synchronous recalculation, e.g. Rate 1500×Qty1→Taxable 1500, VAT 195, Grand Total 1695); **Allocations section** (payments applied); **GL Transactions section** (Account/Debit/Credit — proves each doc drives ledger postings viewable inline); OPTION menu (Edit, Make Duplicate, Void, **Create Credit Note**, Print).
Data model: Invoice { id, customerId, code, referenceNo, invoiceDate, dueDate, isExport, warehouseId, lines[], notes, subTotal, discountPct, nonTaxableTotal, taxableTotal, vatTotal, grandTotal, status(Draft/Approved/Void), reportingTags[] }; InvoiceLine { productId, qty, unit, rate, discountPct, taxRateId, amount }; PaymentAllocation { invoiceId, paymentId, amount }; GlJournalEntry { transactionType, transactionId, accountId, debit, credit } generated on approval.

### 4. Credit Notes
Approved/Draft tabs. Columns: Customer, Note No, **Reference No** (original Invoice No or free-text). Always tied back to a specific Invoice.

### 5. Customer Payment
"Receipts" — money received. Columns: Received From, Entry No, Reference, Date, Amount, **Deposited To** (bank/cash/wallet account).

### 6. Customers
Contacts table filtered to `Type = Customer`.

### 7. Allocate Customer Payment
Payment-allocation workbench. Tabs: Unallocated/Allocated. Columns: Type (Journal Voucher, Customer Payment, Quick Receipt), Date, Entry No, Customer, Amount, **Allocated**, **Balance**. 92 rows observed — "payment on account" is a common real-world flow.
Data model: generic `PaymentAllocation` join keyed by (sourceType, sourceId, targetType, targetId, amount) spanning multiple source transaction types against Invoices.

---

## Purchase Module
Sub-modules: **Purchase Order, Purchase Bills, Expenses, Supplier Payment, Debit Notes, Suppliers, Allocate Supplier Payments**.

### 1. Purchase Order
Mirror of Sales Order. **Confirmed live**: approved PO offers exactly one conversion target, "Convert to Bill" — single hop, no intermediate document. No stock validation on PO approval (expected).

### 2. Purchase Bills
Mirror of Invoice. Additions: yellow "You can record this payment" banner; **Supplier Invoice Reference** (supplier's own bill number); **Is Import** (Yes/No); **Warehouse\*** required (confirms stock increment on approval); **TDS Details section** (TDS Account, TDS Type, TDS Amount); **Import Details section** (Country, Date, Document No, shown when Is Import=Yes); Payments section; GL Transactions (confirmed live: approving a Supplier Payment posts Debit [Supplier] / Credit [Bank/Cash] — mirror of Customer Payment's Debit [Bank] / Credit [Customer]). Post-approval modal narrower than Invoice (Add New/Print only, no Approve & New/Send Mail).
Data model: PurchaseBill extends Invoice shape + supplierInvoiceReference, isImport, importCountry, importDate, importDocumentNo, tdsAccountId, tdsTypeId, tdsAmount, warehouseId.

### 3. Expenses
Separate document type from Purchase Bills for non-inventory spend. New Expense form has an **"Accounts"** line-item table (Select Account, Amount, Tax) instead of Product lines — each line debits a GL account directly. Also: Supplier Name*, Supplier Invoice Reference No, Date*, Due Date*, Currency+Exchange Rate, Notes, totals panel, **"TDS is applicable"** toggle, Custom Fields.
Data model: Expense { id, supplierId, code, supplierInvoiceReference, date, dueDate, currency, exchangeRate, lines[] {accountId, amount, taxRateId}, notes, totals, tdsApplicable, tdsDetails? }.

### 4. Supplier Payment
Mirror of Customer Payment. Columns: Paid To, Entry No, Reference, Date, Amount, **Paid From**.

### 5. Debit Notes
Mirror of Credit Notes, issued against a Purchase Bill.

### 6. Suppliers
Contacts filtered to `Type = Supplier`.

### 7. Allocate Supplier Payments
Mirror of Allocate Customer Payment. Type column includes more variety: Production Journal, Journal Voucher, Debit Note, Supplier Payment, Expense, Quick Payment, even Invoice (a Contact can be both customer and supplier). Reinforces PaymentAllocation as a generic polymorphic join.

---

## Accounting Module
Sub-modules: **Journal Voucher, Cash Transfers, Quick Payment, Quick Receipt, Charts Of Account, Bank Accounts, Cheque Register**.

### 1. Journal Voucher
Manual double-entry entry — the most fundamental accounting primitive. New form: #JV (draft code), Date*, Reference, Currency+Exchange Rate*, multi-line Accounts table with per-line **DR Amount**/**CR Amount** columns, live Total row for both, and a **"Difference: Rs. 0"** balancing indicator.
Data model: JournalVoucher { id, code, date, reference, currency, exchangeRate, lines[] }; JournalVoucherLine { accountId, debit, credit }, invariant sum(debit)==sum(credit) — canonical example for the whole GL posting engine.

### 2. Cash Transfers
Inter-account transfers. **Confirmed live**: Transfer From Account* (single picker), Date*, #Transfer, Reference, Currency+Exchange Rate*, multi-row "Transferred To" table (account+amount per row, fan-out to multiple destinations in one transaction), running Total, Note, Custom Fields.
Data model: CashTransfer { id, code, date, reference, fromAccountId, toAccountId, amount } — internally a balanced multi-line GL journal entry.

### 3. Quick Payment / 4. Quick Receipt
Simplified entry not requiring a specific Customer/Supplier link. Same column shape as Customer/Supplier Payment. Likely same underlying Payment/Receipt aggregate, lighter creation form, no mandatory allocation.

### 5. Charts Of Account
159 accounts / 87 groups. 5 canonical root types: Assets, Liability, Equity, Income, Expenses (Tree View). Two tabs: Accounts (Account Code, Name, Account Type, Parent Group), Groups (self-referencing tree). Account codes follow a prefix convention (DE=Direct Expenses, DI=Direct Income, IE=Indirect Expenses, CL=Current Liability).
Data model: AccountGroup { id, name, type(Asset/Liability/Equity/Income/Expense), parentGroupId } self-referencing tree; Account (leaf) { id, code, name, type, groupId, isActive }.

### 6. Bank Accounts
Card-grid view of every bank/cash/wallet account with running balance. Tabs: All/Inactive.
Data model: BankAccount likely a specialized subtype/flag on Account.

### 7. Cheque Register
Physical-cheque tracking. Dashboard tab (period + customer/supplier filter, Cheque Received/Issued counters, combined Cheque Lists table). Tabs: Cheque Received, Cheque Issued.
Data model: Cheque { id, chequeNo, direction(Received/Issued), accountId, bankId, amount, date, status(Pending/Cleared/Bounced?), linkedPaymentId }.

---

## Inventory Module
Sub-modules: **Products, Variant Products, Variant Attributes, Product Category, Units Of Measurement, Warehouse Transfer, Inventory Adjustment, Bills Of Materials, Production Order, Production Journal**.

### 1. Products
335 goods. Tabs: Goods/Service. New Product form: Type toggle, Name*, Code (auto), Category*, Tax, Primary Unit*, **HS Code**, "Available For Sale" toggle, "+Add More Details".
Product detail: Selling/Purchase Price, Tax, Primary Unit, **Valuation Method: FIFO** confirmed, Sales/Sales Return/Purchase/Purchase Return Account mappings, Overview panel (Opening/In/Out/Balance Quantity), **Secondary Unit** table (multi-UOM with own conversion rate + pricing), Recent Transactions + "View Inventory Ledger" link.
**Full JSON shape confirmed live** (via `form_data` conversion payload): `{ id, inactive, created_by_id, created_at, name, name_lower, description, type, code, hs_code, selling_price, purchase_price, product_category_id, tax, primary_unit_id, sales_account_id, purchase_account_id, sales_return_account_id, purchase_return_account_id, valuation_method, re_order_level, track_inventory, is_variant, available_for_sale, variant_count, secondary_units[] {...}, service_charge_applicable, barcodes, print_profile_id, print_profile{...}, marketplace_skus[], sku_id }`.
Data model: Product { id, type(Goods/Service), name, code, categoryId, taxRateId, primaryUnitId, hsCode, availableForSale, sellingPrice, purchasePrice, valuationMethod(FIFO), salesAccountId, salesReturnAccountId, purchaseAccountId, purchaseReturnAccountId, reOrderLevel, trackInventory(bool), printProfileId? }; ProductSecondaryUnit { productId, unitId, conversionRate, sellingPrice, purchasePrice }.

### 2. Variant Products
Attribute/variant system layered on Product. Detail page: "Attributes Used" section, **Variant Details** table (each variant with own SKU/Barcode/Name/Selling/Purchase Price).
Data model: VariantProduct (Product subtype) { id, baseProductFields, attributesUsed[] }; ProductVariant { id, variantProductId, sku, barcode, name, sellingPrice, purchasePrice, attributeValueCombination{} }.

### 3. Variant Attributes
Reusable attribute-definition catalog. 11 attributes (size, Color, RAM, ROM, Screen Size, GRAPHICS, etc).
Data model: VariantAttribute { id, name, options[] }.

### 4. Product Category
Hierarchical, 76 categories. Data model: ProductCategory { id, name, parentCategoryId }.

### 5. Units Of Measurement
28 units (Bag/BG, Kilogram/kgs, Litre/Ltr, etc). Flat lookup: UnitOfMeasurement { id, name, shortName }.

### 6. Warehouse Transfer
Moves stock between named locations (Patan, Kathmandu, Lalitpur — confirmed as the exact warehouse set required on Invoice/Purchase Bill in the hands-on pass). Approved/Draft tabs.
Data model: Warehouse { id, name }; WarehouseTransfer { id, code, date, reference, sourceWarehouseId, destinationWarehouseId, lines[] (product, qty) } — decrements FIFO layers at source, creates new layers at destination, no GL impact.

### 7. Inventory Adjustment
Manual stock correction. List shows an **Amount** column (unlike Warehouse Transfer) — implies direct costing/GL impact. Format uses lakh/crore digit grouping (e.g. "2,13,75,000").
Data model: InventoryAdjustment { id, code, date, reference, lines[] (product, warehouse, qtyDelta, unitCost), totalAmount }.

### 8. Bills Of Materials (BOM)
Master-data "recipe" for manufacturing. 23 BOMs. Detail page: Details (Product, Output Quantity, "Manufacture on every sales" flag, Notes), **Raw Materials (Input)** table (Product, Qty, Qty/Unit), **By Product (Output)** table (Product, % of Cost, Qty, Qty/Unit), **Expense** table (Expense Term, Amount, Amount/Unit).
Data model: BillOfMaterials { id, finishedProductId, outputQuantity, outputUnit, manufactureOnEverySale(bool), notes, rawMaterials[] {productId, qty, qtyPerOutputUnit}, byProducts[] {productId, costAllocationPct, qty, qtyPerOutputUnit}, expenseTerms[] {name, amount, amountPerUnit} }.

### 9. Production Order
Planning-stage, uncosted. Status lifecycle independent of Approved/Draft (Planned/InProgress/Completed). Raw Materials table has Qty only (no costing). Banner: "Convert to Production Journal" — confirms Production Order (plan) → Production Journal (executed) chain, mirroring Purchase Order → Purchase Bill.
Data model: ProductionOrder { id, code, date, productId, outputQuantity, reference, notes, status, rawMaterialLines[] {productId, qty}, convertedToProductionJournalId? }.

### 10. Production Journal
Actual costed execution. Raw Materials table fully costed (Product, Qty, Rate, Amount). By Product table costed. Expenses table. **Cost roll-up summary box**: Raw Material Cost, Production Expenses, **Total Cost of Production** (= sum of those two), Cost Allocated to By-product, **Finished Goods Cost** (= Total Cost − By-product allocation), **Cost Per Unit** (= Finished Goods Cost ÷ Output Quantity) — this is the computed FIFO cost layer the finished product enters stock at.
Data model: ProductionJournal { id, code, date, productId, outputQuantity, reference, notes, rawMaterialLines[] {productId, qty, rate, amount}, byProductLines[] {...}, expenseLines[] {term, amount}, rawMaterialCost, productionExpenseCost, totalCostOfProduction, costAllocatedToByProduct, finishedGoodsCost, costPerUnit }. On approval: consumes FIFO layers for raw materials, creates new FIFO layer for finished good at computed cost, likely emits GL Transactions (unconfirmed which accounts — open item).

### Inventory module — cross-cutting notes
- Stock valuation is FIFO throughout.
- Three-stage production chain: BOM (template) → Production Order (planned) → Production Journal (executed/costed) — architecturally analogous to Purchase Order → Purchase Bill / Quotation → Invoice (both confirmed single-hop conversions).
- Multi-warehouse and multi-UOM both need threading through the stock-ledger/FIFO-layer read model.

---

## Reports Module
Landing page: 8 category cards — **Accounting, Receivable, Payable, Sales Report, Purchase Report, Tax Report, Inventory Report, System Report, Analytics Report**.

### Full catalog
- **Accounting**: Transaction list, Journal report, General Ledger Summary, Detail General Ledger, GL Master Report, Trial Balance, Income Statement, Balance Sheet, Cash Flow Summary.
- **Receivable**: Customer Receivable Summary, Customer Ageing Summary, Invoice Age, Customer Statement.
- **Payable**: Supplier Payable Summary, Supplier Ageing Summary, Purchase Bill Age, Supplier Statement.
- **Sales Report**: Sales By Customer, Sales By Item, Sales By Customer (Monthly), Sales By Item (Monthly), Sales Master Report, Sales Summary Report.
- **Purchase Report**: Purchase By Supplier, Purchase By Item, Purchase By Supplier (Monthly), Purchase By Item (Monthly), Purchase Master Report.
- **Tax Report**: Sales Register, Migrated Sales Register, Sales Return Register, Purchase Register, Migrated Purchase Register, Purchase Return Register, VAT Summary Report, TDS Report, Annex 13 Report, Annex 5 Materialised View Report.
- **Inventory Report**: Inventory Position, Inventory Ageing, Inventory Movement, Inventory Ledger, Product Profitability Report, Inventory Master Report, Production Summary Report, Production Variance Report, Production Planning Report.
- **System Report**: Activity Log, User Log.
- **Analytics Report**: Net Trading Assets, Exceptional Report, Ratio Analysis Report.

### Shared report-runner UI pattern
Breadcrumb, OPTION ⋮ menu (Export, Print), Show Filters drawer. Top filter bar: Period (date-range, defaults to global filter) + 0-3 report-specific filters + green GENERATE button (reports don't auto-load). Financial-statement reports render as expandable hierarchical tree matching Chart of Accounts; transactional/register reports render as flat paginated tables. Report Filters drawer: Show Columns checkboxes, View Options (Expand All), Reporting Tags filter block. Export: Range (Current View vs Full List), Expand All, Export Format (Spreadsheet .xlsx). Print is a separate action.

### Reports opened and confirmed in detail
- **Trial Balance** — hierarchical (5 root types → groups → leaf accounts), Opening Dr/Cr, Transaction Dr/Cr, Closing Dr/Cr per account with group subtotals.
- **Balance Sheet** — same hierarchical tree, single Amount column.
- **Income Statement** (URL reads `profit-loss`) — Direct Income → Cost Of Sales (Opening Inventory + Purchases − Closing Inventory) → **Gross Profit/(Loss)** → Indirect Income → Indirect Expenses → **Net Profit/(Loss)**.
- **Customer Ageing Summary** — Account Name, Contact Group, Credit Term, 1-30/31-60/61-90/91+ Days/Total, as of a single date.
- **Customer Statement** — full running-balance ledger per customer/account, Opening Balance row + every transaction row (mixes customer- and supplier-side transactions for contacts playing both roles).
- **Sales Master Report** — denormalized fact table: Contact, Type, Contact Group, Warehouse, Location, Entry No, Reference No, Entry Date, Product Code, Product, Quantity, Rate, Amount, Item Discount, Transaction Discount, Net Sales, Vat Type, Vat Amount, Total Amount.
- **Annex 13 Report** — PAN, Trade Name, Type, Opening Balance, Service Purchase Capital, Service Purchase Others, Goods Purchase Capital, Goods Purchase Others, Service Sales, Goods Sales, Closing Balance, filtered to Amount ≥ threshold (100,000 NPR default). **Capital-vs-Others classification UI location still unresolved** after the hands-on pass — check Account Group edit form in Charts of Account next.
- **Activity Log** — User, Action (CREATE/UPDATE/APPROVE), Log Source (aggregate/document type), Source Code, DateTime. 519 events / ~3 weeks observed — confirms every CREATE/UPDATE/APPROVE command emits an audit event automatically (strong argument for a MediatR pipeline behavior).
- **Ratio Analysis Report** — Liquidity (Current, Quick, Cash Ratio), Solvency (Debt-to-Equity, Debt Ratio), Efficiency (Inventory Turnover, Receivables Turnover, Asset Turnover, AR/AP Days, Inventory Holding Period, Cash Conversion Cycle), Profitability (Gross/Net Profit Margin, ROA, ROE) — all derived from Balance Sheet/Income Statement figures.

### Reports module — cross-cutting notes
- CQRS query side is genuinely distinct from write side — reports never edit data, only drill down into the write-side aggregate view.
- Reporting Tags is a tenant-defined, multi-dimensional slicing mechanism (like Xero tracking categories / QuickBooks classes) — `ReportingTagCategory { id, name }` + `ReportingTagOption { id, categoryId, value }` + many-to-many `TransactionReportingTag`.
- Export pattern (Current View vs Full List) suggests two backend capabilities: paginated/filtered on-screen query + separate unpaginated bulk-export job.

---

## Configurations Module
Left nav: **Apps, Users & Permissions, Import / Export, Opening Balances, Tigg Subscriptions, Organization**.

Apps (nested list, 14 entries): **General, Custom Status, Banks, CRM, Workflow, Credit Terms, Cost Terms, Payment Mode, TDS Type, Reporting Tags, Custom Fields, Printing Templates, Custom Templates, Document Numbering, Alert Scheduler**.

### 1. General
Tenant-wide business-rules policy engine:
- **Suggest Selling Price** — Recent Selling Price vs Fixed Selling Price.
- **Product Price Basis** — Inclusive of VAT vs Exclusive of VAT.
- **Mode of Inventory Tracking** — **Physical Movement** (Delivery Notes/GRN-based) vs **Accounting Movement** (Invoices/Purchase Bills directly) — this single toggle explains why Delivery Note/GRN never appear in Sales/Purchase on Accounting Movement tenants.
- **Negative Cash Balance** — Reject confirmed (with further Warn/Do Nothing options inferred).
- **Negative Stock Balance** — confirmed live via hands-on pass to behave as **Warn-and-allow** (Dismiss/Continue dialog, Continue proceeds with full normal approval).
Data model: TenantSettings { suggestSellingPriceMode, pricingBasis, inventoryTrackingMode(PhysicalMovement/AccountingMovement), negativeCashBalanceAction(Reject/Warn/DoNothing), negativeStockBalanceAction(Reject/Warn/DoNothing), vatAccountId }. **Single most architecturally important setting in the scan** — `inventoryTrackingMode` should gate DeliveryNote/GRN command handlers at the domain layer, not just hide UI.

### 2. Custom Status
Configurable status pipelines per document type (Sales Order, Purchase Order, Quotation, Cheque, Production Order, etc) — ordered, colored, user-editable label lists.
Data model: CustomStatusDefinition { id, documentType, name, color, sortOrder, isActive }.

### 3. Banks
Simple master list of banks (empty in scanned tenant). Data model: Bank { id, name, logoUrl? }.

### 4. CRM (config)
Lead Source and Deal Stages lookup lists. Data model: LeadSource { id, name }, DealStage { id, name, sortOrder, color? }.

### 5. Workflow (config)
Task Types (Follow up, Notify, Email, etc), name+color. Data model: TaskType { id, name, color }.

### 6. Credit Terms
Named payment terms resolving to days (+ early-payment discount rule). Data model: CreditTerm { id, name, days, earlyPaymentDiscountPct?, earlyPaymentDays? }.

### 7. Cost Terms
Two sections: Additional Cost Terms (landed-cost items — Freight, Insurance, Customs Duty) and Production Cost Terms (Expense Term values for BOM/Production Journal). Data model: CostTerm { id, name, category(AdditionalCost/ProductionCost) }.

### 8. Payment Mode
Cash, Cheque, Bank Transfer, eSewa, Khalti, etc. Confirmed optional on payment forms (left blank in hands-on pass). Data model: PaymentMode { id, name, requiresChequeDetails(bool)?, requiresBankAccount(bool)? }.

### 9. TDS Type
Pre-seeded Nepal IRD withholding-tax categories with government revenue codes (e.g. "11111 Individual or Proprietorship Firm"). Data model: TdsType { id, code, name, ratePct? } — likely system-seeded reference data, versioned per fiscal year.

### 10. Reporting Tags
Confirms ReportingTagCategory + Options model. "+ADD NEW REPORTING TAG" form (category name + option list).

### 11. Custom Fields
Generic cross-document custom-field framework. Each definition: Name, **Type** (Text, Number, Description, Choices confirmed; possibly more), checkboxes for which document types it applies to. Confirmed 17 applicable document types: Sales Invoice, Quotation, Sales Order, Credit Note, Customer Payment, Quick Receipt, Purchase Order, Purchase Bill, Expense, Debit Note, Supplier Payment, Quick Payment, Journal Voucher, Cash Transfer, Production Order, Production Journal.
Data model: CustomFieldDefinition { id, name, type, choiceOptions[]?, applicableDocumentTypes[] } + generic CustomFieldValue { fieldDefinitionId, documentType, documentId, value } (EAV-style).

### 12. Printing Templates
Per-document-type gallery of named layouts (Standard, Modern, Traditional, Minimal, Retail, Classic), one active per document type. A per-product `print_profile` override also confirmed live in the Product JSON.
Data model: PrintingTemplate { id, documentType, name, isActive, layoutDefinition }.

### 13. Custom Templates
Merge-style letter/email templates, distinct from Printing Templates. Types: Customer Balance Confirmation, Supplier Balance Confirmation, Terms and Conditions, Email.
Data model: CustomTemplate { id, type, name, body }.

### 14. Document Numbering
The numbering engine. **Re-confirmed live end-to-end during hands-on pass**: every document gets its real code only at Approve; before that shows literal "DRAFT". Per document type: **Prefix**, **Next Number**, **Auto/Manual** mode, **Reset every fiscal year** toggle, **Add fiscal year in code** toggle (produces the "/83-84" suffix). Also: **Enable Location-wise Next Number** toggle. Separate numbering pools for Chart-of-Accounts codes and Contact/Item codes. Prefixes confirmed: INV, PB, JV, Q, SO, PO, CN, DN, PAY, REC, EXP, CTRN, WT, ADJ, PRO, PJ, DO (Delivery Note), GRN (Goods Received Note).
Data model: DocumentNumberingRule { id, documentType, prefix, nextNumber, mode(Auto/Manual), resetEveryFiscalYear(bool), includeFiscalYearInCode(bool), locationWiseNumbering(bool) }.

### 15. Alert Scheduler
Scheduled email alerts. "+ ADD NEW ALERT": Alert Name*, Medium (Email only), Alert Type (Daily Transaction Summary, CRM Report), Recipients*, Schedule (Daily confirmed) + time picker.
Data model: AlertDefinition { id, name, medium, alertType, recipients[], scheduleFrequency, scheduleTime, isActive }.

### 16. Users & Permissions
Three tabs: **Users, Role Reference, Invited Users**.

**Users** — active accounts list (Name+role badge, Phone, Email). Onboarding is purely invite-based via "+ INVITE USER" — no direct create-user-with-password flow.

**Role Reference** — the tenant's role catalog. 5 pre-seeded roles: Accountant, Admin, Purchase, Sales, View Only. Edit Role Reference panel, 6 collapsible permission-group sections:
- **General** — view/create/edit/inactive checkboxes over Contact, Charts of Account, Banks, Products, Dashboard Summary (view-only); separate Document Permission block (Add/Delete/View).
- **Transactions** — core matrix, grouped by module (Sales, Purchase, Accounting, Inventory), document types × **View/Create/Edit/Approve/Void** checkboxes.
- **Settings** — flat list, single checkbox each: APP Configuration, Organisation Configuration View, Organisation Configuration Edit (separate from View), Opening Balance View, Opening Balance Edit, User & Permissions, Import Export, Subscription, Marketplace.
- **Reports** — one checkbox per individual report, same 8 category headers as Reports module.
- **HeadOffice** — second Sales/Inventory matrix scoped to HeadOffice location.
- **POS Restaurant** / **POS Retail** — same matrix shape, resolved to be literal Billing Location entries with a location type, not abstract flags.
Data model: Role { id, name, permissions } — permissions best modeled as `RolePermission { roleId, permissionKey, isGranted }`, permissionKey a stable string (`"HeadOffice.Sales.Invoice.Approve"`), evaluated by one `IAuthorizationBehavior` in the MediatR pipeline (150+ checkboxes observed across just 6 groups on one role — too large for hand-written per-capability policies).

**Invited Users** — pending-invitation queue (not opened in detail, low priority given ERP-only scope).

**+ INVITE USER flow** — Email Address* + Role/Permission* (single-select from Role Reference catalog) → "Send Invitation". Confirms one-role-per-user model (no multi-role/composition observed).
Data model: UserInvitation { id, email, roleId, invitedByUserId, invitedAt, status(Pending/Accepted/Expired), token }; User { id, email, name, phone, roleId, isActive }.

### 17. Import / Export
Bulk import wizard, 2-step: Upload Type (Product, Customer, Supplier, Contact, Account, Product Category, Account Group + Create New vs Update Existing) → Upload file (drag-and-drop, "Download [X] Template" link, async processing warning).
Data model: ImportJob { id, entityType, mode(Create/Update), fileUrl, status(Processing/Completed/Failed), resultSummary? } — background job pattern.

### 18. Opening Balances
Two tabs:
- **Account** — one row per Chart-of-Accounts leaf account, expandable inline entry form: **Location** dropdown, Currency, Conversion Rate, Amount, DR/CR toggle, Reporting Tags.
- **Product** — inventory-side equivalent (Category/Quantity/Rate/Amount).
Data model: OpeningBalanceLine { id, accountId, locationId, currency, conversionRate, amount, drCr, reportingTags[] }; OpeningStockLine { id, productId, locationId, categoryId, quantity, rate, amount } — both "day zero" transactions scoped by fiscal year Accounting Start Date.

### 19. Tigg Subscriptions
Read-only plan summary: Subscription Plan (usage-quota-based, e.g. "Standard (0 Txn, 0 Products)"), Subscription Amount, Expiry Date, entitlement flags **Location Enabled**, **Warehouse Enabled**, **IRD Verified**, **IRD Sync Enabled**.
Data model: TenantSubscription { id, planId, transactionQuota, productQuota, amount, expiryDate, isActive, locationEnabled(bool), warehouseEnabled(bool), irdVerified(bool), irdSyncEnabled(bool) }.

### 20. Organization
Six sub-tabs: **Overview, Tasks, Documents, Features, Migration, Backup**.
- **Overview** — Name*, Display Name*, Email, Pan No, Phone No, Registered Address, Website, Accounting Starting Date, **Vat Registered** checkbox, **Lock Transaction** panel (single Lock Date — period-close control, edited via "EDIT LOCK DATE" as a distinct permission).
- **Tasks** — same universal Task list pattern, scoped to the Organization record itself.
- **Features** — structural/topology config, resolves the POS mystery:
  - **Billing Location** — list with Code/Name/Address/Warehouse: HO—HeadOffice (Main Warehouse), 1002—POS Restaurant, 1003—POS Retail.
  - **Multiple Warehouse** — Name/Phone/Address list.
  - **Multiple Currency** — Code/Name/Symbol list, seeded with standard catalog (NPR, USD, GBP, EUR, CNY, JPY, INR, CAD, CHF, +more).
  Data model: BillingLocation { id, code, name, address, warehouseId, locationType(HeadOffice/Standard/PosRestaurant/PosRetail) }; Warehouse { id, name, phone, address }; Currency { id, code, name, symbol, isActive }.
- **Migration** — legacy tax-register import (Sales Register, Purchase Register with `?type=migration` flag) — distinct from general Import/Export, feeds the "Migrated Sales/Purchase Register" reports.
- **Backup** — full-tenant data export, "BACKUP NOW" + history table (async job pattern).

### Configurations — cross-cutting notes
- Users & Permissions confirms maker-checker Approve flag is per-role, per-document-type, per-location-scope.
- POS Restaurant/POS Retail fully resolved as Billing Location records of a particular type, gated by Tigg Subscriptions entitlement flags. **Rebuild scope: ERP/back-office only**, POS deferred — but domain model should treat "location" (with type/kind) as first-class from day one.
- Multi-branch/multi-location confirmed as first-class, spanning Inventory, Document Numbering, Opening Balances, Users & Permissions.
- **The Annex 13 Capital-vs-Others purchase-expenditure classification remains the one open gap** from the entire scan — not found in Purchase Bill/Expense line detail, Custom Fields, Cost Terms, or Organization/Features, even after the hands-on pass. The Account Group edit form in Charts of Account (never opened) remains the recommended next place to look.

---

## Hands-on Transaction-Creation Pass (live walkthrough, Moonbeam tenant)

Purpose: everything above this section was compiled from static UI observation. This section documents a live, end-to-end walkthrough of both the Sales and Purchase document chains — actually creating, saving, and approving real documents.

### Sales-side chain: Quotation → Invoice → Customer Payment

**1. Quotation** — created via Sales > Quotations > Add New: Customer "aa", product "super liter" (P0411), Qty 1 cyl, Rate 1500, Tax 13% Vat → Taxable Total 1,500, VAT 195, Grand Total 1,695. Saved as DRAFT; post-save modal offered **Approve / Approve & New / Add New / Print** — confirmed two-step Draft→Approve lifecycle live for the first time. Approve assigned real code **Q0009/83-84** only at that point.

**2. Conversion mechanism (major finding)** — approved Quotation showed "Convert to Invoice" banner (no "Convert to Sales Order" offered). Clicking it revealed: **not a server-side "convert" command** — it's a client-side navigation to the target's "Add New" route with `?form_data=<URL-encoded JSON>` carrying the complete source snapshot (full Contact object, all line items with embedded product objects, amounts, currency, conversion rate) plus **`referrer_type: "Quotation"`** and **`referrer_id: <id>`**. The pre-filled form is then reviewed/edited and saved as an independent new document carrying a referrer link. **Architectural implication**: model conversion as "pre-fill a new-document creation command with source data, plus a referrer link field" — not a distinct domain "Convert" command. Confirmed a second time on Purchase Order→Purchase Bill.

**3. Invoice-specific field confirmed** — pre-filled Invoice form required **Warehouse\*** (Kathmandu/Patan/Lalitpur), not present on Quotation — confirms Invoice, unlike Quotation, directly affects stock. Exchange Rate To NPR (required, default 1) present as on every transactional document.

**4. Live tax/VAT recalculation confirmed** — Tax dropdown exactly 3 options (No Vat/0 Vat/13% Vat, identical on Invoice and Purchase Bill). Selecting 13% Vat immediately recalculated Taxable Total/VAT with no perceptible delay — synchronous, likely client-side calculation.

**5. Negative Stock Balance validation (new finding)** — Approving the Invoice surfaced a "Negative Stock Balance" dialog ("super liter (P0411) — 1 Units") with **Dismiss**/**Continue** buttons. Continue proceeded with full normal approval (real code INV0039/83-84, status Approved, "Record Payment" banner appeared). Direct live-confirmed counterpart to Negative Cash Balance's Reject setting, except this one is **Warn-and-allow**. Recommended domain design: command-validation policy step returning a warning the client must acknowledge (Continue) before resubmitting with an "override negative stock" flag.

**6. Customer Payment / Allocations (new finding)** — "Record Payment" navigated to `sales/payments-received/add` via the same `?form_data=` mechanism plus a new **`included_allocation_ids[]=<invoice id>`** array parameter. New Customer Payment form required **Received Account\*** (Chart-of-Accounts bank/cash picker), exposed **Bank charge** and **TDS** toggles (off by default). **Payment Allocations** table pre-populated with the target Invoice (Amount 1,695, Left to Allocate 1,695, This Allocation 1695, FIFO checkmark, per-row Clear + table-level Clear All). Details panel showed a **live GL journal preview before Approve** — "amancha cash bank account | Debit 1,695.00" / "aa | Credit 1,695.00" — the double-entry postings are computed and shown pre-emptively, not only after posting. Approving assigned real code **REC0029/83-84**, same GL entries then shown as posted under "GL Transactions".

### Purchase-side chain: Purchase Order → Purchase Bill → Supplier Payment

**7. Purchase Order** — created: Supplier "001" (Birgunj Trader), product "super liter" (P0411), Qty 1 cyl, Rate 1200, Tax 13% Vat → Taxable 1,200, VAT 156, Grand Total 1,356. Approve assigned **PO0009/83-84**. **No negative-stock validation triggered on PO approval** — confirms PO genuinely does not move stock.

**8. Conversion to Purchase Bill (confirms symmetry)** — approved PO showed "Convert to Bill" (exactly one target, no intermediate document). Same `?form_data=` mechanism, `referrer_type: "PurchaseOrder"` — this payload is where the full Product entity JSON shape was captured (see Inventory > Products above).

**9. Purchase Bill-specific field confirmed** — mirroring Invoice, pre-filled Purchase Bill required **Warehouse\*** (same 3 options), confirming stock increment on approval. Same Tax dropdown and live recalculation.

**10. Purchase Bill approval — no stock warning (as expected)** — approving (code **PB0043/83-84**) triggered no Negative Stock Balance dialog, consistent with a Purchase Bill increasing stock. Post-approval modal narrower: **Add New / Print only**.

**11. Supplier Payment / Allocations (confirms full mirror)** — "Record Payment" navigated to `purchases/payments-made/add`, same `?form_data=` + `included_allocation_ids[]=<purchase bill id>` mechanism. New Supplier Payment form identical shape (Paid To*/Paid From* instead of Received From*/Received Account*, same Bank charge/TDS toggles). Approving assigned **PAY0022/83-84** (confirming PAY = Supplier Payment prefix, distinct from Customer Payment's REC). **GL Transactions confirmed**: Debit "001" (Supplier/AP account) 1,356.00 / Credit "amancha cash bank account" 1,356.00 — exact mirror of Customer Payment's posting.

### Hands-on pass — summary of resolved/updated findings
- **Corrected**: Quotation → Sales Order → Invoice is *not* a system-enforced 3-stage chain. Quotation → Invoice is a direct single hop; Sales Order is independent, cross-referenced only via free-text Reference No. Same single-hop pattern confirmed on Purchase side.
- **Newly documented**: the `?form_data=<JSON>` + `referrer_type`/`referrer_id` conversion mechanism, confirmed on two independent pairs, inferred to generalize to every "Convert to X" button referenced elsewhere.
- **Newly documented**: Record Payment flow uses the same URL-parameter mechanism plus `included_allocation_ids[]`, pre-filling a fully-functional FIFO-defaulted Payment Allocations table.
- **Newly documented**: a live GL-journal preview is shown before Approve/Save, not just after.
- **Newly documented**: Warehouse\* is required specifically on Invoice and Purchase Bill (stock-moving documents), not on Quotation/Sales Order/Purchase Order (planning documents).
- **Newly documented**: document numbering assigned only at Approve time, confirmed live across all six documents created in this pass.
- **Newly documented**: Negative Stock Balance validation exists (Invoice-approval side only) and behaves as Warn-and-allow on this tenant.
- **Newly documented**: full Product entity JSON shape (see Inventory > Products), including previously-unconfirmed fields `re_order_level`, `track_inventory`, `print_profile`.
- **Still open**: Annex 13 Capital-vs-Others classification gap not resolved by this pass. Check Account Group edit form in Charts of Account next.
- **Not yet done**: this pass covered Quotation→Invoice→Customer Payment and Purchase Order→Purchase Bill→Supplier Payment in full, but did not exercise Credit Note, Debit Note, Void, or Delivery Note/GRN (the latter inapplicable on this tenant's Accounting Movement mode anyway).

---

*This doc is structurally complete across all 9 top-level in-app modules, the pre-login Signup & Onboarding flow, and a live hands-on transaction-creation pass exercising both the Sales and Purchase chains end-to-end with real approvals and GL postings observed. Remaining open items for a future pass, in priority order: (1) the Annex 13 Capital-vs-Others purchase-expenditure classification (check the Account Group edit form in Charts of Account), (2) Credit Note / Debit Note / Void flows (not yet exercised hands-on), (3) confirming whether Production Order → Production Journal and Invoice → Credit Note use the same `?form_data=` conversion mechanism confirmed for Quotation→Invoice and Purchase Order→Purchase Bill, (4) locating the literal "Negative Stock Balance" toggle under Configurations > Apps > General to confirm its exact option set (Reject/Warn/DoNothing) alongside the already-documented Negative Cash Balance setting.*

---

## Confirm-live pass for the parity plan (2026-09-02, Moonbeam tenant, read-only)

Opened before phases 26–34 were finalised. Nothing was saved or sent; the Send Email dialog was opened and dismissed. Screens the original scan never opened are marked **(new)**.

### Reports (all 40 catalog entries present; the Migrated Sales/Purchase Register entries are absent on this tenant)
- **Net Trading Assets (new)** — filters: Period, **Compare**, **Exclude Advance**. Rows: Receivables (Receivables from Customers + Advance to Suppliers), Payables (Payable to Suppliers + Advance from Customers), Inventory Items, Net Trading Assets. Pure GL/stock balances.
- **Exceptional Report (new)** — Period only. Twelve fixed rows, each a balance with DR/CR: Inactive Accounts with Outstanding Balances, Minor Account Balance Exception, Expense Accounts with Credit Balances, Income Accounts with Debit Balances, Asset Accounts with Credit Balances, Liability Accounts with Debit Balances, Customers with Credit Balances, Bank and Cash Accounts with Negative Balances, Suppliers with Debit Balances, Inactive Inventory Items with Balances, Negative Inventory Balances, Non-actionable Account Balances.
- **Sales Return Register (new)** — a real, separate statutory register (Devanagari headers, same family as the Sales Register): Date, Invoice/CN No, Buyer Name, Buyer PAN, Total Return, Tax-exempt Return Value, Taxable Return Value, Tax. One row per Credit Note; a footer Total. So returns are **not** just negative rows in the main register (phase-19's folding was a simplification, not parity). Purchase Return Register is its mirror.
- **User Log (new)** — filters Period, User. Columns: Full Name, Email, Date-time, Device (OS), IP Address, Description (Login Success / Logout Success / Login Fail), Device Info (browser + version). A **login-event log**, not derivable from the audit trail; needs its own row written by the auth endpoints, including failed attempts by email.
- **Transaction list (new)** — filters Txn Type, Transaction Status. Columns: Transaction Date, Txn type, Transaction No, Reference No, Status (Draft/Approved), Amount, Created By, Approved By, Approved At, Created At, Description (contact name + notes). The dashboard's Transactions feed deep-links into it with `transaction_type[]` and `status[]` query params.
- **Inventory Movement (new)** — filters Product Category, Product, Warehouse. Per product: Opening / In / Out / Balance, each as Quantity, Rate, Value.
- **Sales Summary Report (new)** — a fiscal-year picker (BS year "2083 - 2084") and a "Select Mode" multi-select instead of a date range; one row per BS month: Sub Total, Discount, **Service Charge**, Non Taxable Sales, Taxable Sales, VAT, Total. Service Charge is a product-level flag (`service_charge_applicable`) this codebase does not model — decide in 26b whether to carry an always-zero column or omit it with a note.
- **Invoice Age (new)** — filters Customer, Txn Type. Columns: Invoice Date, Due Date, #No, Reference No, Customer (with code), Contact Group, Invoice Amount, Paid, Balance, Status (Overdue/Current), Age Days. **Journal Vouchers posting to a customer appear as ageable documents** alongside invoices.
- **Customer Receivable Summary (new)** — filter Contact Group. Columns: Customer, Contact Group, Closing Balance (negative in parentheses = credit).
- **Journal report / General Ledger Summary / Detail General Ledger / GL Master Report (generated 2026-09-03, phase 26a)** — the four the 2026-09-02 pass listed but never ran. Their filters, column sets and layouts are recorded in full in `docs/phase-26a-status.md`'s "Confirm-live pass" section rather than repeated here. Headlines: the Journal report is one block per document with a per-document Total (paged by document, 205 documents vs the same period's 547 GL Master lines); GL Summary is Code/accounts, Parent, Group Type, Account Class, Opening, Transaction Dr/Cr, Closing, where **Group Type is the top-level group, not the immediate parent**; Detail GL is one section per account with Opening/running/Closing rows and Description = contra account + narration, its "Group by" offering only Account and Sub Account; GL Master is one row per line, SubAccount empty throughout. All four render a Payment as **Customer Payment** or **Supplier Payment** by Direction.
- Report URL slugs, for the record: `general-ledger`, `general-ledger-detail`, `general-ledger-materialized`, `customer-receivable`, `invoice-ageing`, `sales-customer(-summary)`, `sales-item(-summary)`, `sales-summary`, `purchase-supplier(-summary)`, `purchase-item(-summary)`, `sales-return-register`, `purchase-return-register`, `inventory-summary` (=Position), `inventory-moment` (=Movement), `inventory-moment-summary` (=Ledger), `inventory-materialized`, `activity-log`, `user-log`, `net-trading-assets`, `exceptional-report`.

### Forms and settings
- **Invoice add form**: Customer, Reference No, Invoice Code, Invoice Date, Due Date, **Currency (default Nepalese Rupee) + Exchange Rate To NPR\***, Warehouse\*, "This is export sales", lines, Custom Fields, "TDS is applicable", "+ Add Terms and Conditions", "+ Add Reporting Tags". **No Location field** while Billing Location is disabled.
- **Invoice detail**: tabs Overview / Tasks / Documents / Activity; actions **Send Email** and **View Print Preview**; a Terms and conditions section; an Allocations table; GL Transactions with PRINT.
- **Send Email dialog (new)**: Template\* (Custom Template of type Email), To\* (+ More / CC / BCC), Reply To\* (defaults to the user), Subject\*, "Attach Invoice PDF" checkbox (on), drag-and-drop extra attachments.
- **Purchase Bill add form**: Supplier, Reference No, Bill Number, Bill Date, Due Date, Supplier Invoice Reference No, Warehouse\*, Currency + Exchange Rate To NPR\*, Is Import, lines, **Additional Cost** section, "TDS is applicable", Custom Fields, Reporting Tags.
- **Additional Cost (new, confirmed on the Purchase Bill itself)**: an "Add product-wise" toggle; rows of Cost Terms (the AdditionalCost lookup: Clearing Charge, Custom Duty, Excise Duty, Freight, Insurance, Other Cost, Transportation …) × Product ("All Product" or a chosen product when product-wise) × **Method = Value | Quantity** (the allocation basis across the bill's lines) × Amount (NPR).
- **New Contact modal**, after "+ Add More Details": **Accept Purchase** toggle (a customer that can also be billed as a supplier), Additional Field, Email Address, Credit Terms, **Credit Limit**.
- **Configurations > General** — the page has changed since the scan: Suggest Selling Price (Recent / Fixed), Product Price Basis (Inclusive / Exclusive of VAT), Negative Cash Balance (Reject / Warn / Do Nothing), Negative Item Balance (same three), **Credit Limit Exceeds (Reject / Warn / Do Nothing — new)**, VAT on Purchase account, VAT on Sales account. **"Mode of Inventory Tracking" (Physical vs Accounting Movement) is no longer on this page.**
- **Document Numbering** still lists **DeliveryNote → DO1** and **GoodsReceivedNote → GRN1**, both at next-number 1: the documents exist in the product but have never been used on this tenant.
- **Organization > Features**: Billing Location **Disabled** ("reach out to Tigg Support" — an entitlement, not a toggle); Multiple Warehouse Enabled (Kathmandu, Patan, Lalitpur); Multiple Currency list shows NPR only, with **ADD NEW CURRENCY** available. Organization tabs on this tenant: Overview, Tasks, Documents, Features, Migration, Developer Mode (no Backup tab).
- **Tigg Subscriptions**: Standard (0 Txn, 0 Products), amount 0.00, expiry 31-10-2026 ("expire in 59 days"), Location Enabled No, Warehouse Enabled Yes, IRD Verified No, IRD Sync Enabled No.
- **Opening Balances > Account**, inline row form: **Currency, Conversion Rate, Amount, DR/CR** (+ a reporting-tag control). No Location column while the feature is disabled.
- **Chart of Accounts** contains a **"Forex Gain"** account (Income, group "Foreign Exchange Gain"): the product realises exchange differences into the GL.
- **Users & Permissions** route is `#/config/user-permission/users`; the page did not render its body in the automation browser within the wait, so the role editor was not re-read (Phase 14 read it in full).

### Appendix, 2026-09-04 — multi-currency confirm-live pass (phase 28)

Read on the Moonbeam UAT tenant. Recorded here because three of these contradict or sharpen what the
original pass assumed, and one is a limitation of the reference product itself.

- **Organization > Features > Multiple Currency** — a `CODE / NAME / SYMBOL` table, a Show Inactive
  toggle, an ADD NEW CURRENCY action, and exactly one `switch` element in the section whose
  `aria-checked` is `true`. The list holds **only NPR**, with Show Inactive checked or not.
- **The Add New Currency dialog is `Currency` (a "Select Currency" picker) + `Name*` + `Symbol*` +
  Save — and the picker returns "No data"** (two 400s in the console), so no second currency can be
  activated on this tenant. This is what blocked phase 28's decisive experiment.
- **Invoice add form** (`#/sales/invoices/add`): the Currency `ant-select` lists the tenant's active
  currencies as `NPR / Nepalese Rupee`; `Exchange Rate To NPR *` is an `ant-input-number` with
  **`disabled: true`, value `1`**. **Customer Payment** (`#/sales/payments-received/add`) carries the
  identical pair, with `Amount` above it.
- **Opening Balances > Account**, expanded row: `Currency` / `Conversion Rate` (the same
  `ant-input-number`, disabled) / `Amount` / `DR` / Add Reporting Tags — the same control as the
  document forms, under a different label.
- **Chart of Accounts**: searching accounts for `forex` returns exactly `II0006 — Forex Gain`
  (Income, group "Foreign Exchange Gain"); searching groups for `Foreign` returns exactly
  `Foreign Exchange Gain` (Income, parent Indirect Income). **There is no Forex Loss account and no
  Foreign Exchange Loss group**, and no revaluation document anywhere in the product.
- **Printed invoice** (`collection-report-html`): line amounts bare, `Amount in Words` naming the
  currency ("... Nepalese Rupee"), `Net Total  NPR 3,06,500.00` carrying the code, BS dates, and
  **no base-currency column anywhere in the frame**.
- **Navigation note:** this app is hash-routed (`#/config/organization/features`,
  `#/accounting/chartsofaccount/accounts`, `#/config/opening-balances/account`,
  `#/sales/invoices/add`), so `navigate` to a hash URL is far more reliable than clicking through its
  ant-design menus. Guessed routes silently redirect to a default page rather than 404.

### Appendix, 2026-09-04 — landed cost / Additional Cost confirm-live pass (phase 29)

Read on the Moonbeam UAT tenant. **The roadmap's "decisive experiment" (approve a bill carrying a
Freight row and read its GL) did not need to be run: two already-approved bills on this tenant
carry Additional Cost rows**, and their detail pages answer every open question read-only. Nothing
was created, saved or submitted.

- **The add form's Additional Cost section** (`#/purchases/purchases-bill/add`, revealed by a
  `+ Add Additional Cost` link). Columns exactly: `Cost Terms | Product | Method | Amount (NPR)`.
  Defaults on a fresh row: Product = **All Product**, Method = **Value**. Method's only options are
  **Value** and **Quantity**. Cost Terms lists the tenant's *active* AdditionalCost terms (8 here:
  Addidsoajfdsoj, Clearing Charge, Custom Duty, Excise Duty, Freight, Insurance, Other Cost,
  Transportation — the 9th, `zxcas`, shows on an old bill but not in the picker, so it is inactive).
- **There is no payee field of any kind** — a row names a Cost Term and nothing else.
- **The Product picker lists `All Product` plus every line on the bill, Service lines included.**
  Verified by putting two Goods lines and one Service line (AWS Consulting, P0593) on a draft: all
  three appear. So the reference product does **not** restrict landed cost to goods.
- **"Add product-wise" is an `ant-checkbox`, and it swaps the whole section's shape.** Off: the
  four-column rule rows above. On: the section becomes a matrix — `Products` down the side, **one
  column per cost term** across the top, an amount typed into each cell — plus an `Import` action.
  No Method column, because a hand-typed cell needs no allocation rule.
- **After approval the allocation is stored and displayed**, as that same matrix, on the bill's
  Overview immediately below the totals block (unlabelled, inside the Details card). So the
  per-(line, cost term) allocation is persisted, not merely folded into a cost.
- **Additional cost is NOT in the document totals.** Bill 6000: one line, 100 @ 200 = 20,000, nine
  cost terms at 100 each (900 total). Sub Total 20,000, Grand Total **NPR 20,000**.
- **It posts NOTHING to the general ledger.** Bill 6000's GL Transactions panel is exactly
  `Purchase Goods 20,000.00 DR` / `123 (the supplier) 20,000.00 CR`, totalling 20,000/20,000. The
  second bill (2 lines, 6,300,000 of goods, 1,800 of additional cost) is likewise 6,300,000 flat.
  **The supplier is not credited for it and no other account is touched.**
- **It IS capitalised into stock valuation, per line, to the rupee.** Inventory Movement for the
  period: `SSSS (P0597)` shows In **100 @ 209 = 20,900** — exactly (20,000 + 900) / 100. On the
  two-line bill, `Classis 350 cc (P0599)` totals In 60 @ 650,015 = **39,000,900**, of which our
  bill's line is 10 @ 600,000 = 6,000,000 **+ 900** = 6,000,900, the other 50 units contributing a
  round 33,000,000. Both reconcile exactly.
- **Reading of the whole:** that tenant is *periodic* in the general ledger (Goods debit "Purchase
  Goods", a Direct Expense — the same fact phase 25 found), so landed cost there lives **only** in
  the stock/costing subsystem and has nowhere in the GL to go. This is phase-25 Decision A's
  situation exactly, and phase 29 diverges the same way and for the same reason.
- Both sample bills were entered **product-wise**: the two-line bill shows a flat 100 in every cell
  for both products despite a 20:1 value ratio and a 2:1 quantity ratio, which no Value or Quantity
  allocation could produce. So no live example of the Method-based allocation exists to check the
  formula against; pro-rata by line value / by line quantity is taken as read.
