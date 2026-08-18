using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Inventory;
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
    DbSet<OrganizationMembership> OrganizationMemberships { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<CreditTerm> CreditTerms { get; }
    DbSet<PaymentMode> PaymentModes { get; }
    DbSet<CustomStatus> CustomStatuses { get; }
    DbSet<ReportingTagCategory> ReportingTagCategories { get; }
    DbSet<ReportingTagOption> ReportingTagOptions { get; }
    DbSet<DocumentNumberingRule> DocumentNumberingRules { get; }
    DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; }
    DbSet<CustomFieldValue> CustomFieldValues { get; }
    DbSet<ContactGroup> ContactGroups { get; }
    DbSet<Contact> Contacts { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<UnitOfMeasurement> UnitsOfMeasurement { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductSecondaryUnit> ProductSecondaryUnits { get; }
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
    DbSet<TaskType> TaskTypes { get; }
    DbSet<WorkTask> Tasks { get; }

    /// <summary>
    /// Generic accessor mirroring DbContext's own Set&lt;TEntity&gt;() -- lets the generic
    /// ListLookupsQuery&lt;TLookup&gt;/DeleteLookupCommand&lt;TLookup&gt; handlers
    /// (Application.Configuration) reach the right table without the interface needing a named
    /// property per lookup type consumed generically.
    /// </summary>
    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
