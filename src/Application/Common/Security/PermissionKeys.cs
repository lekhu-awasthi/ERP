namespace ErpApp.Application.Common.Security;

/// <summary>
/// Stable permission-key catalog (architecture-spec.md §3.7's "PermissionKey a stable string"),
/// checked against RolePermission rows by AuthorizationBehavior. Phase 1c seeds only the
/// handful of keys needed to prove the pipeline fires end to end (see RoleConfiguration's seed
/// data); each later phase's commands add their own constants here as they're built.
/// </summary>
public static class PermissionKeys
{
    /// <summary>Global permission: granted to any authenticated user, not checked against a
    /// specific Organization's roles (there is no Organization -- and thus no role -- yet when
    /// this fires). See IOrganizationScoped's remarks.</summary>
    public const string OrganizationCreate = "Tenancy.Organization.Create";

    public const string OrganizationInviteUser = "Tenancy.Organization.InviteUser";

    public const string OrganizationAcceptRequest = "Tenancy.Organization.AcceptRequest";

    // Phase 2 (Configuration foundation) -- one View/Manage pair per lookup type rather than one
    // shared key, so a later Role Reference editor can toggle e.g. "Member can edit CreditTerm"
    // independently of "Member can edit PaymentMode" (see phase-2-status.md's scope decisions).
    // .Manage covers Create/Update/Delete as a single grant -- these are simple tenant-wide named
    // lists, not warranting the Create/Edit/Delete/Approve split §3.7 reserves for transactional
    // documents.
    public const string CreditTermView = "Configuration.CreditTerm.View";
    public const string CreditTermManage = "Configuration.CreditTerm.Manage";

    public const string PaymentModeView = "Configuration.PaymentMode.View";
    public const string PaymentModeManage = "Configuration.PaymentMode.Manage";

    public const string CustomStatusView = "Configuration.CustomStatus.View";
    public const string CustomStatusManage = "Configuration.CustomStatus.Manage";

    public const string ReportingTagCategoryView = "Configuration.ReportingTagCategory.View";
    public const string ReportingTagCategoryManage = "Configuration.ReportingTagCategory.Manage";

    public const string ReportingTagOptionView = "Configuration.ReportingTagOption.View";
    public const string ReportingTagOptionManage = "Configuration.ReportingTagOption.Manage";

    public const string CustomFieldDefinitionView = "Configuration.CustomFieldDefinition.View";
    public const string CustomFieldDefinitionManage = "Configuration.CustomFieldDefinition.Manage";

    // Phase 20c (Cost Terms) -- same Member-View-only/Admin-write split as every other
    // Configuration lookup (CreditTerm/PaymentMode/TdsType): a tenant-wide control-plane named
    // list, not per-user working data. Members read it because Phase 25's BOM/Production Journal
    // forms will need to populate a cost-term picker; only Admins curate the list itself.
    public const string CostTermView = "Configuration.CostTerm.View";
    public const string CostTermManage = "Configuration.CostTerm.Manage";

    // Phase 3 (Contacts & Catalog). ContactGroup/ProductCategory/UnitOfMeasurement are
    // taxonomy/control-plane, same shape as Phase 2's lookups -- Member gets View only, Manage
    // denied. Contact/Product are working data Members create/edit daily -- Member gets
    // View+Manage (see phase-3-status.md's scope decisions).
    public const string ContactGroupView = "Contacts.ContactGroup.View";
    public const string ContactGroupManage = "Contacts.ContactGroup.Manage";

    public const string ContactView = "Contacts.Contact.View";
    public const string ContactManage = "Contacts.Contact.Manage";

    public const string ProductCategoryView = "Catalog.ProductCategory.View";
    public const string ProductCategoryManage = "Catalog.ProductCategory.Manage";

    public const string UnitOfMeasurementView = "Catalog.UnitOfMeasurement.View";
    public const string UnitOfMeasurementManage = "Catalog.UnitOfMeasurement.Manage";

    public const string ProductView = "Catalog.Product.View";
    public const string ProductManage = "Catalog.Product.Manage";

    // Phase 4 (Accounting core). AccountGroup/Account are simple master data (Chart of Accounts),
    // same View/Manage pair as Phase 2/3's taxonomy lookups. JournalVoucher/CashTransfer are the
    // first real ApprovableTransaction document types -- architecture-spec.md §3.2/§3.7's finer
    // {Module}.{DocumentType}.{View,Create,Edit,Approve} split starts here rather than being
    // retrofitted once Sales/Purchase (Phase 5+) also need Approve as a distinct permission (see
    // phase-4-status.md's scope decisions).
    public const string AccountGroupView = "Accounting.AccountGroup.View";
    public const string AccountGroupManage = "Accounting.AccountGroup.Manage";

    public const string AccountView = "Accounting.Account.View";
    public const string AccountManage = "Accounting.Account.Manage";

    public const string JournalVoucherView = "Accounting.JournalVoucher.View";
    public const string JournalVoucherCreate = "Accounting.JournalVoucher.Create";
    public const string JournalVoucherEdit = "Accounting.JournalVoucher.Edit";
    public const string JournalVoucherApprove = "Accounting.JournalVoucher.Approve";

    public const string CashTransferView = "Accounting.CashTransfer.View";
    public const string CashTransferCreate = "Accounting.CashTransfer.Create";
    public const string CashTransferEdit = "Accounting.CashTransfer.Edit";
    public const string CashTransferApprove = "Accounting.CashTransfer.Approve";

    // Phase 5 (Sales chain). Warehouse is simple master data (View/Manage pair, same as
    // AccountGroup/Account). Quotation/SalesOrder/Invoice/CreditNote/Payment continue the
    // Phase 4 maker-checker split -- {Module}.{DocumentType}.{View,Create,Edit,Approve}.
    public const string WarehouseView = "Tenancy.Warehouse.View";
    public const string WarehouseManage = "Tenancy.Warehouse.Manage";

    public const string QuotationView = "Sales.Quotation.View";
    public const string QuotationCreate = "Sales.Quotation.Create";
    public const string QuotationEdit = "Sales.Quotation.Edit";
    public const string QuotationApprove = "Sales.Quotation.Approve";

    public const string SalesOrderView = "Sales.SalesOrder.View";
    public const string SalesOrderCreate = "Sales.SalesOrder.Create";
    public const string SalesOrderEdit = "Sales.SalesOrder.Edit";
    public const string SalesOrderApprove = "Sales.SalesOrder.Approve";

    public const string InvoiceView = "Sales.Invoice.View";
    public const string InvoiceCreate = "Sales.Invoice.Create";
    public const string InvoiceEdit = "Sales.Invoice.Edit";
    public const string InvoiceApprove = "Sales.Invoice.Approve";

    public const string CreditNoteView = "Sales.CreditNote.View";
    public const string CreditNoteCreate = "Sales.CreditNote.Create";
    public const string CreditNoteEdit = "Sales.CreditNote.Edit";
    public const string CreditNoteApprove = "Sales.CreditNote.Approve";

    public const string PaymentView = "Payments.Payment.View";
    public const string PaymentCreate = "Payments.Payment.Create";
    public const string PaymentEdit = "Payments.Payment.Edit";
    public const string PaymentApprove = "Payments.Payment.Approve";

    // Minimal seam for Invoice/Payment's default-GL-account fallback (see TenantSettings'
    // DefaultSalesAccountId/etc.) -- not a full Settings editor, just enough to let an Admin set
    // the three accounting-defaults fields this phase needs.
    public const string AccountingDefaultsManage = "Configuration.AccountingDefaults.Manage";

    // Phase 6 (Purchase chain). PurchaseOrder/PurchaseBill/Expense/DebitNote continue the
    // maker-checker split (View/Create/Edit/Approve). TdsType is a simple View/Manage lookup pair,
    // same shape as CreditTerm/PaymentMode. Payments.Payment.* (above) stays shared across
    // Direction=Received/Paid rather than splitting into Sales.Payment/Purchasing.Payment -- see
    // phase-6-status.md's scope decisions for the reasoning.
    public const string TdsTypeView = "Configuration.TdsType.View";
    public const string TdsTypeManage = "Configuration.TdsType.Manage";

    public const string PurchaseOrderView = "Purchasing.PurchaseOrder.View";
    public const string PurchaseOrderCreate = "Purchasing.PurchaseOrder.Create";
    public const string PurchaseOrderEdit = "Purchasing.PurchaseOrder.Edit";
    public const string PurchaseOrderApprove = "Purchasing.PurchaseOrder.Approve";

    public const string PurchaseBillView = "Purchasing.PurchaseBill.View";
    public const string PurchaseBillCreate = "Purchasing.PurchaseBill.Create";
    public const string PurchaseBillEdit = "Purchasing.PurchaseBill.Edit";
    public const string PurchaseBillApprove = "Purchasing.PurchaseBill.Approve";

    public const string ExpenseView = "Purchasing.Expense.View";
    public const string ExpenseCreate = "Purchasing.Expense.Create";
    public const string ExpenseEdit = "Purchasing.Expense.Edit";
    public const string ExpenseApprove = "Purchasing.Expense.Approve";

    public const string DebitNoteView = "Purchasing.DebitNote.View";
    public const string DebitNoteCreate = "Purchasing.DebitNote.Create";
    public const string DebitNoteEdit = "Purchasing.DebitNote.Edit";
    public const string DebitNoteApprove = "Purchasing.DebitNote.Approve";

    // Phase 7 (Inventory & stock ledger). WarehouseTransfer/InventoryAdjustment continue the
    // maker-checker split (View/Create/Edit/Approve) every ApprovableTransaction in this codebase
    // uses. InventoryLedgerView is a single shared key for both read-only report screens (Stock
    // Position and Inventory Ledger/kardex) -- they're not documents with their own Create/Edit/
    // Approve lifecycle, just views over StockLedgerEntry/StockMovement, so a single View-only key
    // fits better than a document-shaped four-key set (same reasoning as Configuration's simple
    // lookups getting a View/Manage pair instead of the four-key document shape).
    public const string WarehouseTransferView = "Inventory.WarehouseTransfer.View";
    public const string WarehouseTransferCreate = "Inventory.WarehouseTransfer.Create";
    public const string WarehouseTransferEdit = "Inventory.WarehouseTransfer.Edit";
    public const string WarehouseTransferApprove = "Inventory.WarehouseTransfer.Approve";

    public const string InventoryAdjustmentView = "Inventory.InventoryAdjustment.View";
    public const string InventoryAdjustmentCreate = "Inventory.InventoryAdjustment.Create";
    public const string InventoryAdjustmentEdit = "Inventory.InventoryAdjustment.Edit";
    public const string InventoryAdjustmentApprove = "Inventory.InventoryAdjustment.Approve";

    public const string InventoryLedgerView = "Inventory.InventoryLedger.View";

    // Phase 8a (Core Financial Reports) -- one View-only key per report, same shape as
    // InventoryLedgerView above: these are read-only views over GlLine/GlJournalEntry, not
    // documents with their own Create/Edit/Approve lifecycle, so a single View-only key fits
    // better than a four-key document shape. Granted to both Admin and Member (see
    // phase-8a-status.md's scope decision) -- a report over data Member already has View access
    // to via the documents that post it (JournalVoucher/Invoice/PurchaseBill/etc.).
    public const string TrialBalanceView = "Reports.TrialBalance.View";
    public const string BalanceSheetView = "Reports.BalanceSheet.View";
    public const string IncomeStatementView = "Reports.IncomeStatement.View";

    // Phase 8b (Sales & Purchase Master Reports) -- Admin-only, unlike Phase 8a's three reports.
    // Judgment call, explicitly made rather than defaulting to the InventoryLedgerView/Phase-8a
    // precedent: a Master Report is a flat *unaggregated* fact table -- every Rate a tenant ever
    // charged or paid, per line, across every Contact -- not a rollup like Trial
    // Balance/Balance Sheet/Income Statement. A Member with Sales.Invoice.View can already see any
    // one Invoice's own Rate, but this report surfaces that same margin-adjacent data in bulk,
    // sliceable across the whole tenant's history in one screen -- a meaningfully different
    // exposure than "view one document at a time" (see phase-8b-status.md's scope decision).
    public const string SalesMasterReportView = "Reports.SalesMasterReport.View";
    public const string PurchaseMasterReportView = "Reports.PurchaseMasterReport.View";

    // Phase 8c (VAT Summary Report) -- Admin+Member, back to Phase 8a's precedent rather than
    // Phase 8b's Admin-only one. Judgment call, explicitly made: unlike the Master Reports, this
    // query's output is a rollup (six numbers -- three VatRate buckets per side, plus totals), not
    // a flat unaggregated per-transaction fact table -- no single Rate, Customer, or Supplier is
    // ever exposed by this report, only netted totals, the same shape distinction that earned
    // Trial Balance/Balance Sheet/Income Statement their Admin+Member grant in Phase 8a. See
    // phase-8c-status.md's scope decision.
    public const string VatSummaryView = "Reports.VatSummary.View";

    // Phase 8d (TDS Report) -- Admin-only, same bar as Phase 8b's Master Reports, on top of a
    // second, independent reason those reports didn't carry: this register's whole purpose is to
    // list each deductee's identity next to what was withheld from them, which means it's the first
    // report in this codebase to surface a Contact's PAN -- a real government tax-ID field, not
    // margin-adjacent business data like Rate. Either factor alone (flat per-contact fact table, or
    // PAN exposure) would already argue for Admin-only under the Phase 8b precedent; here both point
    // the same direction, so there's no tension to resolve the way Phase 8c's rollup shape argued
    // against Phase 8b's default. See phase-8d-status.md's scope decision.
    public const string TdsReportView = "Reports.TdsReport.View";

    // Phase 8e (Annex 13 Report) -- Admin-only. Weighed explicitly against Phase 8c's VAT Summary
    // Report rather than defaulting to either Phase 8b/8d's Admin-only precedent or Phase 8a/8c's
    // Admin+Member one: this report's output IS a per-Contact rollup (six summed bucket numbers,
    // not one row per transaction) the same shape as VAT Summary's bucketed totals -- but unlike VAT
    // Summary, which nets activity across every Contact into three anonymous VatRate buckets with no
    // party ever named, every Annex 13 row is pinned to one specific Contact's identity, including
    // their PAN. That's the same PAN-exposure factor that made TdsReportView Admin-only, and it isn't
    // diluted by the rollup shape here -- a rollup that still names the party is a materially
    // different exposure than one that doesn't. See phase-8e-status.md's scope decision.
    public const string AnnexThirteenView = "Reports.AnnexThirteen.View";

    // Phase 8f (Annex 5 Report) -- Admin-only, same bar as Phase 8b/8d's flat per-transaction
    // registers, on the same PAN-exposure reasoning as Phase 8d/8e: this is a flat register, one row
    // per Sales bill (Invoice/CreditNote), not a rollup -- and every row names the Customer including
    // their PAN. Both factors that independently justified Admin-only elsewhere (flat fact table,
    // PAN exposure) point the same direction here, so there's no tension to resolve the way Phase
    // 8c's rollup shape argued against Phase 8b's default. See phase-8f-status.md's scope decision.
    public const string AnnexFiveView = "Reports.AnnexFive.View";

    // Phase 9 (Customer & Supplier Ageing + Statement Reports) -- Admin-only, the same bar as every
    // other per-Contact-identity report (Phase 8b/8d/8e/8f), and if anything the strongest case yet:
    // a Statement is a full per-transaction running-balance ledger for one named Contact (every Rate/
    // amount ever billed or paid, not a rollup), and an Ageing Summary lists every Contact's PAN-
    // adjacent identity next to their outstanding balance. Both factors that independently justified
    // Admin-only elsewhere point the same direction here. Customer and Supplier each keep their own
    // key (mirroring SalesMasterReportView/PurchaseMasterReportView's precedent) even though one
    // shared handler answers both -- see ContactAgeingSummaryQuery/ContactStatementQuery's doc
    // comments -- so an Admin can grant Sales-side visibility independently of Purchase-side.
    public const string CustomerAgeingSummaryView = "Reports.CustomerAgeingSummary.View";
    public const string SupplierAgeingSummaryView = "Reports.SupplierAgeingSummary.View";
    public const string CustomerStatementView = "Reports.CustomerStatement.View";
    public const string SupplierStatementView = "Reports.SupplierStatement.View";

    // Phase 12 (Transaction Approval Queue, the first Workflow-context feature) -- a single blanket
    // key, Admin+Member. Not primarily an exposure-control decision like every Reports.*.View key
    // above: AuthorizationBehavior is the *only* mechanism in this codebase that checks a request's
    // OrganizationId against the acting user's OrganizationMemberships (see its own doc comment) --
    // a query with IOrganizationScoped but no IRequirePermission skips that check entirely, so
    // without a key here, tenant isolation itself (NFR-2.1) would depend solely on the handler's own
    // per-document-type Where clauses, which never verify org membership at all. Every other
    // IOrganizationScoped query/command in this codebase also implements IRequirePermission for
    // exactly this reason (confirmed by grep -- no exception exists). Admin+Member (not Admin-only
    // like most Reports.*.View keys) because this key doesn't itself gate exposure -- the query's own
    // per-document-type granted-permission-key filtering (mirroring this same join) is what
    // determines which rows a Member actually sees, so a Member with zero *.Approve permissions
    // anywhere just sees an empty queue, the same "gated to nothing" outcome an Admin-only key would
    // produce, without blocking a Member who legitimately holds one or more *.Approve grants from
    // using the screen at all. See phase-12-status.md's scope decision.
    public const string TransactionApprovalView = "Workflow.TransactionApproval.View";

    // Phase 13 (Tasks, the second Workflow-context feature) -- a single View/Manage pair, not the
    // four-key {View,Create,Edit,Approve} maker-checker shape every ApprovableTransaction uses:
    // WorkTask has no Approve concept at all. Both granted to Member (not View-only) -- Task is
    // routine daily-use working data any Member should be able to create/complete, the same
    // Member-View+Manage precedent Contact/Product set in Phase 3, not a financial document needing
    // maker-checker separation. TaskTypeView/TaskTypeManage mirror every other Configuration
    // lookup's Member-View-only/Admin-write split (CreditTerm/PaymentMode/TdsType).
    public const string TaskView = "Workflow.Task.View";
    public const string TaskManage = "Workflow.Task.Manage";

    public const string TaskTypeView = "Configuration.TaskType.View";
    public const string TaskTypeManage = "Configuration.TaskType.Manage";

    // Phase 14 (Role Reference) -- Admin-only for both keys, the one deliberate exception to this
    // codebase's usual "grant Member whatever routine daily-use working data needs" default:
    // granting a Member the ability to view/edit the permission matrix would let a Member either
    // see every other Role's exact grants (a privilege-escalation reconnaissance surface) or, with
    // Manage, grant themselves (or any custom role they belong to) anything at all -- the one place
    // in the whole permission system where Member access would be self-defeating. See
    // phase-14-status.md's scope decision.
    public const string RoleView = "Tenancy.Role.View";
    public const string RoleManage = "Tenancy.Role.Manage";

    // Phase 15 (Deals, the CRM module's first feature) -- Crm.Deal.* is a View/Manage pair, not
    // the four-key {View,Create,Edit,Approve} maker-checker shape: Deal has no Approve concept,
    // same as WorkTask in Phase 13. Both granted to Member -- product-requirements.md's Sales
    // Staff persona explicitly "manages Deals/Contacts" as routine daily-use data, the same
    // Member-View+Manage precedent Contact/Product/Task set. Crm.LeadSource.*/Crm.DealStage.* are
    // ordinary Configuration-lookup pairs (Member View-only, Admin write), same shape as
    // TdsType/TaskType.
    public const string DealView = "Crm.Deal.View";
    public const string DealManage = "Crm.Deal.Manage";

    public const string LeadSourceView = "Crm.LeadSource.View";
    public const string LeadSourceManage = "Crm.LeadSource.Manage";

    public const string DealStageView = "Crm.DealStage.View";
    public const string DealStageManage = "Crm.DealStage.Manage";

    // Phase 16a (Void lifecycle + lock-date enforcement) -- one *.Void key per ApprovableTransaction
    // type, added alongside each type's existing {View,Create,Edit,Approve} set rather than folded
    // into Approve: voiding an already-Approved document reverses posted GL/consumed-or-created
    // stock and is at least as consequential as approving it in the first place, so it gets its own
    // maker-checker grant. Admin-granted/Member-denied by default for every one of these 13 keys
    // (RolePermissionConfiguration.HasData), the same default every *.Approve key already uses.
    public const string QuotationVoid = "Sales.Quotation.Void";
    public const string SalesOrderVoid = "Sales.SalesOrder.Void";
    public const string InvoiceVoid = "Sales.Invoice.Void";
    public const string CreditNoteVoid = "Sales.CreditNote.Void";
    public const string PaymentVoid = "Payments.Payment.Void";
    public const string PurchaseOrderVoid = "Purchasing.PurchaseOrder.Void";
    public const string PurchaseBillVoid = "Purchasing.PurchaseBill.Void";
    public const string ExpenseVoid = "Purchasing.Expense.Void";
    public const string DebitNoteVoid = "Purchasing.DebitNote.Void";
    public const string JournalVoucherVoid = "Accounting.JournalVoucher.Void";
    public const string CashTransferVoid = "Accounting.CashTransfer.Void";
    public const string WarehouseTransferVoid = "Inventory.WarehouseTransfer.Void";
    public const string InventoryAdjustmentVoid = "Inventory.InventoryAdjustment.Void";

    // Lock date (NFR-3.4) -- Admin-only, same "one deliberate exception to Member-gets-routine-
    // working-data" bar Phase 14's Tenancy.Role.* keys set: letting a Member move or clear the
    // lock date would let them reopen exactly the backdated-write window this feature exists to
    // close.
    public const string OrganizationLockDateManage = "Tenancy.Organization.LockDateManage";

    // Phase 16d (System Audit report) -- a flat per-user activity register naming every Create/
    // Update/Approve/Void action any member of the org took, the same PAN/per-transaction-identity
    // exposure factor that made TdsReportView Admin-only (phase-8b-status.md's discriminator):
    // seeing "who did what" across the whole org is materially more sensitive than any one
    // document type's own View permission.
    public const string SystemAuditView = "Reports.SystemAudit.View";

    // Phase 17 (Accounting breadth) -- Bank is a routine Configuration lookup (same Member-View/
    // Admin-Manage split as every other lookup in this file). Bank Accounts is a distinct nav
    // entry (its own screen) but creating/editing one goes through the existing
    // CreateAccount/UpdateAccountCommand -- still literally an Account row -- so it deliberately
    // reuses AccountManage rather than a new BankAccountManage key; only the screen's own new
    // capability (the live-balance list) is new, hence BankAccountView only, no ...Manage sibling.
    // OpeningBalance is a real new screen+capability (no existing command it piggybacks on) so it
    // gets its own View/Edit pair, matching FR-3.4's own confirmed View-vs-Edit permission split
    // (not the View/Create/Edit/Approve/Void shape of a real document -- see decision recorded in
    // docs/phase-17-status.md). Cheque gets the standard View/Manage pair; no separate
    // status-transition key -- decision #4 (docs/phase-17-status.md) found no GL side-effect on any
    // Cheque status transition, so it doesn't rise to the maker-checker bar a Void or Approve key
    // exists for.
    public const string BankView = "Configuration.Bank.View";
    public const string BankManage = "Configuration.Bank.Manage";
    public const string BankAccountView = "Accounting.BankAccount.View";
    public const string ChequeView = "Accounting.Cheque.View";
    public const string ChequeManage = "Accounting.Cheque.Manage";
    public const string OpeningBalanceView = "Accounting.OpeningBalance.View";
    public const string OpeningBalanceEdit = "Accounting.OpeningBalance.Edit";

    // Phase 18 (CRM completion) -- Contact Personnel / Attachments / Comments deliberately ride on
    // the existing Contacts.Contact.* pair rather than new keys: live-confirmed against the Tigg
    // reference product, neither sub-tab has its own permission screen or gating distinct from the
    // parent Contact -- both are reached only through a Contact's own detail page, already gated by
    // ContactView/ContactManage. See docs/phase-18-status.md decision #7. A Contact's own "SMS
    // History" activity sub-tab uses Crm.SmsLogView instead (below) -- one ListSmsLogsQuery serves
    // both that sub-tab and the standalone SMS module's org-wide History tab, and both are
    // Admin+Member same as ContactView, so splitting the query by caller context would add
    // complexity without changing who can see what.
    //
    // SMS gets its own key set, standalone (not folded into Contacts.Contact.*), since it's a
    // distinct nav module (CRM > SMS) with its own screen, not a Contact-detail sub-tab:
    // - Crm.Sms.Send is Admin-only, the one deliberate exception in this feature set -- sending
    //   consumes paid credits and reaches external contacts directly, the same "flat/sensitive
    //   action" bar that made Tenancy.Role.*/Tenancy.Organization.LockDateManage Admin-only.
    // - Crm.SmsTemplate.* is a routine View/Manage pair, same shape as Crm.LeadSource.*/
    //   Crm.DealStage.*.
    // - Crm.SmsCreditLedger.View (Admin+Member -- routine "how many credits are left" visibility,
    //   the same Phase 8a/17-style rollup reasoning as OpeningBalanceView) is split from
    //   Crm.SmsCreditLedger.Adjust (Admin-only -- manually crediting/correcting the balance, the
    //   same "settable starting number" sensitivity as OpeningBalanceEdit).
    // - Crm.SmsLog.View (Admin+Member) gates the standalone SMS module's own org-wide "SMS
    //   History" tab (every send across every Contact) -- a broader exposure than the
    //   already-ContactView-gated per-Contact sub-tab, but still routine send-log visibility, not
    //   a PAN-adjacent register, so it stays Admin+Member rather than joining the Admin-only bar.
    public const string SmsSend = "Crm.Sms.Send";
    public const string SmsTemplateView = "Crm.SmsTemplate.View";
    public const string SmsTemplateManage = "Crm.SmsTemplate.Manage";
    public const string SmsCreditLedgerView = "Crm.SmsCreditLedger.View";
    public const string SmsCreditLedgerAdjust = "Crm.SmsCreditLedger.Adjust";
    public const string SmsLogView = "Crm.SmsLog.View";

    // Phase 19 (Reporting Tags + remaining reports) -- see docs/phase-19-status.md decision #7 for
    // the full reasoning per key.
    // - CashFlowSummaryView / RatioAnalysisView: Admin+Member, same bar as Phase 8a's three
    //   statements -- both are rollups (Bank/Cash movement summary; ratios computed from
    //   Balance Sheet/Income Statement figures) with no PAN/per-transaction exposure beyond what a
    //   Member with ordinary document View access can already piece together.
    // - SalesRegisterView / PurchaseRegisterView: Admin-only, same bar as every flat per-transaction
    //   register with PAN exposure (Phase 8b/8d/8e/8f) -- both factors (flat fact table, PAN column)
    //   independently justify it, no tension to resolve.
    // - StockAgeingView: Admin+Member, weighed against InventoryLedgerView -- a per-product×bucket
    //   rollup, not a per-transaction fact table, no PAN/contact exposure; same shape class as Stock
    //   Position, not Sales Master Report.
    // - ProductProfitabilityView: Admin-only -- the one genuine judgment call. Exposes per-product
    //   Cost Of Sales next to Sales in the same row, a direct margin readout a Member with ordinary
    //   Sales.Invoice.View/InventoryLedgerView access cannot reconstruct today (Invoice screens show
    //   Rate, never COGS unit cost). Closer to Sales Master Report's "bulk margin-adjacent data"
    //   reasoning (Phase 8b) than InventoryLedgerView's, so it joins the Admin-only set.
    // - Reporting Tags: no new key -- attaching a tag to a Quotation/Invoice rides on that document
    //   type's own existing Edit permission (see SetTransactionReportingTagsCommand), since tagging
    //   is a detail-page edit action, not a distinct capability.
    public const string CashFlowSummaryView = "Reports.CashFlowSummary.View";
    public const string SalesRegisterView = "Reports.SalesRegister.View";
    public const string PurchaseRegisterView = "Reports.PurchaseRegister.View";
    public const string StockAgeingView = "Reports.StockAgeing.View";
    public const string ProductProfitabilityView = "Reports.ProductProfitability.View";
    public const string RatioAnalysisView = "Reports.RatioAnalysis.View";

    // Phase 20d (Printing Templates / Custom Templates, FR-11.2/11.3) -- Admin-only for both pairs,
    // a judgment call rather than the CreditTerm/PaymentMode/CostTerm Member-View-by-default norm:
    // unlike those lookups, nothing here ever populates a Member-facing picker on a document form --
    // the print action itself (rendering a document to PDF) doesn't even read these tables, it rides
    // on the target document type's own existing View permission (see PrintDocumentPermissions, the
    // same "no new key" reasoning SetCustomStatusCommand/SetTransactionReportingTagsCommand used for
    // their own write actions). So a Member has no legitimate reason to view or manage either list --
    // this is pure admin curation of a control-plane gallery/text library, the same bar Tenancy.Role.*
    // set in Phase 14. Not re-confirmed live against the reference tenant's own Member-role gating
    // (Step 1's confirm-live pass covered screen shape, not the permission boundary) -- flagged in
    // docs/phase-20d-status.md as a judgment call to revisit if that turns out wrong.
    public const string PrintingTemplateView = "Configuration.PrintingTemplate.View";
    public const string PrintingTemplateManage = "Configuration.PrintingTemplate.Manage";

    public const string CustomTemplateView = "Configuration.CustomTemplate.View";
    public const string CustomTemplateManage = "Configuration.CustomTemplate.Manage";

    // Phase 20f (tenant feature-flag enforcement, FR-2.6). A View key with no Manage counterpart,
    // because there is nothing to manage: the entitlements are chosen once at Organization
    // creation and are immutable afterwards (live-confirmed -- the reference product's own
    // subscription screen is read-only and tells you to contact vendor support to change one).
    //
    // Admin+Member, departing from the Admin-only bar Phase 20d's control-plane keys set, for a
    // concrete reason rather than by analogy: the Angular shell reads this query to decide which
    // feature-gated nav entries to render, so *every* signed-in role needs it or a Member's nav
    // silently shows Inventory links that then 403 at the API. It also exposes nothing sensitive
    // -- plan name, trial dates, and seven booleans, no PAN, no contact identity, no
    // per-transaction data -- which puts it squarely in the "bounded, routine" half of this
    // codebase's permission-derivation rule.
    public const string SubscriptionView = "Tenancy.Subscription.View";
}
