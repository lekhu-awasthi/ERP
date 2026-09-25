using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.ApproveGoodsReceivedNote;

/// <summary>
/// Phase 58 -- numbers the GRN and receives its Goods lines into the <b>physical</b> ledger. That is
/// the whole of it: no FIFO layer, no GL entry, and so nothing for phase 37's conservation law to
/// check -- the accounting ledger does not move until the Purchase Bill is approved, in either mode.
/// A receipt needs no availability check.
/// </summary>
public sealed class ApproveGoodsReceivedNoteCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser)
    : IRequestHandler<ApproveGoodsReceivedNoteCommand, ApproveGoodsReceivedNoteResult>
{
    public async Task<ApproveGoodsReceivedNoteResult> Handle(
        ApproveGoodsReceivedNoteCommand request, CancellationToken cancellationToken)
    {
        var goodsReceivedNote = await db.GoodsReceivedNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Goods received note not found.");

        if (goodsReceivedNote.Status != GoodsReceivedNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft goods received note can be approved.");
        }

        if (goodsReceivedNote.Lines.Count == 0)
        {
            throw new ConflictException("A goods received note needs at least one line to be approved.");
        }

        var code = await numberGenerator.GetNextNumberAsync(
            request.OrganizationId, DocumentType.GoodsReceivedNote, cancellationToken, goodsReceivedNote.LocationId);

        goodsReceivedNote.Approve(currentUser.UserId, code);

        await PhysicalStockWriter.RecordAsync(
            db, request.OrganizationId, DocumentType.GoodsReceivedNote, goodsReceivedNote.Id,
            goodsReceivedNote.WarehouseId, goodsReceivedNote.Date, goodsReceivedNote.LocationId,
            StockMovementDirection.In,
            [.. goodsReceivedNote.Lines.Select(x => new PhysicalStockWriter.LineMovement(x.ProductId, x.PrimaryQuantity))],
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveGoodsReceivedNoteResult(
            goodsReceivedNote.Id, goodsReceivedNote.Code, goodsReceivedNote.Status, goodsReceivedNote.ApprovedAt);
    }
}
