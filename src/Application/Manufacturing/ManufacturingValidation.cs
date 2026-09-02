using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing;

/// <summary>Shared existence checks reused by every Manufacturing Create/Update handler -- mirrors
/// Sales.SalesValidation/Purchasing.PurchasingValidation/Inventory.InventoryValidation's precedent
/// (one small validation helper per module, not a shared cross-module one).</summary>
internal static class ManufacturingValidation
{
    /// <summary>
    /// Phase 24's sweep, extended to this phase's four new line-taking handlers. Every product a
    /// BOM, Production Order or Production Journal names goes through ProductVariantRules, so a
    /// variant <i>parent</i> can never reach a stock bucket nothing receives into.
    /// ProductVariantSweepGuardTests fails the build if a handler here skips it.
    /// </summary>
    public static async Task EnsureProductsExistAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        await ProductVariantRules.EnsureProductsExistAndAreTransactableAsync(
            db, organizationId, productIds, cancellationToken);
    }

    /// <summary>A Service product carries no stock, so it can be neither consumed nor produced --
    /// the same gate WarehouseTransfer and InventoryAdjustment apply, and for the same reason.
    /// Called after EnsureProductsExistAsync so a missing product reports as not-found.</summary>
    public static async Task EnsureProductsAreGoodsAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var distinctIds = productIds.Distinct().ToList();

        var nonGoodsCount = await db.Products.CountAsync(
            x => x.OrganizationId == organizationId && distinctIds.Contains(x.Id) && x.Type != ProductType.Goods,
            cancellationToken);

        if (nonGoodsCount > 0)
        {
            throw new ConflictException(
                "Only Goods products can be manufactured or consumed in production -- Service products carry no stock.");
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

    /// <summary>
    /// Phase 20c built CostTerm with two categories precisely so this check could exist: a
    /// production form must offer Production Cost terms and never landed-cost ones. Checked here
    /// rather than only in the UI, because the category is what gives the term its meaning in the
    /// roll-up.
    /// </summary>
    public static async Task EnsureCostTermsAreProductionCostsAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> costTermIds, CancellationToken cancellationToken)
    {
        var distinctIds = costTermIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return;
        }

        var found = await db.CostTerms
            .Where(x => x.OrganizationId == organizationId && distinctIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Category })
            .ToListAsync(cancellationToken);

        if (found.Count != distinctIds.Count)
        {
            throw new NotFoundException("One or more cost terms were not found.");
        }

        if (found.Exists(x => x.Category != CostTermCategory.ProductionCost))
        {
            throw new ConflictException(
                "Only Production Cost terms can be used on a bill of materials, production order or production journal.");
        }
    }

    /// <summary>A BOM referenced by a Production Order/Journal must belong to this tenant. It is
    /// deliberately <i>not</i> required to match the document's finished product: the BOM is a
    /// template the user chose to load, and re-checking it here would turn a default into a
    /// constraint (Decision D).</summary>
    public static async Task EnsureBillOfMaterialsExistsAsync(
        IAppDbContext db, Guid organizationId, Guid? billOfMaterialsId, CancellationToken cancellationToken)
    {
        if (billOfMaterialsId is not { } id)
        {
            return;
        }

        var exists = await db.BillsOfMaterials.AnyAsync(
            x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Bill of materials not found.");
        }
    }
}
