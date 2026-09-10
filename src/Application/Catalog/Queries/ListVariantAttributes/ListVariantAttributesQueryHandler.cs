using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Queries.ListVariantAttributes;

public sealed class ListVariantAttributesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListVariantAttributesQuery, PagedResult<VariantAttributeResult>>
{
    public async Task<PagedResult<VariantAttributeResult>> Handle(
        ListVariantAttributesQuery request, CancellationToken cancellationToken)
    {
        var query = db.VariantAttributes
            .Include(x => x.Options)
            .Where(x => x.OrganizationId == request.OrganizationId);

        if (request.ActiveOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        // Phase 34b (NFR-6.1) -- the list search. A separate composed `.Where()`, never folded
        // into one predicate with a null check: an expression tree does not short-circuit, so
        // `term == null || x.Code.Contains(term)` hands EF a null to translate on the unrestricted
        // branch, which is almost every caller (phase-33's gotcha).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Name.Contains(term));
        }

        var page = await query.OrderBy(x => x.Name)
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        return new PagedResult<VariantAttributeResult>(
            page.Items.Select(VariantAttributeMapper.ToResult).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}
