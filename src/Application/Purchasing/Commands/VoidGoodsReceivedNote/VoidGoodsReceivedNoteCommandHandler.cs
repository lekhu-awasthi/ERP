using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.VoidGoodsReceivedNote;

/// <summary>
/// Phase 58 -- puts back exactly what Approve took (phase 43's rule: what a document does to stock is
/// what its Void owes), refusing when the goods have already left the warehouse. See
/// <see cref="PhysicalStockWriter.ReverseAsync"/> for that refusal and why it diverges from the
/// reference product.
///
/// <para>The Purchase Order it was received against stays received, the same one-shot rule a
/// voided Purchase Bill leaves on its order's Converted status: a new GRN for the same goods is raised
/// by hand.</para>
/// </summary>
public sealed class VoidGoodsReceivedNoteCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidGoodsReceivedNoteCommand, VoidGoodsReceivedNoteResult>
{
    public async Task<VoidGoodsReceivedNoteResult> Handle(
        VoidGoodsReceivedNoteCommand request, CancellationToken cancellationToken)
    {
        var goodsReceivedNote = await db.GoodsReceivedNotes
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Goods received note not found.");

        if (goodsReceivedNote.Status != GoodsReceivedNoteStatus.Approved)
        {
            throw new ConflictException("Only an Approved goods received note can be voided.");
        }

        // Fail fast, before mutating anything -- the phase-57 lesson that a refusal after a mutation
        // leaves the tracked entity dirty.
        await PhysicalStockWriter.ReverseAsync(
            db, request.OrganizationId, DocumentType.GoodsReceivedNote, goodsReceivedNote.Id,
            goodsReceivedNote.Date, refuseNegative: true, cancellationToken);

        goodsReceivedNote.Void(currentUser.UserId);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidGoodsReceivedNoteResult(
            goodsReceivedNote.Id, goodsReceivedNote.Code, goodsReceivedNote.Status, goodsReceivedNote.VoidedAt);
    }
}
