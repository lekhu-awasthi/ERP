using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.VoidDeliveryNote;

/// <summary>
/// Phase 58 -- puts the goods back on the shelf: the mirror of every Out row Approve wrote. Always
/// allowed, because a restock can only raise a balance. Confirmed live: voiding a Delivery Note
/// returned the physical ledger to exactly where it stood before the note (2026-09-24). The Sales
/// Order it delivered stays delivered, the same one-shot rule as a voided GRN's order.
/// </summary>
public sealed class VoidDeliveryNoteCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidDeliveryNoteCommand, VoidDeliveryNoteResult>
{
    public async Task<VoidDeliveryNoteResult> Handle(VoidDeliveryNoteCommand request, CancellationToken cancellationToken)
    {
        var deliveryNote = await db.DeliveryNotes
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Delivery note not found.");

        if (deliveryNote.Status != DeliveryNoteStatus.Approved)
        {
            throw new ConflictException("Only an Approved delivery note can be voided.");
        }

        await PhysicalStockWriter.ReverseAsync(
            db, request.OrganizationId, DocumentType.DeliveryNote, deliveryNote.Id, deliveryNote.Date,
            refuseNegative: false, cancellationToken);

        deliveryNote.Void(currentUser.UserId);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidDeliveryNoteResult(deliveryNote.Id, deliveryNote.Code, deliveryNote.Status, deliveryNote.VoidedAt);
    }
}
