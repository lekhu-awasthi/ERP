using ErpApp.Application.Inventory.Stock;
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

        // Phase 52 -- the catalogue is read here and never again: ResolveAsync turns the unit
        // each line names into the factor frozen onto it, so editing (or deleting) the product's
        // unit row afterwards cannot reach back and change what this document did to stock.
        var units = await DocumentLineUnitResolver.ResolveAsync(
            db, request.OrganizationId,
            [.. request.Lines.Select(x => new DocumentLineUnitResolver.LineUnitInput(x.ProductId, x.UnitId))],
            cancellationToken);

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            warehouseTransfer.AddLine(line.ProductId, line.Quantity, units[i].UnitId, units[i].ConversionFactor);
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
