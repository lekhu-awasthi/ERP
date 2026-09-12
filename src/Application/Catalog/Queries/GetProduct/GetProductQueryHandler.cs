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
            // Phase 36 -- the form has to be able to show the location set it is about to
            // overwrite. A detail query projecting or under-including is the default failure here:
            // phase 35a found 14 of 15 detail reads dropping a field the write path stored.
            .Include(x => x.Locations)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");
    }
}
