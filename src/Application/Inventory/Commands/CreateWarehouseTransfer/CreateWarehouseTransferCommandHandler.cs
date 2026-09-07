using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
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
        // Phase 32 -- resolved through the one shared resolver so this type follows the
        // tenant's LocationScopeMode identically to the sales-side ones. Under the default
        // mode that setting puts this document out of scope and the resolver returns null.
        warehouseTransfer.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.WarehouseTransfer, request.LocationId,
            cancellationToken));

        await db.SaveChangesAsync(cancellationToken);

        return new CreateWarehouseTransferResult(warehouseTransfer.Id, warehouseTransfer.Code, warehouseTransfer.Status);
    }
}
