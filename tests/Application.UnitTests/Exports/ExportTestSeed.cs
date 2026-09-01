using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Exports;

/// <summary>
/// One tenant with at least one row in every one of FR-2.8's five categories, plus a real Admin user
/// with real role grants. The grants are real <c>RolePermission</c> rows rather than a bypassed
/// check, because "only an Admin of this organization can download a full-tenant dump" is one of
/// this phase's load-bearing claims and a test that stubbed it would prove nothing.
/// </summary>
internal sealed record ExportTenant(
    Guid OrganizationId,
    Guid AdminUserId,
    string OrganizationName,
    string ProductCode,
    string ContactCode,
    string AccountCode);

internal static class ExportTestSeed
{
    /// <summary>
    /// Seeds a complete tenant. <paramref name="marker"/> is woven into every seeded code and name,
    /// so a tenant-isolation test can assert on "no cell anywhere in this workbook contains org B's
    /// marker" -- which is a far stronger claim than counting rows.
    /// </summary>
    public static async Task<ExportTenant> SeedAsync(
        IAppDbContext db, string marker = "A", params string[] grantedPermissionKeys)
    {
        var organization = Organization.Create(
            $"Acme Traders {marker}", "Trading", null, new DateOnly(2026, 4, 1), true,
            $"ws-{Guid.NewGuid():N}", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);

        var user = User.Register($"Ram Bahadur {marker}", $"admin-{Guid.NewGuid():N}@acme.test", "9800000000", "hash");
        db.Users.Add(user);

        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organization.Id, user.Id, MembershipRole.Admin));

        // Admin is a shared system role (OrganizationId is null), so a test seeding two tenants must
        // not add it twice.
        if (!await db.Roles.AnyAsync(r => r.Id == Role.AdminId))
        {
            db.Roles.Add(Role.Create(Role.AdminId, "Admin"));
        }

        var keys = grantedPermissionKeys.Length > 0
            ? grantedPermissionKeys
            : [PermissionKeys.ExportJobManage, PermissionKeys.ExportJobView];

        foreach (var key in keys)
        {
            db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.AdminId, key, true));
        }

        var category = ProductCategory.Create(organization.Id, $"Snacks {marker}", null);
        var unit = UnitOfMeasurement.Create(organization.Id, $"Box {marker}", "box");
        var contactGroup = ContactGroup.Create(organization.Id, $"Kathmandu {marker}", null);
        var warehouse = Warehouse.Create(organization.Id, $"Main Store {marker}");
        db.ProductCategories.Add(category);
        db.UnitsOfMeasurement.Add(unit);
        db.ContactGroups.Add(contactGroup);
        db.Warehouses.Add(warehouse);

        var productCode = $"P-{marker}-0001";
        var product = Product.Create(
            organization.Id, ProductType.Goods, $"Salted Cashew {marker}", productCode, category.Id, unit.Id,
            "1905", true, 100m, 80m, VatRate.ThirteenPercentVat, 5, true);
        db.Products.Add(product);

        var contactCode = $"C-{marker}-0001";
        db.Contacts.Add(Contact.Create(
            organization.Id, ContactType.Customer, $"Everest Retail {marker}", contactCode,
            $"Kathmandu-{marker}", "304567847", "9841768644", $"{marker}@example.test", contactGroup.Id, 0m));

        var accountGroup = AccountGroup.Create(organization.Id, $"Current Assets {marker}", AccountRootType.Asset, null);
        db.AccountGroups.Add(accountGroup);

        var accountCode = $"AC-{marker}-0001";
        var cash = Account.Create(
            organization.Id, accountCode, $"Cash {marker}", AccountRootType.Asset, accountGroup.Id);
        var sales = Account.Create(
            organization.Id, $"AC-{marker}-0002", $"Sales {marker}", AccountRootType.Income, accountGroup.Id);
        db.Accounts.Add(cash);
        db.Accounts.Add(sales);

        db.GlJournalEntries.Add(GlJournalEntry.Post(
            organization.Id,
            DocumentType.Invoice,
            Guid.NewGuid(),
            [new GlLineInput(cash.Id, 1000m, 0m), new GlLineInput(sales.Id, 0m, 1000m)]));

        db.StockMovements.Add(StockMovement.Create(
            organization.Id, product.Id, warehouse.Id, StockMovementDirection.In, 10m, 80m,
            DocumentType.PurchaseBill, Guid.NewGuid(), new DateOnly(2026, 8, 20)));

        await db.SaveChangesAsync();

        return new ExportTenant(
            organization.Id, user.Id, organization.Name, productCode, contactCode, accountCode);
    }

    /// <summary>A tenant with a user and permissions but no business data at all -- a brand-new
    /// Organization, which must still export successfully.</summary>
    public static async Task<ExportTenant> SeedEmptyAsync(IAppDbContext db, string marker = "E")
    {
        var organization = Organization.Create(
            $"Fresh Co {marker}", "Trading", null, new DateOnly(2026, 4, 1), true,
            $"ws-{Guid.NewGuid():N}", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);

        var user = User.Register($"New Admin {marker}", $"admin-{Guid.NewGuid():N}@fresh.test", "9800000000", "hash");
        db.Users.Add(user);

        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organization.Id, user.Id, MembershipRole.Admin));

        if (!await db.Roles.AnyAsync(r => r.Id == Role.AdminId))
        {
            db.Roles.Add(Role.Create(Role.AdminId, "Admin"));
        }

        db.RolePermissions.Add(
            RolePermission.Create(Guid.NewGuid(), Role.AdminId, PermissionKeys.ExportJobManage, true));
        db.RolePermissions.Add(
            RolePermission.Create(Guid.NewGuid(), Role.AdminId, PermissionKeys.ExportJobView, true));

        await db.SaveChangesAsync();

        return new ExportTenant(
            organization.Id, user.Id, organization.Name, string.Empty, string.Empty, string.Empty);
    }

    /// <summary>Queues a job directly, bypassing the enqueue command, for tests about the runner
    /// rather than about the request.</summary>
    public static async Task<Guid> QueueJobAsync(IAppDbContext db, ExportTenant tenant, DateTimeOffset now)
    {
        var job = ExportJob.Create(tenant.OrganizationId, tenant.AdminUserId, 5, now);
        db.ExportJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }
}
