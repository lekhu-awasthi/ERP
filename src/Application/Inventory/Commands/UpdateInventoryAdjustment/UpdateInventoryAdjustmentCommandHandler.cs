using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
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

        inventoryAdjustment.UpdateHeader(request.WarehouseId, request.Date, request.Reference);
        inventoryAdjustment.ClearLines();
        foreach (var line in request.Lines)
        {
            inventoryAdjustment.AddLine(line.ProductId, line.Direction, line.Quantity, line.UnitCost);
        }

        db.InventoryAdjustmentLines.RemoveRange(oldLines);
        db.InventoryAdjustmentLines.AddRange(inventoryAdjustment.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateInventoryAdjustmentResult(inventoryAdjustment.Id, inventoryAdjustment.Code, inventoryAdjustment.Status);
    }
}
