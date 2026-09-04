using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.UpdateQuotation;

public sealed class UpdateQuotationCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateQuotationCommand, UpdateQuotationResult>
{
    public async Task<UpdateQuotationResult> Handle(UpdateQuotationCommand request, CancellationToken cancellationToken)
    {
        // Include(x => x.Lines) so the handler knows which existing line rows to remove below --
        // see phase-4-status.md's "replace a whole encapsulated child collection" gotcha.
        var quotation = await db.Quotations
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Quotation not found.");

        if (quotation.Status != QuotationStatus.Draft)
        {
            throw new ConflictException("Only a Draft quotation can be edited.");
        }

        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var oldLines = quotation.Lines.ToList();

        quotation.UpdateHeader(request.ContactId, request.Date, request.ExpiryDate, request.Reference, request.DiscountPct);
        quotation.SetTerms(request.Terms);

        quotation.ClearLines();
        foreach (var line in request.Lines)
        {
            quotation.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.QuotationLines.RemoveRange(oldLines);
        db.QuotationLines.AddRange(quotation.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateQuotationResult(quotation.Id, quotation.Code, quotation.Status);
    }
}
