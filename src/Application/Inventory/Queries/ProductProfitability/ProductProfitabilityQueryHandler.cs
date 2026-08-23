using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.ProductProfitability;

public sealed class ProductProfitabilityQueryHandler(IAppDbContext db)
    : IRequestHandler<ProductProfitabilityQuery, ProductProfitabilityDto>
{
    public async Task<ProductProfitabilityDto> Handle(ProductProfitabilityQuery request, CancellationToken cancellationToken)
    {
        var productsQuery = db.Products.Where(x => x.OrganizationId == request.OrganizationId);
        if (request.ProductCategoryId is { } categoryId)
        {
            productsQuery = productsQuery.Where(x => x.CategoryId == categoryId);
        }
        if (request.ProductId is { } productId)
        {
            productsQuery = productsQuery.Where(x => x.Id == productId);
        }
        var products = await productsQuery
            .Select(x => new { x.Id, x.Code, x.Name, x.CategoryId })
            .ToListAsync(cancellationToken);
        var productIds = products.Select(x => x.Id).ToList();

        var categoryNames = await db.ProductCategories
            .Where(x => products.Select(p => p.CategoryId).Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var openingLayers = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.ProductId) && x.TransactionDate < request.FromDate)
            .Select(x => new { x.ProductId, Value = x.QuantityRemaining * x.UnitCost })
            .ToListAsync(cancellationToken);
        var openingByProduct = openingLayers.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Value));

        var closingLayers = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.ProductId) && x.TransactionDate <= request.ToDate)
            .Select(x => new { x.ProductId, Value = x.QuantityRemaining * x.UnitCost })
            .ToListAsync(cancellationToken);
        var closingByProduct = closingLayers.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Value));

        var purchaseBillIds = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var purchaseByProduct = await db.PurchaseBillLines
            .Where(x => purchaseBillIds.Contains(x.PurchaseBillId) && productIds.Contains(x.ProductId))
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Amount, cancellationToken);

        var invoiceIds = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var salesLines = await db.InvoiceLines
            .Where(x => invoiceIds.Contains(x.InvoiceId) && productIds.Contains(x.ProductId))
            .Select(x => new { x.ProductId, x.Amount, x.Quantity, x.CogsUnitCost })
            .ToListAsync(cancellationToken);
        var salesByProduct = salesLines.GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => (
                Sales: g.Sum(x => x.Amount),
                CostOfSales: g.Sum(x => (x.CogsUnitCost ?? 0) * x.Quantity)));

        var rows = products.Select(p =>
        {
            var (sales, costOfSales) = salesByProduct.GetValueOrDefault(p.Id);
            var grossProfit = sales - costOfSales;
            return new ProductProfitabilityRowDto(
                p.Id, p.Code, p.Name, categoryNames.GetValueOrDefault(p.CategoryId, "—"),
                OpeningBalance: openingByProduct.GetValueOrDefault(p.Id),
                Purchase: purchaseByProduct.GetValueOrDefault(p.Id),
                ProductionCost: 0, AdditionalCost: 0,
                ClosingBalance: closingByProduct.GetValueOrDefault(p.Id),
                CostOfSales: costOfSales, Sales: sales, Consumption: 0,
                GrossProfit: grossProfit,
                GrossMarginPct: sales == 0 ? 0 : grossProfit / sales * 100m);
        })
        .OrderBy(x => x.ProductName)
        .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new ProductProfitabilityDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(x => x.Sales), rows.Sum(x => x.CostOfSales), rows.Sum(x => x.GrossProfit));
    }
}
