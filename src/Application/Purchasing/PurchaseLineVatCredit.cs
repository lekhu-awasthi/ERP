using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing;

/// <summary>
/// Phase 99 -- where a purchase line's input-credit terms come from when it is written, and where a debit note's come
/// from when it is posted or reported. One place for both, so the bill's own approval, its preview, the debit note that
/// returns it and the VAT return all read a line's terms the same way.
/// </summary>
internal static class PurchaseLineVatCredit
{
    /// <summary>Each bill line's Rule 41 category: the one the line names, else its product's
    /// (<see cref="Product.VatCreditCategory"/>), else Full for a product that is not found (the handler's own product
    /// check refuses that line anyway).</summary>
    public static async Task<IReadOnlyList<VatCreditCategory>> CategoriesAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyList<(Guid ProductId, VatCreditCategory? Category)> lines,
        CancellationToken cancellationToken)
    {
        var defaults = await ProductCategoriesAsync(
            db, organizationId, [.. lines.Where(x => x.Category is null).Select(x => x.ProductId)], cancellationToken);

        return [.. lines.Select(x => x.Category ?? defaults.GetValueOrDefault(x.ProductId))];
    }

    /// <summary>The products' Rule 41 categories, read once.</summary>
    public static async Task<Dictionary<Guid, VatCreditCategory>> ProductCategoriesAsync(
        IAppDbContext db, Guid organizationId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return [];
        }

        var ids = productIds.Distinct().ToList();
        return await db.Products
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.VatCreditCategory })
            .ToDictionaryAsync(x => x.Id, x => x.VatCreditCategory, cancellationToken);
    }

    /// <summary>A source bill's lines' terms, keyed as a debit note line finds its bill line (product, rate and VAT rate,
    /// phase 19's key for the classification). Two bill lines on one key take the first, as the classification does.</summary>
    public static Dictionary<(Guid ProductId, decimal Rate, VatRate VatRate), (VatCreditUse Use, VatCreditCategory Category)>
        SourceTerms(IEnumerable<(Guid ProductId, decimal Rate, VatRate VatRate, VatCreditUse Use, VatCreditCategory Category)> billLines) =>
        billLines
            .GroupBy(x => (x.ProductId, x.Rate, x.VatRate))
            .ToDictionary(g => g.Key, g => (g.First().Use, g.First().Category));

    /// <summary>
    /// A debit note line's terms: <b>inherited from its bill's matching line</b>, the way it inherits that line's
    /// classification (the user's decision, 2026-10-29). A line with no match (a standalone note, or a converted note
    /// whose rate was changed) is Taxable with its product's own category: Rule 41 is about the goods, so the product
    /// knows it, while what the goods were used for is the bill's to say.
    /// </summary>
    public static (VatCreditUse Use, VatCreditCategory Category) ForDebitNoteLine(
        IReadOnlyDictionary<(Guid ProductId, decimal Rate, VatRate VatRate), (VatCreditUse Use, VatCreditCategory Category)>? sourceTerms,
        Guid productId,
        decimal rate,
        VatRate vatRate,
        IReadOnlyDictionary<Guid, VatCreditCategory> productCategories) =>
        sourceTerms is not null && sourceTerms.TryGetValue((productId, rate, vatRate), out var terms)
            ? terms
            : (VatCreditUse.Taxable, productCategories.GetValueOrDefault(productId));
}
