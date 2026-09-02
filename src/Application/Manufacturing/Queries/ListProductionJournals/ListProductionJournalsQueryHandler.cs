using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ListProductionJournals;

public sealed class ListProductionJournalsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListProductionJournalsQuery, PagedResult<ProductionJournalListItemDto>>
{
    public async Task<PagedResult<ProductionJournalListItemDto>> Handle(
        ListProductionJournalsQuery request, CancellationToken cancellationToken)
    {
        var query =
            from journal in db.ProductionJournals
            join product in db.Products on journal.ProductId equals product.Id
            where journal.OrganizationId == request.OrganizationId
                && (request.Status == null || journal.Status == request.Status)
            orderby journal.CreatedAt descending
            select new ProductionJournalListItemDto(
                journal.Id, journal.Code, journal.Date, journal.Reference, journal.ProductId, product.Name,
                journal.OutputQuantity, journal.FinishedGoodsCost, journal.Status);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
