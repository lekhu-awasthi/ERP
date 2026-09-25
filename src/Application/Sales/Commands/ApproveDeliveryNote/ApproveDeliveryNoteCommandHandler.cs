using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales.Stock;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.ApproveDeliveryNote;

/// <summary>
/// Phase 58 -- issues the Delivery Note's Goods lines from the <b>physical</b> ledger. The stock gate
/// reads that ledger through the tenant's Negative Item Balance setting (Reject / Warn / Do Nothing,
/// the same verdict the Invoice gets from the accounting ledger), then the document is numbered and
/// one Out row is written per Goods line. No FIFO layer is consumed, no COGS is computed and nothing
/// is posted: all of that is the Invoice's, in both modes.
///
/// <para>The gate sums each product's <b>primary</b> quantity, so a line entered in cartons is
/// compared against the shelf in the unit the shelf is counted in.</para>
/// </summary>
public sealed class ApproveDeliveryNoteCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IStockAvailabilityPolicy stockAvailabilityPolicy)
    : IRequestHandler<ApproveDeliveryNoteCommand, ApproveDeliveryNoteResult>
{
    public async Task<ApproveDeliveryNoteResult> Handle(ApproveDeliveryNoteCommand request, CancellationToken cancellationToken)
    {
        var deliveryNote = await db.DeliveryNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Delivery note not found.");

        if (deliveryNote.Status != DeliveryNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft delivery note can be approved.");
        }

        if (deliveryNote.Lines.Count == 0)
        {
            throw new ConflictException("A delivery note needs at least one line to be approved.");
        }

        var productIds = deliveryNote.Lines.Select(x => x.ProductId).Distinct().ToList();
        var goodsIds = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id) && x.Type == ProductType.Goods)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var requirements = deliveryNote.Lines
            .Where(x => goodsIds.Contains(x.ProductId))
            .GroupBy(x => x.ProductId)
            .Select(g => new StockRequirement(g.Key, g.Sum(x => x.PrimaryQuantity.Value)))
            .ToList();

        var stockStatus = await stockAvailabilityPolicy.CheckPhysicalRequirementsAsync(
            request.OrganizationId, deliveryNote.WarehouseId, requirements, cancellationToken);

        if (stockStatus == StockAvailabilityStatus.Reject)
        {
            throw new ConflictException(
                "Insufficient stock in the warehouse to approve this delivery note. Adjust the quantities or "
                    + "receive the goods before approving.");
        }

        if (stockStatus == StockAvailabilityStatus.Warn && !request.OverrideWarning)
        {
            throw new StockAvailabilityWarningException(
                "One or more lines on this delivery note exceed the stock physically in the selected warehouse. "
                    + "Approve again to continue anyway.");
        }

        var code = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.DeliveryNote, cancellationToken, deliveryNote.LocationId);

        deliveryNote.Approve(currentUser.UserId, code);

        await PhysicalStockWriter.RecordAsync(
            db, request.OrganizationId, DocumentType.DeliveryNote, deliveryNote.Id,
            deliveryNote.WarehouseId, deliveryNote.Date, deliveryNote.LocationId,
            StockMovementDirection.Out,
            [.. deliveryNote.Lines.Select(x => new PhysicalStockWriter.LineMovement(x.ProductId, x.PrimaryQuantity))],
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveDeliveryNoteResult(deliveryNote.Id, deliveryNote.Code, deliveryNote.Status, deliveryNote.ApprovedAt);
    }
}
