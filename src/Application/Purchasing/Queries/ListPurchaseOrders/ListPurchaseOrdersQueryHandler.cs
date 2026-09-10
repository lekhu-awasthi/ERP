using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseOrders;

public sealed class ListPurchaseOrdersQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListPurchaseOrdersQuery, PagedResult<PurchaseOrder>>
{
    public async Task<PagedResult<PurchaseOrder>> Handle(ListPurchaseOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = db.PurchaseOrders.Where(x => x.OrganizationId == request.OrganizationId);

        // Phase 32b -- a caller granted this key only at certain billing locations sees only
        // those locations' rows, rather than being refused the list outright. Null (and so no
        // filter at all) for everyone holding the key organization-wide, which is every caller
        // on every tenant that has not opened the Location-specific permission section.
        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, request.PermissionKey, cancellationToken);

        if (allowedLocations is not null)
        {
            query = query.Where(x => x.LocationId != null && allowedLocations.Contains(x.LocationId.Value));
        }

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        // Phase 34b (NFR-6.1) -- the list search. A separate composed `.Where()`, never folded
        // into one predicate with a null check: an expression tree does not short-circuit, so
        // `term == null || x.Code.Contains(term)` hands EF a null to translate on the unrestricted
        // branch, which is almost every caller (phase-33's gotcha).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Code.Contains(term) || (x.Reference != null && x.Reference.Contains(term)));
        }

        // Phase 34b -- the shell's global date range, over this document's own business date. See
        // `IDateRangeFilteredQuery`: only aggregates with a business date implement it.
        if (request.FromDate is { } fromDate)
        {
            query = query.Where(x => x.Date >= fromDate);
        }

        if (request.ToDate is { } toDate)
        {
            query = query.Where(x => x.Date <= toDate);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
