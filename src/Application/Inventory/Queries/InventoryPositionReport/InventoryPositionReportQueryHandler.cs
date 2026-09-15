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

        // Phase 44 -- "Display Warehouse in Column": the same per-warehouse split, rendered across
        // instead of down. One row per product again, with a quantity column per warehouse.
        //
        // Live (Moonbeam 2026-09-15) this is a modifier of Group by Warehouse -- its checkbox is
        // disabled until that one is ticked -- so the validator refuses it on its own rather than
        // silently ignoring it (phase-43's rule: a field an aggregate will not honour should be
        // refused, not accepted and dropped).
        //
        // <b>Where this codebase knowingly diverges.</b> On the live tenant, Group by Warehouse
        // *alone* changed nothing at all: same 319 rows, same columns, same totals, on a tenant with
        // four warehouses. Phase 36 had already built it as a row split, which is what its name
        // plainly means and which is useful. Matching the live no-op would mean deleting a working
        // feature to reproduce what looks like a defect, so the row split stays and the column
        // crosstab is added beside it. Recorded rather than quietly reconciled.
        if (request.DisplayWarehouseInColumn)
        {
            return await CrosstabAsync(request, movements, products, cancellationToken);
        }

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

    /// <summary>
    /// Phase 44 -- the Display Warehouse in Column view: one row per product, one quantity column
    /// per warehouse, Qty the signed total across them, Rate and Amount single.
    ///
    /// <para>Both halves come from the <b>same</b> <c>StockFactReader.Summarise</c> the row-per-
    /// warehouse view uses -- the per-warehouse cells from a summary per warehouse group, the Qty,
    /// Rate and Amount from a summary over every movement. That is phase-36's rule for this report
    /// restated: a grouped figure and an ungrouped one must not be able to disagree, so neither is
    /// re-derived.</para>
    /// </summary>
    private async Task<InventoryPositionReportDto> CrosstabAsync(
        InventoryPositionReportQuery request,
        IReadOnlyList<StockFactReader.Movement> movements,
        InventoryReportProducts products,
        CancellationToken cancellationToken)
    {
        // Every warehouse in the tenant, in a stable order -- see WarehouseColumns for why not only
        // the ones carrying a balance.
        var warehouses = await db.Warehouses
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);
        var columns = warehouses
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byWarehouse = movements
            .GroupBy(m => m.WarehouseId)
            .ToDictionary(
                g => g.Key,
                g => StockFactReader.Summarise([.. g], request.FromDate)
                    .ToDictionary(f => f.ProductId, f => f.BalanceQuantity));

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
                    facts.BalanceValue,
                    Warehouse: null,
                    WarehouseQuantities:
                    [
                        .. columns.Select(c =>
                            byWarehouse.TryGetValue(c.Id, out var quantities)
                                ? quantities.GetValueOrDefault(facts.ProductId)
                                : 0m),
                    ]);
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
            rows.Sum(x => x.Quantity), rows.Sum(x => x.Amount),
            WarehouseColumns: [.. columns.Select(c => c.Name)]);
    }
}
