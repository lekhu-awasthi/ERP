using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Imports;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Imports;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Imports;

/// <summary>
/// One tenant, one Admin user with real role grants, and the master data an import row resolves
/// against by name. The role grants are real rows rather than a bypassed check, because
/// "AuthorizationBehavior re-checks the permission on every row" is one of this phase's load-bearing
/// claims and a test that stubbed it would prove nothing.
/// </summary>
internal sealed record ImportTenant(
    Guid OrganizationId, Guid AdminUserId, Guid CategoryId, Guid UnitId, Guid ContactGroupId);

internal static class ImportTestSeed
{
    public const string CategoryName = "Snacks";
    public const string UnitName = "Box";
    public const string ContactGroupName = "Kathmandu";

    public static readonly string[] ProductHeaders =
    [
        "Product Code", "HS Code", "Product Type", "Product Name", "Category",
        "VAT Applicable", "Primary Unit", "Selling Price", "Purchase Price",
        "Reorder Level", "Track Inventory", "Available For Sale",
    ];

    public static readonly string[] SupplierHeaders =
    [
        "Code", "Supplier Name", "Contact Group", "Phone No", "Email", "Address", "PAN", "Opening Balance",
    ];

    public static async Task<ImportTenant> SeedAsync(
        IAppDbContext db, params string[] grantedPermissionKeys)
    {
        var organization = Organization.Create(
            "Acme Traders", "Trading", null, new DateOnly(2026, 4, 1), true,
            $"ws-{Guid.NewGuid():N}", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);

        var user = User.Register("Ram Bahadur", $"admin-{Guid.NewGuid():N}@acme.test", "9800000000", "hash");
        db.Users.Add(user);

        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organization.Id, user.Id, MembershipRole.Admin));

        // Admin is a shared system role (OrganizationId is null), so a test that seeds two tenants
        // must not add it twice.
        if (!await db.Roles.AnyAsync(r => r.Id == Role.AdminId))
        {
            db.Roles.Add(Role.Create(Role.AdminId, "Admin"));
        }

        var keys = grantedPermissionKeys.Length > 0
            ? grantedPermissionKeys
            : [PermissionKeys.ImportJobManage, PermissionKeys.ProductManage, PermissionKeys.ContactManage];

        foreach (var key in keys)
        {
            db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.AdminId, key, true));
        }

        var category = ProductCategory.Create(organization.Id, CategoryName, null);
        var unit = UnitOfMeasurement.Create(organization.Id, UnitName, "box");
        var contactGroup = ContactGroup.Create(organization.Id, ContactGroupName, null);
        db.ProductCategories.Add(category);
        db.UnitsOfMeasurement.Add(unit);
        db.ContactGroups.Add(contactGroup);

        await db.SaveChangesAsync();

        return new ImportTenant(organization.Id, user.Id, category.Id, unit.Id, contactGroup.Id);
    }

    /// <summary>
    /// Queues a job and stores a placeholder blob for it. The blob's <i>content</i> is irrelevant --
    /// <see cref="ErpApp.Application.UnitTests.TestSupport.StubImportFileReader"/> supplies the parsed
    /// sheet -- but the key must resolve, because the processor opens the file before it parses it
    /// and a missing key is (correctly) a job failure rather than a test-setup no-op.
    /// </summary>
    public static async Task<Guid> QueueJobAsync(
        IAppDbContext db,
        ImportTenant tenant,
        ImportEntityType entityType,
        ImportMode mode,
        DateTimeOffset now,
        IFileStorage fileStorage)
    {
        using var placeholder = new MemoryStream([0x50, 0x4B]);
        var storageKey = await fileStorage.SaveAsync(placeholder, "upload.xlsx");

        var job = ImportJob.Create(
            tenant.OrganizationId, entityType, mode, storageKey, "upload.xlsx", tenant.AdminUserId, now);

        db.ImportJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>A product row with the required columns filled and the optional ones defaulted.</summary>
    public static string?[] ProductRow(
        string name,
        string? code = null,
        string type = "Goods",
        string category = CategoryName,
        string vat = "Yes",
        string unit = UnitName,
        string? sellingPrice = "100",
        string? purchasePrice = "80",
        string? reorderLevel = "5",
        string? trackInventory = "Yes",
        string? availableForSale = "Yes") =>
        [code, "1905", type, name, category, vat, unit, sellingPrice, purchasePrice, reorderLevel, trackInventory, availableForSale];

    public static string?[] SupplierRow(
        string name,
        string? code = null,
        string? group = ContactGroupName,
        string? phone = "9841768644",
        string? email = "accounts@example.com",
        string? address = "Kathmandu-32",
        string? pan = "304567847",
        string? openingBalance = "0") =>
        [code, name, group, phone, email, address, pan, openingBalance];
}
