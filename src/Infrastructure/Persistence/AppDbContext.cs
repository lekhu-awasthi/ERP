using ErpApp.Application.Common.Exceptions;
using Microsoft.Data.SqlClient;
using System.Reflection;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Communications;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Crm;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Imports;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<BillingLocation> BillingLocations => Set<BillingLocation>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<CreditTerm> CreditTerms => Set<CreditTerm>();
    public DbSet<PaymentMode> PaymentModes => Set<PaymentMode>();
    public DbSet<PosLocationSettings> PosLocationSettings => Set<PosLocationSettings>();
    public DbSet<PosLocationPaymentMode> PosLocationPaymentModes => Set<PosLocationPaymentMode>();
    public DbSet<PosSession> PosSessions => Set<PosSession>();
    public DbSet<PosCashMovement> PosCashMovements => Set<PosCashMovement>();
    public DbSet<PosArea> PosAreas => Set<PosArea>();
    public DbSet<PosTable> PosTables => Set<PosTable>();
    public DbSet<KitchenStation> KitchenStations => Set<KitchenStation>();
    public DbSet<DiscountScheme> DiscountSchemes => Set<DiscountScheme>();
    public DbSet<DiscountSchemeLine> DiscountSchemeLines => Set<DiscountSchemeLine>();
    public DbSet<DiscountSchemeLocation> DiscountSchemeLocations => Set<DiscountSchemeLocation>();
    public DbSet<PosOrder> PosOrders => Set<PosOrder>();
    public DbSet<PosOrderLine> PosOrderLines => Set<PosOrderLine>();
    public DbSet<KitchenTicket> KitchenTickets => Set<KitchenTicket>();
    public DbSet<KitchenTicketLine> KitchenTicketLines => Set<KitchenTicketLine>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<CustomStatus> CustomStatuses => Set<CustomStatus>();
    public DbSet<CostTerm> CostTerms => Set<CostTerm>();
    public DbSet<PrintingTemplate> PrintingTemplates => Set<PrintingTemplate>();
    public DbSet<PrintingTemplateLocationChoice> PrintingTemplateLocationChoices => Set<PrintingTemplateLocationChoice>();
    public DbSet<CustomTemplate> CustomTemplates => Set<CustomTemplate>();
    public DbSet<ReportingTagCategory> ReportingTagCategories => Set<ReportingTagCategory>();
    public DbSet<ReportingTagOption> ReportingTagOptions => Set<ReportingTagOption>();
    public DbSet<TransactionReportingTag> TransactionReportingTags => Set<TransactionReportingTag>();
    public DbSet<DocumentNumberingRule> DocumentNumberingRules => Set<DocumentNumberingRule>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<UnitOfMeasurement> UnitsOfMeasurement => Set<UnitOfMeasurement>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();
    public DbSet<ProductSecondaryUnit> ProductSecondaryUnits => Set<ProductSecondaryUnit>();
    public DbSet<VariantAttribute> VariantAttributes => Set<VariantAttribute>();
    public DbSet<VariantAttributeOption> VariantAttributeOptions => Set<VariantAttributeOption>();
    public DbSet<ProductVariantAttributeUsage> ProductVariantAttributeUsages => Set<ProductVariantAttributeUsage>();
    public DbSet<ProductVariantValue> ProductVariantValues => Set<ProductVariantValue>();
    public DbSet<ProductLocation> ProductLocations => Set<ProductLocation>();
    public DbSet<AccountGroup> AccountGroups => Set<AccountGroup>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalVoucher> JournalVouchers => Set<JournalVoucher>();
    public DbSet<JournalVoucherLine> JournalVoucherLines => Set<JournalVoucherLine>();
    public DbSet<GlJournalEntry> GlJournalEntries => Set<GlJournalEntry>();
    public DbSet<GlLine> GlLines => Set<GlLine>();
    public DbSet<CashTransfer> CashTransfers => Set<CashTransfer>();
    public DbSet<CashTransferLine> CashTransferLines => Set<CashTransferLine>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationLine> QuotationLines => Set<QuotationLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<InvoiceTender> InvoiceTenders => Set<InvoiceTender>();
    public DbSet<InvoicePrint> InvoicePrints => Set<InvoicePrint>();
    public DbSet<CreditNotePayout> CreditNotePayouts => Set<CreditNotePayout>();
    public DbSet<CreditNotePrint> CreditNotePrints => Set<CreditNotePrint>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteLine> CreditNoteLines => Set<CreditNoteLine>();
    public DbSet<DeliveryNote> DeliveryNotes => Set<DeliveryNote>();
    public DbSet<DeliveryNoteLine> DeliveryNoteLines => Set<DeliveryNoteLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Cheque> Cheques => Set<Cheque>();

    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();

    public DbSet<BankReconciliation> BankReconciliations => Set<BankReconciliation>();
    public DbSet<TdsType> TdsTypes => Set<TdsType>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceivedNote> GoodsReceivedNotes => Set<GoodsReceivedNote>();
    public DbSet<GoodsReceivedNoteLine> GoodsReceivedNoteLines => Set<GoodsReceivedNoteLine>();
    public DbSet<PurchaseBill> PurchaseBills => Set<PurchaseBill>();
    public DbSet<PurchaseBillLine> PurchaseBillLines => Set<PurchaseBillLine>();
    public DbSet<PurchaseBillAdditionalCost> PurchaseBillAdditionalCosts => Set<PurchaseBillAdditionalCost>();
    public DbSet<PurchaseBillAdditionalCostAllocation> PurchaseBillAdditionalCostAllocations => Set<PurchaseBillAdditionalCostAllocation>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseLine> ExpenseLines => Set<ExpenseLine>();
    public DbSet<DebitNote> DebitNotes => Set<DebitNote>();
    public DbSet<DebitNoteLine> DebitNoteLines => Set<DebitNoteLine>();
    public DbSet<StockLedgerEntry> StockLedgerEntries => Set<StockLedgerEntry>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PhysicalStockMovement> PhysicalStockMovements => Set<PhysicalStockMovement>();
    public DbSet<DocumentLineSerial> DocumentLineSerials => Set<DocumentLineSerial>();
    public DbSet<WarehouseTransfer> WarehouseTransfers => Set<WarehouseTransfer>();
    public DbSet<WarehouseTransferLine> WarehouseTransferLines => Set<WarehouseTransferLine>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();
    public DbSet<InventoryAdjustmentLine> InventoryAdjustmentLines => Set<InventoryAdjustmentLine>();
    public DbSet<BillOfMaterials> BillsOfMaterials => Set<BillOfMaterials>();
    public DbSet<BomRawMaterialLine> BomRawMaterialLines => Set<BomRawMaterialLine>();
    public DbSet<BomByProductLine> BomByProductLines => Set<BomByProductLine>();
    public DbSet<BomExpenseLine> BomExpenseLines => Set<BomExpenseLine>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionOrderRawMaterialLine> ProductionOrderRawMaterialLines => Set<ProductionOrderRawMaterialLine>();
    public DbSet<ProductionOrderByProductLine> ProductionOrderByProductLines => Set<ProductionOrderByProductLine>();
    public DbSet<ProductionOrderExpenseLine> ProductionOrderExpenseLines => Set<ProductionOrderExpenseLine>();
    public DbSet<ProductionJournal> ProductionJournals => Set<ProductionJournal>();
    public DbSet<ProductionJournalRawMaterialLine> ProductionJournalRawMaterialLines => Set<ProductionJournalRawMaterialLine>();
    public DbSet<ProductionJournalByProductLine> ProductionJournalByProductLines => Set<ProductionJournalByProductLine>();
    public DbSet<ProductionJournalExpenseLine> ProductionJournalExpenseLines => Set<ProductionJournalExpenseLine>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<DealStage> DealStages => Set<DealStage>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealAssignee> DealAssignees => Set<DealAssignee>();
    public DbSet<Audit> Audits => Set<Audit>();
    public DbSet<OpeningBalanceLine> OpeningBalanceLines => Set<OpeningBalanceLine>();
    public DbSet<OpeningStockLine> OpeningStockLines => Set<OpeningStockLine>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<ContactPersonnel> ContactPersonnel => Set<ContactPersonnel>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<SmsTemplate> SmsTemplates => Set<SmsTemplate>();
    public DbSet<SmsLog> SmsLogs => Set<SmsLog>();
    public DbSet<SmsCreditLedgerEntry> SmsCreditLedgerEntries => Set<SmsCreditLedgerEntry>();
    public DbSet<AlertDefinition> AlertDefinitions => Set<AlertDefinition>();
    public DbSet<AlertSendLog> AlertSendLogs => Set<AlertSendLog>();

    // Phase 30 -- Communications.
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    public DbSet<EmailSendLog> EmailSendLogs => Set<EmailSendLog>();

    public DbSet<EmailSendAttachment> EmailSendAttachments => Set<EmailSendAttachment>();

    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

    public DbSet<ImportJobRow> ImportJobRows => Set<ImportJobRow>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<MigratedSalesRegisterEntry> MigratedSalesRegisterEntries => Set<MigratedSalesRegisterEntry>();
    public DbSet<MigratedPurchaseRegisterEntry> MigratedPurchaseRegisterEntries => Set<MigratedPurchaseRegisterEntry>();
    public DbSet<UploadedDocument> UploadedDocuments => Set<UploadedDocument>();
    public DbSet<UserLoginEvent> UserLoginEvents => Set<UserLoginEvent>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    // IAppDbContext.Set<TEntity>() -- satisfied implicitly by DbContext's own public
    // Set<TEntity>() (identical signature), needed by the generic
    // ListLookupsQuery<TLookup>/DeleteLookupCommand<TLookup> handlers.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Phase 34c (NFR-5.1) -- the tenant index sweep. Runs after the configurations so it can see
        // the indexes they declare and leave those alone; see TenantIndexConvention for the rule and
        // for what it refuses to let through.
        TenantIndexConvention.Apply(modelBuilder);
    }

    /// <summary>
    /// Enforces Audit's own "append-only, no code path can update or delete a row" exit criterion
    /// (roadmap Phase 16d) as a real mechanism, not just an absence of an Update/Delete handler --
    /// a private constructor + no public mutator prevents Application code from ever composing a
    /// change, but nothing stops a future EF Core Update/Remove call reaching an Audit entity
    /// directly, so this checks the change tracker itself right before every save.
    ///
    /// <para>Phase 93 -- and turns two documents putting one serial into stock at the same moment into a 409. The ledger
    /// refuses a serial already in stock before it writes (phase 92's <c>SerialStock</c>), but that is a read and then a
    /// write: two approvals racing for one unit both read it absent, and the second meets phase 51's filtered unique
    /// index as a <c>DbUpdateException</c>, which surfaced as a 500 telling the caller nothing. Nothing is saved either
    /// way; the answer is the one the ledger's own check gives.</para>
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var mutatedAudits = ChangeTracker.Entries<Audit>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (mutatedAudits.Count > 0)
        {
            throw new InvalidOperationException(
                "Audit rows are append-only and can never be updated or deleted.");
        }

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSerialInStockViolation(ex))
        {
            throw new ConflictException(SerialInStockRaceMessage);
        }
    }

    /// <summary>Phase 93 -- the 409's words: the same unit received twice, by two documents at once.</summary>
    public const string SerialInStockRaceMessage =
        "A serial number this document puts into stock was put into stock by another document at the same moment, so "
        + "it would be in stock twice. Nothing was saved; reload and check the serial numbers.";

    /// <summary>The name EF gives phase 51's unique index over a layer's serial, and the SQL Server errors a duplicate
    /// key in a unique index or constraint raises.</summary>
    private const string SerialInStockIndexName = "IX_StockLedgerEntries_OrganizationId_ProductId_SerialNo";

    private static bool IsSerialInStockViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(SerialInStockIndexName, StringComparison.Ordinal);
}
