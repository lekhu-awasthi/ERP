using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Queries.ListProducts;

public sealed class ListProductsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListProductsQuery, PagedResult<Product>>
{
    public async Task<PagedResult<Product>> Handle(ListProductsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Products.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Type is { } type)
        {
            query = query.Where(x => x.Type == type);
        }

        // Written as an explicit switch rather than a shared predicate helper: EF Core cannot
        // translate a captured Func inside Where (CLAUDE.md's generic-Func gotcha, phase-9 bug #1),
        // and there are only two non-default cases.
        query = request.VariantFilter switch
        {
            ProductVariantFilter.Transactable => query.Where(x => !x.HasVariants),
            ProductVariantFilter.VariantParents => query.Where(x => x.HasVariants),
            _ => query,
        };

        // Phase 34b (NFR-6.1) -- the list search. A separate composed `.Where()`, never folded
        // into one predicate with a null check: an expression tree does not short-circuit, so
        // `term == null || x.Code.Contains(term)` hands EF a null to translate on the unrestricted
        // branch, which is almost every caller (phase-33's gotcha).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term) || (x.Sku != null && x.Sku.Contains(term)));
        }

        return await query.OrderBy(x => x.Name).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
