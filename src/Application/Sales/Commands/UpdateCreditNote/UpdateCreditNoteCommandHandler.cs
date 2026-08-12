using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.UpdateCreditNote;

public sealed class UpdateCreditNoteCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateCreditNoteCommand, UpdateCreditNoteResult>
{
    public async Task<UpdateCreditNoteResult> Handle(UpdateCreditNoteCommand request, CancellationToken cancellationToken)
    {
        var creditNote = await db.CreditNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Credit note not found.");

        if (creditNote.Status != CreditNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft credit note can be edited.");
        }

        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var oldLines = creditNote.Lines.ToList();

        creditNote.UpdateHeader(request.ContactId, request.Date, request.Reference);
        creditNote.ClearLines();
        foreach (var line in request.Lines)
        {
            creditNote.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate);
        }

        db.CreditNoteLines.RemoveRange(oldLines);
        db.CreditNoteLines.AddRange(creditNote.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateCreditNoteResult(creditNote.Id, creditNote.Code, creditNote.Status);
    }
}
