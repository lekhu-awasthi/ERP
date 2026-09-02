using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Queries.GetBomTemplate;

/// <summary>
/// Returns null rather than 404 when the product has no BOM: "this product has no recipe" is an
/// ordinary answer to LOAD BOM, not an error, and the UI simply leaves the lines alone.
/// </summary>
public sealed class GetBomTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetBomTemplateQuery, BomTemplateDto?>
{
    public async Task<BomTemplateDto?> Handle(GetBomTemplateQuery request, CancellationToken cancellationToken)
    {
        var bom = await db.BillsOfMaterials
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId && x.IsActive,
                cancellationToken);

        if (bom is null || request.OutputQuantity <= 0)
        {
            return null;
        }

        var scale = request.OutputQuantity / bom.OutputQuantity;

        var productIds = bom.RawMaterials.Select(x => x.ProductId).Concat(bom.ByProducts.Select(x => x.ProductId));
        var products = await ProductLabels.LoadAsync(db, request.OrganizationId, productIds, cancellationToken);

        var costTermIds = bom.Expenses.Select(x => x.CostTermId).Distinct().ToList();
        var costTerms = await db.CostTerms
            .Where(x => x.OrganizationId == request.OrganizationId && costTermIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return new BomTemplateDto(
            bom.Id,
            bom.OutputQuantity,
            request.OutputQuantity,
            [.. bom.RawMaterials.Select(line =>
            {
                var product = products.GetValueOrDefault(line.ProductId);
                return new BomTemplateRawMaterialDto(
                    line.ProductId, product?.Name ?? string.Empty, product?.Code ?? string.Empty, line.Quantity * scale);
            })],
            [.. bom.ByProducts.Select(line =>
            {
                var product = products.GetValueOrDefault(line.ProductId);

                // The percentage is a ratio already, so it is the one figure that does not scale.
                return new BomTemplateByProductDto(
                    line.ProductId, product?.Name ?? string.Empty, product?.Code ?? string.Empty,
                    line.CostAllocationPct, line.Quantity * scale);
            })],
            [.. bom.Expenses.Select(line => new BomTemplateExpenseDto(
                line.CostTermId, costTerms.GetValueOrDefault(line.CostTermId) ?? string.Empty, line.Amount * scale))]);
    }
}
