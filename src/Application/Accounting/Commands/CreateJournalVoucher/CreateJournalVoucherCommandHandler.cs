using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateJournalVoucher;

public sealed class CreateJournalVoucherCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateJournalVoucherCommand, CreateJournalVoucherResult>
{
    public async Task<CreateJournalVoucherResult> Handle(CreateJournalVoucherCommand request, CancellationToken cancellationToken)
    {
        await AccountingValidation.EnsureAccountsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.AccountId), cancellationToken);

        var journalVoucher = JournalVoucher.Create(request.OrganizationId, request.Date, request.Reference);
        foreach (var line in request.Lines)
        {
            journalVoucher.AddLine(line.AccountId, line.Debit, line.Credit);
        }

        db.JournalVouchers.Add(journalVoucher);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateJournalVoucherResult(journalVoucher.Id, journalVoucher.Code, journalVoucher.Status);
    }
}
