using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateQuotation;

public sealed class CreateQuotationCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateQuotationCommand, CreateQuotationResult>
{
    public async Task<CreateQuotationResult> Handle(CreateQuotationCommand request, CancellationToken cancellationToken)
    {
        await SalesValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var quotation = Quotation.Create(request.OrganizationId, request.ContactId, request.Date, request.ExpiryDate, request.Reference);
        foreach (var line in request.Lines)
        {
            quotation.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate);
        }

        db.Quotations.Add(quotation);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateQuotationResult(quotation.Id, quotation.Code, quotation.Status);
    }
}
