using ErpApp.Application.Common.Persistence;
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

        var creditNote = CreditNote.Create(
            request.OrganizationId, request.ContactId, request.Date, request.Reference, request.ReferrerType, request.ReferrerId);
        foreach (var line in request.Lines)
        {
            creditNote.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate);
        }

        db.CreditNotes.Add(creditNote);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCreditNoteResult(creditNote.Id, creditNote.Code, creditNote.Status);
    }
}
