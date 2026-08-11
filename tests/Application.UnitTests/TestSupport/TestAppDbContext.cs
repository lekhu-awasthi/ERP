using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // RowVersion is a SQL Server-generated concurrency token (rowversion column) the real
        // AppDbContext maps via IsRowVersion(); the InMemory provider has no equivalent
        // store-generation, so it's excluded from this test-only model entirely.
        modelBuilder.Entity<User>().Ignore(u => u.RowVersion);
        modelBuilder.Entity<Organization>().Ignore(o => o.RowVersion);
        modelBuilder.Entity<DocumentNumberingRule>().Ignore(r => r.RowVersion);

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
    }

    public static IAppDbContext Create() => new TestAppDbContext(
        new DbContextOptionsBuilder<TestAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
