using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ListProductionJournals;

public sealed class ListProductionJournalsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListProductionJournalsQuery, PagedResult<ProductionJournalListItemDto>>
{
    public async Task<PagedResult<ProductionJournalListItemDto>> Handle(
        ListProductionJournalsQuery request, CancellationToken cancellationToken)
    {

        // Phase 32b -- see LocationAccessScope: null (no filter) for every caller holding the key
        // organization-wide, a narrowed set for one granted it only at particular locations.
        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, request.PermissionKey, cancellationToken);

        var query =
            from journal in db.ProductionJournals
            join product in db.Products on journal.ProductId equals product.Id
            where journal.OrganizationId == request.OrganizationId
                && (request.Status == null || journal.Status == request.Status)
                && (allowedLocations == null
                    || (journal.LocationId != null && allowedLocations.Contains(journal.LocationId.Value)))
            orderby journal.CreatedAt descending
            select new ProductionJournalListItemDto(
                journal.Id, journal.Code, journal.Date, journal.Reference, journal.ProductId, product.Name,
                journal.OutputQuantity, journal.FinishedGoodsCost, journal.Status);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
