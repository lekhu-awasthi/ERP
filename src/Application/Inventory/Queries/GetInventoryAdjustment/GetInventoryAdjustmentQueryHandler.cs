using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.GetInventoryAdjustment;

public sealed class GetInventoryAdjustmentQueryHandler(IAppDbContext db)
    : IRequestHandler<GetInventoryAdjustmentQuery, InventoryAdjustmentDetailDto>
{
    public async Task<InventoryAdjustmentDetailDto> Handle(GetInventoryAdjustmentQuery request, CancellationToken cancellationToken)
    {
        var inventoryAdjustment = await db.InventoryAdjustments
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Inventory adjustment not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (inventoryAdjustment.Status == InventoryAdjustmentStatus.Approved)
        {
            // Phase 37 -- every entry this document posted, not "the" entry (phase 36's
            // finding, generalised): a cost catch-up rides on the same
            // (SourceDocumentType, SourceDocumentId) pair, and SingleOrDefaultAsync throws on
            // two rows exactly as SingleAsync does. The panel shows what the document really
            // did to the ledger, so it shows all of it.
            var glEntries = await SourceDocumentGlEntries.LoadAsync(
                db, DocumentType.InventoryAdjustment, inventoryAdjustment.Id, cancellationToken);

            glLines = glEntries.Count == 0
                ? null
                : glEntries.SelectMany(e => e.Lines)
                    .Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new InventoryAdjustmentDetailDto(
            inventoryAdjustment.Id,
            inventoryAdjustment.OrganizationId,
            inventoryAdjustment.Code,
            inventoryAdjustment.Date,
            inventoryAdjustment.Reference,
            inventoryAdjustment.WarehouseId,
            inventoryAdjustment.Status,
            inventoryAdjustment.ApprovedByUserId,
            inventoryAdjustment.ApprovedAt,
            inventoryAdjustment.CreatedAt,
            inventoryAdjustment.Lines
                .Select(x => new InventoryAdjustmentLineDto(x.Id, x.ProductId, x.Direction, x.Quantity, x.UnitCost))
                .ToList(),
            glLines,
            inventoryAdjustment.LocationId);
    }
}
