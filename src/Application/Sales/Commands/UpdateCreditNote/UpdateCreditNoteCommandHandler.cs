using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
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

        creditNote.UpdateHeader(request.ContactId, request.Date, request.Reference, request.DiscountPct);

        // Phase 28 -- see the Create handler's note. Draft-only, enforced by the aggregate.
        creditNote.SetCurrency(request.CurrencyCode, request.ExchangeRate);

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        creditNote.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.CreditNote, request.LocationId,
            cancellationToken));
        creditNote.SetTerms(request.Terms);

        creditNote.ClearLines();
        foreach (var line in request.Lines)
        {
            creditNote.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.CreditNoteLines.RemoveRange(oldLines);
        db.CreditNoteLines.AddRange(creditNote.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateCreditNoteResult(creditNote.Id, creditNote.Code, creditNote.Status);
    }
}
