using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ListProductionOrders;

public sealed class ListProductionOrdersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListProductionOrdersQuery, PagedResult<ProductionOrderListItemDto>>
{
    public async Task<PagedResult<ProductionOrderListItemDto>> Handle(
        ListProductionOrdersQuery request, CancellationToken cancellationToken)
    {
        var query =
            from order in db.ProductionOrders
            join product in db.Products on order.ProductId equals product.Id
            where order.OrganizationId == request.OrganizationId
                && (request.Status == null || order.Status == request.Status)
            orderby order.CreatedAt descending
            select new ProductionOrderListItemDto(
                order.Id, order.Code, order.Date, order.Reference, order.ProductId, product.Name,
                order.OutputQuantity, order.Status);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
