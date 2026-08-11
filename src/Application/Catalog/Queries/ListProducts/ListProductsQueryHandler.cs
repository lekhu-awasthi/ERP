using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Queries.ListProducts;

public sealed class ListProductsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListProductsQuery, IReadOnlyList<Product>>
{
    public async Task<IReadOnlyList<Product>> Handle(ListProductsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Products.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Type is { } type)
        {
            query = query.Where(x => x.Type == type);
        }

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }
}
