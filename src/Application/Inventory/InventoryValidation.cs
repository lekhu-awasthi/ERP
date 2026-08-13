using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory;

/// <summary>Shared existence checks reused by every Inventory Create/Update handler -- mirrors
/// Sales.SalesValidation/Purchasing.PurchasingValidation's precedent (one small validation helper
/// per module, not a shared cross-module one).</summary>
internal static class InventoryValidation
{
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

    /// <summary>A Service product has no physical stock to move/adjust -- WarehouseTransfer and
    /// InventoryAdjustment both require every line's Product to be Type=Goods, same "Service
    /// Products never touch stock" gate Invoice/PurchaseBill's approval handlers apply. Called
    /// after EnsureProductsExistAsync, so a not-found product is reported as not-found rather than
    /// this less-specific message.</summary>
    public static async Task EnsureProductsAreGoodsAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var distinctIds = productIds.Distinct().ToList();

        var nonGoodsCount = await db.Products.CountAsync(
            x => x.OrganizationId == organizationId && distinctIds.Contains(x.Id) && x.Type != ProductType.Goods,
            cancellationToken);

        if (nonGoodsCount > 0)
        {
            throw new ConflictException("Only Goods products can be moved or adjusted in inventory -- Service products carry no stock.");
        }
    }
}
