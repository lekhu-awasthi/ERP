using ErpApp.Application.Catalog;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.ListPosProducts;

public sealed class ListPosProductsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPosProductsQuery, PagedResult<PosProductDto>>
{
    public async Task<PagedResult<PosProductDto>> Handle(
        ListPosProductsQuery request, CancellationToken cancellationToken)
    {
        // The till's own refusals first (inactive, no till, no entitlement), so a grid is never
        // painted for a location that cannot sell.
        var till = await PosTill.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);
        var locationId = till.Location.Id;

        var basis = await db.TenantSettings
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => (ProductPriceBasis?)x.ProductPriceBasis)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        // Each condition its own composed .Where(): an expression tree does not short-circuit, so a
        // null-guarded term inside one predicate hands EF a null to translate (phase 33).
        var query = db.Products
            .AsNoTracking()
            .Include(x => x.SecondaryUnits)
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.AvailableForSale && x.IsActive && !x.HasVariants)
            .Where(x => x.Locations.Count == 0 || x.Locations.Any(l => l.LocationId == locationId));

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(x => x.CategoryId == categoryId);
        }

        // Exact, for a scanner: single-argument equality is case-insensitive on SQL Server's
        // collation, as Contains is (phase 34b).
        if (SearchTerm.Normalize(request.Code) is { } code)
        {
            query = query.Where(x => x.Barcode == code || x.Code == code || x.Sku == code);
        }

        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x =>
                x.Name.Contains(term) || x.Code.Contains(term)
                || (x.Sku != null && x.Sku.Contains(term)) || (x.Barcode != null && x.Barcode.Contains(term)));
        }

        var page = await query.ToKeyPagedResultAsync(
            x => x.Id, q => q.OrderBy(x => x.Name), request.Page, request.PageSize, cancellationToken);

        // Projected after the page is fetched (phase 42/56): the unit names are one read for the page.
        var unitIds = page.Items
            .SelectMany(p => p.SecondaryUnits.Select(u => u.UnitId).Append(p.PrimaryUnitId))
            .Distinct()
            .ToList();

        var unitNames = await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == request.OrganizationId && unitIds.Contains(x.Id))
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
}
