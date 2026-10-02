using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;

namespace ErpApp.Application.Pos.Queries.ListPosProducts;

public sealed class ListPosProductsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPosProductsQuery, PagedResult<PosProductDto>>
{
    public async Task<PagedResult<PosProductDto>> Handle(
        ListPosProductsQuery request, CancellationToken cancellationToken)
    {
        // The till's own refusals first (inactive, no till, no entitlement), so a grid is never
        // painted for a location that cannot sell.
        var till = await PosTill.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);

        // Phase 64 -- the reading itself is shared with the restaurant's order screen.
        return await PosSellableProducts.ListAsync(
            db, request.OrganizationId, till.Location.Id, request.Search, request.Code, request.CategoryId,
            request.Page, request.PageSize, cancellationToken);
    }
}
