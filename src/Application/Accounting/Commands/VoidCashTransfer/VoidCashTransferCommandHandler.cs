using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.VoidCashTransfer;

public sealed class VoidCashTransferCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidCashTransferCommand, VoidCashTransferResult>
{
    public async Task<VoidCashTransferResult> Handle(VoidCashTransferCommand request, CancellationToken cancellationToken)
    {
        var cashTransfer = await db.CashTransfers.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Cash transfer not found.");

        if (cashTransfer.Status != CashTransferStatus.Approved)
        {
            throw new ConflictException("Only an Approved cash transfer can be voided.");
        }

        cashTransfer.Void(currentUser.UserId);

        // Phase 37 -- reverses whatever is outstanding rather than mirroring one entry. This
        // type posts only one today, but every void in this codebase now asks the same
        // question of the ledger (Application.Accounting.Posting.SourceDocumentGlEntries), so
        // there is no handler left for a second entry to surprise.
        await SourceDocumentGlEntries.ReverseOutstandingAsync(
            db, DocumentType.CashTransfer, cashTransfer.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidCashTransferResult(cashTransfer.Id, cashTransfer.Code, cashTransfer.Status, cashTransfer.VoidedAt);
    }
}
