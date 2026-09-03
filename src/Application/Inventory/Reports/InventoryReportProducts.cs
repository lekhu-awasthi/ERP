using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Reports;

/// <summary>
/// The product-side facts every phase-26c inventory report prints beside its numbers -- the
/// "Code/Goods" column the live reports render as <c>Name (Code)</c>, the Category name, and the
/// primary unit's short name for the UOM column -- loaded once for the whole report rather than
/// per row.
///
/// <para>It also owns the <b>product filter</b>, because two of the three narrowings a caller can
/// ask for (Category, Product) have to be resolved against <c>Product</c> before
/// <see cref="StockFactReader"/> can use them: a <c>StockMovement</c> knows its product id but
/// nothing about that product's category. Resolving here means the reports state their filter once
/// and get a product-id list back, and it is also what lets a category filter that matches nothing
/// return an empty report rather than an unfiltered one.</para>
/// </summary>
internal sealed class InventoryReportProducts
{
    internal sealed record ProductFacts(
        Guid Id, string Name, string Code, Guid CategoryId, string CategoryName, string Unit, bool IsActive)
    {
        /// <summary>The live reports' "Code/Goods" column, verbatim: <c>Name (Code)</c>.</summary>
        public string Display => $"{Name} ({Code})";
    }

    private readonly Dictionary<Guid, ProductFacts> _byId;

    private InventoryReportProducts(Dictionary<Guid, ProductFacts> byId, IReadOnlyList<Guid>? matchingIds)
    {
        _byId = byId;
        MatchingIds = matchingIds;
    }

    /// <summary>
    /// The ids that survive the caller's Category/Product filter, or null when neither was given --
    /// null meaning "do not narrow", which lets <see cref="StockFactReader"/> skip a pointless
    /// <c>Contains</c> over every product in the tenant.
    /// </summary>
    public IReadOnlyList<Guid>? MatchingIds { get; }

    public ProductFacts? For(Guid productId) => _byId.GetValueOrDefault(productId);

    public static async Task<InventoryReportProducts> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid? categoryId,
        Guid? productId,
        CancellationToken cancellationToken)
    {
        var products = await db.Products
            .Where(p => p.OrganizationId == organizationId)
            .Select(p => new { p.Id, p.Name, p.Code, p.CategoryId, p.PrimaryUnitId, p.IsActive })
            .ToListAsync(cancellationToken);

        var categories = await db.ProductCategories
            .Where(c => c.OrganizationId == organizationId)
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var units = await db.UnitsOfMeasurement
            .Where(u => u.OrganizationId == organizationId)
            .Select(u => new { u.Id, u.ShortName })
            .ToDictionaryAsync(u => u.Id, u => u.ShortName, cancellationToken);

        var byId = products.ToDictionary(
            p => p.Id,
            p => new ProductFacts(
                p.Id,
                p.Name,
                p.Code,
                p.CategoryId,
                categories.GetValueOrDefault(p.CategoryId, string.Empty),
                units.GetValueOrDefault(p.PrimaryUnitId, string.Empty),
                p.IsActive));

        IReadOnlyList<Guid>? matching = null;
        if (categoryId is not null || productId is not null)
        {
            matching = byId.Values
                .Where(p => (categoryId is null || p.CategoryId == categoryId)
                    && (productId is null || p.Id == productId))
                .Select(p => p.Id)
                .ToList();
        }

        return new InventoryReportProducts(byId, matching);
    }
}
