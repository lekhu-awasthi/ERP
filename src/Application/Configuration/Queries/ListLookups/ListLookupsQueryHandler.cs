using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Queries.ListLookups;

public sealed class ListLookupsQueryHandler<TLookup>(IAppDbContext db)
    : IRequestHandler<ListLookupsQuery<TLookup>, IReadOnlyList<TLookup>>
    where TLookup : class, ITenantLookupEntity
{
    public async Task<IReadOnlyList<TLookup>> Handle(ListLookupsQuery<TLookup> request, CancellationToken cancellationToken)
    {
        // EF.Property (string-keyed) rather than x.OrganizationId/x.Name -- when TLookup is a
        // generic type parameter constrained by an interface, the compiler emits the member
        // access against the *interface's* PropertyInfo, which EF's translator can't reliably
        // match back to the concrete entity's mapped property. EF.Property resolves by name
        // against the model directly, sidestepping that.
        return await db.Set<TLookup>()
            .Where(x => EF.Property<Guid>(x, nameof(ITenantLookupEntity.OrganizationId)) == request.OrganizationId)
            .OrderBy(x => EF.Property<string>(x, nameof(ITenantLookupEntity.Name)))
            .ToListAsync(cancellationToken);
    }
}
