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

        return await query.OrderBy(x => x.Name).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
