using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Queries.ProductionSummary;

public sealed class ProductionSummaryQueryHandler(IAppDbContext db)
    : IRequestHandler<ProductionSummaryQuery, ProductionSummaryReportDto>
{
    public async Task<ProductionSummaryReportDto> Handle(
        ProductionSummaryQuery request, CancellationToken cancellationToken)
    {
        // Approved only: a Draft has no costs at all, and a Void one is a run that did not happen.
        var query = db.ProductionJournals
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.Status == ProductionJournalStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate);

        if (request.ProductId is { } productId)
        {
            query = query.Where(x => x.ProductId == productId);
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(x => db.Products.Any(p => p.Id == x.ProductId && p.CategoryId == categoryId));
        }

        var journals = await query.OrderByDescending(x => x.Date).ThenBy(x => x.Code).ToListAsync(cancellationToken);

        // Totals over the whole filtered set, before paging (phase-16c bug #1).
        var totals = new ProductionSummaryTotalsDto(
            journals.Sum(x => x.RawMaterialCost ?? 0),
            journals.Sum(x => x.ProductionExpenseCost ?? 0),
            journals.Sum(x => x.CostAllocatedToByProduct ?? 0),
            journals.Sum(x => x.FinishedGoodsCost ?? 0));

        var productIds = journals.Select(x => x.ProductId)
            .Concat(journals.SelectMany(x => x.RawMaterials.Select(l => l.ProductId)))
            .Concat(journals.SelectMany(x => x.ByProducts.Select(l => l.ProductId)));
        var products = await ProductLabels.LoadAsync(db, request.OrganizationId, productIds, cancellationToken);

        var costTermIds = journals.SelectMany(x => x.Expenses.Select(l => l.CostTermId)).Distinct().ToList();
        var costTerms = await db.CostTerms
            .Where(x => x.OrganizationId == request.OrganizationId && costTermIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        ProductionSummaryItemDto Item(Guid id, decimal quantity, decimal? rate, decimal? amount)
        {
            var product = products.GetValueOrDefault(id);
            return new ProductionSummaryItemDto(
                id, product?.Name ?? string.Empty, product?.Code ?? string.Empty, product?.UnitName,
                quantity, rate, amount);
        }

        var rows = journals.Select(journal => new ProductionSummaryRowDto(
            journal.Id,
            journal.Date,
            journal.Code,
            journal.Reference,
            Item(journal.ProductId, journal.OutputQuantity, journal.FinishedGoodsUnitCost, journal.FinishedGoodsCost),
            [.. journal.RawMaterials.Select(l => Item(l.ProductId, l.Quantity, l.ConsumedUnitCost, l.Amount))],
            [.. journal.ByProducts.Select(l => Item(l.ProductId, l.Quantity, l.AllocatedUnitCost, l.AllocatedAmount))],
            [.. journal.Expenses.Select(l => new ProductionSummaryExpenseDto(
                costTerms.GetValueOrDefault(l.CostTermId) ?? string.Empty, l.Amount))],
            journal.RawMaterialCost ?? 0,
            journal.ProductionExpenseCost ?? 0,
            journal.TotalCostOfProduction ?? 0,
            journal.CostAllocatedToByProduct ?? 0,
            journal.FinishedGoodsCost ?? 0)).ToList();

        return new ProductionSummaryReportDto(
            request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize),
            totals);
    }
}
