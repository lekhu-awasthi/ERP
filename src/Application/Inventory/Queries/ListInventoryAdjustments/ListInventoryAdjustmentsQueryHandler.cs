using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.ListInventoryAdjustments;

public sealed class ListInventoryAdjustmentsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListInventoryAdjustmentsQuery, IReadOnlyList<InventoryAdjustment>>
{
    public async Task<IReadOnlyList<InventoryAdjustment>> Handle(
        ListInventoryAdjustmentsQuery request, CancellationToken cancellationToken)
    {
        var query = db.InventoryAdjustments.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
