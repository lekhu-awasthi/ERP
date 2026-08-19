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

        var originalEntry = await db.GlJournalEntries
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.SourceDocumentType == DocumentType.InventoryAdjustment && x.SourceDocumentId == inventoryAdjustment.Id,
                cancellationToken);

        inventoryAdjustment.Void(currentUser.UserId);

        foreach (var line in inventoryAdjustment.Lines.Where(
            x => x.Direction == InventoryAdjustmentDirection.Decrease && x.ConsumedUnitCost is not null))
        {
            await stockLedgerService.IncrementAsync(
                request.OrganizationId, line.ProductId, inventoryAdjustment.WarehouseId, line.Quantity, line.ConsumedUnitCost!.Value,
                DocumentType.InventoryAdjustment, inventoryAdjustment.Id, inventoryAdjustment.Date, cancellationToken);
        }

        db.GlJournalEntries.Add(GlJournalEntry.PostReversalOf(originalEntry));

        await db.SaveChangesAsync(cancellationToken);

        return new VoidInventoryAdjustmentResult(
            inventoryAdjustment.Id, inventoryAdjustment.Code, inventoryAdjustment.Status, inventoryAdjustment.VoidedAt);
    }
}
