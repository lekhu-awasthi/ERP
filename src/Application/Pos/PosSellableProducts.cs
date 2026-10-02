using ErpApp.Application.Catalog;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Pos.Queries.ListPosProducts;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos;

/// <summary>
/// The till's product grid, read for any till: what is sellable at one location, by category, by
/// search, or by an exact code. Phase 62 wrote it inside <c>ListPosProductsQueryHandler</c>; phase 64
/// lifted it here rather than copying it, because the restaurant's order screen reads the same grid
/// under its own key (<c>Pos.Order.Operate</c> -- a waiter is not a cashier) and two copies of "what is
/// sellable here" is how one would drift (phase 62 Decision D's reason for lifting
/// <c>ToExclusiveRate</c>).
/// </summary>
internal static class PosSellableProducts
{
    public static async Task<PagedResult<PosProductDto>> ListAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid locationId,
        string? search,
        string? code,
        Guid? categoryId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var basis = await PriceBasisAsync(db, organizationId, cancellationToken);

        // Each condition its own composed .Where(): an expression tree does not short-circuit, so a
        // null-guarded term inside one predicate hands EF a null to translate (phase 33).
        var query = db.Products
            .AsNoTracking()
            .Include(x => x.SecondaryUnits)
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.AvailableForSale && x.IsActive && !x.HasVariants)
            .Where(x => x.Locations.Count == 0 || x.Locations.Any(l => l.LocationId == locationId));

        if (categoryId is { } category)
        {
            query = query.Where(x => x.CategoryId == category);
        }

        // Exact, for a scanner: single-argument equality is case-insensitive on SQL Server's
        // collation, as Contains is (phase 34b).
        if (SearchTerm.Normalize(code) is { } exact)
        {
            query = query.Where(x => x.Barcode == exact || x.Code == exact || x.Sku == exact);
        }

        if (SearchTerm.Normalize(search) is { } term)
        {
            query = query.Where(x =>
                x.Name.Contains(term) || x.Code.Contains(term)
                || (x.Sku != null && x.Sku.Contains(term)) || (x.Barcode != null && x.Barcode.Contains(term)));
        }

        var page = await query.ToKeyPagedResultAsync(
            x => x.Id, q => q.OrderBy(x => x.Name), pageNumber, pageSize, cancellationToken);

        // Projected after the page is fetched (phase 42/56): the unit names are one read for the page.
        var unitIds = page.Items
            .SelectMany(p => p.SecondaryUnits.Select(u => u.UnitId).Append(p.PrimaryUnitId))
            .Distinct()
            .ToList();

        var unitNames = await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == organizationId && unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ShortName, cancellationToken);

        var items = page.Items
            .Select(p =>
            {
                var rate = ProductPrices.ToExclusiveRate(p.SellingPrice, p.VatRate, basis);

                // The primary first, synthesised from the product -- phase 45's shape, the one the
                // ERP's line-unit control renders. A secondary whose unit row is gone would be a blank
                // option, so it is dropped, as that control drops it.
                var units = new List<PosProductUnitDto>
                {
                    new(p.PrimaryUnitId, unitNames.GetValueOrDefault(p.PrimaryUnitId, ""), 1m, rate),
                };

                units.AddRange(p.SecondaryUnits
                    .Where(u => unitNames.ContainsKey(u.UnitId))
                    .OrderBy(u => u.ConversionRate)
                    .Select(u => new PosProductUnitDto(
                        u.UnitId, unitNames[u.UnitId], u.ConversionRate,
                        ProductPrices.ToExclusiveRate(u.SellingPrice, p.VatRate, basis))));

                return new PosProductDto(
                    p.Id, p.Code, p.Name, p.CategoryId, p.Type, p.VatRate, rate, p.ServiceChargeApplicable,
                    p.BatchTracking, p.SerialTracking, p.Barcode, units);
            })
            .ToList();

        return new PagedResult<PosProductDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public static async Task<ProductPriceBasis> PriceBasisAsync(
        IAppDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        await db.TenantSettings
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => (ProductPriceBasis?)x.ProductPriceBasis)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Tenant settings not found.");
}
