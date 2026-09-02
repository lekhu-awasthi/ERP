using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Queries.GetProductionJournal;

public sealed class GetProductionJournalQueryHandler(IAppDbContext db)
    : IRequestHandler<GetProductionJournalQuery, ProductionJournalDetailDto>
{
    public async Task<ProductionJournalDetailDto> Handle(
        GetProductionJournalQuery request, CancellationToken cancellationToken)
    {
        var journal = await db.ProductionJournals
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Production journal not found.");

        var productIds = new List<Guid> { journal.ProductId };
        productIds.AddRange(journal.RawMaterials.Select(x => x.ProductId));
        productIds.AddRange(journal.ByProducts.Select(x => x.ProductId));
        var products = await ProductLabels.LoadAsync(db, request.OrganizationId, productIds, cancellationToken);

        var costTermIds = journal.Expenses.Select(x => x.CostTermId).Distinct().ToList();
        var costTerms = await db.CostTerms
            .Where(x => x.OrganizationId == request.OrganizationId && costTermIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var glLines = await db.GlJournalEntries
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.SourceDocumentType == DocumentType.ProductionJournal && x.SourceDocumentId == journal.Id)
            .SelectMany(x => x.Lines)
            .Select(x => new ProductionGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit))
            .ToListAsync(cancellationToken);

        var finished = products.GetValueOrDefault(journal.ProductId);

        return new ProductionJournalDetailDto(
            journal.Id,
            journal.Code,
            journal.Date,
            journal.Reference,
            journal.ProductId,
            finished?.Name ?? string.Empty,
            finished?.Code ?? string.Empty,
            finished?.UnitName,
            journal.OutputQuantity,
            journal.WarehouseId,
            journal.BillOfMaterialsId,
            journal.Notes,
            journal.Status,
            journal.ReferrerType,
            journal.ReferrerId,
            journal.RawMaterialCost,
            journal.ProductionExpenseCost,
            journal.TotalCostOfProduction,
            journal.CostAllocatedToByProduct,
            journal.FinishedGoodsCost,
            journal.FinishedGoodsUnitCost,
            journal.CostRoundingAdjustment,
            journal.ApprovedAt,
            journal.VoidedAt,
            journal.CreatedAt,
            [.. journal.RawMaterials.Select(line =>
            {
                var product = products.GetValueOrDefault(line.ProductId);
                return new ProductionJournalRawMaterialLineDto(
                    line.Id, line.ProductId, product?.Name ?? string.Empty, product?.Code ?? string.Empty,
                    product?.UnitName, line.Quantity, line.ConsumedUnitCost, line.Amount);
            })],
            [.. journal.ByProducts.Select(line =>
            {
                var product = products.GetValueOrDefault(line.ProductId);
                return new ProductionJournalByProductLineDto(
                    line.Id, line.ProductId, product?.Name ?? string.Empty, product?.Code ?? string.Empty,
                    product?.UnitName, line.CostAllocationPct, line.Quantity, line.AllocatedUnitCost, line.AllocatedAmount);
            })],
            [.. journal.Expenses.Select(line => new ProductionJournalExpenseLineDto(
                line.Id, line.CostTermId, costTerms.GetValueOrDefault(line.CostTermId) ?? string.Empty, line.Amount))],
            glLines.Count > 0 ? glLines : null);
    }
}
