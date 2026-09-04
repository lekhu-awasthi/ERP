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
        await AccountingValidation.EnsureContactsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ContactId), cancellationToken);

        var journalVoucher = JournalVoucher.Create(request.OrganizationId, request.Date, request.Reference);

        // Phase 28 -- the currency pair is set right after construction rather than threaded
        // through Create's parameter list; see the aggregate's SetCurrency doc comment for why.
        // Null/null means the base currency at rate 1, so a caller that never heard of this phase
        // gets exactly the behaviour it had before.
        journalVoucher.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        foreach (var line in request.Lines)
        {
            journalVoucher.AddLine(line.AccountId, line.Debit, line.Credit, line.ContactId);
        }

        db.JournalVouchers.Add(journalVoucher);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateJournalVoucherResult(journalVoucher.Id, journalVoucher.Code, journalVoucher.Status);
    }
}
