using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Queries.GetProduct;

public sealed class GetProductQueryHandler(IAppDbContext db) : IRequestHandler<GetProductQuery, Product>
{
    public async Task<Product> Handle(GetProductQuery request, CancellationToken cancellationToken)
    {
        return await db.Products
            .Include(x => x.SecondaryUnits)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");
    }
}
