using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Reports;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryPositionReport;

public sealed class InventoryPositionReportQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<InventoryPositionReportQuery, InventoryPositionReportDto>
{
    public async Task<InventoryPositionReportDto> Handle(
        InventoryPositionReportQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change. Narrows rows in addition to request.LocationId, which is the
        // user's own filter -- two mechanisms, two reasons, both applied.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var products = await InventoryReportProducts.LoadAsync(
            db, request.OrganizationId, request.CategoryId, request.ProductId, cancellationToken);

        var movements = await StockFactReader.LoadMovementsAsync(
            db, request.OrganizationId, products.MatchingIds, request.WarehouseId, request.ToDate, cancellationToken,
            request.LocationId, reportLocations);

        var rows = StockFactReader.Summarise(movements, request.FromDate)
            .Select(facts =>
            {
                var product = products.For(facts.ProductId);
                return new InventoryPositionRowDto(
                    facts.ProductId,
                    product?.Display ?? string.Empty,
                    product?.CategoryName ?? string.Empty,
                    facts.BalanceQuantity,
                    product?.Unit ?? string.Empty,
                    StockFactReader.Rate(facts.BalanceValue, facts.BalanceQuantity),
                    facts.BalanceValue);
            })
            .Where(row => request.BalanceFilter switch
            {
                InventoryBalanceFilter.PositiveOnly => row.Quantity > 0,
                InventoryBalanceFilter.NegativeOnly => row.Quantity < 0,
                _ => true,
            })
            .OrderBy(row => row.Product, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new InventoryPositionReportDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(row => row.Quantity), rows.Sum(row => row.Amount));
    }
}
