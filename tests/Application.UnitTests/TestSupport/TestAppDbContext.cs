using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
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

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>EF Core InMemory-backed IAppDbContext for handler tests, avoiding a SQL Server dependency.</summary>
public sealed class TestAppDbContext(DbContextOptions<TestAppDbContext> options) : DbContext(options), IAppDbContext
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // RowVersion is a SQL Server-generated concurrency token (rowversion column) the real
        // AppDbContext maps via IsRowVersion(); the InMemory provider has no equivalent
        // store-generation, so it's excluded from this test-only model entirely.
        modelBuilder.Entity<User>().Ignore(u => u.RowVersion);
        modelBuilder.Entity<Organization>().Ignore(o => o.RowVersion);
        modelBuilder.Entity<DocumentNumberingRule>().Ignore(r => r.RowVersion);
        modelBuilder.Entity<JournalVoucher>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<CashTransfer>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<Quotation>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<Invoice>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<Invoice>().Ignore(x => x.GrandTotal);
        modelBuilder.Entity<SalesOrder>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<CreditNote>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<Payment>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<PurchaseOrder>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<PurchaseBill>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<PurchaseBill>().Ignore(x => x.GrandTotal);
        modelBuilder.Entity<Expense>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<Expense>().Ignore(x => x.GrandTotal);
        modelBuilder.Entity<DebitNote>().Ignore(x => x.RowVersion);

        // ApplicableDocumentTypes needs the same delimited-string conversion as the real
        // CustomFieldDefinitionConfiguration (Infrastructure) -- IEntityTypeConfiguration classes
        // aren't applied here (this context has no ApplyConfigurationsFromAssembly call, by
        // design: it's a minimal InMemory model for handler tests, not a schema mirror), so
        // anything beyond conventions needs restating.
        modelBuilder.Entity<CustomFieldDefinition>()
            .Property(d => d.ApplicableDocumentTypes)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Length == 0
                    ? new List<DocumentType>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<DocumentType>).ToList());

        // Product.SecondaryUnits is an encapsulated (private-backing-field) collection -- the
        // real ProductConfiguration's HasMany/SetPropertyAccessMode(Field) call isn't applied
        // here (no ApplyConfigurationsFromAssembly, by design), so it's restated.
        modelBuilder.Entity<Product>()
            .HasMany(p => p.SecondaryUnits)
            .WithOne()
            .HasForeignKey("ProductId");
        modelBuilder.Entity<Product>()
            .Metadata.FindNavigation(nameof(Product.SecondaryUnits))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // JournalVoucher.Lines/CashTransfer.Lines/GlJournalEntry.Lines are the same kind of
        // encapsulated (private-backing-field) collection as Product.SecondaryUnits above -- the
        // real Infrastructure configs' HasMany/SetPropertyAccessMode(Field) calls aren't applied
        // here (no ApplyConfigurationsFromAssembly, by design), so they're restated too.
        modelBuilder.Entity<JournalVoucher>().HasMany(x => x.Lines).WithOne().HasForeignKey("JournalVoucherId")
            .IsRequired().OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<JournalVoucher>()
            .Metadata.FindNavigation(nameof(JournalVoucher.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<CashTransfer>().HasMany(x => x.Lines).WithOne().HasForeignKey("CashTransferId");
        modelBuilder.Entity<CashTransfer>()
            .Metadata.FindNavigation(nameof(CashTransfer.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<GlJournalEntry>().HasMany(x => x.Lines).WithOne().HasForeignKey("GlJournalEntryId");
        modelBuilder.Entity<GlJournalEntry>()
            .Metadata.FindNavigation(nameof(GlJournalEntry.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Phase 5 aggregates -- same encapsulated (private-backing-field) child collection
        // restatement as JournalVoucher/CashTransfer/GlJournalEntry above.
        modelBuilder.Entity<Quotation>().HasMany(x => x.Lines).WithOne().HasForeignKey("QuotationId");
        modelBuilder.Entity<Quotation>()
            .Metadata.FindNavigation(nameof(Quotation.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<Invoice>().HasMany(x => x.Lines).WithOne().HasForeignKey("InvoiceId");
        modelBuilder.Entity<Invoice>()
            .Metadata.FindNavigation(nameof(Invoice.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<SalesOrder>().HasMany(x => x.Lines).WithOne().HasForeignKey("SalesOrderId");
        modelBuilder.Entity<SalesOrder>()
            .Metadata.FindNavigation(nameof(SalesOrder.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<CreditNote>().HasMany(x => x.Lines).WithOne().HasForeignKey("CreditNoteId");
        modelBuilder.Entity<CreditNote>()
            .Metadata.FindNavigation(nameof(CreditNote.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<Payment>().HasMany(x => x.Allocations).WithOne().HasForeignKey("PaymentId");
        modelBuilder.Entity<Payment>()
            .Metadata.FindNavigation(nameof(Payment.Allocations))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Phase 6 aggregates -- same encapsulated (private-backing-field) child collection
        // restatement as every prior phase's aggregates above.
        modelBuilder.Entity<PurchaseOrder>().HasMany(x => x.Lines).WithOne().HasForeignKey("PurchaseOrderId");
        modelBuilder.Entity<PurchaseOrder>()
            .Metadata.FindNavigation(nameof(PurchaseOrder.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<PurchaseBill>().HasMany(x => x.Lines).WithOne().HasForeignKey("PurchaseBillId");
        modelBuilder.Entity<PurchaseBill>()
            .Metadata.FindNavigation(nameof(PurchaseBill.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<Expense>().HasMany(x => x.Lines).WithOne().HasForeignKey("ExpenseId");
        modelBuilder.Entity<Expense>()
            .Metadata.FindNavigation(nameof(Expense.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<DebitNote>().HasMany(x => x.Lines).WithOne().HasForeignKey("DebitNoteId");
        modelBuilder.Entity<DebitNote>()
            .Metadata.FindNavigation(nameof(DebitNote.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Phase 7 aggregates -- same encapsulated (private-backing-field) child collection
        // restatement as every prior phase's aggregates above.
        modelBuilder.Entity<WarehouseTransfer>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<WarehouseTransfer>().HasMany(x => x.Lines).WithOne().HasForeignKey("WarehouseTransferId");
        modelBuilder.Entity<WarehouseTransfer>()
            .Metadata.FindNavigation(nameof(WarehouseTransfer.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        modelBuilder.Entity<InventoryAdjustment>().Ignore(x => x.RowVersion);
        modelBuilder.Entity<InventoryAdjustment>().HasMany(x => x.Lines).WithOne().HasForeignKey("InventoryAdjustmentId");
        modelBuilder.Entity<InventoryAdjustment>()
            .Metadata.FindNavigation(nameof(InventoryAdjustment.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Phase 15 (Deals) -- Deal.Assignees is the same kind of encapsulated (private-backing-
        // field) child collection as every prior phase's aggregates above.
        modelBuilder.Entity<Deal>().HasMany(x => x.Assignees).WithOne().HasForeignKey(x => x.DealId);
        modelBuilder.Entity<Deal>()
            .Metadata.FindNavigation(nameof(Deal.Assignees))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }

    public static IAppDbContext Create() => Create(Guid.NewGuid().ToString());

    /// <summary>
    /// Opens a fresh TestAppDbContext instance against a named InMemory database -- pass the same
    /// name across calls to simulate the real Api's one-DbContext-per-request pattern (a second
    /// handler call reads a clean, freshly materialized snapshot rather than reusing the first
    /// call's already-tracked entity instances). Needed for any test that mutates then re-reads an
    /// encapsulated child collection (e.g. JournalVoucher.Lines) across two handler calls -- the
    /// InMemory provider's orphan-deletion tracking on a Clear+re-Add needs the collection loaded
    /// fresh via Include, not merged into entities already tracked from an earlier call in the
    /// same DbContext instance.
    /// </summary>
    public static IAppDbContext Create(string databaseName) => new TestAppDbContext(
        new DbContextOptionsBuilder<TestAppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
}
