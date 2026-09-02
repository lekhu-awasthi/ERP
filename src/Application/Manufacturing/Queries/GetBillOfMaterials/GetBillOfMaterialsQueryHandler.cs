using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Queries.GetBillOfMaterials;

public sealed class GetBillOfMaterialsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetBillOfMaterialsQuery, BillOfMaterialsDetailDto>
{
    public async Task<BillOfMaterialsDetailDto> Handle(
        GetBillOfMaterialsQuery request, CancellationToken cancellationToken)
    {
        var bom = await db.BillsOfMaterials
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Bill of materials not found.");

        var productIds = new List<Guid> { bom.ProductId };
        productIds.AddRange(bom.RawMaterials.Select(x => x.ProductId));
        productIds.AddRange(bom.ByProducts.Select(x => x.ProductId));

        var products = await ProductLabels.LoadAsync(db, request.OrganizationId, productIds, cancellationToken);

        var costTermIds = bom.Expenses.Select(x => x.CostTermId).Distinct().ToList();
        var costTerms = await db.CostTerms
            .Where(x => x.OrganizationId == request.OrganizationId && costTermIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var finished = products.GetValueOrDefault(bom.ProductId);

        return new BillOfMaterialsDetailDto(
            bom.Id,
            bom.ProductId,
            finished?.Name ?? string.Empty,
            finished?.Code ?? string.Empty,
            finished?.UnitName,
            bom.OutputQuantity,
            bom.ManufactureOnEverySale,
            bom.Notes,
            bom.IsActive,
            bom.CreatedAt,
            [.. bom.RawMaterials.Select(line =>
            {
                var product = products.GetValueOrDefault(line.ProductId);
                return new BomRawMaterialLineDto(
                    line.Id, line.ProductId, product?.Name ?? string.Empty, product?.Code ?? string.Empty,
                    product?.UnitName, line.Quantity, line.Quantity / bom.OutputQuantity);
            })],
            [.. bom.ByProducts.Select(line =>
            {
                var product = products.GetValueOrDefault(line.ProductId);
                return new BomByProductLineDto(
                    line.Id, line.ProductId, product?.Name ?? string.Empty, product?.Code ?? string.Empty,
                    product?.UnitName, line.CostAllocationPct, line.Quantity, line.Quantity / bom.OutputQuantity);
            })],
            [.. bom.Expenses.Select(line => new BomExpenseLineDto(
                line.Id,
                line.CostTermId,
                costTerms.GetValueOrDefault(line.CostTermId) ?? string.Empty,
                line.Amount,
                line.Amount / bom.OutputQuantity))]);
    }
}
