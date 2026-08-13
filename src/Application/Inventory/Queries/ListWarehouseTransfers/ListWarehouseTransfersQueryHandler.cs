using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.ListWarehouseTransfers;

public sealed class ListWarehouseTransfersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListWarehouseTransfersQuery, IReadOnlyList<WarehouseTransfer>>
{
    public async Task<IReadOnlyList<WarehouseTransfer>> Handle(
        ListWarehouseTransfersQuery request, CancellationToken cancellationToken)
    {
        var query = db.WarehouseTransfers.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
