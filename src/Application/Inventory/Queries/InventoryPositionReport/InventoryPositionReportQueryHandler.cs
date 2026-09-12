using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Configuration;
using ErpApp.Application.Inventory.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

        // Phase 36 -- the drawer's Reporting Tags, applied to the movements rather than to the rows:
        // a product carries no tag, its documents do (Domain.Common.DocumentMechanisms.ReportingTags).
        var taggedDocuments = await ReportingTagFilter.ResolveMatchingDocumentsAsync(
            db, request.OrganizationId, request.TagOptionIds, cancellationToken);

        if (taggedDocuments is not null)
        {
            movements = [.. movements.Where(m => taggedDocuments.Contains((m.SourceDocumentType, m.SourceDocumentId)))];
        }

        // Phase 36 -- "Group by Warehouse": one row per product per warehouse. Summarised per
        // warehouse group through the same reader rather than by re-deriving balances, so a grouped
        // row and an ungrouped one cannot disagree.
        var warehouseNames = request.GroupByWarehouse
            ? await db.Warehouses
                .Where(x => x.OrganizationId == request.OrganizationId)
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : [];

        List<(Guid? WarehouseId, List<StockFactReader.Movement> Movements)> groups = request.GroupByWarehouse
            ? [.. movements.GroupBy(m => m.WarehouseId).Select(g => ((Guid?)g.Key, g.ToList()))]
            : [(null, movements)];

        var rows = groups
            .SelectMany(group => StockFactReader.Summarise(group.Movements, request.FromDate)
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
                        facts.BalanceValue,
                        group.WarehouseId is { } warehouseId ? warehouseNames.GetValueOrDefault(warehouseId) : null);
                }))
            .Where(row => request.BalanceFilter switch
            {
                InventoryBalanceFilter.PositiveOnly => row.Quantity > 0,
                InventoryBalanceFilter.NegativeOnly => row.Quantity < 0,
                _ => true,
            })
            .OrderBy(row => row.Warehouse ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Product, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new InventoryPositionReportDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(row => row.Quantity), rows.Sum(row => row.Amount));
    }
}
