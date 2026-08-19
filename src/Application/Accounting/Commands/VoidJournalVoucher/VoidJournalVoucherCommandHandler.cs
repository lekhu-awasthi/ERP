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

        var originalEntry = await db.GlJournalEntries
            .Include(x => x.Lines)
            .SingleAsync(
                x => x.SourceDocumentType == DocumentType.JournalVoucher && x.SourceDocumentId == journalVoucher.Id,
                cancellationToken);

        journalVoucher.Void(currentUser.UserId);

        db.GlJournalEntries.Add(GlJournalEntry.PostReversalOf(originalEntry));

        await db.SaveChangesAsync(cancellationToken);

        return new VoidJournalVoucherResult(journalVoucher.Id, journalVoucher.Code, journalVoucher.Status, journalVoucher.VoidedAt);
    }
}
