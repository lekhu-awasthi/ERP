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
            request.OrganizationId, DocumentType.InventoryAdjustment, cancellationToken, inventoryAdjustment.LocationId);

        inventoryAdjustment.Approve(currentUser.UserId, code);

        var increaseAmount = 0m;
        var decreaseAmount = 0m;
        var costCatchUp = 0m;

        // Phase 51 -- an Inventory Adjustment cannot say which batch or serials it moves, so a
        // tracked product is refused here with a named 409 rather than quietly creating an
        // un-batched layer that reconciles against nothing. The reason, and the re-entry condition,
        // are recorded in StockTrackingRules.RefusedPaths.
        await StockTrackingRules.EnsureNotTrackedAsync(
            db, request.OrganizationId, inventoryAdjustment.Lines.Select(x => x.ProductId),
            "Inventory Adjustment", cancellationToken);

        foreach (var line in inventoryAdjustment.Lines)
        {
            // Phase 54 -- the line's quantity is what the user typed, in the unit they chose; the
            // ledger only speaks the product's primary unit. Both branches convert once, through
            // the factor frozen on the line, and never read the catalogue.
            var primaryQuantity = line.PrimaryQuantity;

            if (line.Direction == InventoryAdjustmentDirection.Increase)
            {
                // Phase 52's law: a unit converts the QUANTITY and never the money. Amount stays
                // Quantity x UnitCost in the entered unit, so the layer's unit cost is
                // Amount / primary quantity -- 2 CT at 1,200 with a factor of 12 is 24 units at
                // 100, which is what the reference product's own movement row reports. Rounded
                // once, at the scale the UnitCost column actually holds.
                var amount = line.Quantity * line.UnitCost;
                var layerUnitCost = primaryQuantity.IsZero
                    ? 0m
                    : Math.Round(
                        amount / primaryQuantity.Value,
                        ExchangeRates.UnitCostScale,
                        MidpointRounding.AwayFromZero);

                costCatchUp += await stockLedgerService.IncrementAsync(
                    request.OrganizationId, line.ProductId, inventoryAdjustment.WarehouseId,
                    primaryQuantity, layerUnitCost,
                    DocumentType.InventoryAdjustment, inventoryAdjustment.Id, inventoryAdjustment.Date, cancellationToken,
                    inventoryAdjustment.LocationId);

                // What the layers actually received, not what the user typed. Without a unit the
                // two are identical; with one they differ by the rounding residue of the division
                // above, and the GL has to debit Inventory the figure the ledger holds or the
                // account and the layers drift apart -- phase 29's rule, and the reason phase 37
                // asserts all three views rather than any two.
                increaseAmount += layerUnitCost * primaryQuantity.Value;
            }
            else
            {
                var averageUnitCost = (await stockLedgerService.ConsumeAsync(
                    request.OrganizationId, line.ProductId, inventoryAdjustment.WarehouseId,
                    primaryQuantity,
                    DocumentType.InventoryAdjustment, inventoryAdjustment.Id, inventoryAdjustment.Date, cancellationToken,
                    inventoryAdjustment.LocationId)).AverageUnitCost;

                // ConsumedUnitCost is per PRIMARY unit, because that is what the FIFO layers this
                // walk consumed were priced in -- which is what lets the Void below restock at it
                // without converting a second time (ExchangeRates' never-convert-twice rule).
                line.RecordConsumedUnitCost(averageUnitCost);
                decreaseAmount += primaryQuantity.Value * averageUnitCost;
            }
        }

        var postingInput = new InventoryAdjustmentPostingInput(inventoryAccountId, adjustmentAccountId, increaseAmount, decreaseAmount);
        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(
            request.OrganizationId, DocumentType.InventoryAdjustment, inventoryAdjustment.Id, glLines,
            inventoryAdjustment.LocationId);
        db.GlJournalEntries.Add(glEntry);

        // Phase 37 -- an Increase line can land on a product this warehouse still owes stock for,
        // in which case it pays the debt down first and the difference between the assumed cost and
        // this line's own has to reach the ledger. See StockCostCatchUp.
        await StockCostCatchUp.PostAsync(
            db, request.OrganizationId, DocumentType.InventoryAdjustment, inventoryAdjustment.Id,
            inventoryAdjustment.LocationId, costCatchUp, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveInventoryAdjustmentResult(
            inventoryAdjustment.Id, inventoryAdjustment.Code, inventoryAdjustment.Status, inventoryAdjustment.ApprovedAt);
    }
}
