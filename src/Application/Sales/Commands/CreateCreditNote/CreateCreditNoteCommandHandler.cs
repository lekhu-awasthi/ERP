using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateCreditNote;

public sealed class CreateCreditNoteCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateCreditNoteCommand, CreateCreditNoteResult>
{
    public async Task<CreateCreditNoteResult> Handle(CreateCreditNoteCommand request, CancellationToken cancellationToken)
    {
        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        if (request.ReferrerType == DocumentType.Invoice && request.ReferrerId is { } invoiceId)
        {
            await SalesValidation.EnsureCreditNoteLinesWithinInvoiceRemainingAsync(
                db, request.OrganizationId, invoiceId, request.ContactId, request.DiscountPct, request.Lines, cancellationToken);
        }

        var creditNote = CreditNote.Create(
            request.OrganizationId, request.ContactId, request.Date, request.Reference, request.ReferrerType, request.ReferrerId,
            request.DiscountPct);

        // Phase 28 -- the currency pair is set right after construction rather than threaded
        // through Create's parameter list; see the aggregate's SetCurrency doc comment for why.
        // Null/null means the base currency at rate 1, so a caller that never heard of this phase
        // gets exactly the behaviour it had before.
        creditNote.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        creditNote.SetTerms(request.Terms);

        foreach (var line in request.Lines)
        {
            creditNote.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.CreditNotes.Add(creditNote);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCreditNoteResult(creditNote.Id, creditNote.Code, creditNote.Status);
    }
}
