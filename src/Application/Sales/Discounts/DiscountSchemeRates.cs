using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Discounts;

/// <summary>
/// Phase 97 -- what the discount schemes give a sale at one location on one day: each product's percent (item-wise and
/// category-wise schemes) and the bill's percent by amount (slab schemes). One reader for every reader of a scheme: the
/// ERP forms' suggestion (<c>ListApplicableDiscountsQuery</c>), the till's, a restaurant dish as it is ordered, and the
/// gates that let a scheme's own percent through without a discount key (<see cref="SalesPriceGate"/>, the till sale, an
/// order's discount). A form suggests exactly what the gate keeps, phase 81's rule for a rate.
///
/// <para><b>The larger percent wins</b> (the user, 2026-10-21): a product in two schemes, or named by one and in a
/// category of another, gets the larger. A category scheme covers the category's sub-categories, and an item-wise row on a
/// product with variants covers each variant. A slab is "a bill of at least X": within a scheme the highest threshold the
/// bill reaches applies, and across schemes the larger percent. Item and slab discounts stack as a line's and a document's
/// discount always have (phase 16b): the line's first, then the bill's on what is left.</para>
///
/// <para><b>Where and when.</b> An active scheme whose dates cover the sale's day, for every location or naming the sale's.
/// A sale with no location (a document type outside the tenant's location scope) gets only the every-location schemes.</para>
/// </summary>
public sealed class DiscountSchemeRates
{
    private readonly IReadOnlyDictionary<Guid, decimal> _productRates;

    private DiscountSchemeRates(IReadOnlyDictionary<Guid, decimal> productRates, IReadOnlyList<SlabStep> slabs)
    {
        _productRates = productRates;
        Slabs = slabs;
    }

    public static DiscountSchemeRates None { get; } = new(new Dictionary<Guid, decimal>(), []);

    /// <summary>The slab schemes merged into one ladder, lowest threshold first: a bill of at least
    /// <see cref="SlabStep.MinimumAmount"/> gets <see cref="SlabStep.DiscountPct"/> until the next step.</summary>
    public IReadOnlyList<SlabStep> Slabs { get; }

    /// <summary>The scheme percent for a product the reader was asked about; zero for one no scheme names.</summary>
    public decimal ForProduct(Guid productId) => _productRates.GetValueOrDefault(productId);

    /// <summary>The products the reader was asked about that a scheme names, with their percent.</summary>
    public IReadOnlyDictionary<Guid, decimal> ProductRates => _productRates;

    /// <summary>The slab percent for a bill of <paramref name="amount"/> before VAT, in the base currency.</summary>
    public decimal ForBillAmount(decimal amount) => ForBillAmount(Slabs, amount);

    public static decimal ForBillAmount(IReadOnlyList<SlabStep> slabs, decimal amount)
    {
        var pct = 0m;
        foreach (var step in slabs)
        {
            if (amount >= step.MinimumAmount)
            {
                pct = step.DiscountPct;
            }
        }

        return pct;
    }

    public static async Task<DiscountSchemeRates> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid? locationId,
        DateOnly date,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var schemes = db.DiscountSchemes.Where(x =>
            x.OrganizationId == organizationId && x.IsActive && x.StartDate <= date && x.EndDate >= date);
        // Composed, never `locationId == null || ...` in one predicate (CLAUDE.md, phase 33).
        schemes = locationId is { } location
            ? schemes.Where(x => x.AllLocations || db.DiscountSchemeLocations.Any(l =>
                l.OrganizationId == organizationId && l.DiscountSchemeId == x.Id && l.BillingLocationId == location))
            : schemes.Where(x => x.AllLocations);

        var rows = await (
                from line in db.DiscountSchemeLines
                join scheme in schemes on line.DiscountSchemeId equals scheme.Id
                where line.OrganizationId == organizationId
                select new { scheme.Id, scheme.Kind, line.ProductId, line.CategoryId, line.MinimumAmount, line.DiscountPct })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return None;
        }

        // Slabs: each scheme's own ladder, merged at every threshold any scheme has by taking the larger percent there.
        var ladders = rows
            .Where(x => x.Kind == DiscountSchemeKind.Slab)
            .GroupBy(x => x.Id)
            .Select(g => g.Select(x => new SlabStep(x.MinimumAmount!.Value, x.DiscountPct)).OrderBy(x => x.MinimumAmount).ToList())
            .ToList();
        var thresholds = ladders.SelectMany(x => x).Select(x => x.MinimumAmount).Distinct().Order().ToList();
        var merged = new List<SlabStep>();
        foreach (var threshold in thresholds)
        {
            var pct = ladders.Max(ladder => ForBillAmount(ladder, threshold));
            if (merged.Count == 0 ? pct > 0m : pct != merged[^1].DiscountPct)
            {
                merged.Add(new SlabStep(threshold, pct));
            }
        }

        var productRates = new Dictionary<Guid, decimal>();
        var itemRows = rows.Where(x => x.Kind != DiscountSchemeKind.Slab).ToList();
        if (itemRows.Count > 0 && productIds.Count > 0)
        {
            var ids = productIds.Distinct().ToList();
            var products = await db.Products
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
                .Select(x => new { x.Id, x.ParentProductId, x.CategoryId })
                .ToListAsync(cancellationToken);

            var parentOf = itemRows.Any(x => x.CategoryId is not null)
                ? await db.ProductCategories
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId)
                    .ToDictionaryAsync(x => x.Id, x => x.ParentCategoryId, cancellationToken)
                : [];

            var byProduct = itemRows.Where(x => x.ProductId is not null)
                .GroupBy(x => x.ProductId!.Value).ToDictionary(g => g.Key, g => g.Max(x => x.DiscountPct));
            var byCategory = itemRows.Where(x => x.CategoryId is not null)
                .GroupBy(x => x.CategoryId!.Value).ToDictionary(g => g.Key, g => g.Max(x => x.DiscountPct));

            foreach (var product in products)
            {
                var pct = Math.Max(
                    byProduct.GetValueOrDefault(product.Id),
                    product.ParentProductId is { } parent ? byProduct.GetValueOrDefault(parent) : 0m);

                // The category and each of its ancestors, guarding against a cycle a bad edit could leave.
                var seen = new HashSet<Guid>();
                for (Guid? category = product.CategoryId; category is { } c && seen.Add(c); category = parentOf.GetValueOrDefault(c))
                {
                    pct = Math.Max(pct, byCategory.GetValueOrDefault(c));
                }

                if (pct > 0m)
                {
                    productRates[product.Id] = pct;
                }
            }
        }

        return new DiscountSchemeRates(productRates, merged);
    }
}

/// <summary>A bill of at least <paramref name="MinimumAmount"/> before VAT gets <paramref name="DiscountPct"/>.</summary>
public sealed record SlabStep(decimal MinimumAmount, decimal DiscountPct);
