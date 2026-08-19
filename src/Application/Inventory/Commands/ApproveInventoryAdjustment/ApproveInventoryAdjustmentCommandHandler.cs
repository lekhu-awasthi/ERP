using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Posting;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Commands.ApproveInventoryAdjustment;

/// <summary>
/// Unlike WarehouseTransfer, this DOES post GL -- see InventoryAdjustment's and
/// InventoryAdjustmentPostingRule's doc comments. An Increase line creates a new FIFO layer at its
/// own stated UnitCost; a Decrease line consumes existing layers via IStockLedgerService (the real
/// cost of what's actually written off, not a user-entered figure). Both totals are summed first,
/// then handed to InventoryAdjustmentPostingRule as one already-resolved input -- BuildLines itself
/// stays a pure function of already-computed amounts, same split every other posting rule in this
/// codebase follows.
/// </summary>
public sealed class ApproveInventoryAdjustmentCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<InventoryAdjustmentPostingInput> postingRule,
    IStockLedgerService stockLedgerService)
    : IRequestHandler<ApproveInventoryAdjustmentCommand, ApproveInventoryAdjustmentResult>
{
    public async Task<ApproveInventoryAdjustmentResult> Handle(
        ApproveInventoryAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var inventoryAdjustment = await db.InventoryAdjustments
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Inventory adjustment not found.");

        if (inventoryAdjustment.Status != InventoryAdjustmentStatus.Draft)
        {
            throw new ConflictException("Only a Draft inventory adjustment can be approved.");
        }

        if (inventoryAdjustment.Lines.Count == 0)
        {
            throw new ConflictException("An inventory adjustment needs at least one line to be approved.");
        }

        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var inventoryAccountId = settings.DefaultInventoryAccountId
            ?? throw new ConflictException(
                "Default Inventory account is not configured. Set it under Accounting Defaults before approving inventory adjustments.");
        var adjustmentAccountId = settings.DefaultInventoryAdjustmentAccountId
            ?? throw new ConflictException(
                "Default Inventory Adjustment account is not configured. Set it under Accounting Defaults before approving inventory adjustments.");

        var code = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.InventoryAdjustment, cancellationToken);

        inventoryAdjustment.Approve(currentUser.UserId, code);

        var increaseAmount = 0m;
        var decreaseAmount = 0m;

        foreach (var line in inventoryAdjustment.Lines)
        {
            if (line.Direction == InventoryAdjustmentDirection.Increase)
            {
                await stockLedgerService.IncrementAsync(
                    request.OrganizationId, line.ProductId, inventoryAdjustment.WarehouseId, line.Quantity, line.UnitCost,
                    DocumentType.InventoryAdjustment, inventoryAdjustment.Id, inventoryAdjustment.Date, cancellationToken);
                increaseAmount += line.Quantity * line.UnitCost;
            }
            else
            {
                var averageUnitCost = await stockLedgerService.ConsumeAsync(
                    request.OrganizationId, line.ProductId, inventoryAdjustment.WarehouseId, line.Quantity,
                    DocumentType.InventoryAdjustment, inventoryAdjustment.Id, inventoryAdjustment.Date, cancellationToken);
                line.RecordConsumedUnitCost(averageUnitCost);
                decreaseAmount += line.Quantity * averageUnitCost;
            }
        }

        var postingInput = new InventoryAdjustmentPostingInput(inventoryAccountId, adjustmentAccountId, increaseAmount, decreaseAmount);
        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.InventoryAdjustment, inventoryAdjustment.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveInventoryAdjustmentResult(
            inventoryAdjustment.Id, inventoryAdjustment.Code, inventoryAdjustment.Status, inventoryAdjustment.ApprovedAt);
    }
}
