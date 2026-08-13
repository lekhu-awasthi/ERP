using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.CreateWarehouseTransfer;

public sealed class CreateWarehouseTransferCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateWarehouseTransferCommand, CreateWarehouseTransferResult>
{
    public async Task<CreateWarehouseTransferResult> Handle(
        CreateWarehouseTransferCommand request, CancellationToken cancellationToken)
    {
        await InventoryValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.FromWarehouseId, cancellationToken);
        await InventoryValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.ToWarehouseId, cancellationToken);
        await InventoryValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);
        await InventoryValidation.EnsureProductsAreGoodsAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var warehouseTransfer = WarehouseTransfer.Create(
            request.OrganizationId, request.FromWarehouseId, request.ToWarehouseId, request.Date, request.Reference);

        foreach (var line in request.Lines)
        {
            warehouseTransfer.AddLine(line.ProductId, line.Quantity);
        }

        db.WarehouseTransfers.Add(warehouseTransfer);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateWarehouseTransferResult(warehouseTransfer.Id, warehouseTransfer.Code, warehouseTransfer.Status);
    }
}
