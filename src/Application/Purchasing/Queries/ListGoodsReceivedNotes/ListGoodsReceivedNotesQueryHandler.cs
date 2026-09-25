using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListGoodsReceivedNotes;

/// <summary>Every clause here is <c>ListPurchaseOrdersQueryHandler</c>'s, with its reasoning; see that
/// handler for the why of each. The search additionally matches the tracking number, which is a
/// column of this list live.</summary>
public sealed class ListGoodsReceivedNotesQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListGoodsReceivedNotesQuery, PagedResult<GoodsReceivedNote>>
{
    public async Task<PagedResult<GoodsReceivedNote>> Handle(
        ListGoodsReceivedNotesQuery request, CancellationToken cancellationToken)
    {
        var query = db.GoodsReceivedNotes.Where(x => x.OrganizationId == request.OrganizationId);

        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, request.PermissionKey, cancellationToken);

        if (allowedLocations is not null)
        {
            query = query.Where(x => x.LocationId != null && allowedLocations.Contains(x.LocationId.Value));
        }

        if (request.LocationId is { } locationId)
        {
            query = query.Where(x => x.LocationId == locationId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Code.Contains(term)
                || (x.Reference != null && x.Reference.Contains(term))
                || (x.TrackingNo != null && x.TrackingNo.Contains(term)));
        }

        if (request.FromDate is { } fromDate)
        {
            query = query.Where(x => x.Date >= fromDate);
        }

        if (request.ToDate is { } toDate)
        {
            query = query.Where(x => x.Date <= toDate);
        }

        IOrderedQueryable<GoodsReceivedNote> Order(IQueryable<GoodsReceivedNote> source) => request.Sort switch
        {
            ListSort.DocumentDate => source.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt),
            _ => source.OrderByDescending(x => x.CreatedAt),
        };

        return await query.ToKeyPagedResultAsync(x => x.Id, Order, request.Page, request.PageSize, cancellationToken);
    }
}
