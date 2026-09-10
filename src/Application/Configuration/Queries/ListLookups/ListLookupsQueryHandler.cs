using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Queries.ListLookups;

public sealed class ListLookupsQueryHandler<TLookup>(IAppDbContext db)
    : IRequestHandler<ListLookupsQuery<TLookup>, PagedResult<TLookup>>
    where TLookup : class, ITenantLookupEntity
{
    public async Task<PagedResult<TLookup>> Handle(ListLookupsQuery<TLookup> request, CancellationToken cancellationToken)
    {
        // EF.Property (string-keyed) rather than x.OrganizationId/x.Name -- when TLookup is a
        // generic type parameter constrained by an interface, the compiler emits the member
        // access against the *interface's* PropertyInfo, which EF's translator can't reliably
        // match back to the concrete entity's mapped property. EF.Property resolves by name
        // against the model directly, sidestepping that.
        var query = db.Set<TLookup>()
            .Where(x => EF.Property<Guid>(x, nameof(ITenantLookupEntity.OrganizationId)) == request.OrganizationId);

        // Phase 34b (NFR-6.1) -- the list search. One edit here gives a search box to every lookup
        // screen in Configurations at once: credit terms, payment modes, TDS types, banks, cost
        // terms, reporting tags, contact and product groups, units, warehouses and the rest all
        // route through this one generic handler.
        //
        // EF.Property again, for the same reason the filter and ordering above use it: TLookup is a
        // generic parameter constrained by an interface, so `x.Name` compiles to a member access
        // against the *interface's* PropertyInfo that EF's translator cannot match back to the
        // concrete entity (phase-2 bug #1).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => EF.Property<string>(x, nameof(ITenantLookupEntity.Name)).Contains(term));
        }

        return await query
            .OrderBy(x => EF.Property<string>(x, nameof(ITenantLookupEntity.Name)))
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
