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
}
