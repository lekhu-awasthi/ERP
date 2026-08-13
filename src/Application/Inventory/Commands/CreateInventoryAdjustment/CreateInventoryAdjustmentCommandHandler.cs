using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.CreateInventoryAdjustment;

public sealed class CreateInventoryAdjustmentCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateInventoryAdjustmentCommand, CreateInventoryAdjustmentResult>
{
    public async Task<CreateInventoryAdjustmentResult> Handle(
        CreateInventoryAdjustmentCommand request, CancellationToken cancellationToken)
    {
        await InventoryValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await InventoryValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);
        await InventoryValidation.EnsureProductsAreGoodsAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var inventoryAdjustment = InventoryAdjustment.Create(request.OrganizationId, request.WarehouseId, request.Date, request.Reference);

        foreach (var line in request.Lines)
        {
            inventoryAdjustment.AddLine(line.ProductId, line.Direction, line.Quantity, line.UnitCost);
        }

        db.InventoryAdjustments.Add(inventoryAdjustment);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateInventoryAdjustmentResult(inventoryAdjustment.Id, inventoryAdjustment.Code, inventoryAdjustment.Status);
    }
}
