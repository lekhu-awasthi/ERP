using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.ListCurrencyCatalog;

public sealed class ListCurrencyCatalogQueryHandler(IAppDbContext db)
    : IRequestHandler<ListCurrencyCatalogQuery, IReadOnlyList<CurrencyCatalogEntryDto>>
{
    public async Task<IReadOnlyList<CurrencyCatalogEntryDto>> Handle(
        ListCurrencyCatalogQuery request, CancellationToken cancellationToken)
    {
        var activated = await db.Currencies
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);

        var activatedSet = activated.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return CurrencyCatalog.All
            .Select(x => CurrencyCatalogEntryDto.From(x, activatedSet.Contains(x.Code)))
            .ToList();
    }
}
