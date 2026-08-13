using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.GetWarehouseTransfer;

public sealed class GetWarehouseTransferQueryHandler(IAppDbContext db)
    : IRequestHandler<GetWarehouseTransferQuery, WarehouseTransferDetailDto>
{
    public async Task<WarehouseTransferDetailDto> Handle(GetWarehouseTransferQuery request, CancellationToken cancellationToken)
    {
        var warehouseTransfer = await db.WarehouseTransfers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Warehouse transfer not found.");

        return new WarehouseTransferDetailDto(
            warehouseTransfer.Id,
            warehouseTransfer.OrganizationId,
            warehouseTransfer.Code,
            warehouseTransfer.Date,
            warehouseTransfer.Reference,
            warehouseTransfer.FromWarehouseId,
            warehouseTransfer.ToWarehouseId,
            warehouseTransfer.Status,
            warehouseTransfer.ApprovedByUserId,
            warehouseTransfer.ApprovedAt,
            warehouseTransfer.CreatedAt,
            warehouseTransfer.Lines.Select(x => new WarehouseTransferLineDto(x.Id, x.ProductId, x.Quantity)).ToList());
    }
}
