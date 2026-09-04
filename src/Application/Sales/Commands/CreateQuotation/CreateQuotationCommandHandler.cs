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

        var quotation = Quotation.Create(
            request.OrganizationId, request.ContactId, request.Date, request.ExpiryDate, request.Reference, request.DiscountPct);

        // Phase 28 -- the currency pair is set right after construction rather than threaded
        // through Create's parameter list; see the aggregate's SetCurrency doc comment for why.
        // Null/null means the base currency at rate 1, so a caller that never heard of this phase
        // gets exactly the behaviour it had before.
        quotation.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        quotation.SetTerms(request.Terms);

        foreach (var line in request.Lines)
        {
            quotation.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.Quotations.Add(quotation);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateQuotationResult(quotation.Id, quotation.Code, quotation.Status);
    }
}
