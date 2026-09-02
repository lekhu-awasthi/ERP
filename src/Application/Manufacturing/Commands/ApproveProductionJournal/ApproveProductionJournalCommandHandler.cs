using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Manufacturing.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.ApproveProductionJournal;

/// <summary>
/// <b>The phase.</b> In order, and the order is load-bearing:
///
/// <list type="number">
/// <item>Check raw-material availability through the tenant's real NegativeStockBalanceAction
/// policy (Decision F), before anything has been mutated.</item>
/// <item>Consume each raw-material line's FIFO layers and stamp the line with what
/// <c>ConsumeAsync</c> actually returned -- never a BOM-planned or user-entered rate. The
/// unrounded weighted average is multiplied by the quantity here, so the line's Amount equals to
/// the cent what the ledger gave up.</item>
/// <item>Compute the roll-up in the Domain (<c>ComputeAndRecordRollUp</c>), which allocates to
/// by-products first and gives the finished goods the remainder.</item>
/// <item>Create the new FIFO layers -- by-products at their allocated unit cost, the finished good
/// at the computed cost per unit.</item>
/// <item>Post one balanced GL entry built from the values actually created.</item>
/// <item><b>One <c>SaveChangesAsync</c> for all of it</b>, per Phase 7 task 4's single-transaction
/// requirement: a run that died between consuming and creating would otherwise destroy stock.</item>
/// </list>
///
/// <para>The conservation law this must satisfy -- raw FIFO cost consumed + production expenses =
/// finished value created + by-product value created (+ the named rounding residue) -- is proven in
/// ProductionJournalCostRollUpTests and asserted end-to-end against real SQL Server.</para>
/// </summary>
public sealed class ApproveProductionJournalCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<ProductionJournalPostingInput> postingRule,
    IStockLedgerService stockLedgerService,
    IStockAvailabilityPolicy stockAvailabilityPolicy)
    : IRequestHandler<ApproveProductionJournalCommand, ApproveProductionJournalResult>
{
    public async Task<ApproveProductionJournalResult> Handle(
        ApproveProductionJournalCommand request, CancellationToken cancellationToken)
    {
        var journal = await db.ProductionJournals
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Production journal not found.");

        if (journal.Status != ProductionJournalStatus.Draft)
        {
            throw new ConflictException("Only a Draft production journal can be approved.");
        }

        if (journal.RawMaterials.Count == 0)
        {
            throw new ConflictException("A production journal needs at least one raw material to be approved.");
        }

        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var inventoryAccountId = settings.DefaultInventoryAccountId
            ?? throw new ConflictException(
                "Default Inventory account is not configured. Set it under Accounting Defaults before approving production journals.");

        // Required unconditionally, like the Inventory account: the production-cost leg can be
        // non-zero even with no expense lines, because the finished unit cost is rounded to the
        // stock ledger's own scale. See TenantSettings.DefaultProductionCostAccountId.
        var productionCostAccountId = settings.DefaultProductionCostAccountId
            ?? throw new ConflictException(
                "Default Production Cost account is not configured. Set it under Accounting Defaults before approving production journals.");

        // Step 1 -- availability, before anything is mutated. Quantities are summed per product
        // because one raw material may legitimately appear on more than one line.
        var requirements = journal.RawMaterials
            .GroupBy(x => x.ProductId)
            .Select(g => new StockRequirement(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        var stockStatus = await stockAvailabilityPolicy.CheckRequirementsAsync(
            request.OrganizationId, journal.WarehouseId, requirements, cancellationToken);

        if (stockStatus == StockAvailabilityStatus.Reject)
        {
            throw new ConflictException(
                "There is not enough raw-material stock in this warehouse to run this production journal.");
        }

        if (stockStatus == StockAvailabilityStatus.Warn && !request.OverrideWarning)
        {
            throw new StockAvailabilityWarningException(
                "There is not enough raw-material stock in this warehouse for one or more raw materials. " +
                "Approve again to proceed anyway.");
        }

        var code = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.ProductionJournal, cancellationToken);

        journal.Approve(currentUser.UserId, code);

        // Step 2 -- consume, recording each line's real FIFO cost.
        foreach (var line in journal.RawMaterials)
        {
            var consumedUnitCost = await stockLedgerService.ConsumeAsync(
                request.OrganizationId, line.ProductId, journal.WarehouseId, line.Quantity,
                DocumentType.ProductionJournal, journal.Id, journal.Date, cancellationToken);

            // Multiply the UNROUNDED average, not the value the column will round on write --
            // this product is exactly what left the ledger.
            line.RecordConsumedCost(consumedUnitCost, line.Quantity * consumedUnitCost);
        }

        // Step 3 -- the roll-up.
        journal.ComputeAndRecordRollUp();

        // Step 4 -- create the new layers, by-products first (they were costed first).
        foreach (var byProduct in journal.ByProducts)
        {
            await stockLedgerService.IncrementAsync(
                request.OrganizationId, byProduct.ProductId, journal.WarehouseId, byProduct.Quantity,
                byProduct.AllocatedUnitCost!.Value, DocumentType.ProductionJournal, journal.Id, journal.Date,
                cancellationToken);
        }

        await stockLedgerService.IncrementAsync(
            request.OrganizationId, journal.ProductId, journal.WarehouseId, journal.OutputQuantity,
            journal.FinishedGoodsUnitCost!.Value, DocumentType.ProductionJournal, journal.Id, journal.Date,
            cancellationToken);

        // Step 5 -- the GL, from the values actually created rather than the theoretical roll-up.
        var postingInput = new ProductionJournalPostingInput(
            inventoryAccountId,
            productionCostAccountId,
            journal.RawMaterialCost!.Value,
            journal.FinishedGoodsCost!.Value,
            journal.CostAllocatedToByProduct!.Value);

        var glLines = postingRule.BuildLines(postingInput);
        db.GlJournalEntries.Add(
            GlJournalEntry.Post(request.OrganizationId, DocumentType.ProductionJournal, journal.Id, glLines));

        // Step 6 -- one transaction for the consumption, the creation and the posting.
        await db.SaveChangesAsync(cancellationToken);

        return new ApproveProductionJournalResult(
            journal.Id,
            journal.Code,
            journal.Status,
            journal.ApprovedAt,
            journal.RawMaterialCost!.Value,
            journal.ProductionExpenseCost!.Value,
            journal.TotalCostOfProduction!.Value,
            journal.CostAllocatedToByProduct!.Value,
            journal.FinishedGoodsCost!.Value,
            journal.FinishedGoodsUnitCost!.Value,
            journal.CostRoundingAdjustment!.Value);
    }
}
