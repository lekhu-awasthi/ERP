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
}
