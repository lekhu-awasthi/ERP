using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Variants;

/// <summary>The single creation path shared by the one-at-a-time command and the matrix generator,
/// so "what a variant is" is decided once.</summary>
public static class ProductVariantFactory
{
    /// <summary>
    /// Creates one variant child, or returns null if that exact combination already exists.
    /// Returning null rather than throwing is what makes generation re-runnable: adding a fifth
    /// colour and regenerating fills only the new combinations. The single-add command turns the
    /// null into a 409, because there the user asked for one specific thing.
    /// </summary>
    public static async Task<Product?> TryCreateAsync(
        IAppDbContext db,
        IDocumentNumberGenerator numberGenerator,
        Product parent,
        IReadOnlyList<VariantCombinationInput> combination,
        string? name,
        string? sku,
        string? barcode,
        decimal sellingPrice,
        decimal purchasePrice,
        VariantCatalogLookup catalog,
        CancellationToken cancellationToken)
    {
        var pairs = combination.Select(x => (x.AttributeId, x.OptionId)).ToList();
        var key = Product.BuildCombinationKey(pairs);

        var exists = await db.Products.AnyAsync(
            x => x.ParentProductId == parent.Id && x.CombinationKey == key, cancellationToken);

        if (exists)
        {
            return null;
        }

        var composed = string.IsNullOrWhiteSpace(name)
            ? ProductVariantMapper.ComposeName(
                parent.Name, combination.Select(x => catalog.OptionValues.GetValueOrDefault(x.OptionId, string.Empty)))
            : name;

        var code = await numberGenerator.GetNextNumberAsync(
            parent.OrganizationId, DocumentType.Product, cancellationToken);

        try
        {
            var variant = parent.CreateVariant(code, composed, pairs, sellingPrice, purchasePrice, sku, barcode);
            db.Products.Add(variant);
            return variant;
        }
        catch (InvalidOperationException ex)
        {
            // Domain guards (option not offered, two values of one attribute, negative price) are
            // user error from an API caller's point of view, not a 500.
            throw new ConflictException(ex.Message);
        }
    }
}
