using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.UpdateJournalVoucher;

public sealed class UpdateJournalVoucherCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateJournalVoucherCommand, UpdateJournalVoucherResult>
{
    public async Task<UpdateJournalVoucherResult> Handle(UpdateJournalVoucherCommand request, CancellationToken cancellationToken)
    {
        // Include(x => x.Lines) is required so the handler knows which existing line rows to
        // remove below -- without it, the private in-memory backing field is empty and
        // ClearLines() has nothing to snapshot.
        var journalVoucher = await db.JournalVouchers
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Journal voucher not found.");

        if (journalVoucher.Status != JournalVoucherStatus.Draft)
        {
            throw new ConflictException("Only a Draft journal voucher can be edited.");
        }

        await AccountingValidation.EnsureAccountsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.AccountId), cancellationToken);
        await AccountingValidation.EnsureContactsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ContactId), cancellationToken);

        // Explicit DbSet Remove/Add rather than relying on collection-navigation-triggered
        // orphan-delete fixup for the replaced lines -- same defensive precedent as
        // AddSecondaryUnitCommandHandler's "add the new child to its own DbSet explicitly"
        // (relying purely on mutating the aggregate's private collection proved unreliable for a
        // same-count Clear+re-Add here).
        var oldLines = journalVoucher.Lines.ToList();

        journalVoucher.UpdateHeader(request.Date, request.Reference);
        journalVoucher.ClearLines();
        foreach (var line in request.Lines)
        {
            journalVoucher.AddLine(line.AccountId, line.Debit, line.Credit, line.ContactId);
        }

        db.JournalVoucherLines.RemoveRange(oldLines);
        db.JournalVoucherLines.AddRange(journalVoucher.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateJournalVoucherResult(journalVoucher.Id, journalVoucher.Code, journalVoucher.Status);
    }
}
