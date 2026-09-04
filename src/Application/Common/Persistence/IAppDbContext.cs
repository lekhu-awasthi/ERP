using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Crm;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Imports;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Persistence;

/// <summary>
/// Application-layer view of the EF Core DbContext. Keeps command/query handlers dependent
/// on this interface (Domain-shaped, DbSet&lt;T&gt; abstraction) rather than on
/// ErpApp.Infrastructure.Persistence.AppDbContext directly, preserving the
/// Api -> Application -> Domain dependency rule (architecture-spec.md §1).
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<VerificationCode> VerificationCodes { get; }
    DbSet<Organization> Organizations { get; }
    DbSet<TenantSettings> TenantSettings { get; }
    DbSet<TenantSubscription> TenantSubscriptions { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Currency> Currencies { get; }
    DbSet<OrganizationMembership> OrganizationMemberships { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<CreditTerm> CreditTerms { get; }
    DbSet<PaymentMode> PaymentModes { get; }
    DbSet<Bank> Banks { get; }
    DbSet<CustomStatus> CustomStatuses { get; }
    DbSet<CostTerm> CostTerms { get; }
    DbSet<PrintingTemplate> PrintingTemplates { get; }
    DbSet<CustomTemplate> CustomTemplates { get; }
    DbSet<ReportingTagCategory> ReportingTagCategories { get; }
    DbSet<ReportingTagOption> ReportingTagOptions { get; }
    DbSet<TransactionReportingTag> TransactionReportingTags { get; }
    DbSet<DocumentNumberingRule> DocumentNumberingRules { get; }
    DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; }
    DbSet<CustomFieldValue> CustomFieldValues { get; }
    DbSet<ContactGroup> ContactGroups { get; }
    DbSet<Contact> Contacts { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<UnitOfMeasurement> UnitsOfMeasurement { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductSecondaryUnit> ProductSecondaryUnits { get; }
    DbSet<VariantAttribute> VariantAttributes { get; }
    DbSet<VariantAttributeOption> VariantAttributeOptions { get; }
    DbSet<ProductVariantAttributeUsage> ProductVariantAttributeUsages { get; }
    DbSet<ProductVariantValue> ProductVariantValues { get; }
    DbSet<AccountGroup> AccountGroups { get; }
    DbSet<Account> Accounts { get; }
    DbSet<JournalVoucher> JournalVouchers { get; }
    DbSet<JournalVoucherLine> JournalVoucherLines { get; }
    DbSet<GlJournalEntry> GlJournalEntries { get; }
    DbSet<GlLine> GlLines { get; }
    DbSet<CashTransfer> CashTransfers { get; }
    DbSet<CashTransferLine> CashTransferLines { get; }
    DbSet<Quotation> Quotations { get; }
    DbSet<QuotationLine> QuotationLines { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceLine> InvoiceLines { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<SalesOrderLine> SalesOrderLines { get; }
    DbSet<CreditNote> CreditNotes { get; }
    DbSet<CreditNoteLine> CreditNoteLines { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAllocation> PaymentAllocations { get; }
    DbSet<Cheque> Cheques { get; }
    DbSet<TdsType> TdsTypes { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<PurchaseBill> PurchaseBills { get; }
    DbSet<PurchaseBillLine> PurchaseBillLines { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<ExpenseLine> ExpenseLines { get; }
    DbSet<DebitNote> DebitNotes { get; }
    DbSet<DebitNoteLine> DebitNoteLines { get; }
    DbSet<StockLedgerEntry> StockLedgerEntries { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<WarehouseTransfer> WarehouseTransfers { get; }
    DbSet<WarehouseTransferLine> WarehouseTransferLines { get; }
    DbSet<InventoryAdjustment> InventoryAdjustments { get; }
    DbSet<InventoryAdjustmentLine> InventoryAdjustmentLines { get; }
    DbSet<BillOfMaterials> BillsOfMaterials { get; }
    DbSet<BomRawMaterialLine> BomRawMaterialLines { get; }
    DbSet<BomByProductLine> BomByProductLines { get; }
    DbSet<BomExpenseLine> BomExpenseLines { get; }
    DbSet<ProductionOrder> ProductionOrders { get; }
    DbSet<ProductionOrderRawMaterialLine> ProductionOrderRawMaterialLines { get; }
    DbSet<ProductionOrderByProductLine> ProductionOrderByProductLines { get; }
    DbSet<ProductionOrderExpenseLine> ProductionOrderExpenseLines { get; }
    DbSet<ProductionJournal> ProductionJournals { get; }
    DbSet<ProductionJournalRawMaterialLine> ProductionJournalRawMaterialLines { get; }
    DbSet<ProductionJournalByProductLine> ProductionJournalByProductLines { get; }
    DbSet<ProductionJournalExpenseLine> ProductionJournalExpenseLines { get; }
    DbSet<TaskType> TaskTypes { get; }
    DbSet<WorkTask> Tasks { get; }
    DbSet<LeadSource> LeadSources { get; }
    DbSet<DealStage> DealStages { get; }
    DbSet<Deal> Deals { get; }
    DbSet<DealAssignee> DealAssignees { get; }
    DbSet<Audit> Audits { get; }
    DbSet<OpeningBalanceLine> OpeningBalanceLines { get; }
    DbSet<OpeningStockLine> OpeningStockLines { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<ContactPersonnel> ContactPersonnel { get; }
    DbSet<Comment> Comments { get; }
    DbSet<SmsTemplate> SmsTemplates { get; }
    DbSet<SmsLog> SmsLogs { get; }
    DbSet<SmsCreditLedgerEntry> SmsCreditLedgerEntries { get; }
    DbSet<AlertDefinition> AlertDefinitions { get; }
    DbSet<AlertSendLog> AlertSendLogs { get; }
    DbSet<ImportJob> ImportJobs { get; }
    DbSet<ImportJobRow> ImportJobRows { get; }
    DbSet<ExportJob> ExportJobs { get; }
    DbSet<MigratedSalesRegisterEntry> MigratedSalesRegisterEntries { get; }
    DbSet<MigratedPurchaseRegisterEntry> MigratedPurchaseRegisterEntries { get; }
    DbSet<UploadedDocument> UploadedDocuments { get; }
    DbSet<UserLoginEvent> UserLoginEvents { get; }

    /// <summary>
    /// Generic accessor mirroring DbContext's own Set&lt;TEntity&gt;() -- lets the generic
    /// ListLookupsQuery&lt;TLookup&gt;/DeleteLookupCommand&lt;TLookup&gt; handlers
    /// (Application.Configuration) reach the right table without the interface needing a named
    /// property per lookup type consumed generically.
    /// </summary>
    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
