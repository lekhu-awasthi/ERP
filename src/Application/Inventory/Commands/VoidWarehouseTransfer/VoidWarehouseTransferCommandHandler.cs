using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Commands.VoidWarehouseTransfer;

/// <summary>
/// No GL (WarehouseTransfer never posts one). Stock: the destination-side layers Approve created
/// (one per line, in ToWarehouseId) must still be fully intact -- captured here (ProductId,
/// Quantity, UnitCost) before IStockLedgerService.ReverseIncrementAsync validates-and-zeroes them
/// (409 if any has already been consumed onward), then restocked back into FromWarehouseId at the
/// exact same carried-over cost, undoing the location move symmetrically.
/// </summary>
public sealed class VoidWarehouseTransferCommandHandler(
    IAppDbContext db, ICurrentUserService currentUser, IStockLedgerService stockLedgerService)
    : IRequestHandler<VoidWarehouseTransferCommand, VoidWarehouseTransferResult>
{
    public async Task<VoidWarehouseTransferResult> Handle(
        VoidWarehouseTransferCommand request, CancellationToken cancellationToken)
    {
        var warehouseTransfer = await db.WarehouseTransfers.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Warehouse transfer not found.");

        if (warehouseTransfer.Status != WarehouseTransferStatus.Approved)
        {
            throw new ConflictException("Only an Approved warehouse transfer can be voided.");
        }

        var destinationLayers = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.SourceDocumentType == DocumentType.WarehouseTransfer && x.SourceDocumentId == warehouseTransfer.Id)
            .Select(x => new { x.ProductId, x.QuantityIn, x.UnitCost })
            .ToListAsync(cancellationToken);

        // Fail fast (409) before mutating anything if any destination layer is already consumed.
        await stockLedgerService.ReverseIncrementAsync(
            request.OrganizationId, DocumentType.WarehouseTransfer, warehouseTransfer.Id, warehouseTransfer.Date,
            cancellationToken);

        warehouseTransfer.Void(currentUser.UserId);

        foreach (var layer in destinationLayers)
        {
            await stockLedgerService.IncrementAsync(
                request.OrganizationId, layer.ProductId, warehouseTransfer.FromWarehouseId, layer.QuantityIn, layer.UnitCost,
                DocumentType.WarehouseTransfer, warehouseTransfer.Id, warehouseTransfer.Date, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new VoidWarehouseTransferResult(
            warehouseTransfer.Id, warehouseTransfer.Code, warehouseTransfer.Status, warehouseTransfer.VoidedAt);
    }
}
