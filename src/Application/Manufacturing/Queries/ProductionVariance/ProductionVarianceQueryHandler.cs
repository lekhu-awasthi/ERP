using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Queries.ProductionVariance;

public sealed class ProductionVarianceQueryHandler(IAppDbContext db)
    : IRequestHandler<ProductionVarianceQuery, PagedResult<ProductionVarianceRowDto>>
{
    public async Task<PagedResult<ProductionVarianceRowDto>> Handle(
        ProductionVarianceQuery request, CancellationToken cancellationToken)
    {
        var query = db.ProductionJournals
            .Include(x => x.RawMaterials).Include(x => x.ByProducts)
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.Status == ProductionJournalStatus.Approved
                && x.BillOfMaterialsId != null
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

        var bomIds = journals.Select(x => x.BillOfMaterialsId!.Value).Distinct().ToList();
        var boms = await db.BillsOfMaterials
            .Include(x => x.RawMaterials).Include(x => x.ByProducts)
            .Where(x => x.OrganizationId == request.OrganizationId && bomIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var productIds = journals.Select(x => x.ProductId)
            .Concat(journals.SelectMany(x => x.RawMaterials.Select(l => l.ProductId)))
            .Concat(journals.SelectMany(x => x.ByProducts.Select(l => l.ProductId)))
            .Concat(boms.Values.SelectMany(b => b.RawMaterials.Select(l => l.ProductId)))
            .Concat(boms.Values.SelectMany(b => b.ByProducts.Select(l => l.ProductId)));
        var products = await ProductLabels.LoadAsync(db, request.OrganizationId, productIds, cancellationToken);

        var rows = new List<ProductionVarianceRowDto>();

        foreach (var journal in journals)
        {
            if (!boms.TryGetValue(journal.BillOfMaterialsId!.Value, out var bom) || bom.OutputQuantity <= 0)
            {
                continue;
            }

            // Scale the plan to this run's own output before comparing -- see the query's remarks.
            var scale = journal.OutputQuantity / bom.OutputQuantity;

            var planned = bom.RawMaterials
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => (g.Key, IsByProduct: false), g => g.Sum(x => x.Quantity) * scale);

            foreach (var group in bom.ByProducts.GroupBy(x => x.ProductId))
            {
                planned[(group.Key, IsByProduct: true)] = group.Sum(x => x.Quantity) * scale;
            }

            var actual = journal.RawMaterials
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => (g.Key, IsByProduct: false), g => g.Sum(x => x.Quantity));

            foreach (var group in journal.ByProducts.GroupBy(x => x.ProductId))
            {
                actual[(group.Key, IsByProduct: true)] = group.Sum(x => x.Quantity);
            }

            // Union of both sides, so a material the run used but the plan never mentioned (and a
            // planned material the run skipped entirely) both show up. Either is exactly the kind
            // of deviation this report exists to surface.
            var keys = planned.Keys.Concat(actual.Keys).Distinct()
                .OrderBy(k => k.IsByProduct)
                .ThenBy(k => products.GetValueOrDefault(k.Item1)?.Name ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            var lines = keys.Select(key =>
            {
                var bomQuantity = planned.GetValueOrDefault(key);
                var voucherQuantity = actual.GetValueOrDefault(key);
                var variance = bomQuantity - voucherQuantity;
                var product = products.GetValueOrDefault(key.Item1);

                return new ProductionVarianceLineDto(
                    key.Item1,
                    product?.Name ?? string.Empty,
                    product?.Code ?? string.Empty,
                    product?.UnitName,
                    key.IsByProduct,
                    voucherQuantity,
                    bomQuantity,
                    variance,
                    bomQuantity == 0 ? null : variance / bomQuantity * 100m);
            }).ToList();

            rows.Add(new ProductionVarianceRowDto(
                journal.Id,
                journal.Date,
                journal.Code,
                journal.Reference,
                journal.ProductId,
                products.GetValueOrDefault(journal.ProductId)?.Name ?? string.Empty,
                journal.OutputQuantity,
                lines));
        }

        return request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);
    }
}
