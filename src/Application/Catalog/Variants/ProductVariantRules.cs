using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Variants;

/// <summary>
/// The one rule that makes Phase 24's stock reconcile, and the only sweep the phase needed.
///
/// Because a variant IS a Product (docs/phase-24-status.md's Decision A), nothing downstream had to
/// learn a new key -- but one new thing became possible that must never happen: transacting the
/// *parent* of a variant matrix. The reference product does offer the parent in its line picker;
/// we refuse it. Selling "T-Shirt" when the sellable things are "T-Shirt / L / Blue" and
/// "T-Shirt / XL / Red" creates a fourth stock bucket that nothing ever receives into, so Stock
/// Position would carry a parent balance reconciling against nothing while every total still added
/// up -- exactly the failure the roadmap's exit criterion exists to catch.
///
/// **This is the complete sweep, and it is four call sites**: SalesValidation, PurchasingValidation
/// and InventoryValidation's EnsureProductsExistAsync (between them every Quotation, SalesOrder,
/// Invoice, CreditNote, PurchaseOrder, PurchaseBill, DebitNote, WarehouseTransfer and
/// InventoryAdjustment line, create and update), plus CreateOrUpdateOpeningStockLine, which reads
/// its single product directly rather than through a helper. ProductVariantSweepGuardTests asserts
/// that list is still exhaustive by reflecting over the handlers rather than trusting this comment.
/// </summary>
public static class ProductVariantRules
{
    /// <summary>Existence + transactability in one round trip. Keeps
    /// NotFoundException("One or more products were not found.") for the missing case, unchanged
    /// from the three helpers this replaced, so existing 404 behaviour is untouched.</summary>
    public static async Task EnsureProductsExistAndAreTransactableAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var distinctIds = productIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return;
        }

        var found = await db.Products
            .Where(x => x.OrganizationId == organizationId && distinctIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.HasVariants })
            .ToListAsync(cancellationToken);

        if (found.Count != distinctIds.Count)
        {
            throw new NotFoundException("One or more products were not found.");
        }

        var parent = found.Find(x => x.HasVariants);
        if (parent is not null)
        {
            throw new ConflictException(
                $"'{parent.Name}' has variants, so it cannot be used on a document line directly -- pick one of its variants instead.");
        }
    }

    /// <summary>Single-product form, for the one caller that already holds the entity.</summary>
    public static void EnsureTransactable(string productName, bool hasVariants)
    {
        if (hasVariants)
        {
            throw new ConflictException(
                $"'{productName}' has variants, so it cannot be used on a document line directly -- pick one of its variants instead.");
        }
    }
}
