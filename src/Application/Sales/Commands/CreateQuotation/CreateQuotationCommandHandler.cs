using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
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

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        quotation.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.Quotation, request.LocationId,
            cancellationToken));
        quotation.SetTerms(request.Terms);

        // Phase 52 -- the catalogue is read here and never again: ResolveAsync turns the unit
        // each line names into the factor frozen onto it, so editing (or deleting) the product's
        // unit row afterwards cannot reach back and change what this document did to stock.
        var units = await DocumentLineUnitResolver.ResolveAsync(
            db, request.OrganizationId,
            [.. request.Lines.Select(x => new DocumentLineUnitResolver.LineUnitInput(x.ProductId, x.UnitId))],
            cancellationToken);

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            quotation.AddLine(
                line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct,
                units[i].UnitId, units[i].ConversionFactor);
        }

        db.Quotations.Add(quotation);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateQuotationResult(quotation.Id, quotation.Code, quotation.Status);
    }
}
