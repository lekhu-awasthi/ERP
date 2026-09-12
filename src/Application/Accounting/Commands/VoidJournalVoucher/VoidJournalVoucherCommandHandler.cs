using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.VoidJournalVoucher;

public sealed class VoidJournalVoucherCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidJournalVoucherCommand, VoidJournalVoucherResult>
{
    public async Task<VoidJournalVoucherResult> Handle(VoidJournalVoucherCommand request, CancellationToken cancellationToken)
    {
        var journalVoucher = await db.JournalVouchers.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Journal voucher not found.");

        if (journalVoucher.Status != JournalVoucherStatus.Approved)
        {
            throw new ConflictException("Only an Approved journal voucher can be voided.");
        }

        journalVoucher.Void(currentUser.UserId);

        // Phase 36 -- every entry, not "the" entry: allocating further against one of this
        // voucher's Contact-tagged lines posts a realised forex leg of its own
        // (ApplyPaymentAllocationCommandHandler), so an Approved voucher can carry more than one.
        await SourceDocumentGlEntries.ReverseOutstandingAsync(
            db, DocumentType.JournalVoucher, journalVoucher.Id, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidJournalVoucherResult(journalVoucher.Id, journalVoucher.Code, journalVoucher.Status, journalVoucher.VoidedAt);
    }
}
