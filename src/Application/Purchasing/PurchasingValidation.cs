using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing;

/// <summary>Shared existence checks reused by every Purchasing Create/Update handler -- mirrors
/// Sales.SalesValidation's precedent, Contact type filtered to Supplier instead of Customer.</summary>
internal static class PurchasingValidation
{
    public static async Task EnsureSupplierExistsAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, CancellationToken cancellationToken)
    {
        var exists = await db.Contacts.AnyAsync(
            x => x.Id == contactId && x.OrganizationId == organizationId && x.Type == ContactType.Supplier, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Supplier not found.");
        }
    }

    public static async Task EnsureProductsExistAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var distinctIds = productIds.Distinct().ToList();

        var existingCount = await db.Products.CountAsync(
            x => x.OrganizationId == organizationId && distinctIds.Contains(x.Id), cancellationToken);

        if (existingCount != distinctIds.Count)
        {
            throw new NotFoundException("One or more products were not found.");
        }
    }

    public static async Task EnsureWarehouseExistsAsync(
        IAppDbContext db, Guid organizationId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var exists = await db.Warehouses.AnyAsync(
            x => x.Id == warehouseId && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Warehouse not found.");
        }
    }

    public static async Task<decimal> ResolveTdsAmountAsync(
        IAppDbContext db, Guid organizationId, Guid? tdsTypeId, decimal tdsBaseAmount, CancellationToken cancellationToken)
    {
        if (tdsTypeId is not { } id)
        {
            return 0;
        }

        var tdsType = await db.TdsTypes.SingleOrDefaultAsync(
            x => x.Id == id && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("TDS type not found.");

        return Math.Round(tdsBaseAmount * tdsType.RatePct / 100m, 4);
    }
}
