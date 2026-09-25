using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Reports;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryVarianceReport;

public sealed class InventoryVarianceReportQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<InventoryVarianceReportQuery, InventoryVarianceReportDto>
{
    public async Task<InventoryVarianceReportDto> Handle(
        InventoryVarianceReportQuery request, CancellationToken cancellationToken)
    {
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var products = await InventoryReportProducts.LoadAsync(
            db, request.OrganizationId, request.CategoryId, request.ProductId, cancellationToken);

        // Both ledgers through the one reader Inventory Position uses, over every warehouse, as of
        // the same day -- so each column agrees with that report in the matching mode.
        var book = await StockFactReader.LoadMovementsAsync(
            db, InventoryTrackingMode.AccountingMovement, request.OrganizationId, products.MatchingIds,
            warehouseId: null, request.AsOfDate, cancellationToken, request.LocationId, reportLocations);
        var actual = await StockFactReader.LoadMovementsAsync(
            db, InventoryTrackingMode.PhysicalMovement, request.OrganizationId, products.MatchingIds,
            warehouseId: null, request.AsOfDate, cancellationToken, request.LocationId, reportLocations);

        // Summarised from a start after the as-of date, so every movement counts as opening: the
        // balance is all that is wanted, and it is Opening + In - Out either way.
        var fromDate = request.AsOfDate.AddDays(1);
        var bookBalances = StockFactReader.Summarise(book, fromDate).ToDictionary(x => x.ProductId, x => x.BalanceQuantity);
        var actualBalances = StockFactReader.Summarise(actual, fromDate).ToDictionary(x => x.ProductId, x => x.BalanceQuantity);

        var rows = bookBalances.Keys.Union(actualBalances.Keys)
            .Select(productId =>
            {
                var bookQuantity = bookBalances.GetValueOrDefault(productId);
                var actualQuantity = actualBalances.GetValueOrDefault(productId);
                var product = products.For(productId);
                return new InventoryVarianceRowDto(
                    productId,
                    product?.Code ?? string.Empty,
                    product?.Name ?? string.Empty,
                    product?.CategoryName ?? string.Empty,
                    product?.Unit ?? string.Empty,
                    bookQuantity,
                    actualQuantity,
                    Math.Abs(actualQuantity - bookQuantity),
                    actualQuantity > bookQuantity
                        ? InventoryVarianceDirection.ToBeShipped
                        : InventoryVarianceDirection.ToBeReceived);
            })
            .Where(row => row.BookBalance != row.ActualBalance)
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);
        var mode = await StockFactReader.ResolveModeAsync(db, request.OrganizationId, requested: null, cancellationToken);

        return new InventoryVarianceReportDto(
            request.AsOfDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount, mode);
    }
}
