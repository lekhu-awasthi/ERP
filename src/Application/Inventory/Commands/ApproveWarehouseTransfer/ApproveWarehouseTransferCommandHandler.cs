using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Commands.ApproveWarehouseTransfer;

/// <summary>
/// The one ApprovableTransaction in this codebase with no GL posting at all -- see
/// WarehouseTransfer's doc comment. For each line, consumes FromWarehouseId's FIFO layers first
/// (throws a friendly ConflictException, not a raw 500, if the source warehouse doesn't have
/// enough -- IStockLedgerService.ConsumeAsync's own invariant), then increments ToWarehouseId at
/// the exact weighted-average cost Consume returned, so the moved stock's value is carried across
/// unchanged (no new value created or destroyed by a location move).
/// </summary>
public sealed class ApproveWarehouseTransferCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IStockLedgerService stockLedgerService)
    : IRequestHandler<ApproveWarehouseTransferCommand, ApproveWarehouseTransferResult>
{
    public async Task<ApproveWarehouseTransferResult> Handle(
        ApproveWarehouseTransferCommand request, CancellationToken cancellationToken)
    {
        var warehouseTransfer = await db.WarehouseTransfers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Warehouse transfer not found.");

        if (warehouseTransfer.Status != WarehouseTransferStatus.Draft)
        {
            throw new ConflictException("Only a Draft warehouse transfer can be approved.");
        }

        if (warehouseTransfer.Lines.Count == 0)
        {
            throw new ConflictException("A warehouse transfer needs at least one line to be approved.");
        }

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.WarehouseTransfer, cancellationToken);

        warehouseTransfer.Approve(currentUser.UserId, code);

        foreach (var line in warehouseTransfer.Lines)
        {
            var averageUnitCost = await stockLedgerService.ConsumeAsync(
                request.OrganizationId, line.ProductId, warehouseTransfer.FromWarehouseId, line.Quantity,
                DocumentType.WarehouseTransfer, warehouseTransfer.Id, warehouseTransfer.Date, cancellationToken);

            await stockLedgerService.IncrementAsync(
                request.OrganizationId, line.ProductId, warehouseTransfer.ToWarehouseId, line.Quantity, averageUnitCost,
                DocumentType.WarehouseTransfer, warehouseTransfer.Id, warehouseTransfer.Date, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveWarehouseTransferResult(
            warehouseTransfer.Id, warehouseTransfer.Code, warehouseTransfer.Status, warehouseTransfer.ApprovedAt);
    }
}
