using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Commands.UpdateWarehouseTransfer;

public sealed class UpdateWarehouseTransferCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateWarehouseTransferCommand, UpdateWarehouseTransferResult>
{
    public async Task<UpdateWarehouseTransferResult> Handle(
        UpdateWarehouseTransferCommand request, CancellationToken cancellationToken)
    {
        var warehouseTransfer = await db.WarehouseTransfers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Warehouse transfer not found.");

        if (warehouseTransfer.Status != WarehouseTransferStatus.Draft)
        {
            throw new ConflictException("Only a Draft warehouse transfer can be edited.");
        }

        await InventoryValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.FromWarehouseId, cancellationToken);
        await InventoryValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.ToWarehouseId, cancellationToken);
        await InventoryValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);
        await InventoryValidation.EnsureProductsAreGoodsAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        // Explicit DbSet Remove/Add for the replaced lines -- see
        // UpdateJournalVoucherCommandHandler's identical comment (the EF Core InMemory-provider
        // Clear+re-Add gotcha this whole codebase has followed since Phase 4).
        var oldLines = warehouseTransfer.Lines.ToList();

        warehouseTransfer.UpdateHeader(request.FromWarehouseId, request.ToWarehouseId, request.Date, request.Reference);
        warehouseTransfer.ClearLines();
        foreach (var line in request.Lines)
        {
            warehouseTransfer.AddLine(line.ProductId, line.Quantity);
        }

        db.WarehouseTransferLines.RemoveRange(oldLines);
        db.WarehouseTransferLines.AddRange(warehouseTransfer.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateWarehouseTransferResult(warehouseTransfer.Id, warehouseTransfer.Code, warehouseTransfer.Status);
    }
}
