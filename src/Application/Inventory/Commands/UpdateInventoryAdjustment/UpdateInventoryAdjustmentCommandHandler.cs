using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Commands.UpdateInventoryAdjustment;

public sealed class UpdateInventoryAdjustmentCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateInventoryAdjustmentCommand, UpdateInventoryAdjustmentResult>
{
    public async Task<UpdateInventoryAdjustmentResult> Handle(
        UpdateInventoryAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var inventoryAdjustment = await db.InventoryAdjustments
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Inventory adjustment not found.");

        if (inventoryAdjustment.Status != InventoryAdjustmentStatus.Draft)
        {
            throw new ConflictException("Only a Draft inventory adjustment can be edited.");
        }

        await InventoryValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await InventoryValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);
        await InventoryValidation.EnsureProductsAreGoodsAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        // Explicit DbSet Remove/Add for the replaced lines -- see
        // UpdateJournalVoucherCommandHandler's identical comment (the EF Core InMemory-provider
        // Clear+re-Add gotcha this whole codebase has followed since Phase 4).
        var oldLines = inventoryAdjustment.Lines.ToList();

        // Phase 54 -- re-resolved on every edit, so a draft re-saved after the product's unit
        // matrix changed picks up the new factor, while an approved document never can.
        var units = await DocumentLineUnitResolver.ResolveAsync(
            db, request.OrganizationId,
            [.. request.Lines.Select(x => new DocumentLineUnitResolver.LineUnitInput(x.ProductId, x.UnitId))],
            cancellationToken);

        inventoryAdjustment.UpdateHeader(request.WarehouseId, request.Date, request.Reference);
        inventoryAdjustment.ClearLines();
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            inventoryAdjustment.AddLine(
                line.ProductId, line.Direction, line.Quantity, line.UnitCost,
                units[i].UnitId, units[i].ConversionFactor);
        }

        db.InventoryAdjustmentLines.RemoveRange(oldLines);
        db.InventoryAdjustmentLines.AddRange(inventoryAdjustment.Lines);

        // Phase 32 -- resolved through the one shared resolver so this type follows the
        // tenant's LocationScopeMode identically to the sales-side ones. Under the default
        // mode that setting puts this document out of scope and the resolver returns null.
        inventoryAdjustment.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.InventoryAdjustment, request.LocationId,
            cancellationToken));

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateInventoryAdjustmentResult(inventoryAdjustment.Id, inventoryAdjustment.Code, inventoryAdjustment.Status);
    }
}
