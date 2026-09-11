using ErpApp.Application.Common.Filtering;
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

        // Phase 34b -- the search and range filters compose over the *entity* queryable, before the
        // projection, rather than over the projected DTO. Filtering after a `select new Dto(...)`
        // makes EF map each member back through the projection, which it can do here but which
        // silently stops being translatable the moment a computed column joins the DTO. Filtering
        // first is the shape that keeps working.
        var journals = db.ProductionJournals
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => request.Status == null || x.Status == request.Status);

        // Phase 35a -- a composed second `Where`; see the sibling ListProductionOrdersQueryHandler
        // for why the `allowedLocations == null || …` form this replaced was untranslatable on the
        // unrestricted branch.
        if (allowedLocations is not null)
        {
            journals = journals.Where(x => x.LocationId != null && allowedLocations.Contains(x.LocationId.Value));
        }

        // Phase 35a -- the user's own Billing Location filter.
        if (request.LocationId is { } locationId)
        {
            journals = journals.Where(x => x.LocationId == locationId);
        }

        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            journals = journals.Where(x => x.Code.Contains(term) || (x.Reference != null && x.Reference.Contains(term)));
        }

        if (request.FromDate is { } fromDate)
        {
            journals = journals.Where(x => x.Date >= fromDate);
        }

        if (request.ToDate is { } toDate)
        {
            journals = journals.Where(x => x.Date <= toDate);
        }

        var query =
            from journal in journals
            join product in db.Products on journal.ProductId equals product.Id
            orderby journal.CreatedAt descending
            select new ProductionJournalListItemDto(
                journal.Id, journal.Code, journal.Date, journal.Reference, journal.ProductId, product.Name,
                journal.OutputQuantity, journal.FinishedGoodsCost, journal.Status, journal.LocationId);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
