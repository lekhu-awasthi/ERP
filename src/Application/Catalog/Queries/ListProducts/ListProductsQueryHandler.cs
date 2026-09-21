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
        // Phase 52 -- the unit matrix rides the list, because every document line picker is fed by
        // this query and the unit control in the Qty cell has to know which units a product offers
        // the moment the product is chosen. This is what the reference product does too: its own
        // line-grid picker calls `products-minimized?...&unit=true` and gets `secondary_units[]`
        // embedded per product (read live 2026-09-17).
        //
        // Phase 54 measured it, and corrected the premise above while doing so. `listAllProducts`
        // does NOT ask for a very large page: MAX_PAGE_SIZE is 200, so the join is over 200 rows,
        // not over the 20,001 a big tenant holds. On the phase-34c dataset with 20,000 secondary
        // units seeded, the Include costs +377 logical reads (+6.8%) on the 200-row picker page and
        // +336 on the 50-row grid page -- the same ~350 for four times the child rows, i.e. the cost
        // of a seek into the child table rather than a per-row lookup. Both search paths are
        // unchanged at 452. Kept: it buys the thing the unit control cannot work without, and the
        // alternative is a round trip per line. Numbers and method: tools/scale/comparison-phase54.md.
        var query = db.Products
            .Include(x => x.SecondaryUnits)
            .Where(x => x.OrganizationId == request.OrganizationId);

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

        // Phase 36 -- available at this billing location: restricted to it, or not restricted at
        // all. A separate composed `.Where()` for the same reason the search below is one, and the
        // empty-set-means-everywhere rule is the whole reason for the first disjunct.
        if (request.LocationId is { } locationId)
        {
            query = query.Where(x => x.Locations.Count == 0 || x.Locations.Any(l => l.LocationId == locationId));
        }

        // Phase 34b (NFR-6.1) -- the list search. A separate composed `.Where()`, never folded
        // into one predicate with a null check: an expression tree does not short-circuit, so
        // `term == null || x.Code.Contains(term)` hands EF a null to translate on the unrestricted
        // branch, which is almost every caller (phase-33's gotcha).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term) || (x.Sku != null && x.Sku.Contains(term)));
        }

        return await query.ToKeyPagedResultAsync(
            x => x.Id, q => q.OrderBy(x => x.Name), request.Page, request.PageSize, cancellationToken);
    }
}
