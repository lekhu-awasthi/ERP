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

        var originalEntry = await db.GlJournalEntries
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.SourceDocumentType == DocumentType.CashTransfer && x.SourceDocumentId == cashTransfer.Id, cancellationToken);

        cashTransfer.Void(currentUser.UserId);

        db.GlJournalEntries.Add(GlJournalEntry.PostReversalOf(originalEntry));

        await db.SaveChangesAsync(cancellationToken);

        return new VoidCashTransferResult(cashTransfer.Id, cashTransfer.Code, cashTransfer.Status, cashTransfer.VoidedAt);
    }
}
