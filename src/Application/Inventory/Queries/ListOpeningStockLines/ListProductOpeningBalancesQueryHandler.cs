using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.ListOpeningStockLines;

public sealed class ListProductOpeningBalancesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListProductOpeningBalancesQuery, PagedResult<ProductOpeningBalanceDto>>
{
    public async Task<PagedResult<ProductOpeningBalanceDto>> Handle(
        ListProductOpeningBalancesQuery request, CancellationToken cancellationToken)
    {
        var query =
            from product in db.Products
            join category in db.ProductCategories on product.CategoryId equals category.Id
            join line in db.OpeningStockLines.Where(
                    x => x.OrganizationId == request.OrganizationId && x.WarehouseId == request.WarehouseId)
                on product.Id equals line.ProductId into lines
            from line in lines.DefaultIfEmpty()
            where product.OrganizationId == request.OrganizationId && product.TrackInventory
            orderby product.Code
            select new ProductOpeningBalanceDto(
                product.Id, product.Code, product.Name, category.Name,
                line == null ? 0m : line.Quantity, line == null ? 0m : line.Rate,
                line == null ? 0m : line.Quantity * line.Rate);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
