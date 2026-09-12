using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Commands.VoidInventoryAdjustment;

/// <summary>
/// Stock: an Increase line's own layer (created at Approve) must still be fully intact --
/// IStockLedgerService.ReverseIncrementAsync rejects (409) the whole void if any has already been
/// consumed onward, mirroring VoidPurchaseBillCommandHandler's guard. A Decrease line's own
/// InventoryAdjustmentLine.ConsumedUnitCost (recorded at Approve, this phase's own addition
/// mirroring InvoiceLine.CogsUnitCost) restocks it back via IncrementAsync -- always succeeds. GL:
/// a mirror-image reversal of the Approve-time entry (each direction's *total*, per
/// InventoryAdjustmentPostingRule's own per-direction-total design).
/// </summary>
public sealed class VoidInventoryAdjustmentCommandHandler(
    IAppDbContext db, ICurrentUserService currentUser, IStockLedgerService stockLedgerService)
    : IRequestHandler<VoidInventoryAdjustmentCommand, VoidInventoryAdjustmentResult>
{
    public async Task<VoidInventoryAdjustmentResult> Handle(
        VoidInventoryAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var inventoryAdjustment = await db.InventoryAdjustments
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Inventory adjustment not found.");

        if (inventoryAdjustment.Status != InventoryAdjustmentStatus.Approved)
        {
            throw new ConflictException("Only an Approved inventory adjustment can be voided.");
        }

        // Fail fast (409) before mutating anything if any Increase line's layer is already consumed.
        await stockLedgerService.ReverseIncrementAsync(
            request.OrganizationId, DocumentType.InventoryAdjustment, inventoryAdjustment.Id, inventoryAdjustment.Date,
            cancellationToken);

        inventoryAdjustment.Void(currentUser.UserId);

        // Phase 37 -- reverse what is outstanding, and do it before the catch-up below is added, so
        // the reversal never sweeps up the entry this void is about to post. An Increase line that
        // covered a shortfall has already put a second entry on this document (phase 36: one entry
        // per approved document is a habit, not an invariant), and `SingleAsync` over the pair was
        // a 500 waiting for the first tenant to oversell.
        await SourceDocumentGlEntries.ReverseOutstandingAsync(
            db, DocumentType.InventoryAdjustment, inventoryAdjustment.Id, cancellationToken);

        var costCatchUp = 0m;

        foreach (var line in inventoryAdjustment.Lines.Where(
            x => x.Direction == InventoryAdjustmentDirection.Decrease && x.ConsumedUnitCost is not null))
        {
            costCatchUp += await stockLedgerService.IncrementAsync(
                request.OrganizationId, line.ProductId, inventoryAdjustment.WarehouseId, line.Quantity, line.ConsumedUnitCost!.Value,
                DocumentType.InventoryAdjustment, inventoryAdjustment.Id, inventoryAdjustment.Date, cancellationToken,
                inventoryAdjustment.LocationId);
        }

        await StockCostCatchUp.PostAsync(
            db, request.OrganizationId, DocumentType.InventoryAdjustment, inventoryAdjustment.Id,
            inventoryAdjustment.LocationId, costCatchUp, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidInventoryAdjustmentResult(
            inventoryAdjustment.Id, inventoryAdjustment.Code, inventoryAdjustment.Status, inventoryAdjustment.VoidedAt);
    }
}
