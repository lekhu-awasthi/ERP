using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;

public sealed class ApproveJournalVoucherCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<JournalVoucher> postingRule)
    : IRequestHandler<ApproveJournalVoucherCommand, ApproveJournalVoucherResult>
{
    public async Task<ApproveJournalVoucherResult> Handle(ApproveJournalVoucherCommand request, CancellationToken cancellationToken)
    {
        var journalVoucher = await db.JournalVouchers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Journal voucher not found.");

        if (journalVoucher.Status != JournalVoucherStatus.Draft)
        {
            throw new ConflictException("Only a Draft journal voucher can be approved.");
        }

        // Fail fast with a friendly 409 before Approve()'s own (stricter, InvalidOperationException
        // -> 500) invariant check ever runs -- same two conditions, checked here first purely for
        // a clean HTTP status.
        if (journalVoucher.Lines.Count < 2
            || journalVoucher.Lines.Sum(x => x.Debit) != journalVoucher.Lines.Sum(x => x.Credit))
        {
            throw new ConflictException(
                "A journal voucher needs at least two lines with total Debit equal to total Credit to be approved.");
        }

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.JournalVoucher, cancellationToken);

        journalVoucher.Approve(currentUser.UserId, code);

        var glLines = postingRule.BuildLines(journalVoucher);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.JournalVoucher, journalVoucher.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveJournalVoucherResult(journalVoucher.Id, journalVoucher.Code, journalVoucher.Status, journalVoucher.ApprovedAt);
    }
}
