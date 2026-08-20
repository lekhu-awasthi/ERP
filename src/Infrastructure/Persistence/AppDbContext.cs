using System.Reflection;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Crm;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Payments;
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
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<CreditTerm> CreditTerms => Set<CreditTerm>();
    public DbSet<PaymentMode> PaymentModes => Set<PaymentMode>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<CustomStatus> CustomStatuses => Set<CustomStatus>();
    public DbSet<ReportingTagCategory> ReportingTagCategories => Set<ReportingTagCategory>();
    public DbSet<ReportingTagOption> ReportingTagOptions => Set<ReportingTagOption>();
    public DbSet<DocumentNumberingRule> DocumentNumberingRules => Set<DocumentNumberingRule>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<UnitOfMeasurement> UnitsOfMeasurement => Set<UnitOfMeasurement>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductSecondaryUnit> ProductSecondaryUnits => Set<ProductSecondaryUnit>();
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
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteLine> CreditNoteLines => Set<CreditNoteLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Cheque> Cheques => Set<Cheque>();
    public DbSet<TdsType> TdsTypes => Set<TdsType>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseBill> PurchaseBills => Set<PurchaseBill>();
    public DbSet<PurchaseBillLine> PurchaseBillLines => Set<PurchaseBillLine>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseLine> ExpenseLines => Set<ExpenseLine>();
    public DbSet<DebitNote> DebitNotes => Set<DebitNote>();
    public DbSet<DebitNoteLine> DebitNoteLines => Set<DebitNoteLine>();
    public DbSet<StockLedgerEntry> StockLedgerEntries => Set<StockLedgerEntry>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<WarehouseTransfer> WarehouseTransfers => Set<WarehouseTransfer>();
    public DbSet<WarehouseTransferLine> WarehouseTransferLines => Set<WarehouseTransferLine>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();
    public DbSet<InventoryAdjustmentLine> InventoryAdjustmentLines => Set<InventoryAdjustmentLine>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<DealStage> DealStages => Set<DealStage>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealAssignee> DealAssignees => Set<DealAssignee>();
    public DbSet<Audit> Audits => Set<Audit>();
    public DbSet<OpeningBalanceLine> OpeningBalanceLines => Set<OpeningBalanceLine>();
    public DbSet<OpeningStockLine> OpeningStockLines => Set<OpeningStockLine>();

    // IAppDbContext.Set<TEntity>() -- satisfied implicitly by DbContext's own public
    // Set<TEntity>() (identical signature), needed by the generic
    // ListLookupsQuery<TLookup>/DeleteLookupCommand<TLookup> handlers.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    /// <summary>
    /// Enforces Audit's own "append-only, no code path can update or delete a row" exit criterion
    /// (roadmap Phase 16d) as a real mechanism, not just an absence of an Update/Delete handler --
    /// a private constructor + no public mutator prevents Application code from ever composing a
    /// change, but nothing stops a future EF Core Update/Remove call reaching an Audit entity
    /// directly, so this checks the change tracker itself right before every save.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var mutatedAudits = ChangeTracker.Entries<Audit>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (mutatedAudits.Count > 0)
        {
            throw new InvalidOperationException(
                "Audit rows are append-only and can never be updated or deleted.");
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
