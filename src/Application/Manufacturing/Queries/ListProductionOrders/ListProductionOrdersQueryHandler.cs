using ErpApp.Application.Common.Filtering;
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

        // Phase 34b -- filters compose over the entity queryable, before the projection. See the
        // sibling ListProductionJournalsQueryHandler for why that ordering is deliberate.
        var orders = db.ProductionOrders
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => request.Status == null || x.Status == request.Status)
            .Where(x => allowedLocations == null
                || (x.LocationId != null && allowedLocations.Contains(x.LocationId.Value)));

        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            orders = orders.Where(x => x.Code.Contains(term) || (x.Reference != null && x.Reference.Contains(term)));
        }

        if (request.FromDate is { } fromDate)
        {
            orders = orders.Where(x => x.Date >= fromDate);
        }

        if (request.ToDate is { } toDate)
        {
            orders = orders.Where(x => x.Date <= toDate);
        }

        var query =
            from order in orders
            join product in db.Products on order.ProductId equals product.Id
            orderby order.CreatedAt descending
            select new ProductionOrderListItemDto(
                order.Id, order.Code, order.Date, order.Reference, order.ProductId, product.Name,
                order.OutputQuantity, order.Status, order.CustomStatusId);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
