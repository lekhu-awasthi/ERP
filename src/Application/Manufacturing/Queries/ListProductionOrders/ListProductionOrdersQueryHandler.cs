using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ListProductionOrders;

public sealed class ListProductionOrdersQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListProductionOrdersQuery, PagedResult<ProductionOrderListItemDto>>
{
    public async Task<PagedResult<ProductionOrderListItemDto>> Handle(
        ListProductionOrdersQuery request, CancellationToken cancellationToken)
    {

        // Phase 32b -- see LocationAccessScope: null (no filter) for every caller holding the key
        // organization-wide, a narrowed set for one granted it only at particular locations.
        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, request.PermissionKey, cancellationToken);

        var query =
            from order in db.ProductionOrders
            join product in db.Products on order.ProductId equals product.Id
            where order.OrganizationId == request.OrganizationId
                && (request.Status == null || order.Status == request.Status)
                && (allowedLocations == null
                    || (order.LocationId != null && allowedLocations.Contains(order.LocationId.Value)))
            orderby order.CreatedAt descending
            select new ProductionOrderListItemDto(
                order.Id, order.Code, order.Date, order.Reference, order.ProductId, product.Name,
                order.OutputQuantity, order.Status, order.CustomStatusId);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
