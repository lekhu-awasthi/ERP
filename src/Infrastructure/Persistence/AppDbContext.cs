using System.Reflection;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
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

    // IAppDbContext.Set<TEntity>() -- satisfied implicitly by DbContext's own public
    // Set<TEntity>() (identical signature), needed by the generic
    // ListLookupsQuery<TLookup>/DeleteLookupCommand<TLookup> handlers.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
